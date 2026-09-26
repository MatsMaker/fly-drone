using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FlyDrone.Drone;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace FlyDrone.World
{
    /// <summary>
    /// Тримає навколо цілі квадрат тайлів terrain.
    /// Рахунок даних — у фонових потоках, застосування — не більше одного тайла на кадр,
    /// вивантажені тайли повертаються в пул.
    /// </summary>
    public class TerrainStreamer : MonoBehaviour
    {
        [SerializeField] private WorldSettings settings;
        [SerializeField] private Transform target;
        [Tooltip("Матеріал з шейдером Universal Render Pipeline/Terrain/Lit")]
        [SerializeField] private Material terrainMaterial;
        [Tooltip("Префаб дерева з LODGroup. Можна залишити порожнім — тоді без дерев.")]
        [SerializeField] private GameObject treePrefab;
        [SerializeField] private Transform spawnPoint;
        [SerializeField] private float spawnHeightAboveGround = 2f;

        private static readonly ProfilerMarker ApplyMarker = new ProfilerMarker("TerrainStreamer.Apply");
        private static readonly ProfilerMarker BuildSyncMarker = new ProfilerMarker("TerrainStreamer.BuildOnMainThread");

        private readonly Dictionary<TileCoord, Terrain> _active = new Dictionary<TileCoord, Terrain>();
        private readonly HashSet<TileCoord> _pending = new HashSet<TileCoord>();
        private readonly ConcurrentQueue<TileData> _ready = new ConcurrentQueue<TileData>();
        private readonly ConcurrentQueue<TileCoord> _failed = new ConcurrentQueue<TileCoord>();
        private readonly Stack<Terrain> _pool = new Stack<Terrain>();
        private readonly List<TileCoord> _toRemove = new List<TileCoord>();
        private readonly List<TileCoord> _offsets = new List<TileCoord>();

        private TileBuildParams _params;
        private TerrainLayer[] _layers;
        private TreePrototype[] _treePrototypes;
        private int _runningJobs;

        public int ActiveTiles => _active.Count;
        public int PendingTiles => _pending.Count;
        public double LastBuildMs { get; private set; }
        public double LastApplyMs { get; private set; }

        private void Start()
        {
            _params = TileBuildParams.From(settings);
            _layers = TerrainLayerFactory.CreateDefault();
            _treePrototypes = treePrefab != null
                ? new[] { new TreePrototype { prefab = treePrefab } }
                : Array.Empty<TreePrototype>();

            BuildSortedOffsets();

            // Найближчі 3×3 тайли — синхронно: дрон не має стартувати над порожнечею.
            TileCoord center = WorldToTile(target.position);
            foreach (TileCoord offset in _offsets)
            {
                if (TileCoord.Chebyshev(offset, new TileCoord(0, 0)) > 1) break;
                var c = new TileCoord(center.X + offset.X, center.Z + offset.Z);
                if (IsInsideWorld(c)) Apply(TileBuilder.Build(c, _params));
            }

            PlaceAtSpawn();
        }

        private void Update()
        {
            TileCoord center = WorldToTile(target.position);

            UnloadFarTiles(center);
            RequestMissingTiles(center);

            while (_failed.TryDequeue(out TileCoord failed))
            {
                _pending.Remove(failed);
            }

            // Time slicing: застосування тайла дороге (SetHeights, колайдер), тому максимум один за кадр.
            if (_ready.TryDequeue(out TileData data))
            {
                _pending.Remove(data.Coord);
                bool stillNeeded = TileCoord.Chebyshev(data.Coord, center) <= settings.unloadRadius;
                if (stillNeeded && !_active.ContainsKey(data.Coord))
                {
                    Apply(data);
                }
            }
        }

        private void OnDestroy()
        {
            foreach (Terrain t in _active.Values) DestroyTerrain(t);
            foreach (Terrain t in _pool) DestroyTerrain(t);
            _active.Clear();
            _pool.Clear();
            TerrainLayerFactory.Destroy(_layers);
        }

        // ---------- Публічне API ----------

        /// <summary>Висота землі в точці, якщо тайл уже завантажено.</summary>
        public bool TryGetHeight(Vector3 worldPos, out float height)
        {
            if (_active.TryGetValue(WorldToTile(worldPos), out Terrain t))
            {
                height = t.SampleHeight(worldPos) + t.transform.position.y;
                return true;
            }

            height = 0f;
            return false;
        }

        // ---------- Стрімінг ----------

        private void UnloadFarTiles(TileCoord center)
        {
            _toRemove.Clear();
            foreach (TileCoord c in _active.Keys)
            {
                if (TileCoord.Chebyshev(c, center) > settings.unloadRadius) _toRemove.Add(c);
            }

            foreach (TileCoord c in _toRemove) Release(c);
        }

        private void RequestMissingTiles(TileCoord center)
        {
            // Зсуви відсортовані за відстанню — першими вантажаться найближчі тайли.
            foreach (TileCoord offset in _offsets)
            {
                var c = new TileCoord(center.X + offset.X, center.Z + offset.Z);
                if (!IsInsideWorld(c) || _active.ContainsKey(c) || _pending.Contains(c)) continue;

                if (settings.useBackgroundThreads)
                {
                    if (Volatile.Read(ref _runningJobs) >= settings.maxConcurrentJobs) return;
                    Schedule(c);
                }
                else
                {
                    // «Наївний» режим для замірів «до»: усе в головному потоці.
                    using (BuildSyncMarker.Auto())
                    {
                        Apply(TileBuilder.Build(c, _params));
                    }
                    return; // один тайл на кадр, інакше гра зависне на секунди
                }
            }
        }

        private void Schedule(TileCoord c)
        {
            _pending.Add(c);
            Interlocked.Increment(ref _runningJobs);
            TileBuildParams p = _params;

            Task.Run(() =>
            {
                try
                {
                    _ready.Enqueue(TileBuilder.Build(c, p));
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                    _failed.Enqueue(c);
                }
                finally
                {
                    Interlocked.Decrement(ref _runningJobs);
                }
            });
        }

        private void Apply(TileData data)
        {
            using (ApplyMarker.Auto())
            {
                var sw = Stopwatch.StartNew();

                Terrain terrain = _pool.Count > 0 ? _pool.Pop() : CreateTerrain();
                TerrainData td = terrain.terrainData;

                // Позицію — до дерев: тайл з пулу переїжджає, а кеш дерев terrain не стежить за трансформом.
                terrain.transform.localPosition = new Vector3(data.Coord.X * settings.tileSize, 0f, data.Coord.Z * settings.tileSize);

                td.SetHeights(0, 0, data.Heights);
                td.SetAlphamaps(0, 0, data.Alphamaps);
                td.SetTreeInstances(_treePrototypes.Length > 0 ? data.Trees : Array.Empty<TreeInstance>(), true);

                terrain.gameObject.name = $"Tile {data.Coord}";
                terrain.gameObject.SetActive(true);

                _active[data.Coord] = terrain;
                Terrain.SetConnectivityDirty(); // оновити сусідів — прибирає шви LOD між тайлами

                LastBuildMs = data.BuildMs;
                LastApplyMs = sw.Elapsed.TotalMilliseconds;
            }
        }

        private void Release(TileCoord c)
        {
            Terrain terrain = _active[c];
            _active.Remove(c);

            if (_pool.Count < settings.poolSize)
            {
                terrain.gameObject.SetActive(false);
                _pool.Push(terrain);
            }
            else
            {
                DestroyTerrain(terrain);
            }

            Terrain.SetConnectivityDirty();
        }

        private Terrain CreateTerrain()
        {
            // Порядок важливий: зміна heightmapResolution скидає size.
            var td = new TerrainData
            {
                heightmapResolution = settings.heightmapResolution,
                alphamapResolution = settings.alphamapResolution,
                baseMapResolution = 256
            };
            td.size = new Vector3(settings.tileSize, settings.maxHeight, settings.tileSize);
            td.terrainLayers = _layers;
            td.treePrototypes = _treePrototypes;

            var go = new GameObject("Tile (pooled)");
            go.transform.SetParent(transform, false);

            var terrain = go.AddComponent<Terrain>();
            terrain.terrainData = td;
            go.AddComponent<TerrainCollider>().terrainData = td;

            if (terrainMaterial != null) terrain.materialTemplate = terrainMaterial;
            terrain.heightmapPixelError = settings.pixelError;
            terrain.basemapDistance = settings.basemapDistance;
            terrain.treeDistance = settings.treeDistance;
            terrain.treeBillboardDistance = settings.treeDistance;
            terrain.drawInstanced = settings.drawInstanced;
            terrain.allowAutoConnect = true;
            terrain.groupingID = 0;

            return terrain;
        }

        private static void DestroyTerrain(Terrain terrain)
        {
            // TerrainData створена в коді — Unity не звільнить її разом з GameObject.
            TerrainData td = terrain.terrainData;
            Destroy(terrain.gameObject);
            Destroy(td);
        }

        // ---------- Хелпери ----------

        private void PlaceAtSpawn()
        {
            if (spawnPoint != null && TryGetHeight(spawnPoint.position, out float ground))
            {
                Vector3 p = spawnPoint.position;
                p.y = ground + spawnHeightAboveGround;
                spawnPoint.position = p;
            }

            var drone = target.GetComponent<DroneController>();
            if (drone != null) drone.ResetDrone();
        }

        private TileCoord WorldToTile(Vector3 worldPos)
        {
            // Через локальні координати стрімера: після зсуву floating origin усе лишається узгодженим.
            Vector3 local = transform.InverseTransformPoint(worldPos);
            return new TileCoord(
                Mathf.FloorToInt(local.x / settings.tileSize),
                Mathf.FloorToInt(local.z / settings.tileSize));
        }

        private bool IsInsideWorld(TileCoord c)
        {
            // Світ центрований: при 8 тайлах координати від -4 до 3.
            int half = settings.worldSizeTiles / 2;
            int min = -half;
            int max = settings.worldSizeTiles - half - 1;
            return c.X >= min && c.X <= max && c.Z >= min && c.Z <= max;
        }

        private void BuildSortedOffsets()
        {
            _offsets.Clear();
            int r = settings.loadRadius;
            for (int z = -r; z <= r; z++)
            {
                for (int x = -r; x <= r; x++)
                {
                    _offsets.Add(new TileCoord(x, z));
                }
            }

            _offsets.Sort((a, b) => (a.X * a.X + a.Z * a.Z).CompareTo(b.X * b.X + b.Z * b.Z));
        }
    }
}
