using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Protobuf;
using System;
using System.Collections.Generic;
using THUAI9_Avalonia.Models;

namespace THUAI9_Avalonia.ViewModels
{
    public enum PlaybackState
    {
        Stopped,
        Playing,
        Paused
    }

    public partial class PlaybackViewModel : ViewModelBase
    {
        private const double FrameIntervalMs = 100;
        private const int MaximumReasonableLiveGameMilliseconds = 2 * 60 * 60 * 1000;
        private const int MaximumSingleLiveTimeJumpMilliseconds = 10 * 60 * 1000;
        private static readonly double[] PlaybackSpeeds = { 0.5, 1, 2, 4 };
        private readonly PlaybackReader _reader = new();
        private DispatcherTimer? _playbackTimer;
        private int _currentFrame;
        private int _totalFrames;
        private int _stableLiveGameTimeMs;
        private bool _hasStableLiveGameTime;
        private Action<MessageToClient>? _onMessageReceived;

        [ObservableProperty]
        private PlaybackState state = PlaybackState.Stopped;

        [ObservableProperty]
        private bool isFileLoaded;

        [ObservableProperty]
        private string filePath = string.Empty;

        [ObservableProperty]
        private int playbackSpeedIndex = 1;

        [ObservableProperty]
        private string playbackTimeText = "00:00";

        [ObservableProperty]
        private string frameProgressText = "帧 0/0";

        public IReadOnlyList<string> PlaybackSpeedLabels { get; } = new[] { "0.5x", "1x", "2x", "4x" };

        public double PlaybackSpeed => PlaybackSpeeds[Math.Clamp(PlaybackSpeedIndex, 0, PlaybackSpeeds.Length - 1)];

        public void SetMessageCallback(Action<MessageToClient> callback)
        {
            _onMessageReceived = callback;
        }

        [RelayCommand]
        public void LoadPlayback(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return;
            }

            try
            {
                _reader.Open(filePath);
                FilePath = filePath;
                IsFileLoaded = true;
                _totalFrames = CountTotalFrames();
                _currentFrame = 0;
                State = PlaybackState.Stopped;
                UpdatePlaybackProgress(null);
            }
            catch (Exception ex)
            {
                IsFileLoaded = false;
                FilePath = string.Empty;
                _currentFrame = 0;
                _totalFrames = 0;
                UpdatePlaybackProgress(null);
                System.Diagnostics.Debug.WriteLine($"加载回放失败：{ex.Message}");
                throw;
            }
        }

        private int CountTotalFrames()
        {
            int count = 0;
            while (_reader.ReadNext() != null)
            {
                count++;
            }
            _reader.Reset();
            return count;
        }

        [RelayCommand]
        public void Play()
        {
            if (State == PlaybackState.Playing || !IsFileLoaded)
            {
                return;
            }

            State = PlaybackState.Playing;
            StartPlaybackTimer();
        }

        [RelayCommand]
        public void Pause()
        {
            if (State != PlaybackState.Playing)
            {
                return;
            }

            State = PlaybackState.Paused;
            StopPlaybackTimer();
        }

        [RelayCommand]
        public void Stop()
        {
            StopPlaybackTimer();
            _reader.Reset();
            _currentFrame = 0;
            State = PlaybackState.Stopped;
            UpdatePlaybackProgress(null);
        }

        private void StartPlaybackTimer()
        {
            StopPlaybackTimer();
            _playbackTimer = new DispatcherTimer
            {
                Interval = GetPlaybackInterval()
            };
            _playbackTimer.Tick += OnPlaybackTimerElapsed;
            _playbackTimer.Start();
        }

        private void StopPlaybackTimer()
        {
            if (_playbackTimer != null)
            {
                _playbackTimer.Stop();
                _playbackTimer.Tick -= OnPlaybackTimerElapsed;
                _playbackTimer = null;
            }
        }

        private void OnPlaybackTimerElapsed(object? sender, EventArgs e)
        {
            var message = _reader.ReadNext();
            if (message == null)
            {
                Stop();
                return;
            }

            _currentFrame++;
            UpdatePlaybackProgress(message);
            _onMessageReceived?.Invoke(message);
        }

        private TimeSpan GetPlaybackInterval()
        {
            return TimeSpan.FromMilliseconds(FrameIntervalMs / Math.Max(PlaybackSpeed, 0.1));
        }

        private void UpdatePlaybackProgress(MessageToClient? message)
        {
            int gameTimeMs = message?.AllMessage?.GameTime ?? (int)Math.Round(_currentFrame * FrameIntervalMs);
            PlaybackTimeText = FormatGameTime(gameTimeMs);
            FrameProgressText = IsFileLoaded ? $"帧 {_currentFrame}/{_totalFrames}" : "帧 0/0";
        }

        public void UpdateLiveProgress(MessageToClient? message, int liveFrameCount)
        {
            int safeFrameCount = Math.Max(liveFrameCount, 0);
            int gameTimeMs = ResolveStableLiveGameTime(message, safeFrameCount);
            PlaybackTimeText = FormatGameTime(gameTimeMs);
            FrameProgressText = safeFrameCount > 0 ? $"实时帧 {safeFrameCount}" : "实时帧 0";
        }

        private int ResolveStableLiveGameTime(MessageToClient? message, int liveFrameCount)
        {
            int estimatedGameTimeMs = EstimateLiveGameTime(liveFrameCount);

            if (message?.AllMessage == null)
            {
                if (message == null && liveFrameCount <= 0)
                {
                    ResetStableLiveGameTime();
                }

                return _hasStableLiveGameTime ? _stableLiveGameTimeMs : estimatedGameTimeMs;
            }

            int rawGameTimeMs = message.AllMessage.GameTime;
            if (AcceptLiveGameTime(rawGameTimeMs, message.GameState))
            {
                _stableLiveGameTimeMs = _hasStableLiveGameTime
                    ? Math.Max(_stableLiveGameTimeMs, rawGameTimeMs)
                    : rawGameTimeMs;
                _hasStableLiveGameTime = true;
            }

            return _hasStableLiveGameTime ? _stableLiveGameTimeMs : estimatedGameTimeMs;
        }

        private bool AcceptLiveGameTime(int rawGameTimeMs, GameState gameState)
        {
            if (rawGameTimeMs < 0 || rawGameTimeMs > MaximumReasonableLiveGameMilliseconds)
            {
                return false;
            }

            if (!_hasStableLiveGameTime)
            {
                return true;
            }

            if (gameState == GameState.GameEnd && rawGameTimeMs > _stableLiveGameTimeMs + MaximumSingleLiveTimeJumpMilliseconds)
            {
                return false;
            }

            return rawGameTimeMs <= _stableLiveGameTimeMs + MaximumSingleLiveTimeJumpMilliseconds;
        }

        private static int EstimateLiveGameTime(int liveFrameCount)
        {
            return (int)Math.Round(Math.Max(liveFrameCount, 0) * FrameIntervalMs);
        }

        private void ResetStableLiveGameTime()
        {
            _stableLiveGameTimeMs = 0;
            _hasStableLiveGameTime = false;
        }

        private static string FormatGameTime(int gameTimeMs)
        {
            if (gameTimeMs < 0)
            {
                gameTimeMs = 0;
            }

            var time = TimeSpan.FromMilliseconds(gameTimeMs);
            return time.TotalHours >= 1
                ? $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}"
                : $"{time.Minutes:00}:{time.Seconds:00}";
        }

        partial void OnPlaybackSpeedIndexChanged(int value)
        {
            OnPropertyChanged(nameof(PlaybackSpeed));
            if (_playbackTimer != null)
            {
                _playbackTimer.Interval = GetPlaybackInterval();
            }
        }

        public override void Dispose()
        {
            StopPlaybackTimer();
            _reader.Dispose();
            base.Dispose();
        }
    }
}
