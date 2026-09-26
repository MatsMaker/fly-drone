using System.Collections.Generic;
using System.Globalization;
using System.IO;
using FlyDrone.World;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FlyDrone.Tools
{
    /// <summary>
    /// Відтворюваний проліт для замірів продуктивності: клавіша B.
    /// Дрон стає кінематичним і летить по прямій над рельєфом, а скрипт збирає час кадрів.
    /// Однаковий маршрут → заміри «до» й «після» можна чесно порівнювати.
    /// </summary>
    public class FlightBenchmark : MonoBehaviour
    {
        [SerializeField] private Rigidbody drone;
        [SerializeField] private TerrainStreamer streamer;
        [Tooltip("Підпис рядка в CSV, напр. before / after-threads / after-all")]
        [SerializeField] private string label = "baseline";
        [SerializeField] private float duration = 60f;
        [Tooltip("Навмисно швидше, ніж літає дрон, — стрес-тест стрімінгу")]
        [SerializeField] private float speed = 35f;
        [SerializeField] private float altitudeAboveGround = 60f;
        [SerializeField] private float warmup = 3f;
        [SerializeField] private Vector3 direction = new Vector3(1f, 0f, 1f);

        private readonly List<float> _frameMs = new List<float>(8192);
        private bool _running;
        private float _time;
        private CollisionDetectionMode _savedMode;

        public bool IsRunning => _running;

        private void Update()
        {
            if (!_running)
            {
                if (Keyboard.current != null && Keyboard.current.bKey.wasPressedThisFrame) Begin();
                return;
            }

            _time += Time.unscaledDeltaTime;
            if (_time > warmup) _frameMs.Add(Time.unscaledDeltaTime * 1000f);
            if (_time >= warmup + duration) Finish();
        }

        private void FixedUpdate()
        {
            if (!_running) return;

            Vector3 dir = direction.normalized;
            Vector3 next = drone.position + dir * (speed * Time.fixedDeltaTime);

            float y = drone.position.y;
            if (streamer.TryGetHeight(next, out float ground))
            {
                y = Mathf.Lerp(y, ground + altitudeAboveGround, 0.05f);
            }

            next.y = y;
            drone.MovePosition(next);
            drone.MoveRotation(Quaternion.LookRotation(dir));
        }

        private void Begin()
        {
            _running = true;
            _time = 0f;
            _frameMs.Clear();

            // Кінематичні тіла підтримують лише speculative continuous — міняємо до перемикання.
            _savedMode = drone.collisionDetectionMode;
            drone.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            drone.isKinematic = true;

            Debug.Log($"[Benchmark] Старт «{label}»: {duration} с, {speed} м/с");
        }

        private void Finish()
        {
            _running = false;
            drone.isKinematic = false;
            drone.collisionDetectionMode = _savedMode;
            drone.linearVelocity = Vector3.zero;
            drone.angularVelocity = Vector3.zero;

            if (_frameMs.Count == 0) return;

            var sorted = new List<float>(_frameMs);
            sorted.Sort();

            float sum = 0f;
            int hitches = 0;
            foreach (float ms in _frameMs)
            {
                sum += ms;
                if (ms > 33.3f) hitches++;
            }

            float avgMs = sum / _frameMs.Count;
            float p99Ms = sorted[Mathf.Min(sorted.Count - 1, (int)(sorted.Count * 0.99f))];
            float maxMs = sorted[sorted.Count - 1];

            string report = string.Format(CultureInfo.InvariantCulture,
                "[Benchmark] {0}: avg {1:0.0} FPS ({2:0.00} ms) | 1% low {3:0.0} FPS | max {4:0.0} ms | кадрів >33 ms: {5}",
                label, 1000f / avgMs, avgMs, 1000f / p99Ms, maxMs, hitches);
            Debug.Log(report);

            string path = Path.Combine(Application.persistentDataPath, "benchmark.csv");
            if (!File.Exists(path))
            {
                File.WriteAllText(path, "label,avg_fps,avg_ms,low1_fps,max_ms,hitches,frames\n");
            }

            File.AppendAllText(path, string.Format(CultureInfo.InvariantCulture,
                "{0},{1:0.0},{2:0.00},{3:0.0},{4:0.0},{5},{6}\n",
                label, 1000f / avgMs, avgMs, 1000f / p99Ms, maxMs, hitches, _frameMs.Count));
            Debug.Log($"[Benchmark] Записано в {path}");
        }
    }
}
