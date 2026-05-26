using System;
using System.Globalization;
using THUAI9.Unity.Playback;
using UnityEngine;

namespace THUAI9.Unity.WebGL
{
    public sealed class LegacyPlaybackInputManager : MonoBehaviour
    {
        public const string LegacyObjectName = "InputManager";
        private const string EmptyPlaybackUrlStatus = "\u72b6\u6001\uff1a\u5728\u7ebf\u56de\u653e\u5730\u5740\u4e3a\u7a7a";
        private static LegacyPlaybackInputManager instance;
        private PlaybackController playbackController;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => GetOrCreate();

        public static LegacyPlaybackInputManager GetOrCreate()
        {
            if (instance != null) return instance;
            instance = FindObjectOfType<LegacyPlaybackInputManager>();
            if (instance != null)
            {
                instance.gameObject.name = LegacyObjectName;
                return instance;
            }

            GameObject go = GameObject.Find(LegacyObjectName) ?? new GameObject(LegacyObjectName);
            instance = go.GetComponent<LegacyPlaybackInputManager>() ?? go.AddComponent<LegacyPlaybackInputManager>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }

            instance = this;
            gameObject.name = LegacyObjectName;
            DontDestroyOnLoad(gameObject);
            RefreshReferences();
        }

        public void AfterInputPlaySpeed(string speed)
        {
            if (!float.TryParse(speed, NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
            {
                Debug.LogWarning($"Ignoring invalid legacy playback speed: {speed}");
                return;
            }

            WithPlaybackController(controller => controller.SetSpeed(value));
        }

        public void AfterInputFilename(string filenameOrUrl)
        {
            if (string.IsNullOrWhiteSpace(filenameOrUrl))
            {
                WithPlaybackController(controller => controller.SetStatusMessage(EmptyPlaybackUrlStatus));
                return;
            }

            string trimmed = filenameOrUrl.Trim().Trim('"');
            WithPlaybackController(controller => controller.LoadPlaybackFile(trimmed));
        }

        private void WithPlaybackController(Action<PlaybackController> action)
        {
            RefreshReferences();
            if (playbackController == null)
            {
                Debug.LogWarning("Legacy playback InputManager could not find PlaybackController.");
                return;
            }

            action(playbackController);
        }

        private void RefreshReferences()
        {
            playbackController ??= FindObjectOfType<PlaybackController>();
        }
    }
}
