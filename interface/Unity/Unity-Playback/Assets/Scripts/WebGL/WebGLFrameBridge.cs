using System;
using THUAI9.Unity.Playback;
using THUAI9.Unity.UI;
using UnityEngine;

namespace THUAI9.Unity.WebGL
{
    public class WebGLFrameBridge : MonoBehaviour
    {
        public const string BridgeObjectName = "WebGLFrameBridge";
        private static WebGLFrameBridge instance;
        private PlaybackController playbackController;
        private UIController uiController;
        private string lastStatusPayload;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void THUAI9_SelectPlaybackFile(string gameObjectName, string callbackName);
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void THUAI9_NotifyUnityReady(string gameObjectName);
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void THUAI9_DispatchUnityEvent(string eventName, string payload);
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap() => GetOrCreate();

        public static WebGLFrameBridge GetOrCreate()
        {
            if (instance != null) return instance;
            instance = FindObjectOfType<WebGLFrameBridge>();
            if (instance != null) return instance;
            GameObject go = GameObject.Find(BridgeObjectName) ?? new GameObject(BridgeObjectName);
            instance = go.AddComponent<WebGLFrameBridge>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            gameObject.name = BridgeObjectName;
            DontDestroyOnLoad(gameObject);
            RefreshReferences();
        }

        private void Start() => NotifyReady();
        private void RefreshReferences()
        {
            playbackController ??= FindObjectOfType<PlaybackController>();
            uiController ??= FindObjectOfType<UIController>();
        }

        private void Update()
        {
            RefreshReferences();
            DispatchPlaybackStatusIfChanged();
        }

        public void RequestPlaybackFile()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            THUAI9_SelectPlaybackFile(gameObject.name, nameof(SetPlaybackFile));
#else
            Debug.Log("Browser playback file picker is only available in WebGL.");
#endif
        }

        public void SetPlaybackFile(string payload)
        {
            PlaybackSelection selection = ParsePlaybackSelection(payload);
            if (selection == null || string.IsNullOrWhiteSpace(selection.url)) { DispatchEvent("playback-error", "missing-url"); return; }
            RefreshReferences();
            if (playbackController == null) { DispatchEvent("playback-error", "missing-playback-controller"); return; }
            uiController?.SetPlaybackPathDisplay(selection.name ?? selection.url);
            DispatchEvent("playback-loading", selection.name ?? selection.url);
            if (selection.size > PlaybackController.MaxRemotePlaybackBytes)
            {
                string status = $"状态：回放文件过大（{selection.size} 字节）";
                playbackController.RejectPlaybackLoad(selection.url, selection.name, status);
                DispatchEvent("playback-error", $"file-too-large:{selection.size}");
                return;
            }

            playbackController.LoadPlaybackUrl(selection.url, selection.name);
        }

        public void SetPlaybackUrl(string url) => SetPlaybackFile(url);
        public void LoadPlaybackUrl(string url) => SetPlaybackFile(url);

        public void LoadPlaybackBase64(string payload)
        {
            PlaybackDataSelection selection = ParsePlaybackDataSelection(payload);
            if (selection == null || string.IsNullOrWhiteSpace(selection.data)) { DispatchEvent("playback-error", "missing-base64-data"); return; }
            try
            {
                RefreshReferences();
                if (playbackController == null) { DispatchEvent("playback-error", "missing-playback-controller"); return; }
                byte[] data;
                if (PlaybackController.TryDecodeDataUrl(selection.data, out byte[] dataUrlBytes))
                {
                    data = dataUrlBytes;
                }
                else
                {
                    string normalizedBase64 = NormalizeBase64Payload(selection.data);
                    int estimatedBytes = EstimateBase64ByteCount(normalizedBase64);
                    if (estimatedBytes > PlaybackController.MaxWebGLBase64Bytes)
                    {
                        DispatchEvent("playback-error", $"base64-too-large:{estimatedBytes}");
                        return;
                    }

                    data = Convert.FromBase64String(normalizedBase64);
                }

                if (data.Length > PlaybackController.MaxWebGLBase64Bytes)
                {
                    DispatchEvent("playback-error", $"base64-too-large:{data.Length}");
                    return;
                }

                DispatchEvent("playback-loading", selection.name ?? "base64 playback");
                uiController?.SetPlaybackPathDisplay(selection.name ?? "base64 playback");
                playbackController.LoadPlaybackBytes(data, selection.name);
            }
            catch (Exception ex) { DispatchEvent("playback-error", ex.Message); }
        }

        public void PlayPlayback(string _ = null) => WithPlaybackController(controller => controller.Play());
        public void PausePlayback(string _ = null) => WithPlaybackController(controller => controller.Pause());
        public void TogglePlayback(string _ = null) => WithPlaybackController(controller => controller.TogglePlayPause());
        public void StopPlayback(string _ = null) => WithPlaybackController(controller => controller.Stop());
        public void StepPlaybackForward(string _ = null) => WithPlaybackController(controller => controller.StepForward());
        public void StepPlaybackBackward(string _ = null) => WithPlaybackController(controller => controller.StepBackward());

        public void SeekPlaybackFrame(string frame)
        {
            if (!int.TryParse(frame, out int index))
            {
                DispatchEvent("playback-error", "invalid-frame-index");
                return;
            }

            WithPlaybackController(controller => controller.SeekToFrame(index));
        }

        public void SetPlaybackSpeed(string speed)
        {
            if (!float.TryParse(speed, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float value))
            {
                DispatchEvent("playback-error", "invalid-speed");
                return;
            }

            WithPlaybackController(controller => controller.SetSpeed(value));
        }

        private void WithPlaybackController(Action<PlaybackController> action)
        {
            RefreshReferences();
            if (playbackController == null)
            {
                DispatchEvent("playback-error", "missing-playback-controller");
                return;
            }

            action(playbackController);
            DispatchPlaybackStatus(force: true);
        }

        private void NotifyReady()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            THUAI9_NotifyUnityReady(gameObject.name);
#endif
        }

        private static void DispatchEvent(string eventName, string payload)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            THUAI9_DispatchUnityEvent(eventName, payload ?? string.Empty);
#endif
        }

        private void DispatchPlaybackStatusIfChanged()
        {
            if (playbackController == null)
            {
                return;
            }

            DispatchPlaybackStatus(force: false);
        }

        private void DispatchPlaybackStatus(bool force)
        {
            if (playbackController == null)
            {
                return;
            }

            string payload = JsonUtility.ToJson(new PlaybackStatus
            {
                loaded = playbackController.PlaybackLoaded,
                isPlaying = playbackController.isPlaying,
                isPaused = playbackController.isPaused,
                currentFrameIndex = playbackController.CurrentFrameIndex,
                totalFrameCount = playbackController.TotalFrameCount,
                elapsedMilliseconds = playbackController.CurrentPlaybackTimeMs,
                speed = playbackController.playSpeed,
                statusText = playbackController.StatusText
            });

            if (!force && payload == lastStatusPayload)
            {
                return;
            }

            lastStatusPayload = payload;
            DispatchEvent("playback-status", payload);
        }

        private static PlaybackSelection ParsePlaybackSelection(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;
            string trimmed = payload.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return new PlaybackSelection { url = trimmed.Trim('"'), name = trimmed.Trim('"') };
            try { return JsonUtility.FromJson<PlaybackSelection>(trimmed); } catch { return new PlaybackSelection { url = trimmed }; }
        }

        private static PlaybackDataSelection ParsePlaybackDataSelection(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload)) return null;
            string trimmed = payload.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal)) return new PlaybackDataSelection { data = trimmed };
            try { return JsonUtility.FromJson<PlaybackDataSelection>(trimmed); } catch { return new PlaybackDataSelection { data = trimmed }; }
        }

        private static int EstimateBase64ByteCount(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return 0;
            string trimmed = base64.Trim();
            int padding = 0;
            if (trimmed.EndsWith("==", StringComparison.Ordinal)) padding = 2;
            else if (trimmed.EndsWith("=", StringComparison.Ordinal)) padding = 1;
            return Math.Max(0, trimmed.Length / 4 * 3 - padding);
        }

        private static string NormalizeBase64Payload(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                return string.Empty;
            }

            return base64
                .Trim()
                .Replace(" ", "+")
                .Replace("\r", string.Empty)
                .Replace("\n", string.Empty)
                .Replace("\t", string.Empty);
        }

        [Serializable] private class PlaybackSelection { public string url; public string name; public long size; }
        [Serializable] private class PlaybackDataSelection { public string data; public string name; }
        [Serializable]
        private class PlaybackStatus
        {
            public bool loaded;
            public bool isPlaying;
            public bool isPaused;
            public int currentFrameIndex;
            public int totalFrameCount;
            public int elapsedMilliseconds;
            public float speed;
            public string statusText;
        }
    }
}
