using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using Google.Protobuf;
using Protobuf;

namespace THUAI9.Unity.Playback
{
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

        public void LoadData(byte[] data)
        {
            if (disposed)
            {
                throw new ObjectDisposedException(nameof(MessageReader));
            }

            messages.Clear();
            currentMsgIndex = -1;

            using (var headerStream = new MemoryStream(data))
            using (var headerReader = new BinaryReader(headerStream))
            {
                byte magic1 = headerReader.ReadByte();
                byte magic2 = headerReader.ReadByte();
                if (magic1 != 'P' || magic2 != 'B')
                {
                    throw new FormatException($"?????????? PB???? {(char)magic1}{(char)magic2}");
                }

                byte version = headerReader.ReadByte();
                _ = headerReader.ReadByte();
                TeamCount = headerReader.ReadUInt32();
                PlayerCount = headerReader.ReadUInt32();

                if (version != PlayBackConstant.FILE_VERSION)
                {
                    throw new FormatException($"?????????????? {version}????? {PlayBackConstant.FILE_VERSION}");
                }
            }

            const int headerSize = 12;
            byte[] compressedData = new byte[data.Length - headerSize];
            Array.Copy(data, headerSize, compressedData, 0, compressedData.Length);

            using (var compressedStream = new MemoryStream(compressedData))
            using (var gzipStream = new GZipStream(compressedStream, CompressionMode.Decompress))
            using (var decompressedStream = new MemoryStream())
            {
                gzipStream.CopyTo(decompressedStream);
                byte[] decompressedData = decompressedStream.ToArray();

                if (!TryLoadIndexedMessages(decompressedData))
                {
                    LoadOfficialStreamMessages(decompressedData);
                }
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

        private void LoadOfficialStreamMessages(byte[] decompressedData)
        {
            messages.Clear();

            using (var protobufStream = new CodedInputStream(decompressedData))
            {
                while (!protobufStream.IsAtEnd)
                {
                    if (messages.Count >= PlayBackConstant.MAX_MESSAGE_COUNT)
                    {
                        throw new FormatException($"???????????{PlayBackConstant.MAX_MESSAGE_COUNT}");
                    }

                    var message = new MessageToClient();
                    protobufStream.ReadMessage(message);
                    messages.Add(message);
                }
            }
        }
    }
}
