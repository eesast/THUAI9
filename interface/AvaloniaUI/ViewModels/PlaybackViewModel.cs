using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Protobuf;
using System;
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
        private readonly PlaybackReader _reader = new();
        private System.Timers.Timer? _playbackTimer;
        private TimeSpan _totalDuration;
        private TimeSpan _currentPosition;
        private int _currentFrame;
        private int _totalFrames;
        private Action<MessageToClient>? _onMessageReceived;

        [ObservableProperty]
        private PlaybackState state = PlaybackState.Stopped;

        [ObservableProperty]
        private double progress;

        [ObservableProperty]
        private string currentPositionText = "00:00";

        [ObservableProperty]
        private string totalDurationText = "10:00";

        [ObservableProperty]
        private double playbackSpeed = 1.0;

        [ObservableProperty]
        private bool isFileLoaded;

        [ObservableProperty]
        private string filePath = string.Empty;

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
                _totalDuration = TimeSpan.FromMilliseconds(_totalFrames * 100);
                TotalDurationText = FormatTime(_totalDuration);
                CurrentPositionText = "00:00";
                Progress = 0;
                _currentFrame = 0;
                State = PlaybackState.Stopped;
            }
            catch (Exception ex)
            {
                IsFileLoaded = false;
                FilePath = string.Empty;
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
            Progress = 0;
            CurrentPositionText = "00:00";
            State = PlaybackState.Stopped;
        }

        [RelayCommand]
        public void Seek(double newProgress)
        {
            if (!IsFileLoaded || _totalFrames == 0)
            {
                return;
            }

            var targetFrame = (int)(newProgress * _totalFrames);
            SeekToFrame(targetFrame);
        }

        private void SeekToFrame(int frameIndex)
        {
            _reader.Reset();
            _currentFrame = 0;

            while (_currentFrame < frameIndex)
            {
                var msg = _reader.ReadNext();
                if (msg == null)
                {
                    break;
                }
                _currentFrame++;
            }

            Progress = (double)_currentFrame / _totalFrames;
            UpdateCurrentPositionText();
        }

        partial void OnPlaybackSpeedChanging(double value)
        {
            if (State == PlaybackState.Playing)
            {
                StopPlaybackTimer();
                StartPlaybackTimer();
            }
        }

        private void StartPlaybackTimer()
        {
            double interval = 100 / PlaybackSpeed;
            _playbackTimer = new System.Timers.Timer(interval);
            _playbackTimer.Elapsed += OnPlaybackTimerElapsed;
            _playbackTimer.Start();
        }

        private void StopPlaybackTimer()
        {
            if (_playbackTimer != null)
            {
                _playbackTimer.Stop();
                _playbackTimer.Dispose();
                _playbackTimer = null;
            }
        }

        private void OnPlaybackTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
        {
            var message = _reader.ReadNext();
            if (message == null)
            {
                Stop();
                return;
            }

            _currentFrame++;
            Progress = (double)_currentFrame / _totalFrames;
            UpdateCurrentPositionText();
            _onMessageReceived?.Invoke(message);
        }

        private void UpdateCurrentPositionText()
        {
            _currentPosition = TimeSpan.FromMilliseconds(_totalDuration.TotalMilliseconds * Progress);
            CurrentPositionText = FormatTime(_currentPosition);
        }

        private string FormatTime(TimeSpan time)
        {
            return $"{(int)time.TotalMinutes:D2}:{time.Seconds:D2}";
        }

        public override void Dispose()
        {
            StopPlaybackTimer();
            _reader.Dispose();
            base.Dispose();
        }
    }
}
