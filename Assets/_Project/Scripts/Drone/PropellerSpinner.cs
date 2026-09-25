using FlyDrone.Drone;
using UnityEngine;

/// <summary>
/// Суто візуальне обертання пропелерів. Фізику не чіпає:
/// контролер дрона передає сюди тягу кожного мотора (0..1).
/// </summary>
public class PropellerSpinner : MonoBehaviour
{
    [System.Serializable]
    public class Propeller
    {
        public Transform transform;          // pivot має бути в центрі маточини
        public bool clockwise = true;        // у квадрокоптера діагональні пари крутяться в один бік
        [Tooltip("Опційно: розмитий диск, що вмикається на високих обертах")]
        public Renderer blurDisc;
        public Renderer bladeMesh;
        [HideInInspector] public float currentRpm;
        [HideInInspector] public float targetThrottle;
    }
    [SerializeField] private DroneController drone;
    [SerializeField] private Propeller[] propellers = new Propeller[4];
    [SerializeField] public Vector3 localSpinAxis = Vector3.right; // для моделей з Blender часто Vector3.forward

    // [Header("Оберти")]
    // [SerializeField] private readonly float idleRpm = 1500f;
    // [SerializeField] private float maxRpm = 9000f;
    // [Tooltip("RPM за секунду — інерція мотора при розкрутці/гальмуванні")]
    // [SerializeField] private float rpmResponse = 25000f;
    [Tooltip("true, якщо передаєте нормалізовану ТЯГУ (тяга ~ RPM², тому беремо корінь)")]
    [SerializeField] private bool inputIsThrust = true;
    [SerializeField] private bool armed = true;

    [Header("Візуал")]
    [Tooltip("Реальні оберти дають стробоскоп на 60 FPS — масштабуємо вниз")]
    [SerializeField] private float visualScale = 0.08f;
    [SerializeField] private float blurSwitchRpm = 4000f;

    public void SetMotorThrottle(int index, float value01)
    {
        if (index >= 0 && index < propellers.Length)
            propellers[index].targetThrottle = Mathf.Clamp01(value01);
    }

    public void SetArmed(bool value) => armed = value;

    private void Update() // візуал — в Update, не у FixedUpdate
    {
        float dt = Time.deltaTime;

        foreach (var p in propellers)
        {
            if (p.transform == null) continue;

            float t = inputIsThrust ? Mathf.Sqrt(drone.Throttle01) : drone.Throttle01;
            float targetRpm = armed ? Mathf.Lerp(drone.Config.idleRpm, drone.Config.maxRpm, t) : 0f;

            p.currentRpm = Mathf.MoveTowards(p.currentRpm, targetRpm, drone.Config.rpmResponse * dt);

            // RPM -> градуси/сек: rpm * 360 / 60 = rpm * 6
            float degPerSec = p.currentRpm * 6f * visualScale;
            // У лівосторонній системі Unity додатний поворот навколо +Y — за годинниковою, якщо дивитися згори
            float dir = p.clockwise ? 1f : -1f;
            p.transform.Rotate(localSpinAxis, dir * degPerSec * dt, Space.Self);

            if (p.blurDisc != null && p.bladeMesh != null)
            {
                bool fast = p.currentRpm > blurSwitchRpm;
                p.blurDisc.enabled = fast;
                p.bladeMesh.enabled = !fast;
            }
        }
    }
}
