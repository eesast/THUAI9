using UnityEngine;

using UnityEngine.UI;



namespace THUAI9.Unity.UI

{

    /// <summary>

    /// 用于在编辑器里快速整理 MainGame 场景 UI 的辅助脚本。

    /// 把它挂在 Canvas 上后，可以通过 Inspector 调整参数，再点击 ContextMenu 执行布局。

    /// </summary>

    [ExecuteInEditMode]

    public class UILayoutSetup : MonoBehaviour

    {

        [ContextMenu("应用推荐布局")]

        public void ForceSetup() => SetupLayout();



        public bool liveUpdate = false;



        [Header("顶部信息区")]

        [Range(0, 80)] public float headerY = 18f;

        [Range(140, 280)] public float statusWidth = 180f;

        [Range(320, 900)] public float legendWidth = 620f;

        [Range(40, 120)] public float headerHeight = 72f;



        [Header("比分文本")]

        [Range(20, 220)] public float scorePanelX = 54f;

        [Range(20, 220)] public float scorePanelY = 156f;

        [Range(160, 480)] public float scorePanelWidth = 376f;

        [Range(18, 180)] public float scoreItemHeight = 180f;
        [Range(6, 22)] public float scoreItemSpacing = 8f;



        [Header("底部回放按钮")]

        [Range(20, 120)] public float buttonY = 42f;

        [Range(56, 120)] public float buttonWidth = 64f;

        [Range(28, 60)] public float buttonHeight = 36f;

        [Range(6, 28)] public float buttonSpacing = 10f;



        [Header("可选调试控件位置")]

        [Range(20, 160)] public float debugPanelY = 108f;

        [Range(220, 720)] public float debugPanelWidth = 520f;

        [Range(30, 160)] public float debugPanelHeight = 120f;



        [Header("引用")]

        public RectTransform gameTimeText;

        public RectTransform[] teamScoreTexts = new RectTransform[4];

        public RectTransform playButton;

        public RectTransform pauseButton;

        public RectTransform stopButton;

        public RectTransform speedDropdown;

        public RectTransform progressSlider;

        public RectTransform previousFrameButton;

        public RectTransform nextFrameButton;

        public RectTransform statusText;



        private void OnEnable()

        {

            TryAutoBind();

        }



        private void Update()

        {

            if (liveUpdate)

            {

                SetupLayout();

            }

        }



        [ContextMenu("自动补全引用")]

        public void TryAutoBind()

        {

            gameTimeText ??= FindRect("GameTimeText");

            playButton ??= FindRect("PlayButton");

            pauseButton ??= FindRect("PauseButton");

            stopButton ??= FindRect("StopButton");

            speedDropdown ??= FindRect("SpeedDropdown");

            progressSlider ??= FindRect("ReplayProgressSlider") ?? FindRect("ProgressSlider");

            previousFrameButton ??= FindRect("PreviousFrameButton");

            nextFrameButton ??= FindRect("NextFrameButton");

            statusText ??= FindRect("StatusText");



            if (teamScoreTexts == null || teamScoreTexts.Length != 4)

            {

                teamScoreTexts = new RectTransform[4];

            }

            for (int i = 0; i < 4; i++)

            {

                teamScoreTexts[i] ??= FindRect($"TeamScoreText{i + 1}");

            }

        }



        [ContextMenu("应用推荐布局")]

        public void SetupLayout()

        {

            TryAutoBind();

            SetupHeader();

            SetupTeamScores();

            SetupPlaybackButtons();

            SetupOptionalDebugControls();

        }



        private void SetupHeader()

        {

            if (statusText != null)

            {

                statusText.anchorMin = new Vector2(0f, 1f);

                statusText.anchorMax = new Vector2(0f, 1f);

                statusText.pivot = new Vector2(0f, 1f);

                statusText.anchoredPosition = new Vector2(12f, -headerY);

                statusText.sizeDelta = new Vector2(statusWidth, headerHeight * 0.45f);

            }



            if (gameTimeText != null)

            {

                gameTimeText.anchorMin = new Vector2(0.5f, 1f);

                gameTimeText.anchorMax = new Vector2(0.5f, 1f);

                gameTimeText.pivot = new Vector2(0.5f, 1f);

                gameTimeText.anchoredPosition = new Vector2(0f, -headerY);

                gameTimeText.sizeDelta = new Vector2(240f, headerHeight * 0.5f);

            }

        }



        private void SetupTeamScores()

        {

            for (int i = 0; i < 4 && i < teamScoreTexts.Length; i++)

            {

                RectTransform rt = teamScoreTexts[i];

                if (rt == null)

                {

                    continue;

                }



                rt.anchorMin = new Vector2(1f, 1f);

                rt.anchorMax = new Vector2(1f, 1f);

                rt.pivot = new Vector2(1f, 1f);

                rt.anchoredPosition = new Vector2(-scorePanelX, -scorePanelY - i * (scoreItemHeight + scoreItemSpacing));

                rt.sizeDelta = new Vector2(scorePanelWidth, scoreItemHeight);



                Text text = rt.GetComponent<Text>();

                if (text != null)

                {

                    text.alignment = TextAnchor.MiddleLeft;

                    text.fontSize = 14;

                    text.fontStyle = FontStyle.Bold;

                    text.color = new Color(0.92f, 0.97f, 1f, 1f);

                    text.resizeTextForBestFit = false;

                    text.horizontalOverflow = HorizontalWrapMode.Wrap;

                    text.verticalOverflow = VerticalWrapMode.Overflow;

                    text.lineSpacing = 1f;

                }

            }

        }



        private void SetupPlaybackButtons()

        {

            RectTransform[] buttons = { playButton, pauseButton, stopButton };

            int validCount = 0;

            foreach (RectTransform button in buttons)

            {

                if (button != null)

                {

                    validCount++;

                }

            }



            if (validCount == 0)

            {

                return;

            }



            float totalWidth = validCount * buttonWidth + (validCount - 1) * buttonSpacing;

            float startX = -totalWidth * 0.5f + buttonWidth * 0.5f;

            int buttonIndex = 0;



            foreach (RectTransform button in buttons)

            {

                if (button == null)

                {

                    continue;

                }



                button.anchorMin = new Vector2(0.5f, 0f);

                button.anchorMax = new Vector2(0.5f, 0f);

                button.pivot = new Vector2(0.5f, 0f);

                button.anchoredPosition = new Vector2(startX + buttonIndex * (buttonWidth + buttonSpacing), buttonY);

                button.sizeDelta = new Vector2(buttonWidth, buttonHeight);

                buttonIndex++;

            }



            if (speedDropdown != null)

            {

                speedDropdown.anchorMin = new Vector2(0.5f, 0f);

                speedDropdown.anchorMax = new Vector2(0.5f, 0f);

                speedDropdown.pivot = new Vector2(0.5f, 0f);

                speedDropdown.anchoredPosition = new Vector2(totalWidth * 0.5f + 82f, buttonY);

                speedDropdown.sizeDelta = new Vector2(138f, buttonHeight + 6f);

            }

        }



        private void SetupOptionalDebugControls()

        {

            if (progressSlider != null)

            {

                progressSlider.anchorMin = new Vector2(0.5f, 0f);

                progressSlider.anchorMax = new Vector2(0.5f, 0f);

                progressSlider.pivot = new Vector2(0.5f, 0f);

                progressSlider.anchoredPosition = new Vector2(0f, debugPanelY);

                progressSlider.sizeDelta = new Vector2(debugPanelWidth, 24f);

            }



            if (statusText != null)

            {

                statusText.anchorMin = new Vector2(0f, 1f);

                statusText.anchorMax = new Vector2(0f, 1f);

                statusText.pivot = new Vector2(0f, 1f);

                statusText.anchoredPosition = new Vector2(12f, -headerY);

                statusText.sizeDelta = new Vector2(statusWidth, 24f);

            }



            if (previousFrameButton != null)

            {

                DestroyLegacyFrameButton(previousFrameButton);

                previousFrameButton = null;

            }



            if (nextFrameButton != null)

            {

                DestroyLegacyFrameButton(nextFrameButton);

                nextFrameButton = null;

            }

        }



        private static void DestroyLegacyFrameButton(RectTransform button)

        {

            if (button == null)

            {

                return;

            }



            if (Application.isPlaying)

            {

                Destroy(button.gameObject);

            }

            else

            {

                DestroyImmediate(button.gameObject);

            }

        }



        private RectTransform FindRect(string objectName)

        {

            GameObject go = GameObject.Find(objectName);

            return go != null ? go.GetComponent<RectTransform>() : null;

        }

    }

}
