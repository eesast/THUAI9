using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CosDownloader
{
    public sealed class ProtectedSyncItem
    {
        public string Key { get; set; } = string.Empty;

        public bool IsDirectory { get; set; }
    }

    internal sealed class LocalSyncProtectionSettings
    {
        public int SchemaVersion { get; set; } = 1;

        public List<ProtectedSyncItem> Items { get; set; } = new List<ProtectedSyncItem>();
    }

    internal static class LocalSyncProtectionStore
    {
        private const string ProtectionDirectoryName = "THUAI9Downloader\\protections";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        public static LocalSyncProtectionSettings Load(string downloadRoot)
        {
            try
            {
                string settingsPath = GetSettingsPath(downloadRoot);
                if (!File.Exists(settingsPath))
                {
                    return new LocalSyncProtectionSettings();
                }

                string json = File.ReadAllText(settingsPath);
                LocalSyncProtectionSettings settings =
                    JsonSerializer.Deserialize<LocalSyncProtectionSettings>(json)
                    ?? new LocalSyncProtectionSettings();

                settings.Items = settings.Items
                    .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                    .Select(item => new ProtectedSyncItem
                    {
                        Key = ProtectedSyncMatcher.NormalizeKey(item.Key, item.IsDirectory),
                        IsDirectory = item.IsDirectory
                    })
                    .Distinct(ProtectedSyncItemComparer.Instance)
                    .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                return settings;
            }
            catch
            {
                return new LocalSyncProtectionSettings();
            }
        }

        public static void Save(string downloadRoot, LocalSyncProtectionSettings settings)
        {
            settings.Items = settings.Items
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .Select(item => new ProtectedSyncItem
                {
                    Key = ProtectedSyncMatcher.NormalizeKey(item.Key, item.IsDirectory),
                    IsDirectory = item.IsDirectory
                })
                .Distinct(ProtectedSyncItemComparer.Instance)
                .OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();

            string settingsPath = GetSettingsPath(downloadRoot);
            string? directory = Path.GetDirectoryName(settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(settingsPath, json);
        }

        private static string GetSettingsPath(string downloadRoot)
        {
            string baseDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                ProtectionDirectoryName);

            string normalizedRoot = Path.GetFullPath(downloadRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToLowerInvariant();

            byte[] hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRoot));
            string hash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            return Path.Combine(baseDirectory, $"{hash}.json");
        }
    }

    internal static class ProtectedSyncMatcher
    {
        public static string NormalizeKey(string key, bool isDirectory)
        {
            string normalized = (key ?? string.Empty)
                .Replace('\\', '/')
                .Trim();

            if (isDirectory)
            {
                normalized = normalized.TrimEnd('/') + "/";
            }
            else
            {
                normalized = normalized.TrimEnd('/');
            }

            return normalized;
        }

        public static bool HasExactRule(
            string key,
            bool isDirectory,
            IReadOnlyCollection<ProtectedSyncItem> protectedItems)
        {
            string normalizedKey = NormalizeKey(key, isDirectory);
            return protectedItems.Any(item =>
                item.IsDirectory == isDirectory
                && string.Equals(
                    NormalizeKey(item.Key, item.IsDirectory),
                    normalizedKey,
                    StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsProtected(
            string key,
            bool isDirectory,
            IReadOnlyCollection<ProtectedSyncItem> protectedItems)
        {
            string normalizedKey = NormalizeKey(key, isDirectory);

            foreach (ProtectedSyncItem protectedItem in protectedItems)
            {
                string protectedKey = NormalizeKey(protectedItem.Key, protectedItem.IsDirectory);

                if (!protectedItem.IsDirectory)
                {
                    if (string.Equals(protectedKey, normalizedKey, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }

                    continue;
                }

                if (normalizedKey.StartsWith(protectedKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal sealed class ProtectedSyncItemComparer : IEqualityComparer<ProtectedSyncItem>
    {
        public static ProtectedSyncItemComparer Instance { get; } = new ProtectedSyncItemComparer();

        public bool Equals(ProtectedSyncItem? x, ProtectedSyncItem? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x == null || y == null)
            {
                return false;
            }

            return x.IsDirectory == y.IsDirectory
                && string.Equals(
                    ProtectedSyncMatcher.NormalizeKey(x.Key, x.IsDirectory),
                    ProtectedSyncMatcher.NormalizeKey(y.Key, y.IsDirectory),
                    StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(ProtectedSyncItem obj)
        {
            return HashCode.Combine(
                ProtectedSyncMatcher.NormalizeKey(obj.Key, obj.IsDirectory).ToLowerInvariant(),
                obj.IsDirectory);
        }
    }
}
