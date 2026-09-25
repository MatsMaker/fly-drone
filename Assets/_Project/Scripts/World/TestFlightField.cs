using UnityEngine;

namespace FlyDrone.World
{
    /// <summary>
    /// Тимчасове поле з «пілонами», щоб відчувати швидкість на першому дні.
    /// На другому дні його замінить terrain.
    /// </summary>
    public class TestFlightField : MonoBehaviour
    {
        [SerializeField] private int count = 300;
        [SerializeField] private float innerRadius = 15f;
        [SerializeField] private float outerRadius = 400f;
        [SerializeField] private Vector2 heightRange = new Vector2(2f, 25f);
        [SerializeField] private int seed = 42;
        [SerializeField] private Material material;

        private void Start()
        {
            var rng = new System.Random(seed);

            for (int i = 0; i < count; i++)
            {
                float angle = (float)rng.NextDouble() * Mathf.PI * 2f;
                // sqrt дає рівномірний розподіл по площі кільця
                float r = Mathf.Sqrt(Mathf.Lerp(innerRadius * innerRadius, outerRadius * outerRadius,
                                                (float)rng.NextDouble()));
                float h = Mathf.Lerp(heightRange.x, heightRange.y, (float)rng.NextDouble());

                GameObject pylon = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pylon.name = $"Pylon_{i:000}";
                pylon.transform.SetParent(transform, false);
                pylon.transform.localPosition = new Vector3(Mathf.Cos(angle) * r, h * 0.5f, Mathf.Sin(angle) * r);
                pylon.transform.localScale = new Vector3(2f, h, 2f);

                if (material != null)
                {
                    pylon.GetComponent<Renderer>().sharedMaterial = material;
                }
            }
        }
    }
}