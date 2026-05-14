using System;
using System.Collections;
using System.IO;
using System.Runtime.InteropServices;
using Protobuf;
using THUAI9.Unity.Core;
using UnityEngine;
using UnityEngine.Networking;

namespace THUAI9.Unity.Playback
{
    public class PlaybackController : MonoBehaviour
    {
        private const string DefaultPlaybackRelativePath = "";

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
        private bool playbackLoaded;
        private string playbackSourceDisplayName;
        private string statusText = "状态：未加载回放文件";
        private int currentFrameIndex = -1;
        private int firstFrameGameTimeMs = -1;
        private int currentPlaybackTimeMs;
        private MessageOfMap playbackMap;

        public bool PlaybackLoaded => playbackLoaded;
        public int TotalFrameCount => messageReader?.GetMessageCount() ?? 0;
        public int CurrentFrameIndex => currentFrameIndex;
        public int CurrentPlaybackTimeMs => currentPlaybackTimeMs;
        public string StatusText => statusText;
        public bool IsAtLastFrame => playbackLoaded && TotalFrameCount > 0 && currentFrameIndex >= TotalFrameCount - 1;

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
            PreparePlaybackLoad(normalizedPath, GetPlaybackDisplayName(normalizedPath), "Status: loading playback file");

            if (!File.Exists(playbackFilePath))
            {
                statusText = "Status: playback file not found";
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
                Debug.LogWarning($"Playback file does not exist: {playbackFilePath}");
                return;
            }

            try
            {
                LoadPlaybackData(File.ReadAllBytes(playbackFilePath));
            }
            catch (Exception ex)
            {
                MarkPlaybackLoadFailed("Status: failed to load playback", ex);
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
                statusText = "Status: playback URL is empty";
                FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
                return;
            }

            string trimmedUrl = url.Trim().Trim('"');
            PreparePlaybackLoad(trimmedUrl, displayName ?? GetPlaybackDisplayName(trimmedUrl), "Status: loading playback from browser");
            loadCoroutine = StartCoroutine(LoadPlaybackUrlCoroutine(trimmedUrl));
        }

        public void LoadPlaybackBytes(byte[] data, string displayName = null)
        {
            PreparePlaybackLoad(displayName ?? "WebGL playback bytes", displayName ?? "WebGL playback", "Status: loading playback bytes from browser");
            LoadPlaybackData(data);
        }

        private IEnumerator LoadPlaybackUrlCoroutine(string url)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(url))
            {
                request.downloadHandler = new DownloadHandlerBuffer();
                yield return request.SendWebRequest();

                loadCoroutine = null;
                if (request.result != UnityWebRequest.Result.Success)
                {
                    MarkPlaybackLoadFailed($"Status: browser playback load failed, {request.error}", null);
                    yield break;
                }

                LoadPlaybackData(request.downloadHandler?.data);
            }
        }

        private void PreparePlaybackLoad(string source, string displayName, string loadingStatus)
        {
            if (playCoroutine != null || isPlaying || isPaused)
            {
                StopInternal(false);
            }

            if (loadCoroutine != null)
            {
                StopCoroutine(loadCoroutine);
                loadCoroutine = null;
            }

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
        }

        private void LoadPlaybackData(byte[] data)
        {
            try
            {
                if (data == null || data.Length == 0)
                {
                    throw new InvalidDataException("Playback data is empty.");
                }

                messageReader ??= new MessageReader();
                messageReader.LoadData(data);
                playbackLoaded = messageReader != null && messageReader.GetMessageCount() > 0;

                if (!playbackLoaded)
                {
                    statusText = "Status: playback file has no readable frames";
                    FrameSourceHub.SetStatus(FrameSourceHub.SourceKind.Playback, BuildPlaybackSourceName(), statusText);
                    Debug.LogWarning($"Playback file contains no readable frames: {playbackFilePath}");
                    return;
                }

                firstFrameGameTimeMs = GetFrameGameTimeMs(messageReader.ReadMessageAt(0));
                currentPlaybackTimeMs = 0;
                playbackMap = FindPlaybackMap();
                statusText = messageReader.IsLegacyVersion
                    ? $"状态：已加载旧版回放 v{messageReader.FileVersion}，共 {messageReader.GetMessageCount()} 帧（建议使用当前逻辑重新生成）"
                    : $"状态：已加载 {messageReader.GetMessageCount()} 帧";
                ClearWebGLDevelopmentConsole();
                if (autoPlayOnLoad)
                {
                    Play();
                }
                else
                {
                    ShowFirstFramePreview();
                }
            }
            catch (Exception ex)
            {
                MarkPlaybackLoadFailed("Status: failed to load playback", ex);
            }
        }

        private void MarkPlaybackLoadFailed(string status, Exception ex)
        {
            playbackLoaded = false;
            statusText = BuildPlaybackFailureStatus(status, ex);
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

            if (ex is InvalidDataException)
            {
                return "状态：回放数据为空或已损坏";
            }

            if (ex is FormatException)
            {
                return "状态：回放文件格式或版本不兼容，请使用当前逻辑组生成的 .thuaipb";
            }

            if (ex is IOException)
            {
                return "状态：读取回放文件失败，请检查文件是否被占用或路径是否有效";
            }

            return "状态：回放加载失败，请换用当前逻辑组生成的 .thuaipb";
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

            if (showFirstFrame && playbackLoaded && messageReader != null && TotalFrameCount > 0)
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

        private void OnDestroy()
        {
            if (loadCoroutine != null)
            {
                StopCoroutine(loadCoroutine);
                loadCoroutine = null;
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
