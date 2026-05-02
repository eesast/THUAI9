using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Live;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI
{
    public class UIController : MonoBehaviour
    {
        [Header("对局时间")]
        public Text gameTimeText;

        [Header("队伍得分")]
        public Text[] teamScoreTexts = new Text[4];

        [Header("回放控制")]
        public Button playButton;
        public Button pauseButton;
        public Button stopButton;
        public Dropdown speedDropdown;

        [Header("数据源控制")]
        public InputField playbackPathInput;
        public Button loadPlaybackButton;
        public InputField serverAddressInput;
        public Button connectLiveButton;
        public Button disconnectLiveButton;

        [Header("可选调试控件")]
        public Slider progressSlider;
        public Button previousFrameButton;
        public Button nextFrameButton;

        [Header("可选调试文本")]
        public Text pauseButtonText;
        public Text frameInfoText;
        public Text statusText;
        public Text gameStateText;
        public Text aiEventText;
        public Text aiEffectText;
        public Text selectionInfoText;

        [Header("自动按名称补全引用")]
        public bool autoBindSceneReferences = true;

        private Playback.PlaybackController playbackController;
        private LiveSpectatorClient liveClient;
        private bool suppressProgressCallback;

        private void Awake()
        {
            playbackController = FindObjectOfType<Playback.PlaybackController>();
            liveClient = FindObjectOfType<LiveSpectatorClient>();
            if (liveClient == null)
            {
                GameObject liveClientObject = new GameObject("LiveSpectatorClient");
                liveClient = liveClientObject.AddComponent<LiveSpectatorClient>();
            }

            if (autoBindSceneReferences)
            {
                EnsureRuntimeSourceControls();
                AutoBindIfNeeded();
            }
        }

        private void Start()
        {
            if (playButton != null)
            {
                playButton.onClick.AddListener(OnPlayClicked);
            }

            if (pauseButton != null)
            {
                pauseButton.onClick.AddListener(OnPauseClicked);
            }

            if (stopButton != null)
            {
                stopButton.onClick.AddListener(OnStopClicked);
            }

            if (speedDropdown != null)
            {
                speedDropdown.onValueChanged.AddListener(OnSpeedChanged);
                speedDropdown.value = 1;
            }

            if (loadPlaybackButton != null)
            {
                loadPlaybackButton.onClick.AddListener(OnLoadPlaybackClicked);
            }

            if (connectLiveButton != null)
            {
                connectLiveButton.onClick.AddListener(OnConnectLiveClicked);
            }

            if (disconnectLiveButton != null)
            {
                disconnectLiveButton.onClick.AddListener(OnDisconnectLiveClicked);
            }

            if (progressSlider != null)
            {
                progressSlider.wholeNumbers = true;
                progressSlider.onValueChanged.AddListener(OnProgressChanged);
            }

            if (previousFrameButton != null)
            {
                previousFrameButton.onClick.AddListener(OnPreviousFrameClicked);
            }

            if (nextFrameButton != null)
            {
                nextFrameButton.onClick.AddListener(OnNextFrameClicked);
            }

            UpdatePauseButtonText("Pause");
            UpdateStaticTextFallbacks();
        }

        private void Update()
        {
            UpdateScoreAndTimeUI();
            UpdateDebugUI();
        }

        private void AutoBindIfNeeded()
        {
            gameTimeText ??= FindTextByName("GameTimeText");

            if (teamScoreTexts == null || teamScoreTexts.Length != 4)
            {
                teamScoreTexts = new Text[4];
            }
            for (int i = 0; i < teamScoreTexts.Length; i++)
            {
                teamScoreTexts[i] ??= FindTextByName($"TeamScoreText{i + 1}");
            }

            playButton ??= FindButtonByName("PlayButton");
            pauseButton ??= FindButtonByName("PauseButton");
            stopButton ??= FindButtonByName("StopButton");
            speedDropdown ??= FindDropdownByName("SpeedDropdown");
            playbackPathInput ??= FindInputFieldByName("ReplayPathInput");
            loadPlaybackButton ??= FindButtonByName("LoadReplayButton");
            serverAddressInput ??= FindInputFieldByName("ServerAddressInput");
            connectLiveButton ??= FindButtonByName("ConnectLiveButton");
            disconnectLiveButton ??= FindButtonByName("DisconnectLiveButton");
            progressSlider ??= FindSliderByName("ReplayProgressSlider") ?? FindSliderByName("ProgressSlider");
            previousFrameButton ??= FindButtonByName("PreviousFrameButton");
            nextFrameButton ??= FindButtonByName("NextFrameButton");
            frameInfoText ??= FindTextByName("FrameInfoText");
            statusText ??= FindTextByName("StatusText");
            gameStateText ??= FindTextByName("GameStateText");
            aiEventText ??= FindTextByName("AIEventText");
            aiEffectText ??= FindTextByName("AIEffectText");
            selectionInfoText ??= FindTextByName("SelectionInfoText");
            pauseButtonText ??= FindTextByName("PauseButtonText") ?? pauseButton?.GetComponentInChildren<Text>(true);

            if (playbackPathInput != null && string.IsNullOrWhiteSpace(playbackPathInput.text) && playbackController != null)
            {
                playbackPathInput.text = playbackController.playbackFilePath;
            }

            if (serverAddressInput != null && string.IsNullOrWhiteSpace(serverAddressInput.text) && liveClient != null)
            {
                serverAddressInput.text = liveClient.ServerAddress;
            }
        }

        private void UpdateStaticTextFallbacks()
        {
            if (frameInfoText != null && string.IsNullOrEmpty(frameInfoText.text))
            {
                frameInfoText.text = "帧：0/0";
            }

            if (statusText != null && string.IsNullOrEmpty(statusText.text))
            {
                statusText.text = playbackController != null ? playbackController.StatusText : "状态：未找到 PlaybackController";
            }

            if (gameStateText != null && string.IsNullOrEmpty(gameStateText.text))
            {
                gameStateText.text = "对局：等待首帧";
            }

            if (aiEventText != null && string.IsNullOrEmpty(aiEventText.text))
            {
                aiEventText.text = "AI事件：暂无";
            }

            if (aiEffectText != null && string.IsNullOrEmpty(aiEffectText.text))
            {
                aiEffectText.text = "世界修正：暂无";
            }

            if (selectionInfoText != null && string.IsNullOrEmpty(selectionInfoText.text))
            {
                selectionInfoText.text = "选中对象\n点击地图对象查看详情";
            }
        }

        private void UpdateScoreAndTimeUI()
        {
            if (gameTimeText != null)
            {
                gameTimeText.text = $"时间：{FormatPlaybackTime(GetDisplayPlaybackMilliseconds())}";
            }

            if (CoreParam.allMessage == null)
            {
                if (gameStateText != null)
                {
                    gameStateText.text = "对局：等待首帧";
                }
                return;
            }

            for (int i = 0; i < teamScoreTexts.Length; i++)
            {
                if (teamScoreTexts[i] == null)
                {
                    continue;
                }

                if (i < CoreParam.allMessage.Teams.Count)
                {
                    var team = CoreParam.allMessage.Teams[i];
                    teamScoreTexts[i].text = $"队伍{i + 1}：得分 {team.Score}｜原料 {team.Material}｜算力 {team.ComputePower}｜工厂HP {team.FactoryHp}";
                }
                else
                {
                    teamScoreTexts[i].text = $"队伍{i + 1}：暂无数据";
                }
            }

            if (gameStateText != null)
            {
                gameStateText.text = $"对局：{TranslateGameState(CoreParam.gameState)}  模式：{TranslateGameMode(CoreParam.gameMode)}  帧：{CoreParam.frameCount}";
            }
        }

        private int GetDisplayPlaybackMilliseconds()
        {
            if (playbackController != null && playbackController.CurrentFrameIndex >= 0)
            {
                return playbackController.CurrentPlaybackTimeMs;
            }

            if (CoreParam.playbackCurrentFrameIndex >= 0)
            {
                return CoreParam.playbackElapsedMilliseconds;
            }

            return CoreParam.allMessage != null ? Mathf.Max(CoreParam.allMessage.GameTime, 0) : 0;
        }

        private static string FormatPlaybackTime(int totalMilliseconds)
        {
            totalMilliseconds = Mathf.Max(totalMilliseconds, 0);
            int minutes = totalMilliseconds / 60000;
            int seconds = totalMilliseconds / 1000 % 60;
            int milliseconds = totalMilliseconds % 1000;
            return $"{minutes:D2}:{seconds:D2}.{milliseconds:D3}";
        }

        private void UpdateDebugUI()
        {
            if (playbackController == null)
            {
                return;
            }

            bool liveMode = liveClient != null && liveClient.IsLiveMode;

            if (frameInfoText != null)
            {
                if (liveMode)
                {
                    frameInfoText.text = $"实时帧：{CoreParam.frameCount}｜队列：{CoreParam.frameQueue.GetSize()}";
                }
                else
                {
                    int total = playbackController.TotalFrameCount;
                    int current = playbackController.CurrentFrameIndex >= 0 ? playbackController.CurrentFrameIndex + 1 : 0;
                    frameInfoText.text = $"帧：{current}/{total}";
                }
            }

            if (statusText != null)
            {
                statusText.text = liveMode && liveClient != null ? liveClient.StatusText : playbackController.StatusText;
            }

            UpdatePauseButtonVisualState();

            if (progressSlider != null)
            {
                progressSlider.interactable = !liveMode;
                suppressProgressCallback = true;
                progressSlider.maxValue = Mathf.Max(playbackController.TotalFrameCount - 1, 0);
                progressSlider.SetValueWithoutNotify(Mathf.Max(playbackController.CurrentFrameIndex, 0));
                suppressProgressCallback = false;
            }

            UpdateAIEventUI();
        }

        private void UpdateAIEventUI()
        {
            if (aiEventText != null)
            {
                if (CoreParam.latestAIEvent == null)
                {
                    aiEventText.text = "AI事件：暂无";
                }
                else
                {
                    GlobalAIEvent e = CoreParam.latestAIEvent;
                    aiEventText.text = $"AI事件：{TranslateAIEventCategory(e.Category)}\n{e.Title}\n{e.Description}";
                }
            }

            if (aiEffectText != null)
            {
                aiEffectText.text = CoreParam.latestAIEffect == null
                    ? "世界修正：暂无"
                    : $"世界修正：持续 {CoreParam.latestAIEffect.DurationMs / 1000f:0.#}s\n{FormatAIEffect(CoreParam.latestAIEffect)}";
            }
        }

        private void OnPlayClicked()
        {
            ClearCurrentUiSelection();
            StopLiveIfNeeded();
            playbackController?.Play();
            UpdatePauseButtonText("Pause");
        }

        private void OnPauseClicked()
        {
            ClearCurrentUiSelection();
            if ((liveClient != null && liveClient.IsLiveMode) || playbackController == null || !playbackController.isPlaying)
            {
                return;
            }

            if (playbackController.isPaused)
            {
                playbackController.Play();
            }
            else
            {
                playbackController.Pause();
            }

            UpdatePauseButtonVisualState();
        }

        private void OnStopClicked()
        {
            ClearCurrentUiSelection();
            StopLiveIfNeeded();
            playbackController?.Stop();
            UpdatePauseButtonText("Pause");
        }

        private void OnSpeedChanged(int index)
        {
            float speed = index switch
            {
                0 => 0.5f,
                1 => 1.0f,
                2 => 2.0f,
                3 => 4.0f,
                _ => 1.0f
            };

            playbackController?.SetSpeed(speed);
        }

        private void OnLoadPlaybackClicked()
        {
            ClearCurrentUiSelection();
            StopLiveIfNeeded();

            if (playbackController == null)
            {
                return;
            }

            string path = playbackPathInput != null ? playbackPathInput.text : playbackController.playbackFilePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = playbackController.playbackFilePath;
            }

            playbackController.LoadPlaybackFile(path);
            if (playbackPathInput != null)
            {
                playbackPathInput.text = playbackController.playbackFilePath;
            }
            UpdatePauseButtonText("Pause");
        }

        private void OnConnectLiveClicked()
        {
            ClearCurrentUiSelection();
            string address = serverAddressInput != null ? serverAddressInput.text : null;
            liveClient?.StartLive(address);
            UpdatePauseButtonText("Pause");
        }

        private void OnDisconnectLiveClicked()
        {
            ClearCurrentUiSelection();
            liveClient?.StopLive();
            if (playbackController != null && playbackController.PlaybackLoaded)
            {
                playbackController.Stop();
            }
            UpdatePauseButtonText("Pause");
        }

        private void OnPreviousFrameClicked()
        {
            ClearCurrentUiSelection();
            StopLiveIfNeeded();
            playbackController?.StepBackward();
            UpdatePauseButtonText("Pause");
        }

        private void OnNextFrameClicked()
        {
            ClearCurrentUiSelection();
            StopLiveIfNeeded();
            playbackController?.StepForward();
            UpdatePauseButtonText("Pause");
        }

        private void OnProgressChanged(float value)
        {
            if (suppressProgressCallback || playbackController == null || (liveClient != null && liveClient.IsLiveMode))
            {
                return;
            }

            StopLiveIfNeeded();
            playbackController.SeekToFrame(Mathf.RoundToInt(value));
            UpdatePauseButtonText("Pause");
        }

        private void StopLiveIfNeeded()
        {
            if (liveClient != null && liveClient.IsLiveMode)
            {
                liveClient.StopLive();
            }
        }

        private void UpdatePauseButtonVisualState()
        {
            if (playbackController == null)
            {
                UpdatePauseButtonText("Pause");
                return;
            }

            UpdatePauseButtonText(playbackController.isPlaying && playbackController.isPaused ? "Resume" : "Pause");
        }

        private void UpdatePauseButtonText(string newText)
        {
            if (pauseButtonText != null)
            {
                pauseButtonText.text = newText;
                return;
            }

            if (pauseButton != null)
            {
                Text text = pauseButton.GetComponentInChildren<Text>(true);
                if (text != null)
                {
                    text.text = newText;
                }
            }
        }

        private static void ClearCurrentUiSelection()
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private void EnsureRuntimeSourceControls()
        {
            if (FindInputFieldByName("ReplayPathInput") != null || GameObject.Find("SourceControlPanel") != null)
            {
                return;
            }

            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("Canvas");
                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObject.AddComponent<CanvasScaler>();
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            EnsureEventSystem();

            Font font = GetBuiltInUIFont();
            GameObject panel = new GameObject("SourceControlPanel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(canvas.transform, false);
            RectTransform panelRect = panel.GetComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0f, 0f);
            panelRect.anchorMax = new Vector2(0f, 0f);
            panelRect.pivot = new Vector2(0f, 0f);
            panelRect.anchoredPosition = new Vector2(12f, 12f);
            panelRect.sizeDelta = new Vector2(600f, 94f);
            Image panelImage = panel.GetComponent<Image>();
            panelImage.color = new Color(0.03f, 0.05f, 0.07f, 0.78f);

            CreateLabel(panel.transform, "ReplayPathLabel", "Replay", font, new Vector2(44f, 62f), new Vector2(70f, 24f));
            playbackPathInput = CreateInput(panel.transform, "ReplayPathInput",
                playbackController != null ? playbackController.playbackFilePath : "Assets/Playback/test/official_bot_match.thuaipb",
                "Replay file path", font, new Vector2(264f, 62f), new Vector2(360f, 28f));
            loadPlaybackButton = CreateButton(panel.transform, "LoadReplayButton", "Load", font, new Vector2(504f, 62f), new Vector2(82f, 28f), new Color(0.18f, 0.36f, 0.58f, 0.95f));

            CreateLabel(panel.transform, "ServerAddressLabel", "Live", font, new Vector2(44f, 26f), new Vector2(70f, 24f));
            serverAddressInput = CreateInput(panel.transform, "ServerAddressInput",
                liveClient != null ? liveClient.ServerAddress : "127.0.0.1:8888",
                "server:port", font, new Vector2(214f, 26f), new Vector2(260f, 28f));
            connectLiveButton = CreateButton(panel.transform, "ConnectLiveButton", "Connect", font, new Vector2(414f, 26f), new Vector2(86f, 28f), new Color(0.12f, 0.48f, 0.32f, 0.95f));
            disconnectLiveButton = CreateButton(panel.transform, "DisconnectLiveButton", "Disconnect", font, new Vector2(520f, 26f), new Vector2(108f, 28f), new Color(0.48f, 0.18f, 0.18f, 0.95f));
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current != null)
            {
                return;
            }

            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();
            eventSystem.AddComponent<StandaloneInputModule>();
        }


        private static Font GetBuiltInUIFont()
        {
            try
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null)
                {
                    return font;
                }
            }
            catch
            {
            }

            try
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch
            {
                return null;
            }
        }

        private static Text CreateLabel(Transform parent, string name, string text, Font font, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text label = go.GetComponent<Text>();
            label.text = text;
            label.font = font;
            label.fontSize = 14;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(0.82f, 0.9f, 0.95f, 1f);
            return label;
        }

        private static InputField CreateInput(Transform parent, string name, string value, string placeholder, Font font, Vector2 position, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = go.GetComponent<Image>();
            image.color = new Color(0.9f, 0.94f, 0.98f, 0.92f);

            Text text = CreateLabel(go.transform, $"{name}Text", value, font, Vector2.zero, new Vector2(size.x - 18f, size.y - 6f));
            StretchChildRect(text.rectTransform, 8f, 3f, 8f, 3f);
            text.color = new Color(0.04f, 0.06f, 0.08f, 1f);
            text.fontSize = 13;

            Text placeholderText = CreateLabel(go.transform, $"{name}Placeholder", placeholder, font, Vector2.zero, new Vector2(size.x - 18f, size.y - 6f));
            StretchChildRect(placeholderText.rectTransform, 8f, 3f, 8f, 3f);
            placeholderText.color = new Color(0.28f, 0.32f, 0.36f, 0.65f);
            placeholderText.fontSize = 13;

            InputField input = go.GetComponent<InputField>();
            input.textComponent = text;
            input.placeholder = placeholderText;
            input.text = value;
            input.lineType = InputField.LineType.SingleLine;
            return input;
        }

        private static Button CreateButton(Transform parent, string name, string label, Font font, Vector2 position, Vector2 size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Image image = go.GetComponent<Image>();
            image.color = color;

            Button button = go.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = color * 1.25f;
            colors.pressedColor = color * 0.75f;
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            Text text = CreateLabel(go.transform, $"{name}Text", label, font, Vector2.zero, size);
            StretchChildRect(text.rectTransform, 0f, 0f, 0f, 0f);
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.fontSize = 14;
            return button;
        }

        private static void StretchChildRect(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static Text FindTextByName(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            return go != null ? go.GetComponent<Text>() : null;
        }

        private static Button FindButtonByName(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            return go != null ? go.GetComponent<Button>() : null;
        }

        private static Dropdown FindDropdownByName(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            return go != null ? go.GetComponent<Dropdown>() : null;
        }

        private static Slider FindSliderByName(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            return go != null ? go.GetComponent<Slider>() : null;
        }

        private static InputField FindInputFieldByName(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            return go != null ? go.GetComponent<InputField>() : null;
        }

        private static string TranslateGameState(GameState state)
        {
            return state switch
            {
                GameState.GameStart => "开始",
                GameState.GameRunning => "进行中",
                GameState.GameEnd => "结束",
                _ => "未开始"
            };
        }

        private static string TranslateGameMode(GameMode mode)
        {
            return mode switch
            {
                GameMode.Pve => "PVE",
                GameMode.Pvp => "PVP",
                _ => "未知"
            };
        }

        private static string TranslateAIEventCategory(AIEventCategory category)
        {
            return category switch
            {
                AIEventCategory.EconomicEvent => "经济波动",
                AIEventCategory.WeatherEvent => "环境影响",
                AIEventCategory.TechnologyEvent => "科技波动",
                AIEventCategory.CombatEvent => "战斗修正",
                _ => "未知"
            };
        }

        private static string FormatAIEffect(AIWorldEffect effect)
        {
            if (effect == null)
            {
                return string.Empty;
            }

            int count = effect.PriceModifiers.Count + effect.CharacterModifiers.Count + effect.TaskModifiers.Count;
            if (count == 0)
            {
                return "无具体修正项";
            }

            return $"价格 {effect.PriceModifiers.Count} 项｜单位 {effect.CharacterModifiers.Count} 项｜任务 {effect.TaskModifiers.Count} 项";
        }

        private void OnDestroy()
        {
            if (playButton != null)
            {
                playButton.onClick.RemoveListener(OnPlayClicked);
            }

            if (pauseButton != null)
            {
                pauseButton.onClick.RemoveListener(OnPauseClicked);
            }

            if (stopButton != null)
            {
                stopButton.onClick.RemoveListener(OnStopClicked);
            }

            if (speedDropdown != null)
            {
                speedDropdown.onValueChanged.RemoveListener(OnSpeedChanged);
            }

            if (loadPlaybackButton != null)
            {
                loadPlaybackButton.onClick.RemoveListener(OnLoadPlaybackClicked);
            }

            if (connectLiveButton != null)
            {
                connectLiveButton.onClick.RemoveListener(OnConnectLiveClicked);
            }

            if (disconnectLiveButton != null)
            {
                disconnectLiveButton.onClick.RemoveListener(OnDisconnectLiveClicked);
            }

            if (progressSlider != null)
            {
                progressSlider.onValueChanged.RemoveListener(OnProgressChanged);
            }

            if (previousFrameButton != null)
            {
                previousFrameButton.onClick.RemoveListener(OnPreviousFrameClicked);
            }

            if (nextFrameButton != null)
            {
                nextFrameButton.onClick.RemoveListener(OnNextFrameClicked);
            }
        }
    }
}
