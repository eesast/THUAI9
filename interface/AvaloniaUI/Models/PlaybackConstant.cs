using System;
using System.IO;

namespace Playback;

public static class Constants
{
    public const int Version = 9;
    public const int LegacyVersion = 7;
    public static readonly string FileExtension = ".thuaipb";
    public static readonly byte[] FileHeader = [(byte)'P', (byte)'B', Version, 0];

    public static bool IsSupportedVersion(byte version)
    {
        return version == Version || version == LegacyVersion;
    }
}

public class FileFormatNotLegalException(string fileName) : Exception
{
    public string FileName { get; } = fileName;
    public override string Message { get; } = $"文件 {fileName} 不是适用于 THUAI{Constants.Version} 的合法回放文件。";
}

public static class Utils
{
    public static void FileNameRegular(ref string fileName)
    {
        if (!fileName.EndsWith(Constants.FileExtension))
        {
            fileName += Constants.FileExtension;
        }
    }

    public static void WriteHeader(this FileStream fs, uint teamCount, uint playerCount)
    {
        BinaryWriter bw = new(fs);
        bw.Write(Constants.FileHeader);
        bw.Write(teamCount);
        bw.Write(playerCount);
    }

    public static (uint teamCount, uint playerCount) ReadHeader(this FileStream fs)
    {
        BinaryReader br = new(fs);
        byte[] header = br.ReadBytes(Constants.FileHeader.Length);
        if (header.Length != Constants.FileHeader.Length
            || header[0] != Constants.FileHeader[0]
            || header[1] != Constants.FileHeader[1]
            || header[3] != Constants.FileHeader[3]
            || !Constants.IsSupportedVersion(header[2]))
        {
            throw new FileFormatNotLegalException(fs.Name);
        }
        return (br.ReadUInt32(), br.ReadUInt32());
    }
}
