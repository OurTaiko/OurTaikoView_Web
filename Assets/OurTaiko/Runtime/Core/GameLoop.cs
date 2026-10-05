using UnityEngine;

namespace OurTaiko
{
    // Publish the frame's time before collecting input and updating scenes.
    [DefaultExecutionOrder(-32000)]
    sealed class GameLoop : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Install()
        {
            InputManager.Initialize();
            GameTimeline.UpdateFrame();
            if (FindFirstObjectByType<GameLoop>() != null) return;
            var root = new GameObject(nameof(GameLoop)) { hideFlags = HideFlags.HideInHierarchy };
            DontDestroyOnLoad(root);
            root.AddComponent<GameLoop>();
        }

        void Update()
        {
            GameTimeline.UpdateFrame();
            InputManager.OnPreUpdate();
        }
    }
}
