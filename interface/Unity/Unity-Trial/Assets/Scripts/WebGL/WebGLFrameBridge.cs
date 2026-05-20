using THUAI9.Unity.Player;
using UnityEngine;

namespace THUAI9.Unity.WebGL
{
    public class WebGLFrameBridge : MonoBehaviour
    {
        public const string BridgeObjectName = "WebGLFrameBridge";
        private static WebGLFrameBridge instance;
        private TrialSandboxController trial;

#if UNITY_WEBGL && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void THUAI9_NotifyUnityReady(string gameObjectName);
        [System.Runtime.InteropServices.DllImport("__Internal")]
        private static extern void THUAI9_DispatchUnityEvent(string eventName, string payload);
#endif
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)] private static void Bootstrap() => GetOrCreate();
        public static WebGLFrameBridge GetOrCreate(){ if(instance!=null)return instance; instance=FindObjectOfType<WebGLFrameBridge>(); if(instance!=null)return instance; GameObject go=GameObject.Find(BridgeObjectName)??new GameObject(BridgeObjectName); instance=go.AddComponent<WebGLFrameBridge>(); return instance; }
        private void Awake(){ if(instance!=null&&instance!=this){Destroy(gameObject);return;} instance=this; gameObject.name=BridgeObjectName; DontDestroyOnLoad(gameObject); RefreshReferences(); }
        private void Start() => NotifyReady();
        private void RefreshReferences(){ trial ??= FindObjectOfType<TrialSandboxController>(); if(trial==null) trial=new GameObject("TrialSandboxController").AddComponent<TrialSandboxController>(); }
        public void StartTrial(string optionsJson=null){ RefreshReferences(); trial.StartTrial(optionsJson); DispatchEvent("trial-started", optionsJson ?? string.Empty); DispatchEvent("trial-status", trial.StatusText); }
        public void StopTrial(string ignored=null){ RefreshReferences(); trial.StopTrial(); DispatchEvent("trial-stopped", string.Empty); DispatchEvent("trial-status", trial.StatusText); }
        public void ResetTrial(string ignored=null){ RefreshReferences(); trial.ResetTrial(); DispatchEvent("trial-reset", string.Empty); DispatchEvent("trial-status", trial.StatusText); }
        public void TrialAction(string action){ RefreshReferences(); trial.HandleAction(action); DispatchEvent("trial-status", trial.StatusText); }
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
