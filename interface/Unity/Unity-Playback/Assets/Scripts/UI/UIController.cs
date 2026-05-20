using System.IO;
using THUAI9.Unity.Core;
using THUAI9.Unity.Playback;
using THUAI9.Unity.UI.Shared;
using THUAI9.Unity.WebGL;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI
{
    public class UIController : MonoBehaviour
    {
        private static readonly float[] SpeedValues = { 0.5f, 1f, 2f, 4f };
        private SimpleHud hud;
        private Text pathDisplay;
        private Slider progressSlider;
        private Dropdown speedDropdown;
        private PlaybackController playbackController;
        private bool suppressSlider;
        private WorldSelectionController selector;
        private WorldHoverInfoPanel hoverInfoPanel;

        private void Awake()
        {
            playbackController = FindObjectOfType<PlaybackController>() ?? new GameObject("PlaybackManager").AddComponent<PlaybackController>();
            hud = new SimpleHud("THUAI9 云厂竞逐战 - 回放");
            BuildPanel();
            EnsureWorldHoverInfo();
        }

        private void Update()
        {
            hud.UpdateCommon(playbackController != null ? playbackController.CurrentPlaybackTimeMs : CoreParam.playbackElapsedMilliseconds);
            EnsureWorldHoverInfo();
            if (playbackController != null)
            {
                hud.StatusText.text = playbackController.StatusText;
                if (progressSlider != null)
                {
                    suppressSlider = true;
                    progressSlider.maxValue = Mathf.Max(playbackController.TotalFrameCount - 1, 0);
                    progressSlider.value = Mathf.Clamp(playbackController.CurrentFrameIndex, 0, progressSlider.maxValue);
                    suppressSlider = false;
                }

                if (speedDropdown != null)
                {
                    int speedIndex = FindNearestSpeedIndex(playbackController.playSpeed);
#if UNITY_2019_1_OR_NEWER
                    speedDropdown.SetValueWithoutNotify(speedIndex);
#else
                    speedDropdown.value = speedIndex;
#endif
                    speedDropdown.RefreshShownValue();
                }
            }
        }

        private void BuildPanel()
        {
            RectTransform panel = hud.AddPanel("HUD_PlaybackPanel", new Vector2(24f, -108f), new Vector2(620f, 168f));
            hud.Label(panel, "ReplayLabel", "回放", new Vector2(18f, -14f), new Vector2(56f, 30f), 16);
            pathDisplay = hud.Label(panel, "ReplayPathDisplayText", "选择 .thuaipb 文件后自动加载", new Vector2(76f, -14f), new Vector2(402f, 30f), 14, TextAnchor.MiddleLeft);
            pathDisplay.color = new Color(0.62f, 0.72f, 0.80f, 1f);
            hud.Button(panel, "BrowseReplayButton", "选择文件", new Vector2(492f, -14f), new Vector2(92f, 30f), new Color(0.20f, 0.48f, 0.72f, 1f), OnBrowse);
            progressSlider = hud.Slider(panel, "ReplayProgressSlider", new Vector2(18f, -56f), new Vector2(566f, 22f));
            progressSlider.onValueChanged.AddListener(OnSliderChanged);
            hud.Button(panel, "PlayButton", "播放", new Vector2(18f, -88f), new Vector2(82f, 34f), new Color(0.18f, 0.72f, 0.28f, 1f), () => playbackController?.Play());
            hud.Button(panel, "PauseButton", "暂停", new Vector2(108f, -88f), new Vector2(82f, 34f), new Color(0.88f, 0.72f, 0.18f, 1f), () => playbackController?.TogglePlayPause());
            hud.Button(panel, "StopButton", "停止", new Vector2(198f, -88f), new Vector2(82f, 34f), new Color(0.82f, 0.18f, 0.18f, 1f), () => playbackController?.Stop());
            hud.Button(panel, "PrevFrameButton", "上一帧", new Vector2(288f, -88f), new Vector2(82f, 34f), new Color(0.18f, 0.36f, 0.58f, 1f), () => playbackController?.StepBackward());
            hud.Button(panel, "NextFrameButton", "下一帧", new Vector2(378f, -88f), new Vector2(82f, 34f), new Color(0.18f, 0.36f, 0.58f, 1f), () => playbackController?.StepForward());
            speedDropdown = hud.Dropdown(panel, "SpeedDropdown", new Vector2(472f, -88f), new Vector2(112f, 34f), "0.5x", "1x", "2x", "4x");
            speedDropdown.value = 1;
            speedDropdown.onValueChanged.AddListener(i => playbackController?.SetSpeed(SpeedValues[Mathf.Clamp(i, 0, SpeedValues.Length - 1)]));
        }

        private void EnsureWorldHoverInfo()
        {
            if (selector == null)
            {
                selector = FindObjectOfType<WorldSelectionController>() ??
                    new GameObject("WorldSelectionController").AddComponent<WorldSelectionController>();
            }

            selector.targetCamera = Camera.main;
            selector.enableHover = true;
            selector.enableClickSelection = false;

            if (hud != null && hud.Canvas != null)
            {
                hoverInfoPanel = WorldHoverInfoPanel.GetOrCreate(hud.Canvas, Camera.main);
                hoverInfoPanel.ShowWorldHoverInfo = true;
            }
        }

        private void OnBrowse()
        {
            ClearSelection();
#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLFrameBridge.GetOrCreate()?.RequestPlaybackFile();
#elif UNITY_EDITOR
            string path = UnityEditor.EditorUtility.OpenFilePanel("选择 THUAI9 回放文件", Application.dataPath, "thuaipb");
            if (!string.IsNullOrWhiteSpace(path))
            {
                SetPlaybackPathDisplay(Path.GetFileName(path));
                playbackController?.LoadPlaybackFile(path);
            }
#endif
        }

        public void SetPlaybackPathDisplay(string displayText)
        {
            if (pathDisplay == null)
            {
                return;
            }

            pathDisplay.text = string.IsNullOrWhiteSpace(displayText)
                ? "选择 .thuaipb 文件后自动加载"
                : displayText;
            pathDisplay.color = string.IsNullOrWhiteSpace(displayText)
                ? new Color(0.62f, 0.72f, 0.80f, 1f)
                : new Color(0.88f, 0.94f, 0.98f, 1f);
        }

        private void OnSliderChanged(float value) { if (!suppressSlider && playbackController != null && playbackController.PlaybackLoaded) playbackController.SeekToFrame(Mathf.RoundToInt(value)); }
        private static void ClearSelection() { if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null); }
        private static int FindNearestSpeedIndex(float speed)
        {
            int best = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < SpeedValues.Length; i++)
            {
                float distance = Mathf.Abs(SpeedValues[i] - speed);
                if (distance < bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}
