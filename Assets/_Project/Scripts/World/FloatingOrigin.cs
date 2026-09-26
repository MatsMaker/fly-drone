using System;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FlyDrone.World
{
    /// <summary>
    /// Коли ціль відлітає далі порогу від (0,0,0), зсуває весь світ назад до початку координат.
    /// float має ~7 значущих цифр: на 10 км крок координати вже ~1 мм,
    /// і фізика, камера та тіні починають тремтіти.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public class FloatingOrigin : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [Tooltip("Поріг зсуву, м. Для демонстрації — 2 км; у реальній грі зазвичай 5–10 км")]
        [SerializeField] private float threshold = 2000f;

        private readonly List<GameObject> _roots = new List<GameObject>();

        /// <summary>Сумарний зсув — справжня позиція = локальна − TotalOffset.</summary>
        public static Vector3 TotalOffset { get; private set; }

        public static event Action<Vector3> Shifted;

        private void Awake() => TotalOffset = Vector3.zero;

        private void LateUpdate()
        {
            Vector3 p = target.position;
            p.y = 0f; // висоту не чіпаємо: вона й так невелика
            if (p.sqrMagnitude < threshold * threshold) return;

            Shift(-p);
        }

        private void Shift(Vector3 delta)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;

                scene.GetRootGameObjects(_roots);
                foreach (GameObject root in _roots)
                {
                    // Screen Space Overlay Canvas живе в координатах екрана — його не рухаємо.
                    if (root.TryGetComponent(out Canvas _)) continue;
                    root.transform.position += delta;
                }
            }

            // Rigidbody мають одразу дізнатися про нові позиції трансформів.
            Physics.SyncTransforms();

            // Terrain кешує дані дерев (LODGroup-префаб) і не оновлює їх при зсуві трансформа:
            // без Flush лишаються «пеньки» — крона зникає, стовбур обрізаний.
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                terrain.Flush();
            }

            // Камери з демпфуванням інакше «поїдуть» за стрибком цілі.
            foreach (CinemachineCamera cam in FindObjectsByType<CinemachineCamera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                cam.OnTargetObjectWarped(target, delta);
            }

            TotalOffset += delta;
            Shifted?.Invoke(delta);
            Debug.Log($"[FloatingOrigin] Світ зсунуто на {delta}. Сумарний зсув {TotalOffset}");
        }
    }
}
