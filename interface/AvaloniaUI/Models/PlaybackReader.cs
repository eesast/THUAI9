using System;
using System.IO;
using Protobuf;

namespace THUAI9_Avalonia.Models
{
    /// <summary>
    /// 回放文件读取器，对 logic/PlayBack/MessageReader 做了一层封装。
    /// </summary>
    public class PlaybackReader : IDisposable
    {
        private Playback.MessageReader? _reader;
        private bool _disposed;

        public string FilePath { get; private set; } = string.Empty;
        public uint TeamCount { get; private set; }
        public uint PlayerCount { get; private set; }
        public bool IsOpened { get; private set; }
        public bool IsClosed { get; private set; }

        public void Open(string filePath)
        {
            if (!File.Exists(filePath))
            {
                throw new FileNotFoundException("回放文件不存在", filePath);
            }

            Close();

            FilePath = filePath;
            _reader = new Playback.MessageReader(filePath);
            TeamCount = _reader.teamCount;
            PlayerCount = _reader.playerCount;
            IsOpened = true;
            IsClosed = false;
        }

        public MessageToClient? ReadNext()
        {
            if (_reader == null || _reader.Disposed)
            {
                IsClosed = true;
                return null;
            }

            var message = _reader.ReadOne();
            if (message == null)
            {
                IsClosed = true;
            }
            return message;
        }

        public void Close()
        {
            if (_reader != null && !_reader.Disposed)
            {
                _reader.Dispose();
            }
            _reader = null;
            IsOpened = false;
            IsClosed = false;
        }

        public void Reset()
        {
            var currentPath = FilePath;
            Close();
            if (!string.IsNullOrEmpty(currentPath))
            {
                Open(currentPath);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            Close();
            _disposed = true;
        }
    }
}
