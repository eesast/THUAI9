using System;
using System.IO;
using System.Linq;

namespace Playback;

public static class Constants
{
    public const int Version = 7;
    public static readonly string FileExtension = ".thuaipb";
    public static readonly byte[] FileHeader = [(byte)'P', (byte)'B', Version, 0];
}

public class FileFormatNotLegalException(string fileName) : Exception
{
    public string FileName { get; } = fileName;
    public override string Message { get; } = $"The file: {fileName} is not a legal playback file for THUAI{Constants.Version}.";
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
        if (!br.ReadBytes(Constants.FileHeader.Length).SequenceEqual(Constants.FileHeader))
        {
            throw new FileFormatNotLegalException(fs.Name);
        }
        return (br.ReadUInt32(), br.ReadUInt32());
    }
}
