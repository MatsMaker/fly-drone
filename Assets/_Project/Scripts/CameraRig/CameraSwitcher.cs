using FlyDrone.Drone;
using UnityEngine;

namespace FlyDrone.CameraRig
{
    /// <summary>
    /// CinemachineBrain обирає активну камеру з найвищим пріоритетом,
    /// тому достатньо вмикати одну й вимикати решту.
    /// </summary>
    public class CameraSwitcher : MonoBehaviour
    {
        [SerializeField] private DroneInput input;
        [SerializeField] private GameObject[] cameras;

        private int _index;

        private void Start() => Apply();

        private void Update()
        {
            if (!input.ToggleCameraPressed || cameras.Length == 0) return;

            _index = (_index + 1) % cameras.Length;
            Apply();
        }

        private void Apply()
        {
            for (int i = 0; i < cameras.Length; i++)
            {
                cameras[i].SetActive(i == _index);
            }
        }
    }
}
