using UnityEngine;

namespace FlyDrone.Drone
{
    [CreateAssetMenu(fileName = "DroneConfig", menuName = "FlyDrone/Drone Config")]
    public class DroneConfig : ScriptableObject
    {
        [Header("Маса й тяга")]
        [Min(0.1f)] public float mass = 1.2f;

        [Tooltip("Максимальна тяга / вага. При 2.5 дрон висить на 40% газу.")]
        [Range(1.2f, 6f)] public float thrustToWeight = 2.5f;

        [Header("Опір повітря (квадратичний)")]
        public float airDensity = 1.225f;

        [Tooltip("Cd по локальних осях: X — бік, Y — верх/низ, Z — фронт")]
        public Vector3 dragCoefficient = new Vector3(1.0f, 1.2f, 0.9f);

        [Tooltip("Площа проєкції, м², по тих самих осях")]
        public Vector3 referenceArea = new Vector3(0.03f, 0.06f, 0.03f);

        [Tooltip("Rigidbody.angularDamping — трохи гасить обертання в Acro")]
        public float angularDamping = 0.5f;

        [Header("Режим Stabilized (Angle)")]
        [Range(5f, 60f)] public float maxTiltDeg = 35f;
        [Tooltip("м/с при повністю відхиленому стіку газу")]
        public float maxClimbRate = 5f;
        public PidSettings anglePid = new PidSettings(60f, 0f, 12f);
        public PidSettings climbPid = new PidSettings(3f, 0.5f, 0f, 2f);

        [Header("Режим Acro (Rate)")]
        public float maxRateDeg = 360f;
        public PidSettings ratePid = new PidSettings(20f, 0f, 0f);

        [Header("Yaw (обидва режими)")]
        public float maxYawRateDeg = 180f;
        public PidSettings yawRatePid = new PidSettings(8f, 0f, 0f);

        [Header("Обмеження")]
        [Tooltip("Максимальне кутове прискорення по кожній осі, рад/с²")]
        public float maxAngularAccel = 80f;

        public float Gravity => -Physics.gravity.y;

        /// <summary>Максимальна тяга в ньютонах.</summary>
        public float MaxThrust => mass * Gravity * thrustToWeight;

        /// <summary>Газ (0..1), за якого тяга дорівнює вазі.</summary>
        public float HoverThrottle => 1f / thrustToWeight;

        /// <summary>k = ½·ρ·Cd·A по кожній локальній осі. Сила опору F = −k·v·|v|.</summary>
        public Vector3 DragK => 0.5f * airDensity * Vector3.Scale(dragCoefficient, referenceArea);

        [Header("Оберти пропелерів")]
        [SerializeField] public readonly float idleRpm = 0f;
        [SerializeField] public float maxRpm = 9000f;
        [Tooltip("RPM за секунду — інерція мотора при розкрутці/гальмуванні")]
        [SerializeField] public float rpmResponse = 25000f;
    }
}
