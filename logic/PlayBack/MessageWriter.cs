using Google.Protobuf;
using Protobuf;
using System.IO.Compression;

namespace Playback
{
    public class MessageWriter : IDisposable
    {
        public string FileName { get; }

        /// <summary>
        /// 总写入消息数
        /// </summary>
        public uint WrittenNum { get; private set; } = 0;

        private uint sinceLastFlush = 0;
        private readonly uint FlushNum;

        private readonly FileStream fs;
        private readonly GZipStream gzs;
        private readonly CodedOutputStream cos;
        public bool Disposed { get; private set; } = false;

        public MessageWriter(string fileName, uint teamCount, uint playerCount, uint flushNum = 50)
        {
            Utils.FileNameRegular(ref fileName);
            fs = File.Create(fileName);
            FileName = fs.Name;
            fs.WriteHeader(teamCount, playerCount);
            gzs = new(fs, CompressionMode.Compress);
            cos = new(gzs);
            FlushNum = flushNum;
        }

        public void WriteOne(MessageToClient msg)
        {
            if (Disposed) return;
            try
            {
                cos.WriteMessage(msg);
                WrittenNum++;
                sinceLastFlush++;

                if (sinceLastFlush >= FlushNum)
                {
                    cos.Flush();
                    sinceLastFlush = 0;
                }
            }
            catch (Exception)
            {
                // 写回放文件失败不应影响游戏主循环
            }
        }

        public void Flush()
        {
            try
            {
                cos.Flush();
                sinceLastFlush = 0;
            }
            catch { }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (Disposed) return;
            if (disposing)
            {
                try { cos.Flush(); } catch { }
                try { cos.Dispose(); } catch { }
                try { gzs.Dispose(); } catch { }
                try { fs.Dispose(); } catch { }
            }
            Disposed = true;
        }

        ~MessageWriter()
        {
            Dispose(false);
        }
    }
}
