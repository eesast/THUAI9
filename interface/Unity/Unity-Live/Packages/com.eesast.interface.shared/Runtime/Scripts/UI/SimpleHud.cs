using System;
using System.Collections.Generic;
using Protobuf;
using THUAI9.Unity.Core;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.UI.Shared
{
    public sealed class SimpleHud
    {
        private const string FontPath = "Fonts/NotoSansCJKsc-Regular";
        private static readonly Color[] TeamAccentColors =
        {
            new Color(1.00f, 0.08f, 0.08f, 1f),
            new Color(0.08f, 0.95f, 0.18f, 1f),
            new Color(0.12f, 0.48f, 1.00f, 1f),
            new Color(1.00f, 0.92f, 0.05f, 1f)
        };
        private const float TeamStatusRightMargin = 24f;
        private const float TeamStatusTopMargin = 108f;
        private const float TeamStatusWidth = 292f;
        private const float TeamStatusHeight = 180f;
        private const float TeamStatusGap = 12f;
        private const float TeamStatusContentHeight = 430f;

        private readonly int visibleTeamCount;
        private readonly Text[] teamTexts;
        private readonly Font font;
        public Canvas Canvas { get; }
        public Text StatusText { get; private set; }
        public Text TimeText { get; private set; }

        public SimpleHud(string title, int visibleTeamCount = 4)
        {
            this.visibleTeamCount = Mathf.Clamp(visibleTeamCount, 1, TeamAccentColors.Length);
            teamTexts = new Text[this.visibleTeamCount];
            EnsureEventSystem();
            Canvas = EnsureCanvas();
            font = LoadFont();
            HideLegacyModeObjects();
            BuildCommon(title);
        }

        public RectTransform AddPanel(string name, Vector2 anchored, Vector2 size)
        {
            GameObject go = GameObject.Find(name) ?? new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(Canvas.transform, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.026f, 0.043f, 0.065f, 0.92f);
            return rt;
        }

        public Text Label(Transform parent, string name, string text, Vector2 anchored, Vector2 size, int fontSize = 14, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            GameObject go = GameObject.Find(name) ?? new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            Text label = go.GetComponent<Text>();
            label.text = text;
            label.font = font;
            label.fontSize = fontSize;
            label.alignment = anchor;
            label.color = new Color(0.88f, 0.94f, 0.98f, 1f);
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        public Button Button(Transform parent, string name, string text, Vector2 anchored, Vector2 size, Color color, UnityEngine.Events.UnityAction action)
        {
            GameObject go = GameObject.Find(name) ?? new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = color;
            Button button = go.GetComponent<Button>();
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(action);
            Label(go.transform, name + "Text", text, Vector2.zero, size, 14, TextAnchor.MiddleCenter);
            return button;
        }

        public InputField Input(Transform parent, string name, string value, string placeholder, Vector2 anchored, Vector2 size)
        {
            GameObject go = GameObject.Find(name) ?? new GameObject(name, typeof(RectTransform), typeof(Image), typeof(InputField));
            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.04f, 0.08f, 0.12f, 1f);
            InputField input = go.GetComponent<InputField>();
            Text text = Label(go.transform, name + "Text", value, new Vector2(8f, -2f), new Vector2(size.x - 16f, size.y - 4f), 14, TextAnchor.MiddleLeft);
            Text ph = Label(go.transform, name + "Placeholder", placeholder, new Vector2(8f, -2f), new Vector2(size.x - 16f, size.y - 4f), 14, TextAnchor.MiddleLeft);
            ph.color = new Color(0.45f, 0.55f, 0.62f, 1f);
            input.textComponent = text;
            input.placeholder = ph;
            input.text = value;
            return input;
        }

        public Slider Slider(Transform parent, string name, Vector2 anchored, Vector2 size)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = DefaultControls.CreateSlider(new DefaultControls.Resources());
                go.name = name;
            }

            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            Slider slider = go.GetComponent<Slider>();
            slider.targetGraphic = slider.targetGraphic ?? go.GetComponentInChildren<Graphic>();
            slider.wholeNumbers = true;
            SetChildGraphicColor(go.transform, "Background", new Color(0.12f, 0.18f, 0.24f, 1f));
            SetChildGraphicColor(go.transform, "Fill", new Color(0.24f, 0.74f, 0.92f, 1f));
            SetChildGraphicColor(go.transform, "Handle", new Color(0.86f, 0.96f, 1f, 1f));
            return slider;
        }

        public Dropdown Dropdown(Transform parent, string name, Vector2 anchored, Vector2 size, params string[] options)
        {
            GameObject go = GameObject.Find(name);
            if (go == null)
            {
                go = DefaultControls.CreateDropdown(new DefaultControls.Resources());
                go.name = name;
            }

            go.transform.SetParent(parent, false);
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = anchored;
            rt.sizeDelta = size;
            go.GetComponent<Image>().color = new Color(0.08f, 0.13f, 0.18f, 1f);
            Dropdown dropdown = go.GetComponent<Dropdown>();
            StyleDropdownVisuals(go, dropdown);
            Text caption = FindChildText(go.transform, "Label")
                ?? Label(go.transform, name + "Label", options.Length > 0 ? options[0] : string.Empty, Vector2.zero, size, 14, TextAnchor.MiddleCenter);
            ConfigureText(caption, 14, TextAnchor.MiddleCenter);
            dropdown.captionText = caption;
            Text itemLabel = FindChildText(go.transform, "Item Label");
            if (itemLabel != null)
            {
                ConfigureText(itemLabel, 14, TextAnchor.MiddleLeft);
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(new System.Collections.Generic.List<string>(options));
            dropdown.value = Mathf.Clamp(dropdown.value, 0, Mathf.Max(0, options.Length - 1));
            dropdown.RefreshShownValue();
            return dropdown;
        }

        public void UpdateCommon(int milliseconds)
        {
            TimeSpan time = TimeSpan.FromMilliseconds(Mathf.Max(0, CoreParam.ClampDisplayGameMilliseconds(milliseconds)));
            TimeText.text = string.Format("{0:00}:{1:00}", (int)time.TotalMinutes, time.Seconds);
            MessageOfAll allMessage = CoreParam.allMessage;
            for (int i = 0; i < teamTexts.Length; i++)
            {
                int teamIndex = i + 1;
                if (allMessage != null && i < allMessage.Teams.Count)
                {
                    teamTexts[i].text = FormatTeamStatus(teamIndex, allMessage.Teams[i]);
                }
                else if (TryFormatTeamFallback(teamIndex, out string fallback))
                {
                    teamTexts[i].text = fallback;
                }
                else
                {
                    teamTexts[i].text = FormatWaitingTeamStatus(teamIndex);
                }
            }
        }

        private void BuildCommon(string title)
        {
            RectTransform top = AddPanel("HUD_TopBar", Vector2.zero, new Vector2(0f, 76f));
            top.anchorMin = new Vector2(0f, 1f);
            top.anchorMax = new Vector2(1f, 1f);
            top.pivot = new Vector2(0.5f, 1f);
            Label(top, "HUD_TitleText", title, new Vector2(24f, -14f), new Vector2(640f, 48f), 26, TextAnchor.MiddleLeft);
            TimeText = Label(top, "GameTimeText", "00:00", new Vector2(850f, -14f), new Vector2(220f, 48f), 30, TextAnchor.MiddleCenter);
            StatusText = Label(Canvas.transform, "StatusText", string.Empty, new Vector2(24f, -520f), new Vector2(620f, 120f), 16, TextAnchor.UpperLeft);
            for (int i = 0; i < teamTexts.Length; i++)
            {
                RectTransform card = AddPanel(
                    "TeamStatusCard" + (i + 1),
                    new Vector2(-TeamStatusRightMargin, -TeamStatusTopMargin - i * (TeamStatusHeight + TeamStatusGap)),
                    new Vector2(TeamStatusWidth, TeamStatusHeight));
                card.anchorMin = card.anchorMax = new Vector2(1f, 1f);
                card.pivot = new Vector2(1f, 1f);
                Image image = card.GetComponent<Image>();
                if (image != null)
                {
                    image.color = new Color(0.026f, 0.043f, 0.065f, 0.86f);
                    image.raycastTarget = true;
                }

                EnsureTeamAccent(card, i);

                teamTexts[i] = Label(card, "TeamScoreText" + (i + 1), "队伍 " + (i + 1), new Vector2(16f, -8f), new Vector2(TeamStatusWidth - 28f, TeamStatusHeight - 16f), 13, TextAnchor.UpperLeft);
                ConfigureTeamStatusCard(card, teamTexts[i]);
            }

            for (int i = visibleTeamCount; i < TeamAccentColors.Length; i++)
            {
                GameObject extraCard = GameObject.Find("TeamStatusCard" + (i + 1));
                if (extraCard != null)
                {
                    extraCard.SetActive(false);
                }
            }
        }

        private static Canvas EnsureCanvas()
        {
            Canvas found = UnityEngine.Object.FindObjectOfType<Canvas>();
            if (found != null)
            {
                ConfigureCanvas(found);
                return found;
            }

            GameObject go = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = go.GetComponent<Canvas>();
            ConfigureCanvas(canvas);
            return canvas;
        }

        private static void ConfigureCanvas(Canvas canvas)
        {
            if (canvas == null)
            {
                return;
            }

            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvas.GetComponent<CanvasScaler>() ?? canvas.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            if (canvas.GetComponent<GraphicRaycaster>() == null)
            {
                canvas.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private static void HideLegacyModeObjects()
        {
            string[] names =
            {
                "PlayButton",
                "PauseButton",
                "StopButton",
                "SpeedDropdown",
                "ReplayPathInput",
                "BrowseReplayButton",
                "LoadReplayButton",
                "ReplayProgressSlider",
                "RecentReplayDropdown",
                "ProgressSlider",
                "ServerAddressInput",
                "ConnectLiveButton",
                "DisconnectLiveButton",
                "StartTrialButton",
                "HUD_SourcePanel",
                "HUD_ControlPanel",
                "HUD_PlayerPanel",
                "HUD_PlayerPanelToggle",
                "ScorePanel",
                "HUD_ScorePanel"
            };

            foreach (string name in names)
            {
                GameObject go = GameObject.Find(name);
                if (go != null)
                {
                    go.SetActive(false);
                }
            }
        }

        private static void EnsureTeamAccent(RectTransform card, int index)
        {
            if (card == null) return;

            string name = "TeamStatusAccent" + (index + 1);
            Transform existing = card.Find(name);
            GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(card, false);

            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(5f, 0f);

            Image image = go.GetComponent<Image>();
            image.color = TeamAccentColors[Mathf.Clamp(index, 0, TeamAccentColors.Length - 1)];
            image.raycastTarget = false;
        }

        private void ConfigureTeamStatusCard(RectTransform card, Text text)
        {
            if (card == null || text == null)
            {
                return;
            }

            RectTransform viewport = FindOrCreateRect(card.transform, "Viewport", typeof(Image), typeof(Mask));
            viewport.gameObject.SetActive(true);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.offsetMin = new Vector2(16f, 8f);
            viewport.offsetMax = new Vector2(-10f, -8f);
            Image viewportImage = viewport.GetComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.01f);
            viewportImage.raycastTarget = true;
            Mask mask = viewport.GetComponent<Mask>();
            mask.showMaskGraphic = false;

            RectTransform content = FindOrCreateRect(viewport, "Content");
            content.gameObject.SetActive(true);
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
            textRect.sizeDelta = new Vector2(0f, TeamStatusContentHeight - 8f);
            text.font = font;
            text.fontSize = 13;
            text.fontStyle = FontStyle.Normal;
            text.supportRichText = true;
            text.lineSpacing = 1.0f;
            text.alignment = TextAnchor.UpperLeft;
            text.color = new Color(0.88f, 0.94f, 0.98f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            ScrollRect scrollRect = card.GetComponent<ScrollRect>() ?? card.gameObject.AddComponent<ScrollRect>();
            scrollRect.enabled = true;
            scrollRect.viewport = viewport;
            scrollRect.content = content;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 24f;
            scrollRect.verticalNormalizedPosition = 1f;
        }

        private static bool TryFormatTeamFallback(int teamIndex, out string text)
        {
            if (CoreParam.teams.TryGetValue(teamIndex, out MessageOfTeam team) && team != null)
            {
                text = BuildTeamStatusText(
                    teamIndex,
                    team.Score.ToString(),
                    team.Material.ToString(),
                    team.ComputePower.ToString(),
                    "--",
                    FormatTeamTechLevels(team.TechLevels),
                    FormatTeamUuidSummary(teamIndex),
                    FormatTeamMemberStatus(teamIndex));
                return true;
            }

            text = null;
            return false;
        }

        private static string FormatTeamStatus(int teamIndex, MessageOfAll.Types.TeamInfo team)
        {
            if (team == null)
            {
                return FormatWaitingTeamStatus(teamIndex);
            }

            return BuildTeamStatusText(
                teamIndex,
                team.Score.ToString(),
                team.Material.ToString(),
                team.ComputePower.ToString(),
                team.FactoryHp.ToString(),
                FormatTeamTechLevels(team.TechLevels),
                FormatTeamUuidSummary(teamIndex),
                FormatTeamMemberStatus(teamIndex));
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
            string teamLabel = CoreParam.GetTeamDisplayLabel(teamIndex);
            return
                $"<size=15><b><color=#{accent}>{teamLabel}</color>{WideGap(1)}得分：{score}</b></size>\n" +
                $"原料：{material}{WideGap(1)}算力：{computePower}\n" +
                $"工厂血量：{factoryHp}\n" +
                $"科技等级：{techSummary}\n" +
                "<b>成员</b>\n" +
                $"<size=12>{uuidSummary}</size>\n" +
                "<b>成员状态</b>\n" +
                $"<size=12>{memberStatus}</size>";
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

                AddOrMergeTeamMemberUuid(members, character.PlayerId, character.Guid);
            }

            foreach (MessageOfTeam team in CoreParam.teams.Values)
            {
                if (team == null || team.TeamId != teamIndex || team.PlayerId <= 0)
                {
                    continue;
                }

                AddOrMergeTeamMemberUuid(members, team.PlayerId, 0);
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

            var parts = new List<string>(members.Count);
            for (int i = 0; i < members.Count; i++)
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
            switch (type)
            {
                case CharacterType.Drone:
                    return "无人机";
                case CharacterType.Robot:
                    return "机器人";
                case CharacterType.AutonomousCar:
                    return "无人车";
                default:
                    return "未知单位";
            }
        }

        private static string TranslateCharacterState(CharacterState state)
        {
            switch (state)
            {
                case CharacterState.None:
                case CharacterState.Idle:
                    return "空闲";
                case CharacterState.Harvesting:
                    return "采集中";
                case CharacterState.Attacking:
                    return "攻击中";
                case CharacterState.Ocuppying:
                    return "占领中";
                case CharacterState.Trading:
                    return "交易中";
                case CharacterState.Moving:
                    return "移动中";
                case CharacterState.KnockedBack:
                    return "被击退";
                case CharacterState.Deceased:
                    return "已死亡";
                default:
                    return "未知";
            }
        }

        private static void AddOrMergeTeamMemberUuid(List<TeamMemberUuidInfo> members, long playerId, long guid)
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
            switch (key)
            {
                case "Robust":
                    return "生命耐久";
                case "Warrior":
                    return "攻击能力";
                case "MoveSpeed":
                    return "移动速度";
                case "Carry":
                    return "携带容量";
                case "Efficiency":
                    return "采集效率";
                case "Production":
                    return "生产效率";
                case "Storage":
                    return "仓储容量";
                case "Price":
                    return "出售价格";
                case "Cost":
                    return "生产成本";
                case "Market":
                    return "市场能力";
                default:
                    return string.IsNullOrWhiteSpace(key) ? "?" : key;
            }
        }

        private static Color GetTeamAccentColor(int index)
        {
            return TeamAccentColors[Mathf.Clamp(index, 0, TeamAccentColors.Length - 1)];
        }

        private static RectTransform FindOrCreateRect(Transform parent, string name, params Type[] componentTypes)
        {
            Transform existing = parent.Find(name);
            GameObject go = existing != null
                ? existing.gameObject
                : new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);

            foreach (Type componentType in componentTypes)
            {
                if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
                {
                    continue;
                }

                if (go.GetComponent(componentType) == null)
                {
                    go.AddComponent(componentType);
                }
            }

            return go.GetComponent<RectTransform>();
        }

        private static void EnsureEventSystem()
        {
            if (EventSystem.current == null) new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        private static Font LoadFont()
        {
            Font font = Resources.Load<Font>(FontPath);
            if (font != null) return font;
            try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); }
            catch { return null; }
        }

        private void ConfigureText(Text text, int fontSize, TextAnchor anchor)
        {
            if (text == null) return;
            text.font = font;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = new Color(0.88f, 0.94f, 0.98f, 1f);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private void StyleDropdownVisuals(GameObject dropdownObject, Dropdown dropdown)
        {
            if (dropdownObject == null || dropdown == null) return;

            ColorBlock colors = dropdown.colors;
            colors.normalColor = new Color(0.08f, 0.13f, 0.18f, 1f);
            colors.highlightedColor = new Color(0.16f, 0.30f, 0.42f, 1f);
            colors.pressedColor = new Color(0.20f, 0.40f, 0.56f, 1f);
            colors.selectedColor = new Color(0.13f, 0.24f, 0.34f, 1f);
            colors.disabledColor = new Color(0.05f, 0.07f, 0.09f, 0.75f);
            dropdown.colors = colors;

            SetChildGraphicColor(dropdownObject.transform, "Template", new Color(0.04f, 0.07f, 0.10f, 0.98f));
            SetChildGraphicColor(dropdownObject.transform, "Viewport", new Color(0.04f, 0.07f, 0.10f, 0.98f));
            SetChildGraphicColor(dropdownObject.transform, "Item Background", new Color(0.07f, 0.12f, 0.17f, 1f));
            SetChildGraphicColor(dropdownObject.transform, "Item Checkmark", new Color(0.24f, 0.74f, 0.92f, 1f));
            SetChildGraphicColor(dropdownObject.transform, "Scrollbar", new Color(0.05f, 0.09f, 0.13f, 1f));
            SetChildGraphicColor(dropdownObject.transform, "Sliding Area", new Color(0.05f, 0.09f, 0.13f, 1f));
            SetChildGraphicColor(dropdownObject.transform, "Handle", new Color(0.24f, 0.74f, 0.92f, 1f));

            Text[] texts = dropdownObject.GetComponentsInChildren<Text>(true);
            foreach (Text text in texts)
            {
                ConfigureText(text, 14, text.name == "Item Label" ? TextAnchor.MiddleLeft : TextAnchor.MiddleCenter);
                text.color = new Color(0.90f, 0.96f, 1f, 1f);
            }

            Toggle[] toggles = dropdownObject.GetComponentsInChildren<Toggle>(true);
            foreach (Toggle toggle in toggles)
            {
                ColorBlock toggleColors = toggle.colors;
                toggleColors.normalColor = new Color(0.07f, 0.12f, 0.17f, 1f);
                toggleColors.highlightedColor = new Color(0.16f, 0.30f, 0.42f, 1f);
                toggleColors.pressedColor = new Color(0.20f, 0.40f, 0.56f, 1f);
                toggleColors.selectedColor = new Color(0.13f, 0.24f, 0.34f, 1f);
                toggle.colors = toggleColors;
            }
        }

        private static Text FindChildText(Transform root, string childName)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name == childName)
                {
                    return child.GetComponent<Text>();
                }
            }

            return null;
        }

        private static void SetChildGraphicColor(Transform root, string childName, Color color)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in children)
            {
                if (child.name == childName && child.TryGetComponent(out Graphic graphic))
                {
                    graphic.color = color;
                }
            }
        }
    }
}
