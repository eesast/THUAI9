using System;
using Google.Protobuf;
using Protobuf;
using THUAI9.Unity.Live;
using UnityEngine;

namespace THUAI9.Unity.WebGL
{
    public class WebGLFrameBridge : MonoBehaviour
    {
        public const string BridgeObjectName = "WebGLFrameBridge";
        private static WebGLFrameBridge instance;
        private LiveSpectatorClient liveClient;

#if UNITY_WEBGL && !UNITY_EDITOR
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
        private void Awake(){ if(instance!=null&&instance!=this){Destroy(gameObject);return;} instance=this; gameObject.name=BridgeObjectName; DontDestroyOnLoad(gameObject); RefreshReferences(); }
        private void Start() => NotifyReady();
        private void RefreshReferences(){ liveClient ??= FindObjectOfType<LiveSpectatorClient>(); if(liveClient==null) liveClient=new GameObject("LiveSpectatorClient").AddComponent<LiveSpectatorClient>(); }
        public void StartWebLive(string sourceName){ RefreshReferences(); liveClient.StartExternalLive(string.IsNullOrWhiteSpace(sourceName)?"WebGL Live":sourceName); DispatchEvent("live-started", sourceName ?? "WebGL Live"); }
        public void StopWebLive(string ignored=null){ RefreshReferences(); liveClient.StopLive(); DispatchEvent("live-stopped", string.Empty); }
        public void SubmitLiveFrameBase64(string base64){ if(string.IsNullOrWhiteSpace(base64))return; try{SubmitLiveFrame(MessageToClient.Parser.ParseFrom(Convert.FromBase64String(base64)),"WebGL Live");}catch(Exception ex){DispatchEvent("live-frame-error",ex.Message);} }
        public void UpdateMessageByBase64(string base64) => SubmitLiveFrameBase64(base64);
        public void SubmitLiveFrameJson(string json){ if(string.IsNullOrWhiteSpace(json))return; try{SubmitLiveFrame(JsonParser.Default.Parse<MessageToClient>(json),"WebGL Live JSON");}catch(Exception ex){DispatchEvent("live-frame-error",ex.Message);} }
        public void UpdateMessageByJson(string json) => SubmitLiveFrameJson(json);
        private void SubmitLiveFrame(MessageToClient message,string sourceName){ RefreshReferences(); if(liveClient.SubmitExternalLiveFrame(message,sourceName)) DispatchEvent("live-frame", liveClient.ReceivedFrameCount.ToString()); }
        private void NotifyReady()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            THUAI9_NotifyUnityReady(gameObject.name);
#endif
        }
        private static void DispatchEvent(string eventName,string payload)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            THUAI9_DispatchUnityEvent(eventName,payload??string.Empty);
#endif
        }
    }
}
