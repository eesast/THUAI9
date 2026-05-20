using System.IO;
using System.Text;
using UnityEditor;

public static class WebGLBuildScript
{
    public static void BuildWebGL()
    {
        string output = Path.GetFullPath(Path.Combine("..", "Unity-WebGL", "trial"));
        Directory.CreateDirectory(output);
        BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Trial.unity" }, output, BuildTarget.WebGL, BuildOptions.None);
        PatchIndexHtml(output);
    }

    private static void PatchIndexHtml(string output)
    {
        string indexPath = Path.Combine(output, "index.html");
        if (!File.Exists(indexPath)) return;

        string html = File.ReadAllText(indexPath, Encoding.UTF8);
        const string helper = @"      var thuai9Mode = 'trial';
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
        if (api.startTrial) {
          api.startTrial({
            teamId: parseInt(thuai9Query.get('teamId') || '1', 10),
            characterPlayerId: parseInt(thuai9Query.get('characterPlayerId') || '1', 10),
            sideFlag: parseInt(thuai9Query.get('sideFlag') || '1', 10)
          });
        }
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
