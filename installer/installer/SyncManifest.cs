using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CosDownloader
{
    internal sealed class LocalSyncManifest
    {
        public int SchemaVersion { get; set; } = 1;

        public string? CatalogVersion { get; set; }

        public string? UpdatedAt { get; set; }

        public List<LocalSyncManifestEntry> Files { get; set; } = new List<LocalSyncManifestEntry>();
    }

    internal sealed class LocalSyncManifestEntry
    {
        public string Key { get; set; } = string.Empty;

        public string? Hash { get; set; }

        public string? HashAlgorithm { get; set; }

        public long? RemoteSize { get; set; }

        public string? RemoteLastModified { get; set; }

        public long LocalSize { get; set; }

        public string? LocalLastWriteTimeUtc { get; set; }
    }

    internal static class LocalSyncManifestStore
    {
        private const string ManifestFileName = ".thuai9-manifest.json";
        private const string ManifestDirectoryName = "THUAI9Downloader\\manifests";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static LocalSyncManifest Load(string downloadRoot)
        {
            try
            {
                string manifestPath = GetManifestPath(downloadRoot);
                if (!File.Exists(manifestPath))
                {
                    manifestPath = GetLegacyManifestPath(downloadRoot);
                    if (!File.Exists(manifestPath))
                    {
                        return new LocalSyncManifest();
                    }
                }

                string json = File.ReadAllText(manifestPath);
                return JsonSerializer.Deserialize<LocalSyncManifest>(json) ?? new LocalSyncManifest();
            }
            catch
            {
                return new LocalSyncManifest();
            }
        }

        public static void Save(string downloadRoot, LocalSyncManifest manifest)
        {
            manifest.Files = manifest.Files
                .OrderBy(file => file.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string manifestPath = GetManifestPath(downloadRoot);
            string? manifestDirectory = Path.GetDirectoryName(manifestPath);
            if (!string.IsNullOrWhiteSpace(manifestDirectory))
            {
                Directory.CreateDirectory(manifestDirectory);
            }

            string json = JsonSerializer.Serialize(manifest, JsonOptions);
            File.WriteAllText(manifestPath, json);
        }

        public static string GetManifestPath(string downloadRoot)
        {
            string baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ManifestDirectoryName);

            string normalizedRoot = Path.GetFullPath(downloadRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();

            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot));
            string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            return Path.Combine(baseDirectory, $"{hash}.json");
        }

        private static string GetLegacyManifestPath(string downloadRoot)
        {
            return Path.Combine(downloadRoot, ManifestFileName);
        }
    }
}
