using System;
using UnityEngine;

namespace FlyDrone.Drone
{
    [Serializable]
    public struct PidSettings
    {
        public float kp;
        public float ki;
        public float kd;
        [Tooltip("Межа накопиченого інтегралу (анти-windup)")]
        public float integralLimit;

        public PidSettings(float kp, float ki, float kd, float integralLimit = 1f)
        {
            this.kp = kp;
            this.ki = ki;
            this.kd = kd;
            this.integralLimit = integralLimit;
        }
    }

    /// <summary>
    /// Звичайний C#-клас без залежності від MonoBehaviour — легко покрити EditMode-тестами.
    /// </summary>
    public class PidController
    {
        private PidSettings _s;
        private float _integral;
        private float _prevError;
        private bool _hasPrev;

        public PidController(PidSettings settings) => _s = settings;

        /// <summary>Дозволяє тюнити коефіцієнти в інспекторі прямо в Play Mode.</summary>
        public void SetSettings(PidSettings settings) => _s = settings;

        public void Reset()
        {
            _integral = 0f;
            _prevError = 0f;
            _hasPrev = false;
        }

        /// <summary>Похідна по помилці. Підходить для контурів швидкості.</summary>
        public float Update(float error, float dt)
        {
            float derivative = _hasPrev && dt > 0f ? (error - _prevError) / dt : 0f;
            _prevError = error;
            _hasPrev = true;
            return Compute(error, derivative, dt);
        }

        /// <summary>
        /// Похідна по виміру: передаємо швидкість зміни виміряної величини (напр. кутову швидкість).
        /// Немає «удару» D-складової, коли пілот різко смикає стік.
        /// </summary>
        public float UpdateWithRate(float error, float measurementRate, float dt)
        {
            return Compute(error, -measurementRate, dt);
        }

        private float Compute(float error, float errorDerivative, float dt)
        {
            if (_s.ki != 0f)
            {
                _integral = Mathf.Clamp(_integral + error * dt, -_s.integralLimit, _s.integralLimit);
            }

            return _s.kp * error + _s.ki * _integral + _s.kd * errorDerivative;
        }
    }
}
