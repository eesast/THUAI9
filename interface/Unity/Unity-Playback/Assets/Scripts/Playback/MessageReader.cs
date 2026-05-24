using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Google.Protobuf;
using Protobuf;

namespace THUAI9.Unity.Playback
{
    public sealed class PlaybackFileIncompleteException : Exception
    {
        public PlaybackFileIncompleteException(string message, int parsedFrameCount)
            : base(message)
        {
            ParsedFrameCount = parsedFrameCount;
        }

        public int ParsedFrameCount { get; }
    }

    /// <summary>
    /// THUAI9 ????????
    /// ?????????
    /// 1. logic/PlayBack/MessageWriter ????????GZip ????? protobuf length-delimited MessageToClient?
    /// 2. ?? Unity ???????GZip ???????????????protobuf ????
    ///
    /// ?????? logic ??????
    /// - ????PB + ?? + ????
    /// - uint32 teamCount
    /// - uint32 playerCount
    /// </summary>
    public class MessageReader
    {
        private readonly List<MessageToClient> messages = new List<MessageToClient>();
        private int currentMsgIndex = -1;
        private bool disposed;

        public uint TeamCount { get; private set; }
        public uint PlayerCount { get; private set; }
        public byte FileVersion { get; private set; }
        public bool IsLegacyVersion => FileVersion != 0 && FileVersion != PlayBackConstant.FILE_VERSION;
        public bool IsIncompleteTail { get; private set; }
        public string LoadWarning { get; private set; }
        public int DecompressedByteCount { get; private set; }

        public void LoadData(byte[] data)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MessageReader));
            }

            ResetLoadedData();

            if (data == null || data.Length < 12)
            {
                throw new FormatException("回放文件头不完整。");
            }

            using (var headerStream = new MemoryStream(data))
            using (var headerReader = new BinaryReader(headerStream))
            {
                byte magic1 = headerReader.ReadByte();
                byte magic2 = headerReader.ReadByte();
                if (magic1 != 'P' || magic2 != 'B')
                {
                    throw new FormatException($"Invalid playback header magic: {(char)magic1}{(char)magic2}; expected PB.");
                }

                byte version = headerReader.ReadByte();
                _ = headerReader.ReadByte();
                TeamCount = headerReader.ReadUInt32();
                PlayerCount = headerReader.ReadUInt32();
                FileVersion = version;

                if (!PlayBackConstant.IsSupportedFileVersion(version))
                {
                    throw new FormatException($"Unsupported playback version {version}; current version is {PlayBackConstant.FILE_VERSION}.");
                }
            }

            const int headerSize = 12;
            byte[] compressedData = new byte[data.Length - headerSize];
            Array.Copy(data, headerSize, compressedData, 0, compressedData.Length);

            byte[] decompressedData = DecompressWithLimit(compressedData, out bool gzipEndedEarly);
            DecompressedByteCount = decompressedData.Length;

            if (!TryLoadIndexedMessages(decompressedData))
            {
                LoadOfficialStreamMessages(decompressedData, gzipEndedEarly);
            }
            else if (gzipEndedEarly)
            {
                IsIncompleteTail = true;
                LoadWarning = "回放文件末尾缺少 GZip 结束标记，已加载可读取帧。";
            }

            Reset();
        }

        public void Reset()
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MessageReader));
            }

            currentMsgIndex = -1;
        }

        public void StartPlay()
        {
            Reset();
        }

        public MessageToClient ReadNextMessage()
        {
            if (disposed || currentMsgIndex >= messages.Count - 1)
            {
                return null;
            }

            currentMsgIndex++;
            return messages[currentMsgIndex];
        }

        public MessageToClient ReadMessageAt(int index)
        {
            if (disposed || index < 0 || index >= messages.Count)
            {
                return null;
            }

            return messages[index];
        }

        public int GetMessageCount()
        {
            return messages.Count;
        }

        public int GetCurrentIndex()
        {
            return currentMsgIndex;
        }

        public void Seek(int index)
        {
            if (index >= 0 && index < messages.Count)
            {
                currentMsgIndex = index - 1;
            }
        }

        public bool IsAtEnd()
        {
            return currentMsgIndex >= messages.Count - 1;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            messages.Clear();
            disposed = true;
        }

        private void ResetLoadedData()
        {
            messages.Clear();
            currentMsgIndex = -1;
            TeamCount = 0;
            PlayerCount = 0;
            FileVersion = 0;
            IsIncompleteTail = false;
            LoadWarning = null;
            DecompressedByteCount = 0;
        }

        private bool TryLoadIndexedMessages(byte[] decompressedData)
        {
            try
            {
                if (decompressedData.Length < sizeof(int))
                {
                    return false;
                }

                using (var indexedStream = new MemoryStream(decompressedData))
                using (var indexedReader = new BinaryReader(indexedStream))
                {
                    int messageCount = indexedReader.ReadInt32();
                    if (messageCount < 0 || messageCount > PlayBackConstant.MAX_MESSAGE_COUNT)
                    {
                        return false;
                    }

                    long firstPayloadOffset = sizeof(int) + (long)messageCount * sizeof(long);
                    if (firstPayloadOffset > decompressedData.Length)
                    {
                        return false;
                    }

                    long[] offsets = new long[messageCount];
                    long previousOffset = firstPayloadOffset;
                    for (int i = 0; i < messageCount; i++)
                    {
                        offsets[i] = indexedReader.ReadInt64();
                        if (offsets[i] < firstPayloadOffset || offsets[i] < previousOffset || offsets[i] > decompressedData.Length - sizeof(int))
                        {
                            return false;
                        }
                        previousOffset = offsets[i];
                    }

                    var parsedMessages = new List<MessageToClient>(messageCount);
                    for (int i = 0; i < messageCount; i++)
                    {
                        indexedStream.Position = offsets[i];
                        int messageLength = indexedReader.ReadInt32();
                        if (messageLength < 0 || indexedStream.Position + messageLength > decompressedData.Length)
                        {
                            return false;
                        }

                        byte[] messageData = indexedReader.ReadBytes(messageLength);
                        if (messageData.Length != messageLength)
                        {
                            return false;
                        }

                        parsedMessages.Add(MessageToClient.Parser.ParseFrom(messageData));
                    }

                    messages.Clear();
                    messages.AddRange(parsedMessages);
                    return true;
                }
            }
            catch
            {
                messages.Clear();
                return false;
            }
        }

        private void LoadOfficialStreamMessages(byte[] decompressedData, bool gzipEndedEarly)
        {
            var parsedMessages = new List<MessageToClient>();
            int offset = 0;

            while (offset < decompressedData.Length)
            {
                if (parsedMessages.Count >= PlayBackConstant.MAX_MESSAGE_COUNT)
                {
                    throw new FormatException($"Playback message count exceeds {PlayBackConstant.MAX_MESSAGE_COUNT}.");
                }

                if (!TryReadRawVarint32(decompressedData, ref offset, out int messageLength))
                {
                    AcceptOrRejectIncompleteTail(parsedMessages, "回放文件末尾不完整：最后一帧长度字段未写完。");
                    return;
                }

                if (messageLength < 0)
                {
                    throw new FormatException("回放帧长度字段异常。");
                }

                if (offset + messageLength > decompressedData.Length)
                {
                    int missingBytes = offset + messageLength - decompressedData.Length;
                    AcceptOrRejectIncompleteTail(parsedMessages, $"回放文件末尾不完整：最后一帧缺少 {missingBytes} 字节。");
                    return;
                }

                try
                {
                    parsedMessages.Add(MessageToClient.Parser.ParseFrom(decompressedData, offset, messageLength));
                }
                catch (InvalidProtocolBufferException ex)
                {
                    throw new FormatException("回放帧数据损坏，无法解析。", ex);
                }

                offset += messageLength;
            }

            messages.Clear();
            messages.AddRange(parsedMessages);
            if (gzipEndedEarly && parsedMessages.Count > 0)
            {
                IsIncompleteTail = true;
                LoadWarning = "回放文件末尾缺少 GZip 结束标记，已加载可读取帧。";
            }
        }

        private static bool TryReadRawVarint32(byte[] data, ref int offset, out int value)
        {
            value = 0;
            int shift = 0;
            while (shift < 32)
            {
                if (offset >= data.Length)
                {
                    return false;
                }

                byte b = data[offset++];
                value |= (b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                {
                    return true;
                }

                shift += 7;
            }

            throw new FormatException("回放帧长度字段异常。");
        }

        private void AcceptOrRejectIncompleteTail(List<MessageToClient> parsedMessages, string warning)
        {
            if (parsedMessages.Count == 0)
            {
                throw new PlaybackFileIncompleteException(warning, parsedMessages.Count);
            }

            messages.Clear();
            messages.AddRange(parsedMessages);
            IsIncompleteTail = true;
            LoadWarning = warning;
        }

        private static byte[] DecompressWithLimit(byte[] compressedData, out bool endedEarly)
        {
            endedEarly = false;
            using (var compressedStream = new MemoryStream(compressedData))
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            using (var decompressedStream = new MemoryStream())
            {
                byte[] buffer = new byte[81920];
                while (true)
                {
                    int read;
                    try
                    {
                        read = gzipStream.Read(buffer, 0, buffer.Length);
                    }
                    catch (Exception ex) when (
                        decompressedStream.Length > 0
                        && (ex is InvalidDataException || ex is IOException || ex is EndOfStreamException))
                    {
                        endedEarly = true;
                        break;
                    }

                    if (read == 0)
                    {
                        break;
                    }

                    if (decompressedStream.Length + read > PlayBackConstant.MAX_DECOMPRESSED_PLAYBACK_BYTES)
                    {
                        throw new InvalidDataException($"回放解压后超过 {PlayBackConstant.MAX_DECOMPRESSED_PLAYBACK_BYTES} 字节。");
                    }

                    decompressedStream.Write(buffer, 0, read);
                }

                return decompressedStream.ToArray();
            }
        }
    }
}
