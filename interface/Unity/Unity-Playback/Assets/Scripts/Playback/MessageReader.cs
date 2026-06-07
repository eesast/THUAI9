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
    /// THUAI9 回放消息读取器。
    /// 文件格式说明：
    /// 1. logic/PlayBack/MessageWriter 写入 GZip 压缩的 protobuf length-delimited MessageToClient。
    /// 2. Unity 读取时先解压 GZip，再按 length-delimited protobuf 逐帧解析。
    ///
    /// 文件头由 logic 写入：
    /// - 魔数 PB + 版本 + 保留字节
    /// - uint32 teamCount
    /// - uint32 playerCount
    /// </summary>
    public class MessageReader
    {
        private readonly List<FrameSlice> frameSlices = new List<FrameSlice>();
        private byte[] frameData;
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
            byte[] decompressedData = DecompressWithLimit(data, headerSize, data.Length - headerSize, out bool gzipEndedEarly);
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
            if (disposed || currentMsgIndex >= frameSlices.Count - 1)
            {
                return null;
            }

            currentMsgIndex++;
            return ParseFrameAt(currentMsgIndex);
        }

        public MessageToClient ReadMessageAt(int index)
        {
            if (disposed || index < 0 || index >= frameSlices.Count)
            {
                return null;
            }

            return ParseFrameAt(index);
        }

        public int GetMessageCount()
        {
            return frameSlices.Count;
        }

        public int GetCurrentIndex()
        {
            return currentMsgIndex;
        }

        public void Seek(int index)
        {
            if (index >= 0 && index < frameSlices.Count)
            {
                currentMsgIndex = index - 1;
            }
        }

        public bool IsAtEnd()
        {
            return currentMsgIndex >= frameSlices.Count - 1;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            frameSlices.Clear();
            frameData = null;
            disposed = true;
        }

        private void ResetLoadedData()
        {
            frameSlices.Clear();
            frameData = null;
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

                    var parsedSlices = new List<FrameSlice>(messageCount);
                    for (int i = 0; i < messageCount; i++)
                    {
                        indexedStream.Position = offsets[i];
                        int messageLength = indexedReader.ReadInt32();
                        if (messageLength < 0 || indexedStream.Position + messageLength > decompressedData.Length)
                        {
                            return false;
                        }

                        int payloadOffset = checked((int)indexedStream.Position);
                        parsedSlices.Add(new FrameSlice(payloadOffset, messageLength));
                    }

                    frameSlices.Clear();
                    frameSlices.AddRange(parsedSlices);
                    frameData = decompressedData;
                    ValidateLoadedBoundaryFrames();
                    return true;
                }
            }
            catch
            {
                frameSlices.Clear();
                frameData = null;
                return false;
            }
        }

        private void LoadOfficialStreamMessages(byte[] decompressedData, bool gzipEndedEarly)
        {
            var parsedSlices = new List<FrameSlice>();
            int offset = 0;

            while (offset < decompressedData.Length)
            {
                if (parsedSlices.Count >= PlayBackConstant.MAX_MESSAGE_COUNT)
                {
                    throw new FormatException($"Playback message count exceeds {PlayBackConstant.MAX_MESSAGE_COUNT}.");
                }

                if (!TryReadRawVarint32(decompressedData, ref offset, out int messageLength))
                {
                    AcceptOrRejectIncompleteTail(parsedSlices, decompressedData, "回放文件末尾不完整：最后一帧长度字段未写完。");
                    return;
                }

                if (messageLength < 0)
                {
                    throw new FormatException("回放帧长度字段异常。");
                }

                if (offset + messageLength > decompressedData.Length)
                {
                    int missingBytes = offset + messageLength - decompressedData.Length;
                    AcceptOrRejectIncompleteTail(parsedSlices, decompressedData, $"回放文件末尾不完整：最后一帧缺少 {missingBytes} 字节。");
                    return;
                }

                parsedSlices.Add(new FrameSlice(offset, messageLength));

                offset += messageLength;
            }

            frameSlices.Clear();
            frameSlices.AddRange(parsedSlices);
            frameData = decompressedData;
            ValidateLoadedBoundaryFrames();
            if (gzipEndedEarly && parsedSlices.Count > 0)
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

        private void AcceptOrRejectIncompleteTail(List<FrameSlice> parsedSlices, byte[] decompressedData, string warning)
        {
            if (parsedSlices.Count == 0)
            {
                throw new PlaybackFileIncompleteException(warning, parsedSlices.Count);
            }

            frameSlices.Clear();
            frameSlices.AddRange(parsedSlices);
            frameData = decompressedData;
            ValidateLoadedBoundaryFrames();
            IsIncompleteTail = true;
            LoadWarning = warning;
        }

        private MessageToClient ParseFrameAt(int index)
        {
            if (frameData == null || index < 0 || index >= frameSlices.Count)
            {
                return null;
            }

            FrameSlice slice = frameSlices[index];
            return MessageToClient.Parser.ParseFrom(frameData, slice.Offset, slice.Length);
        }

        private void ValidateLoadedBoundaryFrames()
        {
            try
            {
                if (frameSlices.Count <= 0)
                {
                    return;
                }

                _ = ParseFrameAt(0);
                if (frameSlices.Count > 1)
                {
                    _ = ParseFrameAt(frameSlices.Count - 1);
                }
            }
            catch (InvalidProtocolBufferException ex)
            {
                throw new FormatException("回放帧数据损坏，无法解析。", ex);
            }
        }

        private static byte[] DecompressWithLimit(byte[] data, int offset, int count, out bool endedEarly)
        {
            endedEarly = false;
            using (var compressedStream = new MemoryStream(data, offset, count, false))
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

        private readonly struct FrameSlice
        {
            public FrameSlice(int offset, int length)
            {
                Offset = offset;
                Length = length;
            }

            public int Offset { get; }
            public int Length { get; }
        }
    }
}
