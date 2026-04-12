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
        private const double FrameIntervalMs = 100;
        private readonly PlaybackReader _reader = new();
        private System.Timers.Timer? _playbackTimer;
        private int _currentFrame;
        private int _totalFrames;
        private Action<MessageToClient>? _onMessageReceived;

        [ObservableProperty]
        private PlaybackState state = PlaybackState.Stopped;

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
            State = PlaybackState.Stopped;
        }

        private void StartPlaybackTimer()
        {
            _playbackTimer = new System.Timers.Timer(FrameIntervalMs);
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
            _onMessageReceived?.Invoke(message);
        }

        public override void Dispose()
        {
            StopPlaybackTimer();
            _reader.Dispose();
            base.Dispose();
        }
    }
}
