using System.IO;
using System.Text;
using UnityEditor;

public static class WebGLBuildScript
{
    public static void BuildWebGL()
    {
        string output = Path.GetFullPath(Path.Combine("..", "Unity-WebGL", "playback"));
        Directory.CreateDirectory(output);
        BuildPipeline.BuildPlayer(new[] { "Assets/Scenes/Playback.unity" }, output, BuildTarget.WebGL, BuildOptions.None);
        PatchIndexHtml(output);
    }

    private static void PatchIndexHtml(string output)
    {
        string indexPath = Path.Combine(output, "index.html");
        if (!File.Exists(indexPath)) return;

        string html = File.ReadAllText(indexPath, Encoding.UTF8);
        const string helper = @"      var thuai9Mode = 'playback';
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
      function thuai9Bool(value) {
        return value === '1' || value === 'true' || value === 'yes' || value === 'on';
      }
      function thuai9ParseStatus(detail) {
        if (!detail) return null;
        if (typeof detail === 'object') return detail;
        try { return JSON.parse(detail); } catch (_) { return null; }
      }
      function thuai9ApplyPlaybackOptions(api) {
        var name = thuai9Query.get('name');
        var speed = parseFloat(thuai9Query.get('speed') || '');
        var frameText = thuai9Query.get('frame') || thuai9Query.get('startFrame');
        var frame = frameText === null ? NaN : parseInt(frameText, 10);
        var autoplay = thuai9Bool(thuai9Query.get('autoplay') || thuai9Query.get('play'));
        var hasPostLoad = !Number.isNaN(speed) || !Number.isNaN(frame) || autoplay;
        var postLoadApplied = false;
        function applyPostLoad() {
          if (postLoadApplied) return;
          postLoadApplied = true;
          if (!Number.isNaN(speed) && api.setSpeed) api.setSpeed(speed);
          if (!Number.isNaN(frame) && api.seekToFrame) api.seekToFrame(Math.max(0, frame));
          if (autoplay && api.play) api.play();
        }
        function waitThenApply() {
          if (!hasPostLoad) return;
          window.addEventListener('thuai9-playback-status', function handler(event) {
            var status = thuai9ParseStatus(event.detail);
            if (status && status.loaded) {
              window.removeEventListener('thuai9-playback-status', handler);
              applyPostLoad();
            }
          });
        }
        var base64 = thuai9Query.get('base64') || thuai9Query.get('data');
        var url = thuai9Query.get('url');
        if (base64 && api.loadPlaybackBase64) {
          waitThenApply();
          api.loadPlaybackBase64(base64, name || 'query.thuaipb');
          return;
        }
        if (url && api.setPlaybackFile) {
          waitThenApply();
          api.setPlaybackFile(url, name || url);
          return;
        }
        applyPostLoad();
      }

";
        const string instanceHook = @"                thuai9SetUnityInstance(unityInstance);
                window.dispatchEvent(new CustomEvent('thuai9-unity-instance-created', { detail: { mode: thuai9Mode } }));
                thuai9RunWhenReady(thuai9ApplyPlaybackOptions);
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
