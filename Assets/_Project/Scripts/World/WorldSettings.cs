using UnityEngine;

namespace FlyDrone.World
{
    [CreateAssetMenu(fileName = "WorldSettings", menuName = "FlyDrone/World Settings")]
    public class WorldSettings : ScriptableObject
    {
        [Header("Розмір світу")]
        [Tooltip("Сторона одного тайла, м")]
        public float tileSize = 1000f;
        [Tooltip("Тайлів по стороні світу. 8 → 8×8 км")]
        [Min(1)] public int worldSizeTiles = 8;
        [Tooltip("Максимальна висота рельєфу, м")]
        public float maxHeight = 500f;
        [Tooltip("Має бути 2^n + 1: 129, 257, 513")]
        public int heightmapResolution = 257;
        public int alphamapResolution = 256;

        [Header("Рельєф")]
        public int seed = 1337;
        public NoiseSettings noise = NoiseSettings.Default;

        [Header("Текстури (частки від maxHeight / градуси)")]
        [Range(0f, 0.3f)] public float sandLevel = 0.06f;
        [Range(0.3f, 1f)] public float snowLevel = 0.7f;
        [Range(10f, 60f)] public float rockSlopeDeg = 27f;

        [Header("Дерева")]
        [Tooltip("Крок сітки розстановки, м. Менше → більше дерев")]
        public float treeCellSize = 14f;
        [Range(0f, 1f)] public float treeDensity = 0.6f;
        [Range(5f, 45f)] public float treeMaxSlopeDeg = 25f;

        [Header("Стрімінг")]
        [Tooltip("Радіус завантаження в тайлах: 2 → квадрат 5×5 навколо дрона")]
        [Min(1)] public int loadRadius = 2;
        [Tooltip("Має бути більшим за loadRadius — гістерезис, щоб тайли не «блимали» на межі")]
        [Min(2)] public int unloadRadius = 3;
        [Min(1)] public int maxConcurrentJobs = 2;
        [Tooltip("Вимкніть, щоб отримати заміри «до оптимізації»")]
        public bool useBackgroundThreads = true;
        [Tooltip("Скільки вивантажених тайлів тримати для перевикористання")]
        public int poolSize = 8;

        [Header("Рендер terrain")]
        [Tooltip("Допустима похибка LOD у пікселях. 1 = максимальна деталізація і найдорожче")]
        [Range(1f, 20f)] public float pixelError = 6f;
        [Tooltip("Далі цієї відстані terrain малюється однією запеченою текстурою")]
        public float basemapDistance = 300f;
        public float treeDistance = 700f;
        public bool drawInstanced = true;
    }
}
