using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace FlyDrone.World
{
    public readonly struct TileCoord : IEquatable<TileCoord>
    {
        public readonly int X;
        public readonly int Z;

        public TileCoord(int x, int z)
        {
            X = x;
            Z = z;
        }

        public bool Equals(TileCoord other) => X == other.X && Z == other.Z;
        public override bool Equals(object obj) => obj is TileCoord other && Equals(other);
        public override int GetHashCode() => unchecked((X * 73856093) ^ (Z * 19349663));
        public override string ToString() => $"({X},{Z})";

        /// <summary>Відстань у «квадратних кільцях» навколо центру.</summary>
        public static int Chebyshev(TileCoord a, TileCoord b) => Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Z - b.Z));
    }

    /// <summary>
    /// Незмінний знімок налаштувань для фонового потоку.
    /// ScriptableObject з потоку читати не можна, тому копіюємо значення заздалегідь.
    /// </summary>
    public sealed class TileBuildParams
    {
        public float TileSize;
        public float MaxHeight;
        public int HeightRes;
        public int AlphaRes;
        public float SandLevel;
        public float SnowLevel;
        public float RockSlopeDeg;
        public float TreeCellSize;
        public float TreeDensity;
        public float TreeMaxSlopeDeg;
        public int Seed;
        public TerrainField Field;

        public static TileBuildParams From(WorldSettings s) => new TileBuildParams
        {
            TileSize = s.tileSize,
            MaxHeight = s.maxHeight,
            HeightRes = s.heightmapResolution,
            AlphaRes = s.alphamapResolution,
            SandLevel = s.sandLevel,
            SnowLevel = s.snowLevel,
            RockSlopeDeg = s.rockSlopeDeg,
            TreeCellSize = s.treeCellSize,
            TreeDensity = s.treeDensity,
            TreeMaxSlopeDeg = s.treeMaxSlopeDeg,
            Seed = s.seed,
            Field = new TerrainField(s.seed, s.noise)
        };
    }

    public sealed class TileData
    {
        public TileCoord Coord;
        public float[,] Heights;      // [z, x], 0..1
        public float[,,] Alphamaps;   // [z, x, шар]
        public TreeInstance[] Trees;
        public double BuildMs;
    }

    /// <summary>
    /// Рахує все для одного тайла. Жодних звернень до сцени чи Unity-об'єктів —
    /// тому метод можна запускати в Task.Run.
    /// </summary>
    public static class TileBuilder
    {
        // Порядок має збігатися з TerrainLayerFactory.
        public const int Grass = 0;
        public const int Rock = 1;
        public const int Sand = 2;
        public const int Snow = 3;
        public const int LayerCount = 4;

        public static TileData Build(TileCoord coord, TileBuildParams p)
        {
            var sw = Stopwatch.StartNew();

            float[,] heights = BuildHeights(coord, p);
            float[,,] alphamaps = BuildAlphamaps(heights, p);
            TreeInstance[] trees = BuildTrees(coord, heights, p);

            return new TileData
            {
                Coord = coord,
                Heights = heights,
                Alphamaps = alphamaps,
                Trees = trees,
                BuildMs = sw.Elapsed.TotalMilliseconds
            };
        }

        private static float[,] BuildHeights(TileCoord c, TileBuildParams p)
        {
            int res = p.HeightRes;
            float step = p.TileSize / (res - 1);
            var heights = new float[res, res];

            // Світова координата рахується через цілий індекс семпла:
            // крайній стовпець тайла X і перший стовпець тайла X+1 дають ОДНАКОВЕ число,
            // отже й однакову висоту — без швів.
            int baseX = c.X * (res - 1);
            int baseZ = c.Z * (res - 1);

            for (int z = 0; z < res; z++)
            {
                float wz = (baseZ + z) * step;
                for (int x = 0; x < res; x++)
                {
                    float wx = (baseX + x) * step;
                    heights[z, x] = p.Field.Height01(wx, wz);
                }
            }

            return heights;
        }

        private static float[,,] BuildAlphamaps(float[,] heights, TileBuildParams p)
        {
            int res = p.HeightRes;
            int ar = p.AlphaRes;
            float step = p.TileSize / (res - 1);
            var alpha = new float[ar, ar, LayerCount];

            for (int az = 0; az < ar; az++)
            {
                int hz = (int)Math.Round(az * (res - 1) / (double)(ar - 1));
                for (int ax = 0; ax < ar; ax++)
                {
                    int hx = (int)Math.Round(ax * (res - 1) / (double)(ar - 1));
                    float h = heights[hz, hx];
                    float slope = SlopeDeg(heights, hx, hz, res, step, p.MaxHeight);

                    float rock = SmoothStep(p.RockSlopeDeg - 6f, p.RockSlopeDeg + 6f, slope);
                    float snow = SmoothStep(p.SnowLevel - 0.03f, p.SnowLevel + 0.03f, h) * (1f - rock * 0.7f);
                    float sand = (1f - SmoothStep(p.SandLevel, p.SandLevel + 0.02f, h)) * (1f - rock);
                    float grass = Math.Max(0f, 1f - rock - snow - sand);

                    float sum = rock + snow + sand + grass;
                    if (sum < 1e-5f)
                    {
                        grass = 1f;
                        sum = 1f;
                    }

                    alpha[az, ax, Grass] = grass / sum;
                    alpha[az, ax, Rock] = rock / sum;
                    alpha[az, ax, Sand] = sand / sum;
                    alpha[az, ax, Snow] = snow / sum;
                }
            }

            return alpha;
        }

        private static TreeInstance[] BuildTrees(TileCoord c, float[,] heights, TileBuildParams p)
        {
            int res = p.HeightRes;
            float step = p.TileSize / (res - 1);
            int cells = (int)(p.TileSize / p.TreeCellSize);

            // Окремий seed на тайл: тайл завжди отримує ті самі дерева, в якому б порядку не вантажився.
            var rng = new System.Random(unchecked(p.Seed * 486187739 + c.X * 73856093 + c.Z * 19349663));
            var trees = new List<TreeInstance>(cells * cells / 4);
            var white = new Color32(255, 255, 255, 255);

            float minH = p.SandLevel + 0.02f;
            float maxH = p.SnowLevel - 0.05f;

            for (int cz = 0; cz < cells; cz++)
            {
                for (int cx = 0; cx < cells; cx++)
                {
                    // Jittered grid: випадкова точка всередині кожної клітинки — рівномірно, без скупчень.
                    float lx = (cx + (float)rng.NextDouble()) * p.TreeCellSize;
                    float lz = (cz + (float)rng.NextDouble()) * p.TreeCellSize;
                    double roll = rng.NextDouble();
                    float scale = 0.8f + (float)rng.NextDouble() * 0.5f;
                    float rotation = (float)(rng.NextDouble() * Math.PI * 2.0);

                    if (lx >= p.TileSize || lz >= p.TileSize) continue;

                    float forest = p.Field.Forest01(c.X * p.TileSize + lx, c.Z * p.TileSize + lz);
                    if (roll > p.TreeDensity * forest) continue;

                    int hx = Math.Min((int)Math.Round(lx / step), res - 1);
                    int hz = Math.Min((int)Math.Round(lz / step), res - 1);
                    float h = heights[hz, hx];
                    if (h < minH || h > maxH) continue;
                    if (SlopeDeg(heights, hx, hz, res, step, p.MaxHeight) > p.TreeMaxSlopeDeg) continue;

                    trees.Add(new TreeInstance
                    {
                        // Позиція нормалізована 0..1 у межах тайла; висоту Unity підставить сама (snap).
                        position = new Vector3(lx / p.TileSize, h, lz / p.TileSize),
                        prototypeIndex = 0,
                        widthScale = scale,
                        heightScale = scale * (0.9f + (float)(roll * 0.3)),
                        rotation = rotation,
                        color = white,
                        lightmapColor = white
                    });
                }
            }

            return trees.ToArray();
        }

        private static float SlopeDeg(float[,] h, int x, int z, int res, float step, float maxHeight)
        {
            int x0 = Math.Max(x - 1, 0), x1 = Math.Min(x + 1, res - 1);
            int z0 = Math.Max(z - 1, 0), z1 = Math.Min(z + 1, res - 1);

            float dx = (h[z, x1] - h[z, x0]) * maxHeight / ((x1 - x0) * step);
            float dz = (h[z1, x] - h[z0, x]) * maxHeight / ((z1 - z0) * step);

            return (float)(Math.Atan(Math.Sqrt(dx * dx + dz * dz)) * (180.0 / Math.PI));
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = (x - edge0) / (edge1 - edge0);
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return t * t * (3f - 2f * t);
        }
    }
}
