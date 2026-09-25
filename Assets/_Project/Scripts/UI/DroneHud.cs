using FlyDrone.Drone;
using TMPro;
using UnityEngine;

namespace FlyDrone.UI
{
    public class DroneHud : MonoBehaviour
    {
        [SerializeField] private DroneController drone;
        [SerializeField] private TMP_Text label;
        [SerializeField] private float refreshRate = 10f;
        [Tooltip("Шари, які вважаємо землею. Шар Drone сюди не входить.")]
        [SerializeField] private LayerMask groundMask = ~0;

        private Rigidbody _rb;
        private float _timer;
        private float _fps;

        private void Awake()
        {
            _rb = drone.GetComponent<Rigidbody>();
        }

        private void Update()
        {
            float dt = Mathf.Max(Time.unscaledDeltaTime, 1e-5f);
            _fps = Mathf.Lerp(_fps, 1f / dt, 0.1f);

            // Оновлюємо текст 10 разів на секунду: менше алокацій і текст не «мерехтить».
            _timer += dt;
            if (_timer < 1f / refreshRate) return;
            _timer = 0f;

            Transform t = drone.transform;
            Vector3 v = _rb.linearVelocity;

            string agl = Physics.Raycast(t.position, Vector3.down, out RaycastHit hit, 2000f,
                                         groundMask, QueryTriggerInteraction.Ignore)
                ? hit.distance.ToString("0.0")
                : "—";

            float pitch = -Mathf.Asin(Mathf.Clamp(t.forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float roll = -Mathf.Asin(Mathf.Clamp(t.right.y, -1f, 1f)) * Mathf.Rad2Deg;

            label.text =
                "<mspace=0.6em>" +
                $"MODE {drone.Mode}\n" +
                $"SPD  {v.magnitude * 3.6f,6:0.0} km/h\n" +
                $"V/S  {v.y,6:+0.0;-0.0} m/s\n" +
                $"AGL  {agl,6} m\n" +
                $"THR  {drone.Throttle01 * 100f,6:0} %\n" +
                $"PIT  {pitch,6:0}°\n" +
                $"ROL  {roll,6:0}°\n" +
                $"FPS  {_fps,6:0}";
        }
    }
}
