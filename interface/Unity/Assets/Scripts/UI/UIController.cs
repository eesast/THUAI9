using System;
using System.Collections.Generic;
using System.IO;
using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Live;
using THUAI9.Unity.Playback;
using THUAI9.Unity.WebGL;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI
{
    public class UIController : MonoBehaviour
    {
        private const float TeamStatusRightMargin = 36f;
        private const float TeamStatusTopMargin = 156f;
        private const float TeamStatusWidth = 420f;
        private const float TeamStatusHeight = 82f;
        private const float TeamStatusSpacing = 8f;
        private const string RecentReplayPrefsKey = "ReplayRecentPaths";
        private const int MaxRecentReplayCount = 8;
        private const int MaxReplayDiscoveryScanCount = 128;
        private const string CjkFontResourcePath = "Fonts/NotoSansCJKsc-Regular";
        private static Font cachedUiFont;

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
        public Button browsePlaybackButton;
        public Button loadPlaybackButton;
        public Dropdown recentReplayDropdown;
        public Text replayHintText;
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
        private bool suppressRecentReplayCallback;
        private readonly List<string> recentReplayPaths = new List<string>();

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
                ConfigureHudVisualStyle();
                ConfigureTeamStatusLayout();
                ApplyFontToSceneTexts();
                RefreshRecentReplayDropdown();
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

            if (browsePlaybackButton != null)
            {
                browsePlaybackButton.onClick.AddListener(OnBrowsePlaybackClicked);
            }

            if (recentReplayDropdown != null)
            {
                recentReplayDropdown.onValueChanged.AddListener(OnRecentReplayChanged);
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
            ApplyFontToSceneTexts();
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
            browsePlaybackButton ??= FindButtonByName("BrowseReplayButton");
            recentReplayDropdown ??= FindDropdownByName("RecentReplayDropdown");
            replayHintText ??= FindTextByName("ReplayHintText");

            if (serverAddressInput != null && string.IsNullOrWhiteSpace(serverAddressInput.text) && liveClient != null)
            {
                serverAddressInput.text = liveClient.ServerAddress;
            }
        }

        private void UpdateStaticTextFallbacks()
        {
            if (frameInfoText != null && string.IsNullOrEmpty(frameInfoText.text))
            {
                frameInfoText.text = string.Empty;
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

            if (replayHintText != null && IsDefaultReplayHint(replayHintText.text))
            {
                replayHintText.text = string.Empty;
            }
        }

        private void UpdateScoreAndTimeUI()
        {
            if (gameTimeText != null)
            {
                gameTimeText.text = FormatPlaybackTime(GetDisplayPlaybackMilliseconds());
            }

            if (CoreParam.allMessage == null)
            {
                ApplyIdleTeamScoreFallbacks();
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
                    teamScoreTexts[i].text = FormatTeamStatus(i + 1, team);
                }
                else
                {
                    teamScoreTexts[i].text = FormatWaitingTeamStatus(i + 1);
                }
            }

            if (gameStateText != null)
            {
                gameStateText.text = $"对局：{TranslateGameState(CoreParam.gameState)}  模式：{TranslateGameMode(CoreParam.gameMode)}  帧：{CoreParam.frameCount}";
            }
        }

        private int GetDisplayPlaybackMilliseconds()
        {
            if (FrameSourceHub.ActiveKind == FrameSourceHub.SourceKind.Live)
            {
                return GetCurrentLiveGameMilliseconds();
            }

            if (FrameSourceHub.ActiveKind == FrameSourceHub.SourceKind.Playback &&
                playbackController != null &&
                playbackController.CurrentFrameIndex >= 0)
            {
                return playbackController.CurrentPlaybackTimeMs;
            }

            if (FrameSourceHub.ActiveKind == FrameSourceHub.SourceKind.Playback &&
                CoreParam.playbackCurrentFrameIndex >= 0)
            {
                return CoreParam.playbackElapsedMilliseconds;
            }

            return GetCurrentLiveGameMilliseconds();
        }

        private static int GetCurrentLiveGameMilliseconds()
        {
            MessageOfAll allMessage = CoreParam.currentFrame?.AllMessage ?? CoreParam.allMessage;
            return allMessage != null ? Mathf.Max(allMessage.GameTime, 0) : 0;
        }

        private static string FormatPlaybackTime(int totalMilliseconds)
        {
            totalMilliseconds = Mathf.Max(totalMilliseconds, 0);
            int minutes = totalMilliseconds / 60000;
            int seconds = totalMilliseconds / 1000 % 60;
            return $"{minutes:D2}:{seconds:D2}";
        }

        private void ApplyIdleTeamScoreFallbacks()
        {
            if (teamScoreTexts == null)
            {
                return;
            }

            for (int i = 0; i < teamScoreTexts.Length; i++)
            {
                Text text = teamScoreTexts[i];
                if (text == null)
                {
                    continue;
                }

                text.text = FormatWaitingTeamStatus(i + 1);
                text.color = GetTeamAccentColor(i);
            }
        }

        private void UpdateDebugUI()
        {
            if (playbackController == null)
            {
                return;
            }

            bool liveMode = FrameSourceHub.ActiveKind == FrameSourceHub.SourceKind.Live ||
                            (liveClient != null && liveClient.IsLiveMode);

            if (frameInfoText != null)
            {
                if (liveMode)
                {
                    bool liveHasStarted = FrameSourceHub.SubmittedFrameCount > 0 ||
                                          FrameSourceHub.RenderedFrameCount > 0 ||
                                          (liveClient != null && liveClient.ReceivedFrameCount > 0);
                    frameInfoText.text = liveHasStarted
                        ? $"实时观战：已渲染 {FrameSourceHub.RenderedFrameCount} 帧 / 待处理 {FrameSourceHub.QueueSize} 帧"
                        : string.Empty;
                }
                else if (playbackController.PlaybackLoaded)
                {
                    int total = playbackController.TotalFrameCount;
                    int current = playbackController.CurrentFrameIndex >= 0 ? playbackController.CurrentFrameIndex + 1 : 0;
                    frameInfoText.text = $"回放帧：{current}/{total}";
                }
                else
                {
                    frameInfoText.text = string.Empty;
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
                if (liveClient != null && liveClient.HasCurrentEventStatus)
                {
                    string eventName = string.IsNullOrWhiteSpace(liveClient.CurrentEventName) ? "normal" : liveClient.CurrentEventName;
                    string eventDescription = string.IsNullOrWhiteSpace(liveClient.CurrentEventDescription) ? "\u6682\u65e0\u4e8b\u4ef6\u63cf\u8ff0" : liveClient.CurrentEventDescription;
                    aiEventText.text = $"\u4e8b\u4ef6\u72b6\u6001\uff1a{eventName}\n{eventDescription}";
                }
                else if (CoreParam.latestAIEvent == null)
                {
                    aiEventText.text = "AI\u4e8b\u4ef6\uff1a\u6682\u65e0";
                }
                else
                {
                    GlobalAIEvent e = CoreParam.latestAIEvent;
                    aiEventText.text = $"AI\u4e8b\u4ef6\uff1a{TranslateAIEventCategory(e.Category)}\n{e.Title}\n{e.Description}";
                }
            }

            if (aiEffectText != null)
            {
                aiEffectText.text = CoreParam.latestAIEffect == null
                    ? "\u4e16\u754c\u4fee\u6b63\uff1a\u6682\u65e0"
                    : $"\u4e16\u754c\u4fee\u6b63\uff1a\u6301\u7eed {CoreParam.latestAIEffect.DurationMs / 1000f:0.#}s\n{FormatAIEffect(CoreParam.latestAIEffect)}";
            }
        }

        private static string FormatTeamStatus(int teamIndex, MessageOfAll.Types.TeamInfo team)
        {
            string techSummary = FormatTeamTechLevels(team.TechLevels);
            string uuidSummary = FormatTeamUuidSummary(teamIndex);
            return $"队伍 {teamIndex}：分数 {team.Score}，原料 {team.Material}，算力 {team.ComputePower}\n工厂生命 {team.FactoryHp}，科技等级：{techSummary}\n成员 uuid：{uuidSummary}";
        }

        private static string FormatWaitingTeamStatus(int teamIndex)
        {
            return $"队伍 {teamIndex}：等待首帧\n工厂生命 --，科技等级：暂无\n成员 uuid：等待角色创建";
        }

        private static string FormatTeamUuidSummary(int teamIndex)
        {
            var members = new List<TeamMemberUuidInfo>();

            foreach (MessageOfCharacter character in CoreParam.characters.Values)
            {
                if (character == null || character.TeamId != teamIndex)
                {
                    continue;
                }

                AddOrMergeTeamMemberUuid(
                    members,
                    character.PlayerId,
                    character.Guid);
            }

            foreach (MessageOfTeam team in CoreParam.teams.Values)
            {
                if (team == null || team.TeamId != teamIndex || team.PlayerId <= 0)
                {
                    continue;
                }

                AddOrMergeTeamMemberUuid(
                    members,
                    team.PlayerId,
                    0);
            }

            if (members.Count == 0)
            {
                return "暂无（等待角色创建）";
            }

            members.Sort((left, right) =>
            {
                int byPlayer = left.PlayerId.CompareTo(right.PlayerId);
                return byPlayer != 0 ? byPlayer : left.Guid.CompareTo(right.Guid);
            });

            var parts = new List<string>();
            int visibleCount = Mathf.Min(4, members.Count);
            for (int i = 0; i < visibleCount; i++)
            {
                TeamMemberUuidInfo member = members[i];
                string playerLabel = member.PlayerId > 0 ? $"玩家 {member.PlayerId}" : "未登记玩家";
                string uuidLabel = member.Guid > 0 ? member.Guid.ToString() : "暂无";
                parts.Add($"{playerLabel}=uuid {uuidLabel}");
            }

            if (members.Count > visibleCount)
            {
                parts.Add($"其余 {members.Count - visibleCount} 名");
            }

            return string.Join("，", parts);
        }

        private static void AddOrMergeTeamMemberUuid(
            List<TeamMemberUuidInfo> members,
            long playerId,
            long guid)
        {
            for (int i = 0; i < members.Count; i++)
            {
                TeamMemberUuidInfo existing = members[i];
                bool sameRegisteredPlayer = playerId > 0 && existing.PlayerId == playerId;
                bool sameGuidOnly = playerId <= 0 && guid > 0 && existing.Guid == guid;
                if (!sameRegisteredPlayer && !sameGuidOnly)
                {
                    continue;
                }

                if (existing.Guid <= 0 && guid > 0)
                {
                    existing.Guid = guid;
                }

                members[i] = existing;
                return;
            }

            members.Add(new TeamMemberUuidInfo
            {
                PlayerId = playerId,
                Guid = guid
            });
        }

        private struct TeamMemberUuidInfo
        {
            public long PlayerId;
            public long Guid;
        }

        private static string FormatTeamTechLevels(IEnumerable<KeyValuePair<string, int>> techLevels)
        {
            if (techLevels == null)
            {
                return "暂无";
            }

            var parts = new List<string>();
            foreach (KeyValuePair<string, int> kv in techLevels)
            {
                if (kv.Value <= 0)
                {
                    continue;
                }

                parts.Add($"{ShortTechName(kv.Key)} {kv.Value}级");
                if (parts.Count >= 3)
                {
                    break;
                }
            }

            return parts.Count > 0 ? string.Join("、", parts) : "暂无";
        }

        private static string ShortTechName(string key)
        {
            return key switch
            {
                "Robust" => "生命耐久",
                "Warrior" => "攻击能力",
                "MoveSpeed" => "移动速度",
                "Carry" => "携带容量",
                "Efficiency" => "采集效率",
                "Production" => "生产效率",
                "Storage" => "仓储容量",
                "Price" => "出售价格",
                "Cost" => "生产成本",
                "Market" => "市场能力",
                _ => string.IsNullOrWhiteSpace(key) ? "?" : key
            };
        }

        private void ConfigureTeamStatusLayout()
        {
            if (teamScoreTexts == null)
            {
                return;
            }

            for (int i = 0; i < teamScoreTexts.Length; i++)
            {
                Text text = teamScoreTexts[i];
                if (text == null)
                {
                    continue;
                }

                RectTransform rect = text.rectTransform;
                rect.anchorMin = new Vector2(1f, 1f);
                rect.anchorMax = new Vector2(1f, 1f);
                rect.pivot = new Vector2(1f, 1f);
                rect.anchoredPosition = new Vector2(-TeamStatusRightMargin - 16f, -TeamStatusTopMargin - i * (TeamStatusHeight + TeamStatusSpacing));
                rect.sizeDelta = new Vector2(TeamStatusWidth - 44f, TeamStatusHeight);

                text.alignment = TextAnchor.MiddleLeft;
                text.font = GetBuiltInUIFont();
                text.fontSize = 14;
                text.fontStyle = FontStyle.Bold;
                text.color = GetTeamAccentColor(i);
                text.resizeTextForBestFit = false;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.lineSpacing = 1f;
                text.raycastTarget = false;
                EnsureTextShadow(text, new Color(0f, 0f, 0f, 0.72f), new Vector2(1.3f, -1.3f));
            }
        }

        private void ConfigureTeamStatusCard(Text text, int teamIndex)
        {
            if (text == null || text.transform.parent == null)
            {
                return;
            }

            Transform parent = text.transform.parent;
            Color accent = GetTeamAccentColor(teamIndex);
            RectTransform card = FindOrCreateRect(parent, $"TeamStatusCard{teamIndex + 1}", typeof(Image));
            card.anchorMin = new Vector2(1f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(1f, 1f);
            card.anchoredPosition = new Vector2(-TeamStatusRightMargin, -TeamStatusTopMargin - teamIndex * (TeamStatusHeight + TeamStatusSpacing));
            card.sizeDelta = new Vector2(TeamStatusWidth, TeamStatusHeight);

            Image cardImage = card.GetComponent<Image>();
            cardImage.color = new Color(0.035f, 0.060f, 0.085f, 0.88f);
            cardImage.raycastTarget = false;

            RectTransform stripe = FindOrCreateRect(card.transform, "AccentStripe", typeof(Image));
            stripe.anchorMin = new Vector2(0f, 0f);
            stripe.anchorMax = new Vector2(0f, 1f);
            stripe.pivot = new Vector2(0f, 0.5f);
            stripe.anchoredPosition = Vector2.zero;
            stripe.sizeDelta = new Vector2(5f, 0f);
            Image stripeImage = stripe.GetComponent<Image>();
            stripeImage.color = accent;
            stripeImage.raycastTarget = false;

            card.SetSiblingIndex(Mathf.Max(0, text.transform.GetSiblingIndex()));
            text.transform.SetAsLastSibling();
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
                SetReplayHint("未找到 PlaybackController，无法加载回放。", true);
                return;
            }

            string path = playbackPathInput != null ? playbackPathInput.text : playbackController.playbackFilePath;

            LoadPlaybackPathFromUi(path, true);
            UpdatePauseButtonText("Pause");
        }

        private void OnBrowsePlaybackClicked()
        {
            ClearCurrentUiSelection();

#if UNITY_WEBGL && !UNITY_EDITOR
            WebGLFrameBridge.GetOrCreate()?.RequestPlaybackFile();
            SetReplayHint("WebGL: opened browser playback file picker.", false);
#elif UNITY_EDITOR
            string startDirectory = GetPlaybackPickerStartDirectory();
            string path = UnityEditor.EditorUtility.OpenFilePanel("选择 THUAI9 回放文件", startDirectory, "thuaipb");
            if (string.IsNullOrWhiteSpace(path))
            {
                SetReplayHint("已取消选择回放文件。", false);
                return;
            }

            if (playbackPathInput != null)
            {
                playbackPathInput.text = path;
            }
            LoadPlaybackPathFromUi(path, true);
#else
            SetReplayHint("当前运行环境不支持系统文件选择器，请手动输入 .thuaipb 路径。", true);
#endif
        }

        private void OnRecentReplayChanged(int index)
        {
            if (suppressRecentReplayCallback || index < 0 || index >= recentReplayPaths.Count)
            {
                return;
            }

            string path = recentReplayPaths[index];
            if (playbackPathInput != null)
            {
                playbackPathInput.text = path;
            }

            ClearCurrentUiSelection();
            StopLiveIfNeeded();
            LoadPlaybackPathFromUi(path, true);
        }

        private void LoadPlaybackPathFromUi(string path, bool addToRecent)
        {
            if (playbackController == null)
            {
                SetReplayHint("未找到 PlaybackController，无法加载回放。", true);
                return;
            }

            string trimmedPath = path?.Trim().Trim('"');
            if (Playback.PlaybackController.IsPlaybackUrl(trimmedPath))
            {
                playbackController.LoadPlaybackUrl(trimmedPath);
                if (playbackPathInput != null)
                {
                    playbackPathInput.text = trimmedPath;
                }

                SetReplayHint($"Loading Web playback: {ShortenPathForDisplay(trimmedPath)}", false);
                return;
            }

            string resolvedPath = ResolvePlaybackPathForUi(path);
            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                SetReplayHint("回放路径为空：请选择 .thuaipb 文件或输入路径。", true);
                return;
            }

            if (!File.Exists(resolvedPath))
            {
                SetReplayHint($"未找到回放文件：{ShortenPathForDisplay(resolvedPath)}", true);
                return;
            }

            playbackController.LoadPlaybackFile(resolvedPath);
            if (playbackPathInput != null)
            {
                playbackPathInput.text = playbackController.playbackFilePath;
            }

            if (playbackController.PlaybackLoaded)
            {
                if (addToRecent)
                {
                    AddRecentReplayPath(playbackController.playbackFilePath);
                }

                SetReplayHint($"已加载：{Path.GetFileName(playbackController.playbackFilePath)}｜{playbackController.TotalFrameCount} 帧", false);
            }
            else
            {
                SetReplayHint(playbackController.StatusText, true);
            }
        }

        private void OnConnectLiveClicked()
        {
            ClearCurrentUiSelection();
            string address = serverAddressInput != null ? serverAddressInput.text : null;
            liveClient?.StartLive(address);
            SetReplayHint("Live hint: for 4-team tests start Server with --teamCount 4.", false);
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
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                GameObject canvasObject = new GameObject("Canvas");
                canvas = canvasObject.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                canvasObject.AddComponent<GraphicRaycaster>();
            }

            EnsureEventSystem();

            Font font = GetBuiltInUIFont();
            GameObject panel = GameObject.Find("HUD_SourcePanel") ?? GameObject.Find("SourceControlPanel");
            if (panel == null)
            {
                panel = new GameObject("HUD_SourcePanel", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(canvas.transform, false);
            }

            RectTransform panelRect = panel.GetComponent<RectTransform>();
            if (panelRect != null)
            {
                panelRect.anchorMin = new Vector2(0f, 0f);
                panelRect.anchorMax = new Vector2(0f, 0f);
                panelRect.pivot = new Vector2(0f, 0f);
                panelRect.anchoredPosition = new Vector2(24f, 146f);
                panelRect.sizeDelta = new Vector2(740f, 98f);
            }

            Image panelImage = panel.GetComponent<Image>() ?? panel.AddComponent<Image>();
            panelImage.color = new Color(0.026f, 0.043f, 0.065f, 0.94f);
            panelImage.raycastTarget = false;

            ConfigureSourcePanelControls(panel.transform, font);
        }

        private void ConfigureSourcePanelControls(Transform panel, Font font)
        {
            Text replayLabel = FindOrCreateLabel(panel, "HUD_ReplayPathLabel", "回放", font);
            SetChildRect(replayLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -16f), new Vector2(64f, 30f), new Vector2(0f, 1f));

            playbackPathInput = FindInputFieldByName("ReplayPathInput") ?? CreateInput(panel, "ReplayPathInput",
                string.Empty,
                "选择 .thuaipb 或输入路径", font, Vector2.zero, new Vector2(500f, 30f));
            playbackPathInput.transform.SetParent(panel, false);
            if (IsLegacyDefaultReplayPath(playbackPathInput.text))
            {
                playbackPathInput.text = string.Empty;
            }
            SetChildRect(playbackPathInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -16f), new Vector2(420f, 30f), new Vector2(0f, 1f));
            StyleInputField(playbackPathInput, font);

            browsePlaybackButton = FindButtonByName("BrowseReplayButton") ?? CreateButton(panel, "BrowseReplayButton", "选择文件", font, Vector2.zero, new Vector2(94f, 30f), new Color(0.20f, 0.48f, 0.72f, 1f));
            browsePlaybackButton.transform.SetParent(panel, false);
            SetChildRect(browsePlaybackButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(526f, -16f), new Vector2(94f, 30f), new Vector2(0f, 1f));
            StyleButton(browsePlaybackButton, "选择文件", new Color(0.20f, 0.48f, 0.72f, 1f), font);

            loadPlaybackButton = FindButtonByName("LoadReplayButton") ?? CreateButton(panel, "LoadReplayButton", "加载", font, Vector2.zero, new Vector2(82f, 30f), new Color(0.18f, 0.36f, 0.58f, 1f));
            loadPlaybackButton.transform.SetParent(panel, false);
            SetChildRect(loadPlaybackButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(630f, -16f), new Vector2(82f, 30f), new Vector2(0f, 1f));
            StyleButton(loadPlaybackButton, "加载", new Color(0.18f, 0.36f, 0.58f, 1f), font);

            recentReplayDropdown = FindDropdownByName("RecentReplayDropdown") ?? CreateDropdown(panel, "RecentReplayDropdown", font, Vector2.zero, new Vector2(500f, 30f));
            recentReplayDropdown.transform.SetParent(panel, false);
            SetChildRect(recentReplayDropdown.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -52f), new Vector2(420f, 30f), new Vector2(0f, 1f));
            StyleDropdown(recentReplayDropdown, font);
            SetNamedGameObjectActive("HUD_RecentReplayLabel", false);
            SetNamedGameObjectActive("RecentReplayDropdown", false);

            replayHintText = FindTextByName("ReplayHintText") ?? FindOrCreateLabel(panel, "ReplayHintText", string.Empty, font);
            replayHintText.transform.SetParent(panel, false);
            SetChildRect(replayHintText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(602f, -52f), new Vector2(116f, 34f), new Vector2(0f, 1f));
            if (IsDefaultReplayHint(replayHintText.text))
            {
                replayHintText.text = string.Empty;
            }
            replayHintText.fontSize = 14;
            replayHintText.alignment = TextAnchor.UpperLeft;
            replayHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            replayHintText.verticalOverflow = VerticalWrapMode.Truncate;
            replayHintText.color = new Color(0.74f, 0.86f, 0.92f, 1f);

            Text liveLabel = FindOrCreateLabel(panel, "HUD_LiveAddressLabel", "Live", font);
            SetChildRect(liveLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -52f), new Vector2(64f, 30f), new Vector2(0f, 1f));

            serverAddressInput = FindInputFieldByName("ServerAddressInput") ?? CreateInput(panel, "ServerAddressInput",
                liveClient != null ? liveClient.ServerAddress : "127.0.0.1:8888",
                "server:port", font, Vector2.zero, new Vector2(260f, 30f));
            serverAddressInput.transform.SetParent(panel, false);
            SetChildRect(serverAddressInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -52f), new Vector2(260f, 30f), new Vector2(0f, 1f));
            StyleInputField(serverAddressInput, font);

            connectLiveButton = FindButtonByName("ConnectLiveButton") ?? CreateButton(panel, "ConnectLiveButton", "连接", font, Vector2.zero, new Vector2(92f, 30f), new Color(0.12f, 0.48f, 0.32f, 1f));
            connectLiveButton.transform.SetParent(panel, false);
            SetChildRect(connectLiveButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(366f, -52f), new Vector2(92f, 30f), new Vector2(0f, 1f));
            StyleButton(connectLiveButton, "连接", new Color(0.12f, 0.48f, 0.32f, 1f), font);

            disconnectLiveButton = FindButtonByName("DisconnectLiveButton") ?? CreateButton(panel, "DisconnectLiveButton", "断开", font, Vector2.zero, new Vector2(118f, 30f), new Color(0.48f, 0.18f, 0.18f, 1f));
            disconnectLiveButton.transform.SetParent(panel, false);
            SetChildRect(disconnectLiveButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(470f, -52f), new Vector2(118f, 30f), new Vector2(0f, 1f));
            StyleButton(disconnectLiveButton, "断开", new Color(0.48f, 0.18f, 0.18f, 1f), font);
        }

        private void ConfigureHudVisualStyle()
        {
            LayoutRightInfoPanels();
            StylePanel("HUD_TopBar", new Color(0.020f, 0.032f, 0.050f, 0.95f));
            StylePanel("HUD_ScorePanel", new Color(0.035f, 0.060f, 0.085f, 0.88f));
            StylePanel("HUD_EventPanel", new Color(0.035f, 0.060f, 0.085f, 0.88f));
            StylePanel("HUD_InspectorPanel", new Color(0.035f, 0.060f, 0.085f, 0.88f));
            StylePanel("HUD_ControlPanel", new Color(0.020f, 0.032f, 0.050f, 0.94f));
            StylePanel("HUD_SourcePanel", new Color(0.026f, 0.043f, 0.065f, 0.94f));
            LayoutFrameInfoText();
            StyleText("HUD_ScoreTitle", 22, FontStyle.Bold, new Color(0.30f, 0.88f, 0.98f, 1f), TextAnchor.MiddleLeft);
            StyleText("HUD_EventTitle", 20, FontStyle.Bold, new Color(0.30f, 0.88f, 0.98f, 1f), TextAnchor.MiddleLeft);
            StyleText("HUD_InspectorTitle", 20, FontStyle.Bold, new Color(1.00f, 0.76f, 0.30f, 1f), TextAnchor.MiddleLeft);
            StyleText("HUD_TitleText", 30, FontStyle.Bold, new Color(1.00f, 0.78f, 0.34f, 1f), TextAnchor.MiddleLeft);
            StyleText("GameStateText", 18, FontStyle.Normal, new Color(0.88f, 0.94f, 0.98f, 1f), TextAnchor.MiddleRight);
            StyleText("AIEventText", 16, FontStyle.Normal, new Color(0.88f, 0.94f, 0.98f, 1f), TextAnchor.UpperLeft);
            StyleText("AIEffectText", 16, FontStyle.Normal, new Color(0.88f, 0.94f, 0.98f, 1f), TextAnchor.UpperLeft);
            StyleText("SelectionInfoText", 16, FontStyle.Normal, new Color(0.88f, 0.94f, 0.98f, 1f), TextAnchor.UpperLeft);
        }

        private static void LayoutRightInfoPanels()
        {
            LayoutTopRightPanel("HUD_ScorePanel", new Vector2(-24f, -108f), new Vector2(460f, 420f));
            LayoutTopRightPanel("HUD_InspectorPanel", new Vector2(-24f, -552f), new Vector2(460f, 230f));
        }

        private static void LayoutFrameInfoText()
        {
            Text text = FindTextByName("FrameInfoText");
            if (text == null)
            {
                return;
            }

            RectTransform rect = text.rectTransform;
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -46f);
            rect.sizeDelta = new Vector2(420f, 24f);
            text.fontSize = 15;
            text.alignment = TextAnchor.MiddleCenter;
        }

        private static void LayoutTopRightPanel(string objectName, Vector2 anchoredPosition, Vector2 size)
        {
            GameObject go = GameObject.Find(objectName);
            if (go == null)
            {
                return;
            }

            RectTransform rect = go.GetComponent<RectTransform>();
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
        }

        private static void StylePanel(string objectName, Color color)
        {
            GameObject go = GameObject.Find(objectName);
            if (go == null)
            {
                return;
            }

            Image image = go.GetComponent<Image>();
            if (image == null)
            {
                return;
            }

            image.color = color;
            image.raycastTarget = false;
        }

        private static void StyleText(string objectName, int fontSize, FontStyle fontStyle, Color color, TextAnchor alignment)
        {
            Text text = FindTextByName(objectName);
            if (text == null)
            {
                return;
            }

            Font font = GetBuiltInUIFont();
            if (font != null)
            {
                text.font = font;
            }
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;
            text.raycastTarget = false;
            EnsureTextShadow(text, new Color(0f, 0f, 0f, 0.52f), new Vector2(1f, -1f));
        }

        private void RefreshRecentReplayDropdown()
        {
            LoadRecentReplayPaths();
            AddDiscoveredReplayPaths();

            if (recentReplayDropdown == null)
            {
                return;
            }

            suppressRecentReplayCallback = true;
            recentReplayDropdown.ClearOptions();

            if (recentReplayPaths.Count == 0)
            {
                recentReplayDropdown.AddOptions(new List<string> { "最近回放：暂无" });
                recentReplayDropdown.interactable = false;
                suppressRecentReplayCallback = false;
                return;
            }

            List<string> options = new List<string>();
            for (int i = 0; i < recentReplayPaths.Count; i++)
            {
                options.Add(BuildRecentReplayLabel(recentReplayPaths[i]));
            }

            recentReplayDropdown.AddOptions(options);
            recentReplayDropdown.interactable = true;
            recentReplayDropdown.SetValueWithoutNotify(FindRecentIndex(playbackPathInput != null ? playbackPathInput.text : null));
            recentReplayDropdown.RefreshShownValue();
            suppressRecentReplayCallback = false;
        }

        private void LoadRecentReplayPaths()
        {
            recentReplayPaths.Clear();
            string stored = PlayerPrefs.GetString(RecentReplayPrefsKey, string.Empty);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                string[] paths = stored.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < paths.Length; i++)
                {
                    AddRecentReplayPathInMemory(paths[i], false);
                }
            }

            if (playbackController != null && !string.IsNullOrWhiteSpace(playbackController.playbackFilePath))
            {
                string resolved = ResolvePlaybackPathForUi(playbackController.playbackFilePath);
                if (File.Exists(resolved))
                {
                    AddRecentReplayPathInMemory(resolved, false);
                }
            }
        }

        private void AddDiscoveredReplayPaths()
        {
            foreach (string directory in GetReplaySearchDirectories())
            {
                if (recentReplayPaths.Count >= MaxRecentReplayCount || string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                {
                    continue;
                }

                try
                {
                    List<string> discoveredFiles = new List<string>();
                    foreach (string file in Directory.EnumerateFiles(directory, "*.thuaipb", SearchOption.AllDirectories))
                    {
                        discoveredFiles.Add(file);
                        if (discoveredFiles.Count >= MaxReplayDiscoveryScanCount)
                        {
                            break;
                        }
                    }

                    discoveredFiles.Sort((left, right) => File.GetLastWriteTimeUtc(right).CompareTo(File.GetLastWriteTimeUtc(left)));
                    for (int i = 0; i < discoveredFiles.Count && recentReplayPaths.Count < MaxRecentReplayCount; i++)
                    {
                        AddRecentReplayPathInMemory(discoveredFiles[i], false);
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Replay discovery skipped for {directory}: {ex.Message}");
                }
            }
        }

        private void AddRecentReplayPath(string path)
        {
            AddRecentReplayPathInMemory(path, true);
            SaveRecentReplayPaths();
            RefreshRecentReplayDropdown();
        }

        private void AddRecentReplayPathInMemory(string path, bool moveToTop)
        {
            string resolved = ResolvePlaybackPathForUi(path);
            if (string.IsNullOrWhiteSpace(resolved))
            {
                return;
            }

            for (int i = recentReplayPaths.Count - 1; i >= 0; i--)
            {
                if (PathsEqual(recentReplayPaths[i], resolved))
                {
                    if (moveToTop)
                    {
                        recentReplayPaths.RemoveAt(i);
                        recentReplayPaths.Insert(0, resolved);
                    }
                    return;
                }
            }

            if (moveToTop)
            {
                recentReplayPaths.Insert(0, resolved);
            }
            else
            {
                recentReplayPaths.Add(resolved);
            }

            while (recentReplayPaths.Count > MaxRecentReplayCount)
            {
                recentReplayPaths.RemoveAt(recentReplayPaths.Count - 1);
            }
        }

        private void SaveRecentReplayPaths()
        {
            PlayerPrefs.SetString(RecentReplayPrefsKey, string.Join("\n", recentReplayPaths));
            PlayerPrefs.Save();
        }

        private int FindRecentIndex(string path)
        {
            string resolved = ResolvePlaybackPathForUi(path);
            for (int i = 0; i < recentReplayPaths.Count; i++)
            {
                if (PathsEqual(recentReplayPaths[i], resolved))
                {
                    return i;
                }
            }

            return 0;
        }

        private static IEnumerable<string> GetReplaySearchDirectories()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            yield return Path.Combine(projectRoot, "Assets", "Playback");
            yield return Path.Combine(projectRoot, "Playback");
            yield return Path.Combine(projectRoot, "playback");
        }

        private static string GetPlaybackPickerStartDirectory()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string assetsPlayback = Path.Combine(projectRoot, "Assets", "Playback");
            if (Directory.Exists(assetsPlayback))
            {
                return assetsPlayback;
            }

            return projectRoot;
        }

        private static string ResolvePlaybackPathForUi(string rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return rawPath;
            }

            string normalized = rawPath.Trim().Trim('"').Replace('\\', '/');
            if (!normalized.EndsWith(PlayBackConstant.PLAYBACK_EXTENSION, StringComparison.OrdinalIgnoreCase))
            {
                normalized += PlayBackConstant.PLAYBACK_EXTENSION;
            }

            if (Path.IsPathRooted(normalized))
            {
                return Path.GetFullPath(normalized);
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.GetFullPath(Path.Combine(projectRoot, normalized));
        }

        private static bool PathsEqual(string left, string right)
        {
            if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
            {
                return false;
            }

            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLegacyDefaultReplayPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string normalized = path.Trim().Trim('"').Replace('\\', '/');
            return normalized.Equals("Assets/Playback/test/official_bot_match.thuaipb", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/Assets/Playback/test/official_bot_match.thuaipb", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("Assets/Playback/test/test_replay.thuaipb", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/Assets/Playback/test/test_replay.thuaipb", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("test_replay.thuaipb", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDefaultReplayHint(string text)
        {
            return string.IsNullOrWhiteSpace(text)
                || text.Trim().StartsWith("回放：可输入路径", StringComparison.Ordinal);
        }

        private static string BuildRecentReplayLabel(string path)
        {
            string fileName = Path.GetFileName(path);
            string displayPath = ShortenPathForDisplay(path);
            return string.IsNullOrWhiteSpace(fileName) ? displayPath : $"{fileName}  —  {displayPath}";
        }

        private static string ShortenPathForDisplay(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalized = path.Replace('\\', '/');
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
            if (normalized.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalized.Substring(projectRoot.Length + 1);
            }

            return normalized;
        }

        private void SetReplayHint(string message, bool isError)
        {
            if (replayHintText == null)
            {
                return;
            }

            replayHintText.text = message;
            replayHintText.color = isError
                ? new Color(1.00f, 0.46f, 0.34f, 1f)
                : new Color(0.74f, 0.92f, 0.82f, 1f);
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

        private static void SetNamedGameObjectActive(string objectName, bool active)
        {
            GameObject go = GameObject.Find(objectName);
            if (go != null)
            {
                go.SetActive(active);
            }
        }


        private static Font GetBuiltInUIFont()
        {
            if (cachedUiFont != null)
            {
                return cachedUiFont;
            }

            cachedUiFont = Resources.Load<Font>(CjkFontResourcePath);
            if (cachedUiFont != null)
            {
                return cachedUiFont;
            }

            try
            {
                Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null)
                {
                    cachedUiFont = font;
                    return cachedUiFont;
                }
            }
            catch
            {
            }

            try
            {
                cachedUiFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
                return cachedUiFont;
            }
            catch
            {
                return null;
            }
        }

        private static void ApplyFontToSceneTexts()
        {
            Font font = GetBuiltInUIFont();
            if (font == null)
            {
                return;
            }

            foreach (Text text in FindObjectsOfType<Text>(true))
            {
                text.font = font;
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
            text.fontSize = 15;
            return button;
        }

        private static Dropdown CreateDropdown(Transform parent, string name, Font font, Vector2 position, Vector2 size)
        {
            GameObject go = DefaultControls.CreateDropdown(new DefaultControls.Resources());
            go.name = name;
            go.transform.SetParent(parent, false);

            RectTransform rect = go.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Dropdown dropdown = go.GetComponent<Dropdown>();
            dropdown.ClearOptions();
            StyleDropdown(dropdown, font);
            return dropdown;
        }

        private static Text FindOrCreateLabel(Transform parent, string name, string text, Font font)
        {
            Text label = FindTextByName(name);
            if (label != null)
            {
                label.transform.SetParent(parent, false);
                ConfigureLabel(label, text, font);
                return label;
            }

            label = CreateLabel(parent, name, text, font, Vector2.zero, new Vector2(70f, 28f));
            ConfigureLabel(label, text, font);
            return label;
        }

        private static void ConfigureLabel(Text label, string fallbackText, Font font)
        {
            if (!string.IsNullOrWhiteSpace(fallbackText))
            {
                label.text = fallbackText;
            }
            else if (string.IsNullOrWhiteSpace(label.text))
            {
                label.text = fallbackText;
            }

            label.font = font;
            label.fontSize = 15;
            label.fontStyle = FontStyle.Bold;
            label.alignment = TextAnchor.MiddleLeft;
            label.color = new Color(0.30f, 0.88f, 0.98f, 1f);
            label.raycastTarget = false;
        }

        private static RectTransform FindOrCreateRect(Transform parent, string name, params Type[] components)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                return existing.GetComponent<RectTransform>() ?? existing.gameObject.AddComponent<RectTransform>();
            }

            List<Type> componentTypes = new List<Type> { typeof(RectTransform) };
            if (components != null)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] != null && !componentTypes.Contains(components[i]))
                    {
                        componentTypes.Add(components[i]);
                    }
                }
            }

            GameObject go = new GameObject(name, componentTypes.ToArray());
            go.transform.SetParent(parent, false);
            return go.GetComponent<RectTransform>();
        }

        private static void SetChildRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPosition, Vector2 sizeDelta, Vector2 pivot)
        {
            if (rect == null)
            {
                return;
            }

            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
        }

        private static void StyleInputField(InputField input, Font font)
        {
            if (input == null)
            {
                return;
            }

            Image image = input.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.080f, 0.115f, 0.155f, 0.98f);
            }

            if (input.textComponent != null)
            {
                input.textComponent.font = font;
                input.textComponent.fontSize = 15;
                input.textComponent.color = new Color(0.92f, 0.97f, 1f, 1f);
                input.textComponent.alignment = TextAnchor.MiddleLeft;
                input.textComponent.horizontalOverflow = HorizontalWrapMode.Overflow;
                input.textComponent.verticalOverflow = VerticalWrapMode.Truncate;
            }

            Text placeholder = input.placeholder as Text;
            if (placeholder != null)
            {
                placeholder.font = font;
                placeholder.fontSize = 15;
                placeholder.color = new Color(0.55f, 0.66f, 0.74f, 0.85f);
                placeholder.alignment = TextAnchor.MiddleLeft;
            }
        }

        private static void StyleButton(Button button, string label, Color color, Font font)
        {
            if (button == null)
            {
                return;
            }

            Image image = button.GetComponent<Image>();
            if (image != null)
            {
                image.color = color;
            }

            ColorBlock colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.18f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.25f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(color.r, color.g, color.b, 0.45f);
            button.colors = colors;

            Text text = button.GetComponentInChildren<Text>(true);
            if (text != null)
            {
                text.text = label;
                text.font = font;
                text.fontSize = 15;
                text.fontStyle = FontStyle.Bold;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = Color.white;
                text.raycastTarget = false;
            }
        }

        private static void StyleDropdown(Dropdown dropdown, Font font)
        {
            if (dropdown == null)
            {
                return;
            }

            Image image = dropdown.GetComponent<Image>();
            if (image != null)
            {
                image.color = new Color(0.080f, 0.115f, 0.155f, 0.98f);
            }

            if (dropdown.captionText != null)
            {
                dropdown.captionText.font = font;
                dropdown.captionText.fontSize = 15;
                dropdown.captionText.alignment = TextAnchor.MiddleLeft;
                dropdown.captionText.color = new Color(0.92f, 0.97f, 1f, 1f);
                dropdown.captionText.horizontalOverflow = HorizontalWrapMode.Overflow;
            }

            if (dropdown.itemText != null)
            {
                dropdown.itemText.font = font;
                dropdown.itemText.fontSize = 15;
                dropdown.itemText.color = new Color(0.08f, 0.10f, 0.13f, 1f);
            }
        }

        private static void EnsureTextShadow(Text text, Color color, Vector2 distance)
        {
            if (text == null)
            {
                return;
            }

            Shadow shadow = text.GetComponent<Shadow>();
            if (shadow == null)
            {
                shadow = text.gameObject.AddComponent<Shadow>();
            }

            shadow.effectColor = color;
            shadow.effectDistance = distance;
            shadow.useGraphicAlpha = true;
        }

        private static Color GetTeamAccentColor(int teamIndex)
        {
            switch (teamIndex)
            {
                case 0:
                    return new Color(1.00f, 0.06f, 0.06f, 1f);
                case 1:
                    return new Color(0.08f, 1.00f, 0.12f, 1f);
                case 2:
                    return new Color(0.06f, 0.34f, 1.00f, 1f);
                default:
                    return new Color(1.00f, 0.95f, 0.06f, 1f);
            }
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

            if (browsePlaybackButton != null)
            {
                browsePlaybackButton.onClick.RemoveListener(OnBrowsePlaybackClicked);
            }

            if (recentReplayDropdown != null)
            {
                recentReplayDropdown.onValueChanged.RemoveListener(OnRecentReplayChanged);
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
