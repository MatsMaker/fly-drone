namespace FlyDrone.World
{
    /// <summary>
    /// Класичний 2D Perlin noise (improved noise, Ken Perlin 2002).
    /// Власна реалізація замість Mathf.PerlinNoise: детермінована за seed,
    /// без звернень до Unity API — безпечна для фонових потоків.
    /// Після створення об'єкт лише читається, тому його можна ділити між потоками.
    /// </summary>
    public sealed class PerlinNoise
    {
        private readonly int[] _p = new int[512];

        public PerlinNoise(int seed)
        {
            var perm = new int[256];
            for (int i = 0; i < 256; i++) perm[i] = i;

            var rng = new System.Random(seed);
            for (int i = 255; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (perm[i], perm[j]) = (perm[j], perm[i]);
            }

            for (int i = 0; i < 512; i++) _p[i] = perm[i & 255];
        }

        /// <summary>Значення приблизно в діапазоні [-1, 1].</summary>
        /// <param name="period">Період решітки: з малим періодом шум тайлиться (для текстур).</param>
        public float Sample(float x, float y, int period = 256)
        {
            int xi = FastFloor(x);
            int yi = FastFloor(y);
            float xf = x - xi;
            float yf = y - yi;

            int x0 = Mod(xi, period) & 255;
            int x1 = Mod(xi + 1, period) & 255;
            int y0 = Mod(yi, period) & 255;
            int y1 = Mod(yi + 1, period) & 255;

            int aa = _p[_p[x0] + y0];
            int ba = _p[_p[x1] + y0];
            int ab = _p[_p[x0] + y1];
            int bb = _p[_p[x1] + y1];

            float u = Fade(xf);
            float v = Fade(yf);

            float bottom = Lerp(Grad(aa, xf, yf), Grad(ba, xf - 1f, yf), u);
            float top = Lerp(Grad(ab, xf, yf - 1f), Grad(bb, xf - 1f, yf - 1f), u);
            return Lerp(bottom, top, v);
        }

        private static int FastFloor(float x)
        {
            int i = (int)x;
            return x < i ? i - 1 : i;
        }

        private static int Mod(int a, int m)
        {
            int r = a % m;
            return r < 0 ? r + m : r;
        }

        private static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;

        private static float Grad(int hash, float x, float y)
        {
            switch (hash & 7)
            {
                case 0: return x + y;
                case 1: return -x + y;
                case 2: return x - y;
                case 3: return -x - y;
                case 4: return x;
                case 5: return -x;
                case 6: return y;
                default: return -y;
            }
        }
    }
}
