using System;
using Google.Protobuf;
using Protobuf;
using THUAI9.Unity.Live;
using THUAI9.Unity.Playback;
using UnityEngine;

namespace THUAI9.Unity.WebGL
{
    /// <summary>
    /// Stable browser-to-Unity ingress for THUAI9 WebGL pages.
    ///
    /// WebGL must not open the native gRPC client directly. The hosting page
    /// owns browser networking and pushes protobuf frames or playback URLs into
    /// this bridge; Unity keeps rendering through FrameSourceHub via the existing
    /// LiveSpectatorClient and PlaybackController paths.
    /// </summary>
    public class WebGLFrameBridge : MonoBehaviour
    {
        public const string BridgeObjectName = "WebGLFrameBridge";

        private static WebGLFrameBridge instance;
        private PlaybackController playbackController;
        private LiveSpectatorClient liveClient;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void THUAI9_SelectPlaybackFile(string gameObjectName, string callbackName);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void THUAI9_NotifyUnityReady(string gameObjectName);

        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void THUAI9_DispatchUnityEvent(string eventName, string payload);
#endif

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            GetOrCreate();
        }

        public static WebGLFrameBridge GetOrCreate()
        {
            if (instance != null)
            {
                return instance;
            }

            instance = FindObjectOfType<WebGLFrameBridge>();
            if (instance != null)
            {
                return instance;
            }

            GameObject bridgeObject = GameObject.Find(BridgeObjectName) ?? new GameObject(BridgeObjectName);
            instance = bridgeObject.AddComponent<WebGLFrameBridge>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            gameObject.name = BridgeObjectName;
            DontDestroyOnLoad(gameObject);
            RefreshReferences();
        }

        private void Start()
        {
            NotifyReady();
        }

        public void RequestPlaybackFile()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            THUAI9_SelectPlaybackFile(gameObject.name, nameof(SetPlaybackFile));
#else
            Debug.Log("WebGL playback file picker is only available in browser builds.");
#endif
        }

        public void SetPlaybackFile(string payload)
        {
            PlaybackSelection selection = ParsePlaybackSelection(payload);
            if (selection == null || string.IsNullOrWhiteSpace(selection.url))
            {
                Debug.LogWarning("WebGL playback payload did not include a URL.");
                DispatchEvent("playback-error", "missing-url");
                return;
            }

            RefreshReferences();
            if (playbackController == null)
            {
                Debug.LogWarning("PlaybackController not found for WebGL playback load.");
                DispatchEvent("playback-error", "missing-playback-controller");
                return;
            }

            liveClient?.StopLive();
            playbackController.LoadPlaybackUrl(selection.url, selection.name);
            DispatchEvent("playback-loading", selection.name ?? selection.url);
        }

        public void SetPlaybackUrl(string url)
        {
            SetPlaybackFile(url);
        }

        public void LoadPlaybackUrl(string url)
        {
            SetPlaybackFile(url);
        }

        public void LoadPlaybackBase64(string payload)
        {
            PlaybackDataSelection selection = ParsePlaybackDataSelection(payload);
            if (selection == null || string.IsNullOrWhiteSpace(selection.data))
            {
                Debug.LogWarning("WebGL playback base64 payload was empty.");
                DispatchEvent("playback-error", "missing-base64-data");
                return;
            }

            try
            {
                RefreshReferences();
                if (playbackController == null)
                {
                    Debug.LogWarning("PlaybackController not found for WebGL playback base64 load.");
                    DispatchEvent("playback-error", "missing-playback-controller");
                    return;
                }

                byte[] bytes = Convert.FromBase64String(selection.data);
                playbackController.LoadPlaybackBytes(bytes, selection.name);
                liveClient?.StopLive();
                DispatchEvent("playback-loading", selection.name ?? "base64 playback");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to load WebGL playback base64 data ({ex.GetType().Name}).");
                DispatchEvent("playback-error", ex.Message);
            }
        }

        public void StartWebLive(string sourceName)
        {
            RefreshReferences();
            if (liveClient == null)
            {
                liveClient = new GameObject("LiveSpectatorClient").AddComponent<LiveSpectatorClient>();
            }

            playbackController?.Stop();
            liveClient.StartExternalLive(string.IsNullOrWhiteSpace(sourceName) ? "WebGL Live" : sourceName);
            DispatchEvent("live-started", sourceName ?? "WebGL Live");
        }

        public void StopWebLive(string ignored = null)
        {
            RefreshReferences();
            liveClient?.StopLive();
            DispatchEvent("live-stopped", string.Empty);
        }

        public void SubmitLiveFrameBase64(string base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
            {
                return;
            }

            try
            {
                MessageToClient message = MessageToClient.Parser.ParseFrom(Convert.FromBase64String(base64));
                SubmitLiveFrame(message, "WebGL Live");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to parse WebGL live frame base64 ({ex.GetType().Name}).");
                DispatchEvent("live-frame-error", ex.Message);
            }
        }

        public void UpdateMessageByBase64(string base64)
        {
            SubmitLiveFrameBase64(base64);
        }

        public void SubmitLiveFrameJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            try
            {
                MessageToClient message = JsonParser.Default.Parse<MessageToClient>(json);
                SubmitLiveFrame(message, "WebGL Live JSON");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to parse WebGL live frame JSON ({ex.GetType().Name}).");
                DispatchEvent("live-frame-error", ex.Message);
            }
        }

        public void UpdateMessageByJson(string json)
        {
            SubmitLiveFrameJson(json);
        }

        private void SubmitLiveFrame(MessageToClient message, string sourceName)
        {
            RefreshReferences();
            if (liveClient == null)
            {
                liveClient = new GameObject("LiveSpectatorClient").AddComponent<LiveSpectatorClient>();
            }

            playbackController?.Stop();
            if (liveClient.SubmitExternalLiveFrame(message, sourceName))
            {
                DispatchEvent("live-frame", liveClient.ReceivedFrameCount.ToString());
            }
        }

        private void RefreshReferences()
        {
            playbackController ??= FindObjectOfType<PlaybackController>();
            liveClient ??= FindObjectOfType<LiveSpectatorClient>();
        }

        private void NotifyReady()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            THUAI9_NotifyUnityReady(gameObject.name);
#endif
        }

        private static PlaybackSelection ParsePlaybackSelection(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            string trimmed = payload.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                return new PlaybackSelection { url = trimmed.Trim('"'), name = PlaybackController.IsPlaybackUrl(trimmed) ? null : trimmed };
            }

            try
            {
                return JsonUtility.FromJson<PlaybackSelection>(trimmed);
            }
            catch
            {
                return new PlaybackSelection { url = trimmed };
            }
        }

        private static PlaybackDataSelection ParsePlaybackDataSelection(string payload)
        {
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            string trimmed = payload.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                return new PlaybackDataSelection { data = trimmed };
            }

            try
            {
                return JsonUtility.FromJson<PlaybackDataSelection>(trimmed);
            }
            catch
            {
                return new PlaybackDataSelection { data = trimmed };
            }
        }

        private static void DispatchEvent(string eventName, string payload)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            THUAI9_DispatchUnityEvent(eventName, payload ?? string.Empty);
#endif
        }

        [Serializable]
        private class PlaybackSelection
        {
            public string url;
            public string name;
        }

        [Serializable]
        private class PlaybackDataSelection
        {
            public string data;
            public string name;
        }
    }
}
