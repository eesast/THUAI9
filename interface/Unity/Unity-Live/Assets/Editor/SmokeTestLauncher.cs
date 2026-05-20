using THUAI9.Unity.Live;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SmokeTestLauncher
{
    private const string LiveScenePath = "Assets/Scenes/Live.unity";
    private const string DefaultServerAddress = "127.0.0.1:8888";

    [MenuItem("Tools/Smoke Test/Start Live")]
    public static void StartLiveSmoke()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[SmokeTest] Unity is already entering or running Play Mode.");
            return;
        }

        if (!System.IO.File.Exists(LiveScenePath))
        {
            Debug.LogError("[SmokeTest] Missing scene: " + LiveScenePath);
            return;
        }

        EditorSceneManager.OpenScene(LiveScenePath, OpenSceneMode.Single);
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
        Debug.Log("[SmokeTest] Opening Live scene and entering Play Mode.");
    }

    private static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode)
        {
            return;
        }

        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.delayCall += StartLiveSpectator;
    }

    private static void StartLiveSpectator()
    {
        LiveSpectatorClient client = Object.FindObjectOfType<LiveSpectatorClient>();
        if (client == null)
        {
            Debug.LogError("[SmokeTest] LiveSpectatorClient not found in Live scene.");
            return;
        }

        client.StartLive(DefaultServerAddress);
        Debug.Log("[SmokeTest] Live spectator connecting to " + DefaultServerAddress + ".");
    }
}
