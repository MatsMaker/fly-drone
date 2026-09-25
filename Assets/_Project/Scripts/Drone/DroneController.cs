using UnityEngine;

namespace FlyDrone.Drone
{
    public enum FlightMode
    {
        Stabilized,
        Acro
    }

    [RequireComponent(typeof(Rigidbody), typeof(DroneInput))]
    public class DroneController : MonoBehaviour
    {
        [SerializeField] private DroneConfig config;
        [SerializeField] private FlightMode mode = FlightMode.Stabilized;
        [SerializeField] private bool debugDraw = true;

        private Rigidbody _rb;
        private DroneInput _input;
        private Pose _spawn;

        // Stabilized: кут → кутове прискорення
        private PidController _pitchAnglePid;
        private PidController _rollAnglePid;
        // Acro: кутова швидкість → кутове прискорення
        private PidController _pitchRatePid;
        private PidController _rollRatePid;
        // Обидва режими
        private PidController _yawRatePid;
        private PidController _climbPid;

        public FlightMode Mode => mode;
        public DroneConfig Config => config;
        /// <summary>Поточна тяга як частка від максимальної, 0..1.</summary>
        public float Throttle01 { get; private set; }
        public Vector3 LastThrustForce { get; private set; }
        public Vector3 LastDragForce { get; private set; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _input = GetComponent<DroneInput>();

            _rb.mass = config.mass;
            _rb.useGravity = true;
            _rb.linearDamping = 0f;                 // лінійний опір рахуємо самі
            _rb.angularDamping = config.angularDamping;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.maxAngularVelocity = 20f;           // дефолт 7 рад/с обрізає флипи в Acro

            _pitchAnglePid = new PidController(config.anglePid);
            _rollAnglePid = new PidController(config.anglePid);
            _pitchRatePid = new PidController(config.ratePid);
            _rollRatePid = new PidController(config.ratePid);
            _yawRatePid = new PidController(config.yawRatePid);
            _climbPid = new PidController(config.climbPid);

            _spawn = new Pose(transform.position, transform.rotation);
        }

        private void Update()
        {
            // Кнопки — тільки в Update: WasPressedThisFrame у FixedUpdate може пропускати натискання.
            if (_input.ToggleModePressed)
            {
                SetMode(mode == FlightMode.Stabilized ? FlightMode.Acro : FlightMode.Stabilized);
            }

            if (_input.ResetPressed)
            {
                ResetDrone();
            }
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            SyncPidSettings();

            // З інтерполяцією transform у FixedUpdate не збігається з фізичним станом,
            // тому беремо орієнтацію з Rigidbody.
            Quaternion rot = _rb.rotation;
            Quaternion invRot = Quaternion.Inverse(rot);
            Vector3 up = rot * Vector3.up;
            Vector3 angVelLocal = invRot * _rb.angularVelocity;

            // 1. Тяга — завжди вздовж локальної осі Y дрона.
            float thrust = mode == FlightMode.Stabilized
                ? StabilizedThrust(up, dt)
                : AcroThrust();
            Throttle01 = thrust / config.MaxThrust;
            LastThrustForce = up * thrust;
            _rb.AddForce(LastThrustForce, ForceMode.Force);

            // 2. Опір повітря.
            ApplyDrag(rot, invRot);

            // 3. Обертання: цільове кутове прискорення в локальних осях.
            Vector3 angAccel = mode == FlightMode.Stabilized
                ? StabilizedAttitude(rot, invRot, angVelLocal, dt)
                : AcroAttitude(angVelLocal, dt);

            float targetYawRate = _input.Yaw * config.maxYawRateDeg * Mathf.Deg2Rad;
            angAccel.y = _yawRatePid.Update(targetYawRate - angVelLocal.y, dt);

            float lim = config.maxAngularAccel;
            angAccel = new Vector3(
                Mathf.Clamp(angAccel.x, -lim, lim),
                Mathf.Clamp(angAccel.y, -lim, lim),
                Mathf.Clamp(angAccel.z, -lim, lim));

            // Acceleration ігнорує тензор інерції: коефіцієнти PID не залежать від форми колайдера.
            _rb.AddRelativeTorque(angAccel, ForceMode.Acceleration);

            if (debugDraw)
            {
                DrawDebug();
            }
        }

        // ---------- Тяга ----------

        private float StabilizedThrust(Vector3 up, float dt)
        {
            // Компенсуємо нахил: вертикальна складова тяги = T·cos(нахилу).
            float cosTilt = Mathf.Max(Vector3.Dot(up, Vector3.up), 0.5f);

            float targetVy = _input.Vertical * config.maxClimbRate;
            float verticalAccel = _climbPid.Update(targetVy - _rb.linearVelocity.y, dt);

            float thrust = config.mass * (config.Gravity + verticalAccel) / cosTilt;
            return Mathf.Clamp(thrust, 0f, config.MaxThrust);
        }

        private float AcroThrust()
        {
            // Центр стіка = висіння, верх = 100%, низ = 0%. Зручно і на клавіатурі, і на геймпаді.
            float v = _input.Vertical;
            float hover = config.HoverThrottle;
            float throttle = v >= 0f ? hover + v * (1f - hover) : hover + v * hover;
            return throttle * config.MaxThrust;
        }

        // ---------- Опір повітря ----------

        private void ApplyDrag(Quaternion rot, Quaternion invRot)
        {
            Vector3 vLocal = invRot * _rb.linearVelocity;
            Vector3 k = config.DragK;

            // F = −½·ρ·Cd·A·v·|v| окремо по кожній локальній осі:
            // плаский дрон «ловить» повітря верхом сильніше, ніж боком.
            Vector3 fLocal = new Vector3(
                -k.x * vLocal.x * Mathf.Abs(vLocal.x),
                -k.y * vLocal.y * Mathf.Abs(vLocal.y),
                -k.z * vLocal.z * Mathf.Abs(vLocal.z));

            LastDragForce = rot * fLocal;
            _rb.AddForce(LastDragForce, ForceMode.Force);
        }

        // ---------- Орієнтація ----------

        private Vector3 StabilizedAttitude(Quaternion rot, Quaternion invRot, Vector3 angVelLocal, float dt)
        {
            Vector2 stick = _input.Attitude;
            float maxTilt = config.maxTiltDeg;

            // Цільова орієнтація: поточний курс + нахил від стіка.
            // +X локально = ніс вниз (рух уперед), −Z = крен вправо.
            float yawDeg = rot.eulerAngles.y;
            Quaternion target = Quaternion.Euler(0f, yawDeg, 0f)
                              * Quaternion.Euler(stick.y * maxTilt, 0f, -stick.x * maxTilt);

            // Поворот, який переводить поточну орієнтацію в цільову → помилка по осях у радіанах.
            Quaternion delta = target * invRot;
            delta.ToAngleAxis(out float angleDeg, out Vector3 axisWorld);
            if (angleDeg > 180f) angleDeg -= 360f;

            Vector3 errLocal = Vector3.zero;
            if (Mathf.Abs(angleDeg) > 0.01f && IsFinite(axisWorld))
            {
                errLocal = invRot * (axisWorld * (angleDeg * Mathf.Deg2Rad));
            }

            // D-складова по виміряній кутовій швидкості — демпфування без ривків від стіка.
            return new Vector3(
                _pitchAnglePid.UpdateWithRate(errLocal.x, angVelLocal.x, dt),
                0f,
                _rollAnglePid.UpdateWithRate(errLocal.z, angVelLocal.z, dt));
        }

        private Vector3 AcroAttitude(Vector3 angVelLocal, float dt)
        {
            Vector2 stick = _input.Attitude;
            float maxRate = config.maxRateDeg * Mathf.Deg2Rad;

            float targetPitchRate = stick.y * maxRate;
            float targetRollRate = -stick.x * maxRate;

            return new Vector3(
                _pitchRatePid.Update(targetPitchRate - angVelLocal.x, dt),
                0f,
                _rollRatePid.Update(targetRollRate - angVelLocal.z, dt));
        }

        // ---------- Сервісне ----------

        public void SetMode(FlightMode newMode)
        {
            mode = newMode;
            ResetPids();
        }

        public void ResetDrone()
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.position = _spawn.position;
            _rb.rotation = _spawn.rotation;
            transform.SetPositionAndRotation(_spawn.position, _spawn.rotation);
            ResetPids();
        }

        private void ResetPids()
        {
            _pitchAnglePid.Reset();
            _rollAnglePid.Reset();
            _pitchRatePid.Reset();
            _rollRatePid.Reset();
            _yawRatePid.Reset();
            _climbPid.Reset();
        }

        private void SyncPidSettings()
        {
            // Дешево, а дозволяє тюнити DroneConfig прямо під час польоту.
            _pitchAnglePid.SetSettings(config.anglePid);
            _rollAnglePid.SetSettings(config.anglePid);
            _pitchRatePid.SetSettings(config.ratePid);
            _rollRatePid.SetSettings(config.ratePid);
            _yawRatePid.SetSettings(config.yawRatePid);
            _climbPid.SetSettings(config.climbPid);
        }

        private void DrawDebug()
        {
            // Вектори в одиницях «g», щоб були порівнянні між собою.
            float weight = config.mass * config.Gravity;
            Vector3 p = _rb.position;
            Debug.DrawRay(p, LastThrustForce / weight, Color.green);
            Debug.DrawRay(p, LastDragForce / weight * 5f, Color.red);
            Debug.DrawRay(p, _rb.linearVelocity * 0.1f, Color.cyan);
        }

        private static bool IsFinite(Vector3 v)
        {
            return !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z)
                  || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
        }
    }
}
