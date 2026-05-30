using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Google.Protobuf;
using Protobuf;
using THUAI9.Unity.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace THUAI9.Unity.Playback
{
    public class PlaybackController : MonoBehaviour
    {
        private const string DefaultPlaybackRelativePath = "";
        private const int MaxPlaybackTeamSlots = 4;
        public const int MaxRemotePlaybackBytes = 64 * 1024 * 1024;
        public const int MaxWebGLBase64Bytes = 16 * 1024 * 1024;

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void THUAI9_ClearDevelopmentConsole();
#endif

        [Header("回放文件")]
        public string playbackFilePath = DefaultPlaybackRelativePath;

        [Header("播放状态")]
        public bool isPlaying;
        public bool isPaused;
        public bool autoPlayOnLoad = false;

        [Header("播放速度")]
        public float playSpeed = PlayBackConstant.DEFAULT_PLAY_SPEED;

        private MessageReader messageReader;
        private Coroutine loadCoroutine;
        private Coroutine playCoroutine;
        private Coroutine teamNamesCoroutine;
        private RoomTeamInfo[] pendingRoomTeams;
        private int pendingRoomTeamsRevision;
        private bool playbackLoaded;
        private string playbackSourceDisplayName;
        private string statusText = "状态：未加载回放文件";
        private int currentFrameIndex = -1;
        private int firstFrameGameTimeMs = -1;
        private int currentPlaybackTimeMs;
        private int loadRevision;
        private MessageOfMap playbackMap;

        public bool PlaybackLoaded => playbackLoaded;
        public int TotalFrameCount => playbackLoaded && messageReader != null ? messageReader.GetMessageCount() : 0;
        public int CurrentFrameIndex => currentFrameIndex;
        public int CurrentPlaybackTimeMs => currentPlaybackTimeMs;
        public string StatusText => statusText;
        public uint PlaybackTeamCount => messageReader?.TeamCount ?? 0;
        public uint PlaybackPlayerCount => messageReader?.PlayerCount ?? 0;
        public bool IsAtLastFrame => playbackLoaded && TotalFrameCount > 0 && currentFrameIndex >= TotalFrameCount - 1;
        private bool HasBufferedPlaybackFrames => messageReader != null && messageReader.GetMessageCount() > 0;

        public void SetStatusMessage(string message)
        {
            statusText = string.IsNullOrWhiteSpace(message) ? statusText : message;
            if (playbackLoaded)
            {
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
            }
        }

        private void Reset()
        {
            ApplyDefaultPlaybackSettings();
        }

        private void OnValidate()
        {
            if (!Application.isPlaying)
            {
                ApplyDefaultPlaybackSettings();
            }
        }

        private void Start()
        {
            messageReader = new MessageReader();
            ApplyDefaultPlaybackSettings();

            bool shouldLoadInitialPlayback = !string.IsNullOrWhiteSpace(playbackFilePath);
#if UNITY_WEBGL && !UNITY_EDITOR
            // Browser builds receive playback files from the hosting page. Avoid
            // logging a startup error for the editor-only default Assets path.
            shouldLoadInitialPlayback = shouldLoadInitialPlayback && IsPlaybackUrl(playbackFilePath);
#endif
            if (shouldLoadInitialPlayback)
            {
                LoadPlaybackFile(playbackFilePath);
            }
        }

        public void LoadPlaybackFile(string filePath)
        {
            if (IsPlaybackUrl(filePath))
            {
                LoadPlaybackUrl(filePath);
                return;
            }

            string normalizedPath = NormalizePlaybackPath(filePath);
            int revision = PreparePlaybackLoad(normalizedPath, GetPlaybackDisplayName(normalizedPath), "状态：正在加载回放文件");

            if (!File.Exists(playbackFilePath))
            {
                if (!IsCurrentLoad(revision)) return;
                statusText = "状态：未找到回放文件 " + normalizedPath;
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
                Debug.LogWarning($"Playback file does not exist: {playbackFilePath}");
                return;
            }

            try
            {
                LoadPlaybackData(File.ReadAllBytes(playbackFilePath), revision);
            }
            catch (Exception ex)
            {
                MarkPlaybackLoadFailed("状态：回放加载失败", ex, revision);
            }
        }

        public void LoadPlaybackUrl(string url)
        {
            LoadPlaybackUrl(url, null);
        }

        public void LoadPlaybackUrl(string url, string displayName)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                statusText = "状态：回放地址为空";
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
                return;
            }

            string trimmedUrl = url.Trim().Trim('"');
            if (TryDecodeDataUrl(trimmedUrl, out byte[] embeddedData))
            {
                LoadPlaybackBytes(embeddedData, displayName ?? GetPlaybackDisplayName(trimmedUrl));
                return;
            }

            int revision = PreparePlaybackLoad(trimmedUrl, displayName ?? GetPlaybackDisplayName(trimmedUrl), "状态：正在从浏览器加载回放文件");
            StartLoadingRoomTeamNames(trimmedUrl, revision);
            loadCoroutine = StartCoroutine(LoadPlaybackUrlCoroutine(trimmedUrl, revision));
        }

        public void LoadPlaybackBytes(byte[] data, string displayName = null)
        {
            int revision = PreparePlaybackLoad(displayName ?? "WebGL playback bytes", displayName ?? "WebGL playback", "状态：正在从浏览器读取回放数据");
            LoadPlaybackData(data, revision);
        }

        public void RejectPlaybackLoad(string source, string displayName, string failureStatus)
        {
            int revision = PreparePlaybackLoad(source, displayName, "状态：正在检查回放文件");
            MarkPlaybackLoadFailed(failureStatus, null, revision);
        }

        private IEnumerator LoadPlaybackUrlCoroutine(string url, int revision)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.timeout = 30;
                request.downloadHandler = new DownloadHandlerBuffer();
                yield return request.SendWebRequest();

                if (!IsCurrentLoad(revision)) yield break;
                loadCoroutine = null;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    MarkPlaybackLoadFailed($"状态：浏览器读取回放失败：{request.error}", null, revision);
                    yield break;
                }

                byte[] data = request.downloadHandler?.data;
                if (data != null && data.Length > MaxRemotePlaybackBytes)
                {
                    MarkPlaybackLoadFailed($"状态：回放文件过大（{data.Length} 字节）", null, revision);
                    yield break;
                }

                LoadPlaybackData(data, revision);
            }
        }

        private void StartLoadingRoomTeamNames(string playbackUrl, int revision)
        {
            if (!TryBuildRoomTeamsGraphqlRequest(playbackUrl, out string graphqlUrl, out string roomId))
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                if (!TryBuildRoomTeamsGraphqlRequest(Application.absoluteURL, out graphqlUrl, out roomId))
                {
                    return;
                }
#else
                return;
#endif
            }

            teamNamesCoroutine = StartCoroutine(LoadRoomTeamNamesCoroutine(graphqlUrl, roomId, revision));
        }

        private IEnumerator LoadRoomTeamNamesCoroutine(string graphqlUrl, string roomId, int revision)
        {
            string requestBody = BuildRoomTeamsGraphqlRequest(roomId);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(requestBody);
            using (var request = new UnityWebRequest(graphqlUrl, UnityWebRequest.kHttpVerbPOST))
            {
                request.timeout = 10;
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.SetRequestHeader("Accept", "application/json");

                yield return request.SendWebRequest();
                if (!IsCurrentLoad(revision)) yield break;

                teamNamesCoroutine = null;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Failed to load playback team names: {request.error}");
                    yield break;
                }

                ApplyRoomTeamNames(request.downloadHandler?.text, revision);
            }
        }

        private void ApplyRoomTeamNames(string json, int revision)
        {
            if (!IsCurrentLoad(revision) || string.IsNullOrWhiteSpace(json))
            {
                return;
            }

            RoomTeamsGraphqlResponse response;
            try
            {
                response = JsonUtility.FromJson<RoomTeamsGraphqlResponse>(json);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to parse playback team names: {ex.Message}");
                return;
            }

            if (response?.errors != null && response.errors.Length > 0)
            {
                Debug.LogWarning($"Playback team name query failed: {response.errors[0]?.message}");
                return;
            }

            RoomTeamInfo[] teams = response?.data?.contest_room_by_pk?.contest_room_teams;
            if (teams == null || teams.Length == 0)
            {
                return;
            }

            pendingRoomTeams = teams;
            pendingRoomTeamsRevision = revision;
            TryApplyPendingRoomTeamNames(revision);
        }

        private static string BuildRoomTeamsGraphqlRequest(string roomId)
        {
            var request = new RoomTeamsGraphqlRequest
            {
                query = "query PlaybackRoomTeams($room_id: uuid!) { contest_room_by_pk(room_id: $room_id) { contest_room_teams { team_label score player_roles contest_team { team_name } } } }",
                variables = new RoomTeamsGraphqlVariables
                {
                    room_id = roomId
                }
            };
            return JsonUtility.ToJson(request);
        }

        private static bool TryBuildRoomTeamsGraphqlRequest(string playbackUrl, out string graphqlUrl, out string roomId)
        {
            graphqlUrl = null;
            roomId = null;

            if (!Uri.TryCreate(playbackUrl, UriKind.Absolute, out Uri uri))
            {
                return false;
            }

            string[] segments = uri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (TryExtractRoomIdFromPlaybackPath(segments, out string pathRoomId))
            {
                graphqlUrl = BuildGraphqlUrl(uri);
                roomId = pathRoomId;
                return !string.IsNullOrEmpty(graphqlUrl);
            }

            if (TryGetQueryValue(uri, "room", out string queryRoomId)
                && Guid.TryParse(queryRoomId, out _))
            {
                graphqlUrl = BuildGraphqlUrl(uri);
                roomId = queryRoomId;
                return !string.IsNullOrEmpty(graphqlUrl);
            }

            return false;
        }

        private static bool TryExtractRoomIdFromPlaybackPath(string[] segments, out string roomId)
        {
            roomId = null;
            int playbackIndex = Array.FindIndex(segments, segment => string.Equals(segment, "playback", StringComparison.OrdinalIgnoreCase));
            if (playbackIndex <= 0 || playbackIndex >= segments.Length - 1)
            {
                return false;
            }

            string playbackKind = segments[playbackIndex - 1];
            if (!string.Equals(playbackKind, "arena", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(playbackKind, "competition", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string candidateRoomId = Uri.UnescapeDataString(segments[playbackIndex + 1]);
            if (!Guid.TryParse(candidateRoomId, out _))
            {
                return false;
            }

            roomId = candidateRoomId;
            return true;
        }

        private static string BuildGraphqlUrl(Uri uri)
        {
            if (uri == null || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }

            if (string.Equals(uri.Host, "eesast.com", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "www.eesast.com", StringComparison.OrdinalIgnoreCase))
            {
                return uri.Scheme + "://api.eesast.com/v1/graphql";
            }

            return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/') + "/v1/graphql";
        }

        private static bool TryGetQueryValue(Uri uri, string key, out string value)
        {
            value = null;
            string query = uri?.Query;
            if (string.IsNullOrEmpty(query))
            {
                return false;
            }

            string[] parts = query.TrimStart('?').Split('&');
            foreach (string part in parts)
            {
                if (string.IsNullOrEmpty(part))
                {
                    continue;
                }

                string[] kv = part.Split(new[] { '=' }, 2);
                string currentKey = Uri.UnescapeDataString(kv[0].Replace('+', ' '));
                if (!string.Equals(currentKey, key, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : string.Empty;
                return true;
            }

            return false;
        }

        private static string BuildTeamDisplayName(RoomTeamInfo team)
        {
            string label = team?.team_label?.Trim();
            string name = team?.contest_team?.team_name?.Trim();

            bool hasName = !string.IsNullOrWhiteSpace(name);
            bool hasUsefulLabel = !string.IsNullOrWhiteSpace(label)
                && !string.Equals(label, "Team", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(label, "Default", StringComparison.OrdinalIgnoreCase);

            if (hasUsefulLabel && hasName)
            {
                return $"{label}：{name}";
            }

            if (hasName)
            {
                return name;
            }

            return hasUsefulLabel ? label : string.Empty;
        }

        private void TryApplyPendingRoomTeamNames(int revision)
        {
            if (!IsCurrentLoad(revision)
                || pendingRoomTeams == null
                || pendingRoomTeamsRevision != revision
                || messageReader == null
                || messageReader.GetMessageCount() <= 0)
            {
                return;
            }

            Dictionary<long, string> mappedNames = BuildRoomTeamNameMap(pendingRoomTeams);
            if (mappedNames.Count == 0)
            {
                return;
            }

            foreach (var kvp in mappedNames)
            {
                CoreParam.SetTeamDisplayName(kvp.Key, kvp.Value);
            }
        }

        private Dictionary<long, string> BuildRoomTeamNameMap(RoomTeamInfo[] teams)
        {
            if (TryBuildExplicitLabelTeamMap(teams, out Dictionary<long, string> explicitLabelMap))
            {
                return explicitLabelMap;
            }

            if (TryBuildScoreBasedTeamMap(teams, out Dictionary<long, string> scoreMap))
            {
                return scoreMap;
            }

            if (TryBuildRoleBasedTeamMap(teams, out Dictionary<long, string> roleMap))
            {
                return roleMap;
            }

            // The current website query uses contest_room_teams in this same relation order,
            // and the .thuaipb format does not store website team UUIDs. Use it only after
            // label / score / role based mapping cannot prove a better match.
            Debug.LogWarning("Playback team names fell back to website room-team order; no slot field is present in the current room schema.");
            return BuildWebsiteOrderTeamMap(teams);
        }

        private static bool TryBuildExplicitLabelTeamMap(RoomTeamInfo[] teams, out Dictionary<long, string> map)
        {
            map = new Dictionary<long, string>();
            var usedOrdinals = new HashSet<int>();
            for (int i = 0; i < teams.Length; i++)
            {
                if (!TryParseTeamOrdinal(teams[i]?.team_label, out int ordinal)
                    || ordinal <= 0
                    || ordinal > MaxPlaybackTeamSlots
                    || !usedOrdinals.Add(ordinal))
                {
                    map.Clear();
                    return false;
                }

                map[ordinal] = BuildTeamDisplayName(teams[i]);
            }

            return map.Count > 0;
        }

        private bool TryBuildScoreBasedTeamMap(RoomTeamInfo[] teams, out Dictionary<long, string> map)
        {
            map = new Dictionary<long, string>();
            MessageToClient finalFrame = FindLastFrameWithAllMessage(teams.Length);
            if (finalFrame?.AllMessage == null || finalFrame.AllMessage.Teams.Count < teams.Length)
            {
                return false;
            }

            var scoreToTeamIndex = new Dictionary<int, int>();
            var duplicateScores = new HashSet<int>();
            for (int i = 0; i < teams.Length; i++)
            {
                int score = finalFrame.AllMessage.Teams[i].Score;
                if (scoreToTeamIndex.ContainsKey(score))
                {
                    duplicateScores.Add(score);
                }
                else
                {
                    scoreToTeamIndex[score] = i + 1;
                }
            }

            for (int i = 0; i < teams.Length; i++)
            {
                int score = teams[i]?.score ?? 0;
                if (duplicateScores.Contains(score) || !scoreToTeamIndex.TryGetValue(score, out int teamIndex) || map.ContainsKey(teamIndex))
                {
                    map.Clear();
                    return false;
                }

                map[teamIndex] = BuildTeamDisplayName(teams[i]);
            }

            return map.Count == teams.Length;
        }

        private bool TryBuildRoleBasedTeamMap(RoomTeamInfo[] teams, out Dictionary<long, string> map)
        {
            map = new Dictionary<long, string>();
            Dictionary<int, string> playbackSignatures = BuildPlaybackRoleSignatures();
            if (playbackSignatures.Count == 0)
            {
                return false;
            }

            var signatureToTeamIndex = new Dictionary<string, int>();
            var duplicateSignatures = new HashSet<string>();
            foreach (var kvp in playbackSignatures)
            {
                if (string.IsNullOrEmpty(kvp.Value))
                {
                    continue;
                }

                if (signatureToTeamIndex.ContainsKey(kvp.Value))
                {
                    duplicateSignatures.Add(kvp.Value);
                }
                else
                {
                    signatureToTeamIndex[kvp.Value] = kvp.Key;
                }
            }

            for (int i = 0; i < teams.Length; i++)
            {
                string signature = BuildRoleSignatureFromWebsiteRoles(teams[i]?.player_roles);
                if (string.IsNullOrEmpty(signature)
                    || duplicateSignatures.Contains(signature)
                    || !signatureToTeamIndex.TryGetValue(signature, out int teamIndex)
                    || map.ContainsKey(teamIndex))
                {
                    map.Clear();
                    return false;
                }

                map[teamIndex] = BuildTeamDisplayName(teams[i]);
            }

            return map.Count == teams.Length;
        }

        private static Dictionary<long, string> BuildWebsiteOrderTeamMap(RoomTeamInfo[] teams)
        {
            var map = new Dictionary<long, string>();
            int count = Math.Min(teams.Length, MaxPlaybackTeamSlots);
            for (int i = 0; i < count; i++)
            {
                map[i + 1] = BuildTeamDisplayName(teams[i]);
            }

            return map;
        }

        private MessageToClient FindLastFrameWithAllMessage(int expectedTeamCount)
        {
            if (messageReader == null)
            {
                return null;
            }

            for (int i = messageReader.GetMessageCount() - 1; i >= 0; i--)
            {
                MessageToClient frame = messageReader.ReadMessageAt(i);
                if (frame?.AllMessage != null && frame.AllMessage.Teams.Count >= expectedTeamCount)
                {
                    return frame;
                }
            }

            return null;
        }

        private Dictionary<int, string> BuildPlaybackRoleSignatures()
        {
            var teamRoleCounts = new Dictionary<int, Dictionary<string, int>>();
            var seenCharacters = new HashSet<string>();
            if (messageReader == null)
            {
                return new Dictionary<int, string>();
            }

            for (int i = 0; i < messageReader.GetMessageCount(); i++)
            {
                MessageToClient frame = messageReader.ReadMessageAt(i);
                if (frame == null)
                {
                    continue;
                }

                foreach (MessageOfObj obj in frame.ObjMessage)
                {
                    if (obj.MessageOfObjCase != MessageOfObj.MessageOfObjOneofCase.CharacterMessage)
                    {
                        continue;
                    }

                    MessageOfCharacter character = obj.CharacterMessage;
                    int teamId = (int)character.TeamId;
                    if (teamId <= 0)
                    {
                        continue;
                    }

                    string characterKey = teamId + ":" + character.PlayerId + ":" + character.CharacterType;
                    if (!seenCharacters.Add(characterKey))
                    {
                        continue;
                    }

                    if (!teamRoleCounts.TryGetValue(teamId, out Dictionary<string, int> counts))
                    {
                        counts = new Dictionary<string, int>();
                        teamRoleCounts[teamId] = counts;
                    }

                    string role = character.CharacterType.ToString().ToUpperInvariant();
                    counts[role] = counts.TryGetValue(role, out int count) ? count + 1 : 1;
                }
            }

            var signatures = new Dictionary<int, string>();
            foreach (var kvp in teamRoleCounts)
            {
                signatures[kvp.Key] = BuildRoleSignature(kvp.Value);
            }

            return signatures;
        }

        private static string BuildRoleSignatureFromWebsiteRoles(string playerRoles)
        {
            if (string.IsNullOrWhiteSpace(playerRoles))
            {
                return string.Empty;
            }

            var counts = new Dictionary<string, int>();
            CountRoleToken(playerRoles, "AUTONOMOUS_CAR", counts);
            CountRoleToken(playerRoles, "DRONE", counts);
            CountRoleToken(playerRoles, "ROBOT", counts);
            return BuildRoleSignature(counts);
        }

        private static void CountRoleToken(string source, string role, Dictionary<string, int> counts)
        {
            int count = 0;
            int index = 0;
            while ((index = source.IndexOf(role, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                count++;
                index += role.Length;
            }

            if (count > 0)
            {
                counts[role] = count;
            }
        }

        private static string BuildRoleSignature(Dictionary<string, int> counts)
        {
            if (counts == null || counts.Count == 0)
            {
                return string.Empty;
            }

            string[] roles = { "AUTONOMOUS_CAR", "DRONE", "ROBOT" };
            var builder = new StringBuilder();
            foreach (string role in roles)
            {
                if (counts.TryGetValue(role, out int count) && count > 0)
                {
                    if (builder.Length > 0)
                    {
                        builder.Append(';');
                    }

                    builder.Append(role).Append(':').Append(count);
                }
            }

            return builder.ToString();
        }

        private static bool TryParseTeamOrdinal(string label, out int ordinal)
        {
            ordinal = 0;
            if (string.IsNullOrWhiteSpace(label))
            {
                return false;
            }

            int start = -1;
            for (int i = 0; i < label.Length; i++)
            {
                if (!char.IsDigit(label[i]))
                {
                    continue;
                }

                start = i;
                break;
            }

            if (start < 0)
            {
                return false;
            }

            int end = start;
            while (end < label.Length && char.IsDigit(label[end]))
            {
                end++;
            }

            return int.TryParse(label.Substring(start, end - start), out ordinal);
        }

        private int PreparePlaybackLoad(string source, string displayName, string loadingStatus)
        {
            int revision = NextLoadRevision();
            if (playCoroutine != null || isPlaying || isPaused || playbackLoaded || currentFrameIndex >= 0)
            {
                StopInternal(false);
            }

            if (loadCoroutine != null)
            {
                StopCoroutine(loadCoroutine);
                loadCoroutine = null;
            }

            if (teamNamesCoroutine != null)
            {
                StopCoroutine(teamNamesCoroutine);
                teamNamesCoroutine = null;
            }

            messageReader?.Dispose();
            messageReader = new MessageReader();
            GC.Collect();
            CoreParam.ClearTeamDisplayNames();
            pendingRoomTeams = null;
            pendingRoomTeamsRevision = 0;
            playbackFilePath = source;
            playbackSourceDisplayName = string.IsNullOrWhiteSpace(displayName) ? GetPlaybackDisplayName(source) : displayName;
            playbackLoaded = false;
            currentFrameIndex = -1;
            firstFrameGameTimeMs = -1;
            currentPlaybackTimeMs = 0;
            playbackMap = null;
            CoreParam.playbackCurrentFrameIndex = -1;
            CoreParam.playbackElapsedMilliseconds = 0;
            statusText = loadingStatus;
            FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
            return revision;
        }

        private int NextLoadRevision()
        {
            unchecked
            {
                loadRevision++;
                if (loadRevision == 0)
                {
                    loadRevision = 1;
                }
            }

            return loadRevision;
        }

        private bool IsCurrentLoad(int revision) => revision == loadRevision;

        private void LoadPlaybackData(byte[] data)
        {
            LoadPlaybackData(data, loadRevision);
        }

        private void LoadPlaybackData(byte[] data, int revision)
        {
            try
            {
                if (!IsCurrentLoad(revision)) return;

                if (data == null || data.Length == 0)
                {
                    throw new InvalidDataException("Playback data is empty.");
                }

                messageReader ??= new MessageReader();
                messageReader.LoadData(data);
                if (!IsCurrentLoad(revision)) return;

                playbackLoaded = messageReader != null && messageReader.GetMessageCount() > 0;

                if (!playbackLoaded)
                {
                    statusText = "状态：回放文件没有可读取帧";
                    FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
                    Debug.LogWarning($"Playback file contains no readable frames: {playbackFilePath}");
                    return;
                }

                firstFrameGameTimeMs = GetFrameGameTimeMs(messageReader.ReadMessageAt(0));
                currentPlaybackTimeMs = 0;
                playbackMap = FindPlaybackMap();
                TryApplyPendingRoomTeamNames(revision);
                statusText = BuildPlaybackLoadedStatus();
                ClearWebGLDevelopmentConsole();
                if (autoPlayOnLoad)
                {
                    Play();
                }
                else
                {
                    ShowFirstFramePreview(statusText + "，显示首帧");
                }
            }
            catch (Exception ex)
            {
                MarkPlaybackLoadFailed("状态：回放加载失败", ex, revision);
            }
        }

        private void MarkPlaybackLoadFailed(string status, Exception ex)
        {
            MarkPlaybackLoadFailed(status, ex, loadRevision);
        }

        private void MarkPlaybackLoadFailed(string status, Exception ex, int revision)
        {
            if (!IsCurrentLoad(revision))
            {
                Debug.LogWarning($"Ignored stale playback load failure from revision {revision}; current revision is {loadRevision}.");
                return;
            }

            if (playCoroutine != null)
            {
                StopCoroutine(playCoroutine);
                playCoroutine = null;
            }

            isPlaying = false;
            isPaused = false;
            playbackLoaded = false;
            statusText = BuildPlaybackFailureStatus(status, ex);
            currentFrameIndex = -1;
            firstFrameGameTimeMs = -1;
            currentPlaybackTimeMs = 0;
            playbackMap = null;
            CoreParam.playbackCurrentFrameIndex = -1;
            CoreParam.playbackElapsedMilliseconds = 0;
            messageReader?.Dispose();
            messageReader = new MessageReader();
            FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
            if (ex != null)
            {
                Debug.LogWarning($"Playback load failed ({ex.GetType().Name}).");
            }
            else
            {
                Debug.LogWarning(status);
            }
        }

        private static string BuildPlaybackFailureStatus(string fallbackStatus, Exception ex)
        {
            if (ex == null)
            {
                return string.IsNullOrWhiteSpace(fallbackStatus) ? "状态：回放加载失败" : fallbackStatus;
            }

            if (ex is PlaybackFileIncompleteException incomplete)
            {
                return incomplete.ParsedFrameCount > 0
                    ? $"状态：已读取 {incomplete.ParsedFrameCount} 帧，但回放加载未完成"
                    : "状态：回放文件未读到可用帧";
            }

            if (ex is InvalidProtocolBufferException)
            {
                return "状态：回放文件内容不完整或已损坏，请重新生成完整 .thuaipb";
            }

            if (ex is InvalidDataException)
            {
                return "状态：回放数据为空、过大或已损坏";
            }

            if (ex is FormatException formatException
                && (formatException.InnerException is InvalidProtocolBufferException
                    || formatException.Message.IndexOf("回放帧数据损坏", StringComparison.Ordinal) >= 0))
            {
                return "状态：回放文件内容不完整或已损坏，请重新生成完整 .thuaipb";
            }

            if (ex is FormatException)
            {
                return "状态：回放文件格式或版本不兼容，请使用当前逻辑组生成的 .thuaipb";
            }

            if (ex is IOException)
            {
                return "状态：读取回放文件失败：" + BuildSafeExceptionMessage(ex);
            }

            return "状态：回放加载失败，请重新选择文件；若持续失败再确认是否为当前逻辑组生成的 .thuaipb";
        }

        private string BuildPlaybackLoadedStatus()
        {
            string baseStatus = $"状态：已加载 {messageReader.GetMessageCount()} 帧（{messageReader.TeamCount}队/{messageReader.PlayerCount}玩家）";
            if (messageReader.IsLegacyVersion)
            {
                baseStatus += "，旧版回放建议用当前逻辑重新生成";
            }

            return baseStatus;
        }

        private static string BuildSafeExceptionMessage(Exception ex)
        {
            return string.IsNullOrWhiteSpace(ex.Message) ? "未知读写错误" : ex.Message;
        }

        private static void ClearWebGLDevelopmentConsole()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                THUAI9_ClearDevelopmentConsole();
            }
            catch
            {
                // Browser helper is best-effort only; playback success must not depend on the page chrome.
            }
#endif
        }

        private void ShowFirstFramePreview(string previewStatus = null)
        {
            if (!playbackLoaded || messageReader == null || TotalFrameCount <= 0)
            {
                return;
            }

            FrameSourceHub.Reset(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), "状态：准备显示首帧");
            RestoreCachedMap();
            messageReader.Seek(0);

            MessageToClient frame = messageReader.ReadNextMessage();
            if (frame == null)
            {
                currentFrameIndex = -1;
                return;
            }

            currentFrameIndex = messageReader.GetCurrentIndex();
            ApplyPlaybackClock(frame, currentFrameIndex);
            statusText = previewStatus ?? $"状态：已加载 {TotalFrameCount} 帧，显示首帧";
            FrameSourceHub.SubmitImmediate(frame, currentFrameIndex, currentPlaybackTimeMs, statusText);
        }

        public void Play()
        {
            if (!playbackLoaded || messageReader == null)
            {
                statusText = "状态：尚未加载回放文件";
                Debug.LogWarning("No playback file is loaded.");
                return;
            }

            if (IsAtLastFrame && (!isPlaying || isPaused))
            {
                if (playCoroutine != null)
                {
                    StopCoroutine(playCoroutine);
                    playCoroutine = null;
                }

                isPlaying = false;
                isPaused = false;
                ShowFirstFramePreview();
            }

            if (isPlaying && isPaused)
            {
                isPaused = false;
                statusText = "状态：播放中";
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
                return;
            }

            if (isPlaying)
            {
                return;
            }

            if (currentFrameIndex < 0)
            {
                FrameSourceHub.Reset(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), "状态：播放中");
                messageReader.StartPlay();
                currentFrameIndex = -1;
            }

            isPlaying = true;
            isPaused = false;
            statusText = "状态：播放中";
            FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
            playCoroutine = StartCoroutine(PlaybackLoop(currentFrameIndex >= 0));
        }

        public void TogglePlayPause()
        {
            if (isPlaying && !isPaused)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        public void Pause()
        {
            if (!isPlaying)
            {
                return;
            }

            isPaused = true;
            statusText = "状态：已暂停";
            FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
        }

        public void Stop()
        {
            StopInternal(true);
        }

        private void StopInternal(bool showFirstFrame)
        {
            isPlaying = false;
            isPaused = false;

            if (playCoroutine != null)
            {
                StopCoroutine(playCoroutine);
                playCoroutine = null;
            }

            if (showFirstFrame && playbackLoaded && HasBufferedPlaybackFrames)
            {
                ShowFirstFramePreview("状态：已停止，显示首帧");
                return;
            }

            FrameSourceHub.Reset(FrameSourceHub.SourceKind.None, "未选择", "状态：已停止");
            messageReader?.Reset();
            currentFrameIndex = -1;
            currentPlaybackTimeMs = 0;
            statusText = playbackLoaded ? "状态：已停止" : "状态：未加载回放文件";
            FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.None, "未选择", statusText);
        }

        public void SetSpeed(float speed)
        {
            playSpeed = Mathf.Clamp(speed, PlayBackConstant.MIN_PLAY_SPEED, PlayBackConstant.MAX_PLAY_SPEED);
            statusText = $"状态：播放速度 {playSpeed:0.##}x";
            FrameSourceHub.SetStatus(
                playbackLoaded ? FrameSourceHub.SourceKind.Playback : FrameSourceHub.ActiveKind,
                playbackLoaded ? BuildPlaybackSourceName() : FrameSourceHub.ActiveName,
                statusText);
        }

        public bool SeekToFrame(int index)
        {
            if (!playbackLoaded || messageReader == null)
            {
                statusText = "状态：无法跳转，未加载回放文件";
                return false;
            }

            int clampedIndex = Mathf.Clamp(index, 0, Mathf.Max(TotalFrameCount - 1, 0));
            bool wasPlaying = isPlaying && !isPaused;

            if (playCoroutine != null)
            {
                StopCoroutine(playCoroutine);
                playCoroutine = null;
            }

            isPlaying = false;
            isPaused = false;

            FrameSourceHub.Reset(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), "状态：正在跳转");
            RestoreCachedMap();

            messageReader.Seek(clampedIndex);

            var frame = messageReader.ReadNextMessage();
            if (frame == null)
            {
                currentFrameIndex = -1;
                statusText = "状态：跳转失败";
                return false;
            }

            currentFrameIndex = messageReader.GetCurrentIndex();
            ApplyPlaybackClock(frame, currentFrameIndex);
            statusText = $"状态：已定位到第 {currentFrameIndex + 1}/{TotalFrameCount} 帧";
            FrameSourceHub.SubmitImmediate(frame, currentFrameIndex, currentPlaybackTimeMs, statusText);

            if (wasPlaying)
            {
                isPlaying = true;
                statusText = "状态：播放中";
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
                playCoroutine = StartCoroutine(PlaybackLoop(true));
            }

            return true;
        }

        public bool StepForward()
        {
            if (!playbackLoaded)
            {
                statusText = "状态：无法前进，未加载回放文件";
                return false;
            }

            int target = currentFrameIndex < 0 ? 0 : currentFrameIndex + 1;
            if (target >= TotalFrameCount)
            {
                statusText = "状态：已经是最后一帧";
                return false;
            }

            bool result = SeekToFrame(target);
            if (result)
            {
                statusText = $"状态：已前进到第 {currentFrameIndex + 1}/{TotalFrameCount} 帧";
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
            }

            return result;
        }

        public bool StepBackward()
        {
            if (!playbackLoaded)
            {
                statusText = "状态：无法后退，未加载回放文件";
                return false;
            }

            int target = currentFrameIndex <= 0 ? 0 : currentFrameIndex - 1;
            bool result = SeekToFrame(target);
            if (result)
            {
                statusText = $"状态：已后退到第 {currentFrameIndex + 1}/{TotalFrameCount} 帧";
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
            }

            return result;
        }

        private IEnumerator PlaybackLoop(bool currentFrameAlreadyPrepared)
        {
            bool framePrepared = currentFrameAlreadyPrepared;
            int previousGameTimeMs = CoreParam.currentFrame?.AllMessage?.GameTime ?? -1;

            while (isPlaying)
            {
                if (isPaused)
                {
                    yield return null;
                    continue;
                }

                var message = messageReader.ReadNextMessage();
                if (message == null)
                {
                    isPlaying = false;
                    isPaused = false;
                    playCoroutine = null;
                    statusText = "状态：播放结束";
                    FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
                    yield break;
                }

                int currentGameTimeMs = message.AllMessage?.GameTime ?? -1;
                if (framePrepared)
                {
                    float deltaMs = previousGameTimeMs >= 0 && currentGameTimeMs > previousGameTimeMs
                        ? currentGameTimeMs - previousGameTimeMs
                        : PlayBackConstant.MILLISECONDS_PER_FRAME;
                    if (deltaMs <= 0)
                    {
                        deltaMs = PlayBackConstant.MILLISECONDS_PER_FRAME;
                    }
                    else if (deltaMs > PlayBackConstant.MAX_REASONABLE_FRAME_DELTA_MS)
                    {
                        deltaMs = PlayBackConstant.SERVER_FRAME_INTERVAL_MS;
                    }

                    float delaySeconds = deltaMs / 1000f / Mathf.Max(playSpeed, PlayBackConstant.MIN_PLAY_SPEED);
                    yield return WaitWhileRespectingPause(delaySeconds);
                }

                if (!isPlaying)
                {
                    yield break;
                }

                currentFrameIndex = messageReader.GetCurrentIndex();
                ApplyPlaybackClock(message, currentFrameIndex);
                FrameSourceHub.EnqueueFrame(message, currentFrameIndex, currentPlaybackTimeMs, "状态：播放中");
                framePrepared = true;

                previousGameTimeMs = currentGameTimeMs;
            }

            playCoroutine = null;
        }

        private IEnumerator WaitWhileRespectingPause(float seconds)
        {
            float elapsed = 0f;
            while (elapsed < seconds && isPlaying)
            {
                if (!isPaused)
                {
                    elapsed += Time.unscaledDeltaTime;
                }
                yield return null;
            }
        }

        private void RestoreCachedMap()
        {
            if (playbackMap != null)
            {
                CoreParam.map = playbackMap;
            }
        }

        private MessageOfMap FindPlaybackMap()
        {
            for (int i = 0; i < TotalFrameCount; i++)
            {
                MessageToClient frame = messageReader.ReadMessageAt(i);
                if (frame == null)
                {
                    continue;
                }

                foreach (MessageOfObj obj in frame.ObjMessage)
                {
                    if (obj.MessageOfObjCase == MessageOfObj.MessageOfObjOneofCase.MapMessage)
                    {
                        return obj.MapMessage;
                    }
                }
            }

            return null;
        }

        private void ApplyPlaybackClock(MessageToClient frame, int frameIndex)
        {
            currentPlaybackTimeMs = GetElapsedPlaybackMilliseconds(frame, frameIndex);
            FrameSourceHub.ApplyPlaybackClock(frameIndex, currentPlaybackTimeMs);
        }

        private string BuildPlaybackSourceName()
        {
            return string.IsNullOrWhiteSpace(playbackSourceDisplayName)
                ? "Playback"
                : $"Playback: {playbackSourceDisplayName}";
        }

        private int GetElapsedPlaybackMilliseconds(MessageToClient frame, int frameIndex)
        {
            int gameTimeMs = GetFrameGameTimeMs(frame);
            if (firstFrameGameTimeMs >= 0 && gameTimeMs >= firstFrameGameTimeMs)
            {
                int elapsed = gameTimeMs - firstFrameGameTimeMs;
                int fallbackElapsed = CoreParam.ClampDisplayGameMilliseconds(
                    Mathf.RoundToInt(Mathf.Max(frameIndex, 0) * PlayBackConstant.SERVER_FRAME_INTERVAL_MS));
                int reasonableUpperBound = Mathf.RoundToInt((Mathf.Max(frameIndex, 0) + 1) * PlayBackConstant.MAX_REASONABLE_FRAME_DELTA_MS);

                return elapsed <= reasonableUpperBound && elapsed <= CoreParam.MaximumDisplayGameMilliseconds
                    ? elapsed
                    : fallbackElapsed;
            }

            return CoreParam.ClampDisplayGameMilliseconds(
                Mathf.RoundToInt(Mathf.Max(frameIndex, 0) * PlayBackConstant.SERVER_FRAME_INTERVAL_MS));
        }

        private static int GetFrameGameTimeMs(MessageToClient frame)
        {
            return frame?.AllMessage != null ? Mathf.Max(frame.AllMessage.GameTime, 0) : -1;
        }

        private static string NormalizePlaybackPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return filePath;
            }

            string normalized = filePath.Replace('\\', '/');
            if (!normalized.EndsWith(PlayBackConstant.PLAYBACK_EXTENSION, StringComparison.OrdinalIgnoreCase))
            {
                normalized += PlayBackConstant.PLAYBACK_EXTENSION;
            }

            if (Path.IsPathRooted(normalized))
            {
                return normalized;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string projectRelativePath = Path.GetFullPath(Path.Combine(projectRoot, normalized));
            return File.Exists(projectRelativePath) ? projectRelativePath : normalized;
        }

        public static bool IsPlaybackUrl(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            string trimmed = source.Trim().Trim('"');
            if (trimmed.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
                || trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri)
                && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile);
        }

        private static string GetPlaybackDisplayName(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return "Playback";
            }

            string trimmed = source.Trim().Trim('"');
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out Uri uri))
            {
                string uriFileName = SafeFileName(uri.LocalPath);
                if (!string.IsNullOrWhiteSpace(uriFileName))
                {
                    return Uri.UnescapeDataString(uriFileName);
                }

                if (trimmed.StartsWith("blob:", StringComparison.OrdinalIgnoreCase)
                    || trimmed.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    return "WebGL playback";
                }
            }

            string fileName = SafeFileName(trimmed);
            return string.IsNullOrWhiteSpace(fileName) ? trimmed : fileName;
        }

        private static string SafeFileName(string source)
        {
            try
            {
                return Path.GetFileName(source);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool TryDecodeDataUrl(string source, out byte[] data)
        {
            data = null;
            if (string.IsNullOrWhiteSpace(source) || !source.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            int comma = source.IndexOf(',');
            if (comma < 0 || comma >= source.Length - 1)
            {
                return false;
            }

            string metadata = source.Substring(0, comma);
            string payload = source.Substring(comma + 1);
            try
            {
                data = metadata.IndexOf(";base64", StringComparison.OrdinalIgnoreCase) >= 0
                    ? Convert.FromBase64String(payload)
                    : System.Text.Encoding.UTF8.GetBytes(Uri.UnescapeDataString(payload));
                return data != null && data.Length > 0;
            }
            catch
            {
                data = null;
                return false;
            }
        }

        [Serializable]
        private sealed class RoomTeamsGraphqlRequest
        {
            public string query;
            public RoomTeamsGraphqlVariables variables;
        }

        [Serializable]
        private sealed class RoomTeamsGraphqlVariables
        {
            public string room_id;
        }

        [Serializable]
        private sealed class RoomTeamsGraphqlResponse
        {
            public RoomTeamsGraphqlData data;
            public RoomTeamsGraphqlError[] errors;
        }

        [Serializable]
        private sealed class RoomTeamsGraphqlData
        {
            public RoomInfo contest_room_by_pk;
        }

        [Serializable]
        private sealed class RoomInfo
        {
            public RoomTeamInfo[] contest_room_teams;
        }

        [Serializable]
        private sealed class RoomTeamInfo
        {
            public string team_label;
            public int score;
            public string player_roles;
            public ContestTeamInfo contest_team;
        }

        [Serializable]
        private sealed class ContestTeamInfo
        {
            public string team_name;
        }

        [Serializable]
        private sealed class RoomTeamsGraphqlError
        {
            public string message;
        }

        private void OnDestroy()
        {
            if (loadCoroutine != null)
            {
                StopCoroutine(loadCoroutine);
                loadCoroutine = null;
            }

            if (teamNamesCoroutine != null)
            {
                StopCoroutine(teamNamesCoroutine);
                teamNamesCoroutine = null;
            }

            StopInternal(false);
            messageReader?.Dispose();
        }

        private void ApplyDefaultPlaybackSettings()
        {
            if (string.IsNullOrWhiteSpace(playbackFilePath) || IsLegacyDefaultPlaybackPath(playbackFilePath))
            {
                playbackFilePath = DefaultPlaybackRelativePath;
            }

            autoPlayOnLoad = false;
        }

        private static bool IsLegacyDefaultPlaybackPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return false;
            }

            string normalized = filePath.Replace('\\', '/');
            return normalized.EndsWith("/test_replay.thuaipb", StringComparison.OrdinalIgnoreCase)
                || normalized.EndsWith("/official_bot_match.thuaipb", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("test_replay.thuaipb", StringComparison.OrdinalIgnoreCase)
                || normalized.Equals("official_bot_match.thuaipb", StringComparison.OrdinalIgnoreCase);
        }
    }
}
