using System;
using System.Collections.Generic;
using System.IO;
using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Live;
using THUAI9.Unity.Playback;
using THUAI9.Unity.Render;
using THUAI9.Unity.WebGL;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI
{
    public class UIController : MonoBehaviour
    {
        private const float TeamStatusRightMargin = 24f;
        private const float TeamStatusTopMargin = 156f;
        private const float TeamStatusWidth = 292f;
        private const float TeamStatusHeight = 182f;
        private const float TeamStatusColumnSpacing = 12f;
        private const float TeamStatusRowSpacing = 12f;
        private const float TeamStatusContentHeight = 660f;
        private const string RecentReplayPrefsKey = "ReplayRecentPaths";
        private const int MaxRecentReplayCount = 8;
        private const int MaxReplayDiscoveryScanCount = 128;
        private const string CjkFontResourcePath = "Fonts/NotoSansCJKsc-Regular";
        private const int EventPanelMaxCharsPerLine = 28;
        private const int AIEventPanelMaxLines = 4;
        private const int AIEffectPanelMaxLines = 3;
        private const int HoverInfoMaxCharsPerLine = 32;
        private const int HoverInfoMaxBodyLines = 11;
        private const float HoverInfoWidth = 360f;
        private const float HoverInfoHeight = 248f;
        private static readonly Vector2 HoverInfoOffset = new Vector2(18f, -18f);
        private static readonly float[] PlaybackSpeedValues = { 0.5f, 1f, 2f, 4f };
        private static readonly string[] PlaybackSpeedLabels = { "0.5x", "1x", "2x", "4x" };
        private static readonly string[] HoverBlockingUiNames =
        {
            "HUD_TopBar",
            "HUD_SourcePanel",
            "HUD_ControlPanel",
            "HUD_ScorePanel",
            "HUD_EventPanel",
            "HUD_EventLogPanel",
            "HUD_PlayerPanel",
            "HUD_PlayerPanelToggle"
        };
        private static readonly Color TeamStatusBodyColor = new Color(0.88f, 0.94f, 0.98f, 1f);
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
        public Text statusText;
        public Text gameStateText;
        public Text aiEventText;
        public Text aiEffectText;

        [Header("自动按名称补全引用")]
        public bool autoBindSceneReferences = true;

        [Header("实时观战")]
        public bool autoConnectLiveOnStart = false;
        public bool showWorldHoverInfo = true;

        private Playback.PlaybackController playbackController;
        private LiveSpectatorClient liveClient;
        private WorldSelectionController worldSelectionController;
        private EventLogPanelController eventLogPanelController;
        private Canvas rootCanvas;
        private RectTransform hoverInfoPanel;
        private Text hoverInfoTitleText;
        private Text hoverInfoBodyText;
        private RectTransform[] hoverBlockingRects = Array.Empty<RectTransform>();
        private WorldObjectInfo lastHoverInfo;
        private int lastHoverFrame = -1;
        private string lastHoverTitle;
        private string lastHoverDetail;
        private bool suppressProgressCallback;
        private bool suppressRecentReplayCallback;
        private bool hasAutoStartedLive;
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
                DestroyNamedGameObjectIfExists("FrameInfoText");
                EnsureRuntimeSourceControls();
                AutoBindIfNeeded();
                ConfigureHudVisualStyle();
                ConfigureTeamStatusLayout();
                ApplyFontToSceneTexts();
                RefreshRecentReplayDropdown();
            }

            EnsureWorldSelectionController();
            EnsureEventLogPanel();
            EnsureHoverInfoPanel();
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
                ConfigureSpeedDropdown();
                speedDropdown.onValueChanged.AddListener(OnSpeedChanged);
                OnSpeedChanged(speedDropdown.value);
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

            UpdatePauseButtonText("暂停");
            UpdateStaticTextFallbacks();
            ApplyFontToSceneTexts();
            StartLiveAutomatically();
        }

        private void Update()
        {
            UpdateScoreAndTimeUI();
            UpdateDebugUI();
            UpdateWorldHoverInfoPanel();
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
            progressSlider ??= FindSliderByName("ReplayProgressSlider") ?? FindSliderByName("ProgressSlider");
            previousFrameButton ??= FindButtonByName("PreviousFrameButton");
            nextFrameButton ??= FindButtonByName("NextFrameButton");
            statusText ??= FindTextByName("StatusText");
            gameStateText ??= FindTextByName("GameStateText");
            aiEventText ??= FindTextByName("AIEventText");
            aiEffectText ??= FindTextByName("AIEffectText");
            pauseButtonText ??= FindTextByName("PauseButtonText") ?? pauseButton?.GetComponentInChildren<Text>(true);
            browsePlaybackButton ??= FindButtonByName("BrowseReplayButton");
            recentReplayDropdown ??= FindDropdownByName("RecentReplayDropdown");
            replayHintText ??= FindTextByName("ReplayHintText");

            if (serverAddressInput != null && string.IsNullOrWhiteSpace(serverAddressInput.text) && liveClient != null)
            {
                serverAddressInput.text = liveClient.ServerAddress;
            }

            DisableServerConnectionControls();
        }

        private void ConfigureSpeedDropdown()
        {
            if (speedDropdown == null)
            {
                return;
            }

            int selectedIndex = GetNearestSpeedIndex(playbackController != null ? playbackController.playSpeed : 1f);
            speedDropdown.ClearOptions();
            speedDropdown.AddOptions(new List<string>(PlaybackSpeedLabels));
            speedDropdown.interactable = true;
            StyleDropdown(speedDropdown, GetBuiltInUIFont(), 18);
            speedDropdown.SetValueWithoutNotify(selectedIndex);
            speedDropdown.RefreshShownValue();
        }

        private static int GetNearestSpeedIndex(float speed)
        {
            int bestIndex = 1;
            float bestDelta = float.MaxValue;
            for (int i = 0; i < PlaybackSpeedValues.Length; i++)
            {
                float delta = Mathf.Abs(PlaybackSpeedValues[i] - speed);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void UpdateStaticTextFallbacks()
        {
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
            return CoreParam.stableLiveGameMilliseconds;
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
                    string eventName = LocalizeLiveEventName(liveClient.CurrentEventName);
                    string eventDescription = LocalizeLiveEventDescription(liveClient.CurrentEventName, liveClient.CurrentEventDescription);
                    aiEventText.text = BuildBoundedPanelText(
                        AIEventPanelMaxLines,
                        $"事件状态：{eventName}",
                        eventDescription);
                }
                else if (CoreParam.latestAIEvent == null)
                {
                    aiEventText.text = "AI\u4e8b\u4ef6\uff1a\u6682\u65e0";
                }
                else
                {
                    GlobalAIEvent e = CoreParam.latestAIEvent;
                    aiEventText.text = BuildBoundedPanelText(
                        AIEventPanelMaxLines,
                        $"AI事件：{TranslateAIEventCategory(e.Category)}",
                        LocalizeEventPanelText(e.Title, "事件详情"),
                        LocalizeEventPanelText(e.Description, "事件影响已生效"));
                }
            }

            if (aiEffectText != null)
            {
                aiEffectText.text = CoreParam.latestAIEffect == null
                    ? "\u4e16\u754c\u4fee\u6b63\uff1a\u6682\u65e0"
                    : BuildBoundedPanelText(
                        AIEffectPanelMaxLines,
                        $"世界修正：持续 {CoreParam.latestAIEffect.DurationMs / 1000f:0.#}s",
                        FormatAIEffect(CoreParam.latestAIEffect));
            }
        }

        private static string BuildBoundedPanelText(int maxLines, params string[] rawLines)
        {
            List<string> lines = new List<string>(maxLines);
            bool truncated = false;

            for (int i = 0; i < rawLines.Length; i++)
            {
                string remaining = NormalizePanelLine(rawLines[i]);
                if (string.IsNullOrEmpty(remaining))
                {
                    continue;
                }

                while (remaining.Length > 0)
                {
                    if (lines.Count >= maxLines)
                    {
                        truncated = true;
                        break;
                    }

                    int take = Mathf.Min(EventPanelMaxCharsPerLine, remaining.Length);
                    if (take < remaining.Length)
                    {
                        int wrapIndex = FindPanelWrapIndex(remaining, take);
                        if (wrapIndex > 0)
                        {
                            take = wrapIndex;
                        }
                    }

                    string segment = remaining.Substring(0, take).Trim();
                    if (!string.IsNullOrEmpty(segment))
                    {
                        lines.Add(segment);
                    }

                    remaining = remaining.Substring(take).TrimStart();
                    if (remaining.Length > 0 && lines.Count >= maxLines)
                    {
                        truncated = true;
                        break;
                    }
                }

                if (truncated)
                {
                    break;
                }
            }

            if (lines.Count == 0)
            {
                return string.Empty;
            }

            if (truncated)
            {
                int last = lines.Count - 1;
                string line = lines[last].TrimEnd('…');
                if (line.Length >= EventPanelMaxCharsPerLine)
                {
                    line = line.Substring(0, EventPanelMaxCharsPerLine - 1).TrimEnd();
                }
                lines[last] = line + "…";
            }

            return string.Join("\n", lines);
        }

        private static string NormalizePanelLine(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return string.Empty;
            }

            return raw
                .Replace('\r', ' ')
                .Replace('\n', ' ')
                .Replace('\t', ' ')
                .Trim();
        }

        private static int FindPanelWrapIndex(string text, int maxChars)
        {
            int limit = Mathf.Min(maxChars, text.Length - 1);
            for (int i = limit; i > 0; i--)
            {
                char ch = text[i];
                if (char.IsWhiteSpace(ch) || ch == '，' || ch == '。' || ch == '、' || ch == '；' || ch == ';' || ch == '：' || ch == ':' || ch == '｜' || ch == '|')
                {
                    return i + 1;
                }
            }

            return maxChars;
        }

        private static string FormatTeamStatus(int teamIndex, MessageOfAll.Types.TeamInfo team)
        {
            string techSummary = FormatTeamTechLevels(team.TechLevels);
            string uuidSummary = FormatTeamUuidSummary(teamIndex);
            string memberStatus = FormatTeamMemberStatus(teamIndex);
            return BuildTeamStatusText(
                teamIndex,
                team.Score.ToString(),
                team.Material.ToString(),
                team.ComputePower.ToString(),
                team.FactoryHp.ToString(),
                techSummary,
                uuidSummary,
                memberStatus);
        }

        private static string FormatWaitingTeamStatus(int teamIndex)
        {
            return BuildTeamStatusText(
                teamIndex,
                "0",
                "--",
                "--",
                "--",
                "暂无",
                "等待角色创建",
                "等待首帧");
        }

        private static string BuildTeamStatusText(
            int teamIndex,
            string score,
            string material,
            string computePower,
            string factoryHp,
            string techSummary,
            string uuidSummary,
            string memberStatus)
        {
            string accent = ColorUtility.ToHtmlStringRGB(GetTeamAccentColor(teamIndex - 1));
            return
                $"<size=20><b><color=#{accent}>队伍 {teamIndex}</color>{WideGap(2)}得分：{score}</b></size>\n" +
                $"原料：{material}{WideGap(2)}算力：{computePower}\n" +
                $"工厂血量：{factoryHp}\n" +
                $"科技等级：{techSummary}\n" +
                "<b>成员</b>\n" +
                $"<size=14>{uuidSummary}</size>\n" +
                "<b>成员状态</b>\n" +
                $"<size=14>{memberStatus}</size>";
        }

        private static string WideGap(int count)
        {
            return new string('\u3000', Mathf.Max(count, 0));
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
            int visibleCount = members.Count;
            for (int i = 0; i < visibleCount; i++)
            {
                TeamMemberUuidInfo member = members[i];
                string playerLabel = member.PlayerId > 0 ? $"P{member.PlayerId}" : "P?";
                string uuidLabel = member.Guid > 0 ? member.Guid.ToString() : "暂无";
                parts.Add($"{playerLabel}=uuid {uuidLabel}");
            }

            return string.Join("\n", parts);
        }

        private static string FormatTeamMemberStatus(int teamIndex)
        {
            var members = new List<MessageOfCharacter>();

            foreach (MessageOfCharacter character in CoreParam.characters.Values)
            {
                if (character == null || character.TeamId != teamIndex)
                {
                    continue;
                }

                members.Add(character);
            }

            if (members.Count == 0)
            {
                return "暂无成员上报";
            }

            members.Sort((left, right) =>
            {
                int byPlayer = left.PlayerId.CompareTo(right.PlayerId);
                return byPlayer != 0 ? byPlayer : left.Guid.CompareTo(right.Guid);
            });

            var lines = new List<string>(members.Count);
            for (int i = 0; i < members.Count; i++)
            {
                MessageOfCharacter member = members[i];
                string playerLabel = member.PlayerId > 0 ? $"P{member.PlayerId}" : "P?";
                string typeLabel = TranslateCharacterType(member.CharacterType);
                string stateLabel = TranslateCharacterState(member.CharacterActiveState);
                string position = $"({member.X / 1000f:0.0},{member.Y / 1000f:0.0})";
                lines.Add($"{playerLabel} {typeLabel} 生命 {member.Hp} 坐标 {position} {stateLabel}");
            }

            return string.Join("\n", lines);
        }

        private static string TranslateCharacterType(CharacterType type)
        {
            return type switch
            {
                CharacterType.Drone => "无人机",
                CharacterType.Robot => "机器人",
                CharacterType.AutonomousCar => "无人车",
                _ => "未知单位"
            };
        }

        private static string TranslateCharacterState(CharacterState state)
        {
            return state switch
            {
                CharacterState.None => "空闲",
                CharacterState.Idle => "空闲",
                CharacterState.Harvesting => "采集中",
                CharacterState.Attacking => "攻击中",
                CharacterState.Ocuppying => "占领中",
                CharacterState.Trading => "交易中",
                CharacterState.Moving => "移动中",
                CharacterState.KnockedBack => "被击退",
                CharacterState.Deceased => "已死亡",
                _ => "未知"
            };
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

                text.alignment = TextAnchor.UpperLeft;
                text.font = GetBuiltInUIFont();
                text.fontSize = 16;
                text.fontStyle = FontStyle.Bold;
                text.color = TeamStatusBodyColor;
                text.resizeTextForBestFit = false;
                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Overflow;
                text.lineSpacing = 1.02f;
                text.supportRichText = true;
                text.raycastTarget = false;
                EnsureTextShadow(text, new Color(0f, 0f, 0f, 0.72f), new Vector2(1.2f, -1.2f));
                ConfigureTeamStatusCard(text, i);
            }
        }

        private void ConfigureTeamStatusCard(Text text, int teamIndex)
        {
            if (text == null || text.transform.parent == null)
            {
                return;
            }

            Transform parent = text.transform.root;
            if (parent == null)
            {
                parent = text.transform.parent;
            }

            Color accent = GetTeamAccentColor(teamIndex);
            RectTransform card = FindOrCreateRect(parent, $"TeamStatusCard{teamIndex + 1}", typeof(Image), typeof(ScrollRect));
            card.anchorMin = new Vector2(1f, 1f);
            card.anchorMax = new Vector2(1f, 1f);
            card.pivot = new Vector2(1f, 1f);
            int row = teamIndex / 2;
            int column = teamIndex % 2;
            float x = -TeamStatusRightMargin - (1 - column) * (TeamStatusWidth + TeamStatusColumnSpacing);
            float y = -TeamStatusTopMargin - row * (TeamStatusHeight + TeamStatusRowSpacing);
            card.anchoredPosition = new Vector2(x, y);
            card.sizeDelta = new Vector2(TeamStatusWidth, TeamStatusHeight);

            Image cardImage = card.GetComponent<Image>();
            cardImage.color = new Color(0.035f, 0.060f, 0.085f, 0.90f);
            cardImage.raycastTarget = true;

            RectTransform stripe = FindOrCreateRect(card.transform, "AccentStripe", typeof(Image));
            stripe.anchorMin = new Vector2(0f, 0f);
            stripe.anchorMax = new Vector2(0f, 1f);
            stripe.pivot = new Vector2(0f, 0.5f);
            stripe.anchoredPosition = Vector2.zero;
            stripe.sizeDelta = new Vector2(5f, 0f);
            Image stripeImage = stripe.GetComponent<Image>();
            stripeImage.color = accent;
            stripeImage.raycastTarget = false;

            RectTransform viewport = FindOrCreateRect(card.transform, "Viewport", typeof(Image), typeof(Mask));
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.offsetMin = new Vector2(16f, 10f);
            viewport.offsetMax = new Vector2(-12f, -12f);
            Image viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportImage.raycastTarget = true;
            Mask mask = viewport.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            RectTransform content = FindOrCreateRect(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(0f, TeamStatusContentHeight);

            text.transform.SetParent(content, false);
            RectTransform textRect = text.rectTransform;
            textRect.anchorMin = new Vector2(0f, 1f);
            textRect.anchorMax = new Vector2(1f, 1f);
            textRect.pivot = new Vector2(0f, 1f);
            textRect.anchoredPosition = Vector2.zero;
            textRect.sizeDelta = new Vector2(0f, TeamStatusContentHeight - 10f);

            ScrollRect scrollRect = card.GetComponent<ScrollRect>();
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 24f;
            scrollRect.verticalNormalizedPosition = 1f;

            card.SetAsLastSibling();
        }

        private void OnPlayClicked()
        {
            ClearCurrentUiSelection();
            StopLiveIfNeeded();
            playbackController?.Play();
            UpdatePauseButtonText("暂停");
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
            UpdatePauseButtonText("暂停");
        }

        private void OnSpeedChanged(int index)
        {
            int safeIndex = Mathf.Clamp(index, 0, PlaybackSpeedValues.Length - 1);
            float speed = PlaybackSpeedValues[safeIndex];

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
            UpdatePauseButtonText("暂停");
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
            SetReplayHint("实时观战：正在连接；若服务端尚未启动会自动重试。", false);
            UpdatePauseButtonText("暂停");
        }

        private void OnDisconnectLiveClicked()
        {
            ClearCurrentUiSelection();
            liveClient?.StopLive();
            if (playbackController != null && playbackController.PlaybackLoaded)
            {
                playbackController.Stop();
            }
            UpdatePauseButtonText("暂停");
        }

        private void OnPreviousFrameClicked()
        {
            ClearCurrentUiSelection();
            StopLiveIfNeeded();
            playbackController?.StepBackward();
            UpdatePauseButtonText("暂停");
        }

        private void OnNextFrameClicked()
        {
            ClearCurrentUiSelection();
            StopLiveIfNeeded();
            playbackController?.StepForward();
            UpdatePauseButtonText("暂停");
        }

        private void OnProgressChanged(float value)
        {
            if (suppressProgressCallback || playbackController == null || (liveClient != null && liveClient.IsLiveMode))
            {
                return;
            }

            StopLiveIfNeeded();
            playbackController.SeekToFrame(Mathf.RoundToInt(value));
            UpdatePauseButtonText("暂停");
        }

        private void StopLiveIfNeeded()
        {
            if (liveClient != null && liveClient.IsLiveMode)
            {
                liveClient.StopLive();
            }
        }

        private void StartLiveAutomatically()
        {
            if (!autoConnectLiveOnStart || hasAutoStartedLive || liveClient == null)
            {
                return;
            }

            if (playbackController != null &&
                (playbackController.PlaybackLoaded || !string.IsNullOrWhiteSpace(playbackController.playbackFilePath)))
            {
                SetReplayHint("已检测到启动回放配置，实时自动连接已跳过。", false);
                return;
            }

            hasAutoStartedLive = true;
            string address = serverAddressInput != null ? serverAddressInput.text : liveClient.ServerAddress;
            liveClient.StartLive(address);
            SetReplayHint("实时观战：启动后自动连接，连接失败会自动重试。", false);
        }

        private void EnsureWorldSelectionController()
        {
            if (worldSelectionController == null)
            {
                worldSelectionController = FindObjectOfType<WorldSelectionController>();
            }

            if (worldSelectionController == null)
            {
                Canvas canvas = rootCanvas != null ? rootCanvas : FindObjectOfType<Canvas>();
                if (canvas != null)
                {
                    worldSelectionController = canvas.GetComponent<WorldSelectionController>() ?? canvas.gameObject.AddComponent<WorldSelectionController>();
                }
            }

            if (worldSelectionController != null)
            {
                worldSelectionController.targetCamera = Camera.main;
                worldSelectionController.enableHover = true;
            }
        }

        private void EnsureHoverInfoPanel()
        {
            Canvas canvas = rootCanvas != null ? rootCanvas : FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            rootCanvas = canvas;
            Font font = GetBuiltInUIFont();
            hoverInfoPanel = GameObject.Find("HUD_HoverInfoPanel")?.GetComponent<RectTransform>();
            if (hoverInfoPanel == null)
            {
                hoverInfoPanel = FindOrCreateRect(canvas.transform, "HUD_HoverInfoPanel", typeof(Image), typeof(CanvasGroup));
            }
            else
            {
                hoverInfoPanel.SetParent(canvas.transform, false);
            }

            hoverInfoPanel.anchorMin = Vector2.zero;
            hoverInfoPanel.anchorMax = Vector2.zero;
            hoverInfoPanel.pivot = new Vector2(0f, 1f);
            hoverInfoPanel.sizeDelta = new Vector2(HoverInfoWidth, HoverInfoHeight);

            Image image = hoverInfoPanel.GetComponent<Image>() ?? hoverInfoPanel.gameObject.AddComponent<Image>();
            image.color = new Color(0.018f, 0.028f, 0.042f, 0.95f);
            image.raycastTarget = false;

            CanvasGroup group = hoverInfoPanel.GetComponent<CanvasGroup>() ?? hoverInfoPanel.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            hoverInfoTitleText = FindOrCreateHoverText(hoverInfoPanel, "HUD_HoverInfoTitle", font);
            SetChildRect(hoverInfoTitleText.rectTransform, Vector2.up, Vector2.up, new Vector2(14f, -12f), new Vector2(HoverInfoWidth - 28f, 32f), Vector2.up);
            ConfigureHoverText(hoverInfoTitleText, 17, FontStyle.Bold, new Color(0.30f, 0.88f, 0.98f, 1f));

            hoverInfoBodyText = FindOrCreateHoverText(hoverInfoPanel, "HUD_HoverInfoBody", font);
            SetChildRect(hoverInfoBodyText.rectTransform, Vector2.up, Vector2.up, new Vector2(14f, -48f), new Vector2(HoverInfoWidth - 28f, HoverInfoHeight - 60f), Vector2.up);
            ConfigureHoverText(hoverInfoBodyText, 14, FontStyle.Normal, new Color(0.88f, 0.94f, 0.98f, 1f));

            hoverInfoPanel.gameObject.SetActive(false);
            RefreshHoverBlockingRects();
        }

        private void EnsureEventLogPanel()
        {
            Canvas canvas = rootCanvas != null ? rootCanvas : FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                return;
            }

            rootCanvas = canvas;
            eventLogPanelController = FindObjectOfType<EventLogPanelController>(true);
            if (eventLogPanelController == null)
            {
                RectTransform panel = FindOrCreateRect(canvas.transform, "HUD_EventLogPanel", typeof(Image));
                eventLogPanelController = panel.gameObject.AddComponent<EventLogPanelController>();
            }

            eventLogPanelController.Configure(canvas);
            RefreshHoverBlockingRects();
        }

        private static Text FindOrCreateHoverText(RectTransform parent, string name, Font font)
        {
            Transform existing = parent.Find(name);
            Text text = existing != null ? existing.GetComponent<Text>() : null;
            if (text == null)
            {
                RectTransform rect = FindOrCreateRect(parent, name, typeof(Text));
                text = rect.GetComponent<Text>();
            }

            text.font = font;
            text.raycastTarget = false;
            return text;
        }

        private static void ConfigureHoverText(Text text, int fontSize, FontStyle fontStyle, Color color)
        {
            if (text == null)
            {
                return;
            }

            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = false;
            text.supportRichText = false;
            text.lineSpacing = 1.04f;
            EnsureTextShadow(text, new Color(0f, 0f, 0f, 0.62f), new Vector2(1f, -1f));
        }

        private void UpdateWorldHoverInfoPanel()
        {
            if (!showWorldHoverInfo)
            {
                lastHoverInfo = null;
                SetHoverInfoVisible(false);
                return;
            }

            if (hoverInfoPanel == null || hoverInfoTitleText == null || hoverInfoBodyText == null)
            {
                EnsureHoverInfoPanel();
                if (hoverInfoPanel == null)
                {
                    return;
                }
            }

            EnsureWorldSelectionController();
            WorldObjectInfo info = worldSelectionController != null ? worldSelectionController.HoveredInfo : null;
            if (info == null || IsPointerOverAnyUiControl())
            {
                lastHoverInfo = null;
                SetHoverInfoVisible(false);
                return;
            }

            UpdateHoverInfoTextIfNeeded(info);
            PositionHoverInfoPanel();
            SetHoverInfoVisible(true);
        }

        private void UpdateHoverInfoTextIfNeeded(WorldObjectInfo info)
        {
            string title = string.IsNullOrWhiteSpace(info.title) ? info.objectType : info.title;
            string detail = info.detail ?? string.Empty;
            if (ReferenceEquals(info, lastHoverInfo) &&
                info.lastSeenFrame == lastHoverFrame &&
                string.Equals(title, lastHoverTitle, StringComparison.Ordinal) &&
                string.Equals(detail, lastHoverDetail, StringComparison.Ordinal))
            {
                return;
            }

            lastHoverInfo = info;
            lastHoverFrame = info.lastSeenFrame;
            lastHoverTitle = title;
            lastHoverDetail = detail;
            hoverInfoTitleText.text = title;
            hoverInfoBodyText.text = BuildHoverBodyText(info);
        }

        private static string BuildHoverBodyText(WorldObjectInfo info)
        {
            List<string> lines = new List<string>();
            string teamLabel = FormatHoverTeamLabel(info.teamId);
            if (!string.IsNullOrEmpty(teamLabel))
            {
                lines.Add($"队伍：{teamLabel}");
            }

            if (info.guid != 0)
            {
                lines.Add($"ID：{info.guid}");
            }

            if (info.gridX >= 0 && info.gridY >= 0)
            {
                lines.Add($"坐标：({info.gridX}, {info.gridY})");
            }

            if (info.lastSeenFrame > 0)
            {
                lines.Add($"最后更新帧：{info.lastSeenFrame}");
            }

            if (!string.IsNullOrWhiteSpace(info.detail))
            {
                string[] detailLines = info.detail.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                for (int i = 0; i < detailLines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(detailLines[i]))
                    {
                        lines.Add(detailLines[i].Trim());
                    }
                }
            }

            return BuildBoundedHoverText(lines);
        }

        private static string FormatHoverTeamLabel(long teamId)
        {
            return teamId switch
            {
                >= 1 and <= 4 => teamId.ToString(),
                0 => string.Empty,
                long.MaxValue => "未归属",
                _ => "未归属"
            };
        }

        private static string BuildBoundedHoverText(IEnumerable<string> sourceLines)
        {
            List<string> output = new List<string>();
            foreach (string rawLine in sourceLines)
            {
                foreach (string wrapped in WrapHoverLine(rawLine))
                {
                    if (output.Count >= HoverInfoMaxBodyLines)
                    {
                        output[HoverInfoMaxBodyLines - 1] = "…";
                        return string.Join("\n", output);
                    }

                    output.Add(wrapped);
                }
            }

            return string.Join("\n", output);
        }

        private static IEnumerable<string> WrapHoverLine(string line)
        {
            string safeLine = string.IsNullOrWhiteSpace(line) ? string.Empty : line.Trim();
            if (safeLine.Length <= HoverInfoMaxCharsPerLine)
            {
                yield return safeLine;
                yield break;
            }

            for (int start = 0; start < safeLine.Length; start += HoverInfoMaxCharsPerLine)
            {
                int length = Mathf.Min(HoverInfoMaxCharsPerLine, safeLine.Length - start);
                yield return safeLine.Substring(start, length);
            }
        }

        private void PositionHoverInfoPanel()
        {
            if (rootCanvas == null || hoverInfoPanel == null)
            {
                return;
            }

            RectTransform canvasRect = rootCanvas.transform as RectTransform;
            if (canvasRect == null)
            {
                return;
            }

            Camera uiCamera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : rootCanvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, Input.mousePosition, uiCamera, out Vector2 localPoint))
            {
                return;
            }

            Vector2 canvasSize = canvasRect.rect.size;
            Vector2 bottomLeftPoint = localPoint + new Vector2(canvasSize.x * canvasRect.pivot.x, canvasSize.y * canvasRect.pivot.y);
            Vector2 position = bottomLeftPoint + HoverInfoOffset;
            float margin = 10f;

            if (position.x + HoverInfoWidth > canvasSize.x - margin)
            {
                position.x = bottomLeftPoint.x - HoverInfoWidth - HoverInfoOffset.x;
            }

            position.x = Mathf.Clamp(position.x, margin, Mathf.Max(margin, canvasSize.x - HoverInfoWidth - margin));
            position.y = Mathf.Clamp(position.y, HoverInfoHeight + margin, Mathf.Max(HoverInfoHeight + margin, canvasSize.y - margin));
            hoverInfoPanel.anchoredPosition = position;
        }

        private void SetHoverInfoVisible(bool visible)
        {
            if (hoverInfoPanel != null && hoverInfoPanel.gameObject.activeSelf != visible)
            {
                hoverInfoPanel.gameObject.SetActive(visible);
            }
        }

        private bool IsPointerOverAnyUiControl()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return true;
            }

            if (hoverBlockingRects == null || hoverBlockingRects.Length == 0)
            {
                RefreshHoverBlockingRects();
            }

            for (int i = 0; i < hoverBlockingRects.Length; i++)
            {
                RectTransform rect = hoverBlockingRects[i];
                if (rect == null || !rect.gameObject.activeInHierarchy)
                {
                    continue;
                }

                if (IsScreenPointInsideRect(rect, Input.mousePosition))
                {
                    return true;
                }
            }

            return false;
        }

        private void RefreshHoverBlockingRects()
        {
            List<RectTransform> rects = new List<RectTransform>(HoverBlockingUiNames.Length);
            for (int i = 0; i < HoverBlockingUiNames.Length; i++)
            {
                GameObject go = GameObject.Find(HoverBlockingUiNames[i]);
                RectTransform rect = go != null ? go.GetComponent<RectTransform>() : null;
                if (rect != null)
                {
                    rects.Add(rect);
                }
            }

            hoverBlockingRects = rects.ToArray();
        }

        private static bool IsScreenPointInsideRect(RectTransform rect, Vector2 screenPoint)
        {
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera camera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            return RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, camera);
        }

        private void UpdatePauseButtonVisualState()
        {
            if (playbackController == null)
            {
                UpdatePauseButtonText("暂停");
                return;
            }

            UpdatePauseButtonText(playbackController.isPlaying && playbackController.isPaused ? "继续" : "暂停");
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
            else if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
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
                panelRect.anchorMin = new Vector2(0f, 1f);
                panelRect.anchorMax = new Vector2(0f, 1f);
                panelRect.pivot = new Vector2(0f, 1f);
                panelRect.anchoredPosition = new Vector2(24f, -108f);
                panelRect.sizeDelta = new Vector2(520f, 168f);
            }

            Image panelImage = panel.GetComponent<Image>() ?? panel.AddComponent<Image>();
            panelImage.color = new Color(0.026f, 0.043f, 0.065f, 0.94f);
            panelImage.raycastTarget = true;

            ConfigureSourcePanelControls(panel.transform, font);
        }

        private void ConfigureSourcePanelControls(Transform panel, Font font)
        {
            Text replayLabel = FindOrCreateLabel(panel, "HUD_ReplayPathLabel", "回放", font);
            SetChildRect(replayLabel.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -14f), new Vector2(56f, 30f), new Vector2(0f, 1f));

            playbackPathInput = FindInputFieldByName("ReplayPathInput") ?? CreateInput(panel, "ReplayPathInput",
                string.Empty,
                "选择 .thuaipb 或输入路径", font, Vector2.zero, new Vector2(500f, 30f));
            playbackPathInput.transform.SetParent(panel, false);
            if (IsLegacyDefaultReplayPath(playbackPathInput.text))
            {
                playbackPathInput.text = string.Empty;
            }
            SetChildRect(playbackPathInput.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(76f, -14f), new Vector2(282f, 30f), new Vector2(0f, 1f));
            StyleInputField(playbackPathInput, font);

            browsePlaybackButton = FindButtonByName("BrowseReplayButton") ?? CreateButton(panel, "BrowseReplayButton", "选择文件", font, Vector2.zero, new Vector2(94f, 30f), new Color(0.20f, 0.48f, 0.72f, 1f));
            browsePlaybackButton.transform.SetParent(panel, false);
            SetChildRect(browsePlaybackButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(368f, -14f), new Vector2(76f, 30f), new Vector2(0f, 1f));
            StyleButton(browsePlaybackButton, "选择文件", new Color(0.20f, 0.48f, 0.72f, 1f), font);

            loadPlaybackButton = FindButtonByName("LoadReplayButton") ?? CreateButton(panel, "LoadReplayButton", "加载", font, Vector2.zero, new Vector2(82f, 30f), new Color(0.18f, 0.36f, 0.58f, 1f));
            loadPlaybackButton.transform.SetParent(panel, false);
            SetChildRect(loadPlaybackButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(452f, -14f), new Vector2(50f, 30f), new Vector2(0f, 1f));
            StyleButton(loadPlaybackButton, "加载", new Color(0.18f, 0.36f, 0.58f, 1f), font);

            recentReplayDropdown = FindDropdownByName("RecentReplayDropdown") ?? CreateDropdown(panel, "RecentReplayDropdown", font, Vector2.zero, new Vector2(500f, 30f));
            recentReplayDropdown.transform.SetParent(panel, false);
            SetChildRect(recentReplayDropdown.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(76f, -128f), new Vector2(282f, 28f), new Vector2(0f, 1f));
            StyleDropdown(recentReplayDropdown, font);
            SetNamedGameObjectActive("HUD_RecentReplayLabel", false);
            SetNamedGameObjectActive("RecentReplayDropdown", false);

            replayHintText = FindTextByName("ReplayHintText") ?? FindOrCreateLabel(panel, "ReplayHintText", string.Empty, font);
            replayHintText.transform.SetParent(panel, false);
            SetChildRect(replayHintText.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(18f, -132f), new Vector2(-36f, 28f), new Vector2(0f, 1f));
            if (IsDefaultReplayHint(replayHintText.text))
            {
                replayHintText.text = string.Empty;
            }
            replayHintText.fontSize = 14;
            replayHintText.alignment = TextAnchor.UpperLeft;
            replayHintText.horizontalOverflow = HorizontalWrapMode.Wrap;
            replayHintText.verticalOverflow = VerticalWrapMode.Truncate;
            replayHintText.color = new Color(0.74f, 0.86f, 0.92f, 1f);

            MovePlaybackControlsIntoSourcePanel(panel, font);
            DisableServerConnectionControls();
            SetNamedGameObjectActive("HUD_ControlPanel", false);
        }

        private void MovePlaybackControlsIntoSourcePanel(Transform panel, Font font)
        {
            progressSlider = FindSliderByName("ReplayProgressSlider") ?? FindSliderByName("ProgressSlider");
            if (progressSlider != null)
            {
                progressSlider.transform.SetParent(panel, false);
                SetChildRect(progressSlider.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -56f), new Vector2(484f, 22f), new Vector2(0f, 1f));
                StyleSlider(progressSlider);
            }

            playButton = FindButtonByName("PlayButton") ?? CreateButton(panel, "PlayButton", "播放", font, Vector2.zero, new Vector2(82f, 34f), new Color(0.18f, 0.72f, 0.28f, 1f));
            playButton.transform.SetParent(panel, false);
            SetChildRect(playButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -88f), new Vector2(82f, 34f), new Vector2(0f, 1f));
            StyleButton(playButton, "播放", new Color(0.18f, 0.72f, 0.28f, 1f), font);

            pauseButton = FindButtonByName("PauseButton") ?? CreateButton(panel, "PauseButton", "暂停", font, Vector2.zero, new Vector2(82f, 34f), new Color(0.88f, 0.72f, 0.18f, 1f));
            pauseButton.transform.SetParent(panel, false);
            SetChildRect(pauseButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(108f, -88f), new Vector2(82f, 34f), new Vector2(0f, 1f));
            StyleButton(pauseButton, "暂停", new Color(0.88f, 0.72f, 0.18f, 1f), font);
            pauseButtonText = pauseButton.GetComponentInChildren<Text>(true);

            stopButton = FindButtonByName("StopButton") ?? CreateButton(panel, "StopButton", "停止", font, Vector2.zero, new Vector2(82f, 34f), new Color(0.82f, 0.18f, 0.18f, 1f));
            stopButton.transform.SetParent(panel, false);
            SetChildRect(stopButton.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(198f, -88f), new Vector2(82f, 34f), new Vector2(0f, 1f));
            StyleButton(stopButton, "停止", new Color(0.82f, 0.18f, 0.18f, 1f), font);

            speedDropdown = FindDropdownByName("SpeedDropdown");
            if (speedDropdown != null)
            {
                speedDropdown.transform.SetParent(panel, false);
                SetChildRect(speedDropdown.GetComponent<RectTransform>(), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(296f, -88f), new Vector2(112f, 34f), new Vector2(0f, 1f));
                StyleDropdown(speedDropdown, font);
            }
        }

        private void DisableServerConnectionControls()
        {
            SetNamedGameObjectActive("HUD_LiveAddressLabel", false);
            SetNamedGameObjectActive("ServerAddressInput", false);
            SetNamedGameObjectActive("ConnectLiveButton", false);
            SetNamedGameObjectActive("DisconnectLiveButton", false);
            serverAddressInput = null;
            connectLiveButton = null;
            disconnectLiveButton = null;
        }

        private void ConfigureHudVisualStyle()
        {
            LayoutLeftInfoPanels();
            LayoutRightInfoPanels();
            StylePanel("HUD_TopBar", new Color(0.020f, 0.032f, 0.050f, 0.95f));
            StylePanel("HUD_ScorePanel", new Color(0.035f, 0.060f, 0.085f, 0.88f));
            StylePanel("HUD_EventPanel", new Color(0.035f, 0.060f, 0.085f, 0.88f));
            StylePanel("HUD_ControlPanel", new Color(0.020f, 0.032f, 0.050f, 0.94f));
            StylePanel("HUD_SourcePanel", new Color(0.026f, 0.043f, 0.065f, 0.94f));
            SetPanelRaycastTarget("HUD_SourcePanel", true);
            EnsureEventLogPanel();
            StyleText("HUD_ScoreTitle", 22, FontStyle.Bold, new Color(0.30f, 0.88f, 0.98f, 1f), TextAnchor.MiddleLeft);
            StyleText("HUD_EventTitle", 20, FontStyle.Bold, new Color(0.30f, 0.88f, 0.98f, 1f), TextAnchor.MiddleLeft);
            StyleText("HUD_TitleText", 30, FontStyle.Bold, new Color(1.00f, 0.78f, 0.34f, 1f), TextAnchor.MiddleLeft);
            StyleText("GameStateText", 18, FontStyle.Normal, new Color(0.88f, 0.94f, 0.98f, 1f), TextAnchor.MiddleRight);
            StyleText("AIEventText", 16, FontStyle.Normal, new Color(0.88f, 0.94f, 0.98f, 1f), TextAnchor.UpperLeft);
            StyleText("AIEffectText", 16, FontStyle.Normal, new Color(0.88f, 0.94f, 0.98f, 1f), TextAnchor.UpperLeft);
            ConfigureEventInfoText(aiEventText, new Vector2(18f, -44f), new Vector2(-36f, 66f));
            ConfigureEventInfoText(aiEffectText, new Vector2(18f, -112f), new Vector2(-36f, 38f));
        }

        private static void LayoutLeftInfoPanels()
        {
            LayoutTopLeftPanel("HUD_SourcePanel", new Vector2(24f, -108f), new Vector2(520f, 168f));
            LayoutTopLeftPanel("HUD_EventPanel", new Vector2(24f, -296f), new Vector2(430f, 148f));
        }

        private static void LayoutRightInfoPanels()
        {
            LayoutTopRightPanel("HUD_ScorePanel", new Vector2(-24f, -108f), new Vector2(620f, 430f));
        }

        private static void LayoutTopLeftPanel(string objectName, Vector2 anchoredPosition, Vector2 size)
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

            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
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

        private static void SetPanelRaycastTarget(string objectName, bool raycastTarget)
        {
            GameObject go = GameObject.Find(objectName);
            Image image = go != null ? go.GetComponent<Image>() : null;
            if (image != null)
            {
                image.raycastTarget = raycastTarget;
            }
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

        private static void ConfigureEventInfoText(Text text, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            if (text == null)
            {
                return;
            }

            SetChildRect(
                text.rectTransform,
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                anchoredPosition,
                sizeDelta,
                new Vector2(0f, 1f));
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = false;
            text.supportRichText = false;
            text.lineSpacing = 1.05f;
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

        private static void DestroyNamedGameObjectIfExists(string objectName)
        {
            GameObject go = GameObject.Find(objectName);
            if (go == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(go);
            }
            else
            {
                DestroyImmediate(go);
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

        private static void StyleDropdown(Dropdown dropdown, Font font, int fontSize = 15)
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
                dropdown.captionText.fontSize = fontSize;
                dropdown.captionText.fontStyle = FontStyle.Bold;
                dropdown.captionText.alignment = TextAnchor.MiddleCenter;
                dropdown.captionText.color = new Color(0.92f, 0.97f, 1f, 1f);
                dropdown.captionText.horizontalOverflow = HorizontalWrapMode.Overflow;
                dropdown.captionText.verticalOverflow = VerticalWrapMode.Overflow;
            }

            if (dropdown.itemText != null)
            {
                dropdown.itemText.font = font;
                dropdown.itemText.fontSize = fontSize;
                dropdown.itemText.fontStyle = FontStyle.Bold;
                dropdown.itemText.alignment = TextAnchor.MiddleLeft;
                dropdown.itemText.color = new Color(0.92f, 0.97f, 1f, 1f);
                dropdown.itemText.horizontalOverflow = HorizontalWrapMode.Overflow;
                dropdown.itemText.verticalOverflow = VerticalWrapMode.Overflow;
            }

            RectTransform template = dropdown.template;
            if (template != null)
            {
                template.sizeDelta = new Vector2(Mathf.Max(template.sizeDelta.x, 132f), 232f);
                Image templateImage = template.GetComponent<Image>();
                if (templateImage != null)
                {
                    templateImage.color = new Color(0.035f, 0.052f, 0.075f, 0.98f);
                }

                Transform viewport = template.Find("Viewport");
                Image viewportImage = viewport != null ? viewport.GetComponent<Image>() : null;
                if (viewportImage != null)
                {
                    viewportImage.color = new Color(0.035f, 0.052f, 0.075f, 0.98f);
                }

                RectTransform itemRect = template.Find("Viewport/Content/Item") as RectTransform;
                if (itemRect != null)
                {
                    itemRect.sizeDelta = new Vector2(itemRect.sizeDelta.x, 36f);
                }

                Text itemLabel = template.Find("Viewport/Content/Item/Item Label")?.GetComponent<Text>();
                if (itemLabel != null)
                {
                    itemLabel.font = font;
                    itemLabel.fontSize = fontSize;
                    itemLabel.fontStyle = FontStyle.Bold;
                    itemLabel.alignment = TextAnchor.MiddleLeft;
                    itemLabel.color = new Color(0.92f, 0.97f, 1f, 1f);
                    itemLabel.horizontalOverflow = HorizontalWrapMode.Overflow;
                    itemLabel.verticalOverflow = VerticalWrapMode.Overflow;
                }

                Toggle itemToggle = template.Find("Viewport/Content/Item")?.GetComponent<Toggle>();
                if (itemToggle != null)
                {
                    ColorBlock colors = itemToggle.colors;
                    colors.normalColor = new Color(0.055f, 0.082f, 0.112f, 0.98f);
                    colors.highlightedColor = new Color(0.14f, 0.24f, 0.34f, 1f);
                    colors.pressedColor = new Color(0.10f, 0.18f, 0.28f, 1f);
                    colors.selectedColor = colors.highlightedColor;
                    itemToggle.colors = colors;
                }
            }
        }

        private static void StyleSlider(Slider slider)
        {
            if (slider == null)
            {
                return;
            }

            if (slider.fillRect != null)
            {
                Image fill = slider.fillRect.GetComponent<Image>();
                if (fill != null)
                {
                    fill.color = new Color(0.18f, 0.88f, 0.96f, 1f);
                }
            }

            if (slider.targetGraphic != null)
            {
                slider.targetGraphic.color = new Color(1.00f, 0.72f, 0.22f, 1f);
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

        private static string LocalizeLiveEventName(string rawName)
        {
            string name = NormalizePanelLine(rawName);
            if (string.IsNullOrEmpty(name) || IsNormalEventName(name))
            {
                return "正常";
            }

            if (ContainsIgnoreCase(name, "festival of lights"))
            {
                return "灯火节";
            }

            if (ContainsIgnoreCase(name, "festival") || ContainsIgnoreCase(name, "celebration"))
            {
                return "庆典活动";
            }

            if (ContainsIgnoreCase(name, "storm") || ContainsIgnoreCase(name, "rain") || ContainsIgnoreCase(name, "weather"))
            {
                return "天气波动";
            }

            if (ContainsIgnoreCase(name, "chip") || ContainsIgnoreCase(name, "shortage") || ContainsIgnoreCase(name, "supply"))
            {
                return "供应波动";
            }

            if (ContainsIgnoreCase(name, "market") || ContainsIgnoreCase(name, "price"))
            {
                return "市场波动";
            }

            return ContainsAsciiLetter(name) ? "特殊事件" : name;
        }

        private static string LocalizeLiveEventDescription(string rawName, string rawDescription)
        {
            string description = NormalizePanelLine(rawDescription);
            if (string.IsNullOrEmpty(description))
            {
                return "暂无事件描述";
            }

            if (!ContainsAsciiLetter(description))
            {
                return description;
            }

            string name = NormalizePanelLine(rawName);
            if (ContainsIgnoreCase(name, "festival of lights") || ContainsIgnoreCase(description, "festival of lights"))
            {
                return "一年一度的灯火节庆典正在提升市场活跃度。";
            }

            if (ContainsIgnoreCase(description, "boost") || ContainsIgnoreCase(description, "increase"))
            {
                return "当前事件正在提高部分局内收益或效率。";
            }

            if (ContainsIgnoreCase(description, "reduce") || ContainsIgnoreCase(description, "decrease"))
            {
                return "当前事件正在降低部分局内收益或效率。";
            }

            return "当前事件影响已生效，具体效果请以世界修正和局内状态为准。";
        }

        private static string LocalizeEventPanelText(string rawText, string fallback)
        {
            string text = NormalizePanelLine(rawText);
            if (string.IsNullOrEmpty(text))
            {
                return fallback;
            }

            return ContainsAsciiLetter(text) ? fallback : text;
        }

        private static bool IsNormalEventName(string name)
        {
            return string.Equals(name, "normal", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "none", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(name, "no event", StringComparison.OrdinalIgnoreCase);
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return !string.IsNullOrEmpty(text) &&
                   !string.IsNullOrEmpty(value) &&
                   text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ContainsAsciiLetter(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return false;
            }

            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if ((ch >= 'A' && ch <= 'Z') || (ch >= 'a' && ch <= 'z'))
                {
                    return true;
                }
            }

            return false;
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
