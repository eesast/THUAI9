using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CosDownloader
{
    public sealed class ExplorerNode
    {
        public ExplorerNode(string name, string keyPath, bool isDirectory, RemoteFileItem? file = null)
        {
            Name = name;
            KeyPath = keyPath;
            IsDirectory = isDirectory;
            File = file;
            Children = new List<ExplorerNode>();
        }

        public string Name { get; }

        public string KeyPath { get; }

        public bool IsDirectory { get; }

        public bool IsProtected { get; private set; }

        public RemoteFileItem? File { get; private set; }

        public List<ExplorerNode> Children { get; }

        public string DisplayName => IsProtected ? $"{Name}（已保护）" : Name;

        public string BadgeText
        {
            get
            {
                if (IsDirectory)
                {
                    return "DIR";
                }

                string extension = Path.GetExtension(Name).TrimStart('.').ToUpperInvariant();
                if (string.IsNullOrWhiteSpace(extension))
                {
                    return "FILE";
                }

                return extension.Length > 4 ? extension.Substring(0, 4) : extension;
            }
        }

        public string MetaText
        {
            get
            {
                if (IsDirectory)
                {
                    return IsProtected ? "文件夹 | 已保护" : "文件夹";
                }

                if (File == null)
                {
                    return IsProtected ? "已保护" : string.Empty;
                }

                List<string> parts = new List<string>();
                if (File.Size.HasValue)
                {
                    parts.Add($"大小 {FormatSize(File.Size.Value)}");
                }

                if (DateTimeOffset.TryParse(File.LastModified, out DateTimeOffset parsed))
                {
                    parts.Add($"更新 {parsed.ToLocalTime():yyyy-MM-dd HH:mm}");
                }

                if (IsProtected)
                {
                    parts.Add("已保护");
                }

                return string.Join("  |  ", parts);
            }
        }

        public void AttachFile(RemoteFileItem file)
        {
            File = file;
        }

        public void ApplyProtection(IReadOnlyCollection<ProtectedSyncItem> protectedItems)
        {
            IsProtected = ProtectedSyncMatcher.IsProtected(KeyPath, IsDirectory, protectedItems);

            foreach (ExplorerNode child in Children)
            {
                child.ApplyProtection(protectedItems);
            }
        }

        public static List<ExplorerNode> BuildTree(
            IEnumerable<RemoteFileItem> files,
            IReadOnlyCollection<ProtectedSyncItem>? protectedItems = null)
        {
            List<ExplorerNode> roots = new List<ExplorerNode>();
            List<(RemoteFileItem File, string[] Segments)> paths = files
                .Where(file => !string.IsNullOrWhiteSpace(file.Key))
                .Select(file => (File: file, Segments: file.Key.Split('/', StringSplitOptions.RemoveEmptyEntries)))
                .Where(item => item.Segments.Length > 0)
                .ToList();

            int commonPrefixLength = GetCommonDirectoryPrefixLength(paths);

            foreach ((RemoteFileItem file, string[] originalSegments) in paths)
            {
                string[] visibleSegments = originalSegments.Skip(commonPrefixLength).ToArray();

                if (visibleSegments.Length == 0)
                {
                    string fallbackName = Path.GetFileName(file.Key.TrimEnd('/'));
                    if (string.IsNullOrWhiteSpace(fallbackName))
                    {
                        continue;
                    }

                    visibleSegments = new[] { fallbackName };
                }

                List<ExplorerNode> currentLevel = roots;
                ExplorerNode? currentNode = null;

                for (int index = 0; index < visibleSegments.Length; index++)
                {
                    bool isLast = index == visibleSegments.Length - 1;
                    bool isDirectory = !isLast || file.IsDirectory;
                    string segment = visibleSegments[index];
                    string nodeKeyPath = BuildNodeKeyPath(originalSegments, commonPrefixLength, index, isDirectory);

                    currentNode = currentLevel.FirstOrDefault(node =>
                        node.Name.Equals(segment, StringComparison.OrdinalIgnoreCase)
                        && node.IsDirectory == isDirectory
                        && node.KeyPath.Equals(nodeKeyPath, StringComparison.OrdinalIgnoreCase));

                    if (currentNode == null)
                    {
                        currentNode = new ExplorerNode(segment, nodeKeyPath, isDirectory, isDirectory ? null : file);
                        currentLevel.Add(currentNode);
                    }
                    else if (isLast && !isDirectory)
                    {
                        currentNode.AttachFile(file);
                    }

                    currentLevel = currentNode.Children;
                }
            }

            SortNodes(roots);

            if (protectedItems != null && protectedItems.Count > 0)
            {
                foreach (ExplorerNode root in roots)
                {
                    root.ApplyProtection(protectedItems);
                }
            }

            return roots;
        }

        private static string BuildNodeKeyPath(
            IReadOnlyList<string> originalSegments,
            int commonPrefixLength,
            int visibleIndex,
            bool isDirectory)
        {
            int originalLength = Math.Min(originalSegments.Count, commonPrefixLength + visibleIndex + 1);
            string keyPath = string.Join('/', originalSegments.Take(originalLength));

            if (isDirectory)
            {
                return ProtectedSyncMatcher.NormalizeKey(keyPath, true);
            }

            return ProtectedSyncMatcher.NormalizeKey(keyPath, false);
        }

        private static int GetCommonDirectoryPrefixLength(
            IReadOnlyList<(RemoteFileItem File, string[] Segments)> paths)
        {
            if (paths.Count == 0)
            {
                return 0;
            }

            string[] firstDirectorySegments = GetDirectorySegments(paths[0].File, paths[0].Segments);
            if (firstDirectorySegments.Length == 0)
            {
                return 0;
            }

            int commonLength = firstDirectorySegments.Length;
            for (int index = 1; index < paths.Count; index++)
            {
                string[] currentDirectorySegments = GetDirectorySegments(paths[index].File, paths[index].Segments);
                commonLength = Math.Min(commonLength, currentDirectorySegments.Length);

                int matchedLength = 0;
                while (matchedLength < commonLength
                    && string.Equals(
                        firstDirectorySegments[matchedLength],
                        currentDirectorySegments[matchedLength],
                        StringComparison.OrdinalIgnoreCase))
                {
                    matchedLength++;
                }

                commonLength = matchedLength;
                if (commonLength == 0)
                {
                    return 0;
                }
            }

            return commonLength;
        }

        private static string[] GetDirectorySegments(RemoteFileItem file, string[] segments)
        {
            if (file.IsDirectory)
            {
                return segments;
            }

            if (segments.Length <= 1)
            {
                return Array.Empty<string>();
            }

            return segments.Take(segments.Length - 1).ToArray();
        }

        private static void SortNodes(List<ExplorerNode> nodes)
        {
            nodes.Sort((left, right) =>
            {
                if (left.IsDirectory != right.IsDirectory)
                {
                    return left.IsDirectory ? -1 : 1;
                }

                return StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name);
            });

            foreach (ExplorerNode node in nodes)
            {
                if (node.Children.Count > 0)
                {
                    SortNodes(node.Children);
                }
            }
        }

        private static string FormatSize(long size)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double value = size;
            int unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:0.##} {units[unitIndex]}";
        }
    }
}
