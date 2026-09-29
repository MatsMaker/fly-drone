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
            layers[TileBuilder.Grass] = Make("Grass", new Color(0.22f, 0.38f, 0.14f), new Color(0.34f, 0.48f, 0.20f), 8f, 11, 0.05f);
            layers[TileBuilder.Rock]  = Make("Rock",  new Color(0.36f, 0.33f, 0.30f), new Color(0.55f, 0.52f, 0.48f), 12f, 22, 0.15f);
            layers[TileBuilder.Sand]  = Make("Sand",  new Color(0.70f, 0.63f, 0.45f), new Color(0.82f, 0.76f, 0.58f), 6f, 33, 0.10f);
            layers[TileBuilder.Snow]  = Make("Snow",  new Color(0.86f, 0.89f, 0.94f), new Color(0.98f, 0.99f, 1.00f), 10f, 44, 0.30f);
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

        private static TerrainLayer Make(string name, Color a, Color b, float tileMeters, int seed, float smoothness)
        {
            const int size = 128;
            var noise = new PerlinNoise(seed);
            var pixels = new Color32[size * size];
            byte alpha = (byte)(smoothness * 255f);   // альфа = гладкість

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float n = 0.6f * noise.Sample(x / 16f, y / 16f, 8)
                            + 0.4f * noise.Sample(x / 4f, y / 4f, 32);
                    float t = Mathf.Clamp01(0.5f + 0.5f * n);
                    Color32 c = Color.Lerp(a, b, t);
                    c.a = alpha;
                    pixels[y * size + x] = c;
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
                tileSize = new Vector2(tileMeters, tileMeters),
                smoothness = smoothness,
                metallic = 0f
            };
        }
    }
}
