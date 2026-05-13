using THUAI9.Unity.Live;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SmokeTestLauncher
{
    private const string MainGameScenePath = "Assets/Scenes/MainGame.unity";
    private const string DefaultServerAddress = "127.0.0.1:8888";

    [MenuItem("Tools/Smoke Test/Start MainGame Live")]
    public static void StartMainGameLiveSmoke()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[SmokeTest] Unity is already entering or running Play Mode.");
            return;
        }

        if (!System.IO.File.Exists(MainGameScenePath))
        {
            Debug.LogError("[SmokeTest] Missing scene: " + MainGameScenePath);
            return;
        }

        EditorSceneManager.OpenScene(MainGameScenePath, OpenSceneMode.Single);
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        EditorApplication.isPlaying = true;
        Debug.Log("[SmokeTest] Opening MainGame and entering Play Mode.");
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
            Debug.LogError("[SmokeTest] LiveSpectatorClient not found in MainGame.");
            return;
        }

        client.StartLive(DefaultServerAddress);
        Debug.Log("[SmokeTest] Live spectator connecting to " + DefaultServerAddress + ".");
    }
}
