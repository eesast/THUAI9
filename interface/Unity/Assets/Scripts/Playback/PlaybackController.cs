using System;
using System.Collections;
using System.IO;
using Protobuf;
using THUAI9.Unity.Core;
using THUAI9.Unity.Render;
using UnityEngine;

namespace THUAI9.Unity.Playback
{
    public class PlaybackController : MonoBehaviour
    {
        private const string DefaultPlaybackRelativePath = "Assets/Playback/test/official_bot_match.thuaipb";

        [Header("回放文件")]
        public string playbackFilePath = DefaultPlaybackRelativePath;

        [Header("播放状态")]
        public bool isPlaying;
        public bool isPaused;
        public bool autoPlayOnLoad = false;

        [Header("播放速度")]
        public float playSpeed = PlayBackConstant.DEFAULT_PLAY_SPEED;

        private MessageReader messageReader;
        private Coroutine playCoroutine;
        private bool playbackLoaded;
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

            if (!string.IsNullOrWhiteSpace(playbackFilePath))
            {
                LoadPlaybackFile(playbackFilePath);
            }
        }

        public void LoadPlaybackFile(string filePath)
        {
            if (playCoroutine != null || isPlaying || isPaused)
            {
                StopInternal(false);
            }

            playbackFilePath = NormalizePlaybackPath(filePath);
            playbackLoaded = false;
            currentFrameIndex = -1;
            firstFrameGameTimeMs = -1;
            currentPlaybackTimeMs = 0;
            playbackMap = null;
            CoreParam.playbackCurrentFrameIndex = -1;
            CoreParam.playbackElapsedMilliseconds = 0;
            statusText = "状态：正在加载回放文件";

            if (!File.Exists(playbackFilePath))
            {
                statusText = "状态：未找到回放文件";
                Debug.LogError($"Playback file does not exist: {playbackFilePath}");
                return;
            }

            try
            {
                byte[] data = File.ReadAllBytes(playbackFilePath);
                messageReader?.LoadData(data);
                playbackLoaded = messageReader != null && messageReader.GetMessageCount() > 0;

                if (!playbackLoaded)
                {
                    statusText = "状态：回放文件中没有可读取帧";
                    Debug.LogError($"Playback file contains no readable frames: {playbackFilePath}");
                    return;
                }

                firstFrameGameTimeMs = GetFrameGameTimeMs(messageReader.ReadMessageAt(0));
                currentPlaybackTimeMs = 0;
                playbackMap = FindPlaybackMap();
                statusText = $"状态：已加载 {messageReader.GetMessageCount()} 帧";
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
                playbackLoaded = false;
                statusText = "状态：加载回放失败";
                Debug.LogError($"Failed to load playback file: {ex.Message}");
            }
        }

        private void ShowFirstFramePreview(string previewStatus = null)
        {
            if (!playbackLoaded || messageReader == null || TotalFrameCount <= 0)
            {
                return;
            }

            CoreParam.Reset();
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
            CoreParam.frameCount = currentFrameIndex;
            RenderManager.Instance.RenderFrame(frame);
            statusText = previewStatus ?? $"状态：已加载 {TotalFrameCount} 帧，显示首帧";
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
                return;
            }

            if (isPlaying)
            {
                return;
            }

            if (currentFrameIndex < 0)
            {
                CoreParam.Reset();
                messageReader.StartPlay();
                currentFrameIndex = -1;
            }

            isPlaying = true;
            isPaused = false;
            statusText = "状态：播放中";
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

            CoreParam.Reset();
            messageReader?.Reset();
            currentFrameIndex = -1;
            currentPlaybackTimeMs = 0;
            statusText = playbackLoaded ? "状态：已停止" : "状态：未加载回放文件";
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

            CoreParam.Reset();
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
            CoreParam.frameCount = currentFrameIndex;
            RenderManager.Instance.RenderFrame(frame);
            statusText = $"状态：已定位到第 {currentFrameIndex + 1}/{TotalFrameCount} 帧";

            if (wasPlaying)
            {
                isPlaying = true;
                statusText = "状态：播放中";
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

                    float delaySeconds = deltaMs / 1000f / Mathf.Max(playSpeed, PlayBackConstant.MIN_PLAY_SPEED);
                    yield return WaitWhileRespectingPause(delaySeconds);
                }

                if (!isPlaying)
                {
                    yield break;
                }

                currentFrameIndex = messageReader.GetCurrentIndex();
                ApplyPlaybackClock(message, currentFrameIndex);
                CoreParam.frameCount = currentFrameIndex;
                RenderManager.Instance.RenderFrame(message);
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
            CoreParam.playbackCurrentFrameIndex = frameIndex;
            CoreParam.playbackElapsedMilliseconds = currentPlaybackTimeMs;
        }

        private int GetElapsedPlaybackMilliseconds(MessageToClient frame, int frameIndex)
        {
            int gameTimeMs = GetFrameGameTimeMs(frame);
            if (firstFrameGameTimeMs >= 0 && gameTimeMs >= firstFrameGameTimeMs)
            {
                return gameTimeMs - firstFrameGameTimeMs;
            }

            return Mathf.RoundToInt(Mathf.Max(frameIndex, 0) * PlayBackConstant.MILLISECONDS_PER_FRAME);
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

        private void OnDestroy()
        {
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
                || normalized.Equals("test_replay.thuaipb", StringComparison.OrdinalIgnoreCase);
        }
    }
}
