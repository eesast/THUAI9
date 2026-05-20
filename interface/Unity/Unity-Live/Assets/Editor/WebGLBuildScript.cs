using System.IO;
using System.Text;
using UnityEditor;

public static class WebGLBuildScript
{
    public static void BuildWebGL()
    {
        string output = Path.GetFullPath(Path.Combine("..", "Unity-WebGL", "live"));
        Directory.CreateDirectory(output);
        BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Live.unity" }, output, BuildTarget.WebGL, BuildOptions.None);
        PatchIndexHtml(output);
    }

    private static void PatchIndexHtml(string output)
    {
        string indexPath = Path.Combine(output, "index.html");
        if (!File.Exists(indexPath)) return;

        string html = File.ReadAllText(indexPath, Encoding.UTF8);
        const string helper = @"      var thuai9Mode = 'live';
      var thuai9Query = new URLSearchParams(window.location.search);
      function thuai9SetUnityInstance(unityInstance) {
        window.unityInstance = unityInstance;
        window.THUAIGameInstance = unityInstance;
        window.gameInstance = unityInstance;
      }
      function thuai9RunWhenReady(callback) {
        if (window.THUAI9Unity) { callback(window.THUAI9Unity); return; }
        window.addEventListener('thuai9-unity-ready', function () { callback(window.THUAI9Unity); }, { once: true });
      }
      function thuai9ApplyEntryParameters(api) {
        var ws = thuai9Query.get('ws');
        if (ws && api.connectLiveWebSocket) { api.connectLiveWebSocket(ws); return; }
        var source = thuai9Query.get('source') || 'WebGL Live';
        if (api.startWebLive) api.startWebLive(source);
      }

";
        const string instanceHook = @"                thuai9SetUnityInstance(unityInstance);
                window.dispatchEvent(new CustomEvent('thuai9-unity-instance-created', { detail: { mode: thuai9Mode } }));
                thuai9RunWhenReady(thuai9ApplyEntryParameters);
";

        if (!html.Contains("var thuai9Mode ="))
        {
            html = html.Replace("      var buildUrl = \"Build\";", helper + "      var buildUrl = \"Build\";");
        }

        if (!html.Contains("thuai9SetUnityInstance(unityInstance);"))
        {
            html = html.Replace("                loadingBar.style.display = \"none\";", instanceHook + "                loadingBar.style.display = \"none\";");
        }

        File.WriteAllText(indexPath, html, new UTF8Encoding(false));
    }
}
