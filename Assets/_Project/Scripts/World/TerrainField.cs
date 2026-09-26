using System;
using UnityEngine;

namespace FlyDrone.World
{
    [Serializable]
    public struct NoiseSettings
    {
        [Tooltip("Базова частота, 1/м. 1/1500 — пагорби розміром ~1.5 км")]
        public float frequency;
        [Range(1, 8)] public int octaves;
        [Tooltip("У скільки разів росте частота з кожною октавою")]
        public float lacunarity;
        [Tooltip("У скільки разів падає амплітуда з кожною октавою")]
        public float gain;
        [Tooltip("Спотворення координат, м — робить рельєф менш «штучним»")]
        public float warpStrength;
        [Tooltip("Частота плям лісу, 1/м")]
        public float forestFrequency;

        public static NoiseSettings Default => new NoiseSettings
        {
            frequency = 1f / 1500f,
            octaves = 6,
            lacunarity = 2f,
            gain = 0.5f,
            warpStrength = 250f,
            forestFrequency = 1f / 400f
        };
    }

    /// <summary>
    /// Функція висоти світу: координати в метрах → висота 0..1.
    /// Чиста математика: однакова для будь-якого тайла, тому шви між тайлами збігаються.
    /// </summary>
    public sealed class TerrainField
    {
        private readonly PerlinNoise _noise;
        private readonly NoiseSettings _s;

        public TerrainField(int seed, NoiseSettings settings)
        {
            _noise = new PerlinNoise(seed);
            _s = settings;
        }

        public float Height01(float x, float z)
        {
            float f = _s.frequency;

            // Domain warping: зсуваємо координати іншим шумом.
            float wx = x + _s.warpStrength * _noise.Sample(x * f * 0.7f + 31.7f, z * f * 0.7f + 47.3f);
            float wz = z + _s.warpStrength * _noise.Sample(x * f * 0.7f + 83.1f, z * f * 0.7f + 12.9f);

            // М'які пагорби: fBm, 0..1
            float hills = Fbm(wx * f, wz * f) * 0.5f + 0.5f;

            // Гірські хребти: ridged-шум, 0..1
            float ridges = Ridged(wx * f * 0.8f + 500f, wz * f * 0.8f + 500f);

            // Маска «де гори»: дуже низька частота, щоб гори стояли масивами.
            float maskNoise = _noise.Sample(x * f * 0.25f + 900f, z * f * 0.25f + 900f) * 0.5f + 0.5f;
            float mountains = SmoothStep(0.45f, 0.75f, maskNoise);

            float h = Lerp(hills * hills * 0.4f, 0.2f + ridges * 0.8f, mountains);
            return Clamp01(h);
        }

        /// <summary>Щільність лісу 0..1 — плями, а не рівномірна «щітка».</summary>
        public float Forest01(float x, float z)
        {
            float f = _s.forestFrequency;
            float n = _noise.Sample(x * f + 1234.5f, z * f + 678.9f) * 0.5f + 0.5f;
            return SmoothStep(0.4f, 0.65f, n);
        }

        private float Fbm(float x, float z)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < _s.octaves; i++)
            {
                sum += amp * _noise.Sample(x, z);
                norm += amp;
                amp *= _s.gain;
                x *= _s.lacunarity;
                z *= _s.lacunarity;
            }
            return sum / norm; // ≈ -1..1
        }

        private float Ridged(float x, float z)
        {
            float sum = 0f, amp = 1f, norm = 0f;
            for (int i = 0; i < _s.octaves; i++)
            {
                float n = 1f - Math.Abs(_noise.Sample(x, z)); // гострий гребінь там, де шум = 0
                sum += amp * n * n;
                norm += amp;
                amp *= _s.gain;
                x *= _s.lacunarity;
                z *= _s.lacunarity;
            }
            return sum / norm; // 0..1
        }

        // Власні хелпери замість Mathf — жодних сумнівів щодо потокобезпечності.
        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float t = Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
