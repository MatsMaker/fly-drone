using UnityEngine;
using UnityEngine.InputSystem;

namespace FlyDrone.Drone
{
    /// <summary>
    /// Читає ввід раніше за інші скрипти, щоб прапорці «натиснуто в цьому кадрі»
    /// спрацьовували рівно один раз.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class DroneInput : MonoBehaviour
    {
        [SerializeField, Range(0f, 0.3f)] private float deadzone = 0.05f;
        [SerializeField, Range(0f, 1f)] private float expo = 0.3f;

        private InputAction _attitude;
        private InputAction _vertical;
        private InputAction _yaw;
        private InputAction _toggleMode;
        private InputAction _toggleCamera;
        private InputAction _reset;

        /// <summary>x — крен (roll), y — тангаж (pitch), -1..1.</summary>
        public Vector2 Attitude { get; private set; }
        /// <summary>Газ / набір висоти, -1..1.</summary>
        public float Vertical { get; private set; }
        public float Yaw { get; private set; }

        public bool ToggleModePressed { get; private set; }
        public bool ToggleCameraPressed { get; private set; }
        public bool ResetPressed { get; private set; }

        private bool _hasOverride;
        private Vector2 _overrideAttitude;
        private float _overrideVertical;
        private float _overrideYaw;

        /// <summary>
        /// «Шов» для автотестів і автопілота: підміняє стіки програмними значеннями.
        /// </summary>
        public void SetOverride(Vector2 attitude, float vertical, float yaw)
        {
            _hasOverride = true;
            _overrideAttitude = attitude;
            _overrideVertical = vertical;
            _overrideYaw = yaw;

            // Одразу, щоб FixedUpdate до першого Update вже бачив нові значення.
            Attitude = attitude;
            Vertical = vertical;
            Yaw = yaw;
        }

        public void ClearOverride() => _hasOverride = false;

        private void Awake()
        {
            // Mode 2: лівий стік — газ і yaw, правий — pitch і roll.
            _attitude = new InputAction("Attitude", InputActionType.Value, expectedControlType: "Vector2");
            _attitude.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            _attitude.AddBinding("<Gamepad>/rightStick");

            _vertical = new InputAction("Vertical", InputActionType.Value, expectedControlType: "Axis");
            _vertical.AddCompositeBinding("1DAxis")
                .With("Positive", "<Keyboard>/space")
                .With("Negative", "<Keyboard>/leftCtrl");
            _vertical.AddBinding("<Gamepad>/leftStick/y");

            _yaw = new InputAction("Yaw", InputActionType.Value, expectedControlType: "Axis");
            _yaw.AddCompositeBinding("1DAxis")
                .With("Positive", "<Keyboard>/e")
                .With("Negative", "<Keyboard>/q");
            _yaw.AddBinding("<Gamepad>/leftStick/x");

            _toggleMode = new InputAction("ToggleMode", InputActionType.Button);
            _toggleMode.AddBinding("<Keyboard>/m");
            _toggleMode.AddBinding("<Gamepad>/buttonNorth");

            _toggleCamera = new InputAction("ToggleCamera", InputActionType.Button);
            _toggleCamera.AddBinding("<Keyboard>/c");
            _toggleCamera.AddBinding("<Gamepad>/buttonWest");

            _reset = new InputAction("Reset", InputActionType.Button);
            _reset.AddBinding("<Keyboard>/r");
            _reset.AddBinding("<Gamepad>/start");
        }

        private void OnEnable()
        {
            _attitude.Enable();
            _vertical.Enable();
            _yaw.Enable();
            _toggleMode.Enable();
            _toggleCamera.Enable();
            _reset.Enable();
        }

        private void OnDisable()
        {
            _attitude.Disable();
            _vertical.Disable();
            _yaw.Disable();
            _toggleMode.Disable();
            _toggleCamera.Disable();
            _reset.Disable();
        }

        private void OnDestroy()
        {
            _attitude.Dispose();
            _vertical.Dispose();
            _yaw.Dispose();
            _toggleMode.Dispose();
            _toggleCamera.Dispose();
            _reset.Dispose();
        }

        private void Update()
        {
            if (_hasOverride)
            {
                Attitude = _overrideAttitude;
                Vertical = _overrideVertical;
                Yaw = _overrideYaw;
            }
            else
            {
                Vector2 a = _attitude.ReadValue<Vector2>();
                Attitude = new Vector2(Shape(a.x), Shape(a.y));
                Vertical = Shape(_vertical.ReadValue<float>());
                Yaw = Shape(_yaw.ReadValue<float>());
            }

            ToggleModePressed = _toggleMode.WasPressedThisFrame();
            ToggleCameraPressed = _toggleCamera.WasPressedThisFrame();
            ResetPressed = _reset.WasPressedThisFrame();
        }

        /// <summary>Мертва зона + експоненційна крива: точність біля центру, повний хід на краях.</summary>
        private float Shape(float x)
        {
            float abs = Mathf.Abs(x);
            if (abs < deadzone) return 0f;

            float n = (abs - deadzone) / (1f - deadzone);
            float curved = (1f - expo) * n + expo * n * n * n;
            return Mathf.Sign(x) * curved;
        }
    }
}
