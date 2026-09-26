using UnityEngine;

namespace FlyDrone.World
{
    /// <summary>
    /// Створює 4 шари terrain з процедурними текстурами — прототип не потребує жодних ассетів.
    /// 4 шари — не випадково: URP Terrain Lit малює до 4 шарів за один прохід.
    /// </summary>
    public static class TerrainLayerFactory
    {
        public static TerrainLayer[] CreateDefault()
        {
            var layers = new TerrainLayer[TileBuilder.LayerCount];
            layers[TileBuilder.Grass] = Make("Grass", new Color(0.22f, 0.38f, 0.14f), new Color(0.34f, 0.48f, 0.20f), 8f, 11);
            layers[TileBuilder.Rock] = Make("Rock", new Color(0.36f, 0.33f, 0.30f), new Color(0.55f, 0.52f, 0.48f), 12f, 22);
            layers[TileBuilder.Sand] = Make("Sand", new Color(0.70f, 0.63f, 0.45f), new Color(0.82f, 0.76f, 0.58f), 6f, 33);
            layers[TileBuilder.Snow] = Make("Snow", new Color(0.86f, 0.89f, 0.94f), new Color(0.98f, 0.99f, 1.00f), 10f, 44);
            return layers;
        }

        public static void Destroy(TerrainLayer[] layers)
        {
            if (layers == null) return;
            foreach (TerrainLayer layer in layers)
            {
                if (layer == null) continue;
                Object.Destroy(layer.diffuseTexture);
                Object.Destroy(layer);
            }
        }

        private static TerrainLayer Make(string name, Color a, Color b, float tileMeters, int seed)
        {
            const int size = 128;
            var noise = new PerlinNoise(seed);
            var pixels = new Color32[size * size];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Період шуму кратний розміру текстури → текстура тайлиться без швів.
                    float n = 0.6f * noise.Sample(x / 16f, y / 16f, 8)
                            + 0.4f * noise.Sample(x / 4f, y / 4f, 32);
                    float t = Mathf.Clamp01(0.5f + 0.5f * n);
                    pixels[y * size + x] = Color.Lerp(a, b, t);
                }
            }

            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
                anisoLevel = 4
            };
            tex.SetPixels32(pixels);
            tex.Apply(updateMipmaps: true, makeNoLongerReadable: true); // звільняє CPU-копію

            return new TerrainLayer
            {
                name = name,
                diffuseTexture = tex,
                tileSize = new Vector2(tileMeters, tileMeters)
            };
        }
    }
}
