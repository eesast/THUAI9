using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CosDownloader
{
    public sealed class CosService
    {
        private static readonly HttpClient HttpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        private static readonly HashSet<string> ReservedWindowsFileNames = new HashSet<string>(
            new[]
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            },
            StringComparer.OrdinalIgnoreCase);

        public async Task<FileListResult> GetFileListAsync(CancellationToken cancellationToken = default)
        {
            if (!TryBuildUri(CloudFunctionConfig.FileListPath, out Uri? requestUri, out string configError))
            {
                return FileListResult.Fail(configError);
            }

            try
            {
                using HttpResponseMessage response = await HttpClient.GetAsync(requestUri, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return FileListResult.Fail(
                        $"获取文件列表失败 (HTTP {(int)response.StatusCode})",
                        $"endpoint: {requestUri}",
                        $"response: {SafeBody(body)}");
                }

                RemoteFileCatalog catalog = ParseCatalog(body);
                if (catalog.Files.Count == 0)
                {
                    return FileListResult.CreateSuccess(
                        catalog.Files,
                        catalog.Version,
                        catalog.GeneratedAt,
                        "提示：云函数当前没有返回可下载文件。");
                }

                return FileListResult.CreateSuccess(catalog.Files, catalog.Version, catalog.GeneratedAt);
            }
            catch (Exception ex)
            {
                return FileListResult.Fail(
                    "获取文件列表时发生异常",
                    $"endpoint: {requestUri}",
                    $"message: {ex.Message}");
            }
        }

        public async Task<SyncExecutionResult> SyncFilesAsync(
            IReadOnlyCollection<RemoteFileItem> remoteFiles,
            string? downloadRoot = null,
            string? catalogVersion = null,
            IReadOnlyCollection<ProtectedSyncItem>? protectedItems = null,
            Action<SyncProgressInfo>? onProgress = null,
            CancellationToken cancellationToken = default)
        {
            List<RemoteFileItem> files = remoteFiles
                .Where(file => !file.IsDirectory)
                .ToList();

            string resolvedDownloadRoot = string.IsNullOrWhiteSpace(downloadRoot)
                ? GetDefaultDownloadRoot()
                : downloadRoot;

            Directory.CreateDirectory(resolvedDownloadRoot);

            LocalSyncManifest manifest = LocalSyncManifestStore.Load(resolvedDownloadRoot);
            Dictionary<string, LocalSyncManifestEntry> manifestEntries = manifest.Files
                .Where(file => !string.IsNullOrWhiteSpace(file.Key))
                .GroupBy(file => file.Key, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .ToDictionary(file => file.Key, StringComparer.OrdinalIgnoreCase);
            List<ProtectedSyncItem> effectiveProtectedItems = protectedItems?
                .Where(item => !string.IsNullOrWhiteSpace(item.Key))
                .ToList()
                ?? new List<ProtectedSyncItem>();

            List<SyncDecision> decisions = new List<SyncDecision>(files.Count);
            foreach (RemoteFileItem file in files)
            {
                SyncDecision decision = await CreateSyncDecisionAsync(
                    file,
                    manifestEntries,
                    resolvedDownloadRoot,
                    effectiveProtectedItems,
                    cancellationToken);
                decisions.Add(decision);
            }

            int downloadedCount = 0;
            int newDownloadCount = 0;
            int updatedDownloadCount = 0;
            int skippedCount = 0;
            int protectedSkippedCount = 0;
            long totalDownloadBytes = decisions
                .Where(decision => decision.Action != SyncFileAction.Skip && decision.File.Size.HasValue)
                .Sum(decision => decision.File.Size!.Value);
            long completedDownloadBytes = 0;
            Stopwatch stopwatch = Stopwatch.StartNew();

            for (int index = 0; index < decisions.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                SyncDecision decision = decisions[index];
                onProgress?.Invoke(new SyncProgressInfo(
                    decision.Action,
                    decision.File,
                    index + 1,
                    decisions.Count,
                    completedDownloadBytes,
                    totalDownloadBytes,
                    0,
                    decision.File.Size,
                    CalculateBytesPerSecond(completedDownloadBytes, stopwatch)));

                string localPath = BuildLocalPath(resolvedDownloadRoot, decision.File.Key);

                if (decision.Action == SyncFileAction.Skip)
                {
                    skippedCount++;
                    if (string.Equals(decision.Reason, "protected-by-user", StringComparison.Ordinal))
                    {
                        protectedSkippedCount++;
                    }

                    UpsertManifestEntry(manifestEntries, decision.File, localPath);
                    continue;
                }

                long latestFileDownloadedBytes = 0;
                long? latestFileTotalBytes = decision.File.Size;

                string result = await DownloadFileInternalAsync(
                    decision.File,
                    resolvedDownloadRoot,
                    progress =>
                    {
                        latestFileDownloadedBytes = progress.DownloadedBytes;
                        latestFileTotalBytes = progress.TotalBytes;

                        onProgress?.Invoke(new SyncProgressInfo(
                            decision.Action,
                            decision.File,
                            index + 1,
                            decisions.Count,
                            completedDownloadBytes + progress.DownloadedBytes,
                            totalDownloadBytes,
                            progress.DownloadedBytes,
                            progress.TotalBytes,
                            CalculateBytesPerSecond(completedDownloadBytes + progress.DownloadedBytes, stopwatch)));
                    },
                    cancellationToken);
                if (result != "成功")
                {
                    string? manifestSaveError = TrySaveManifest(
                        resolvedDownloadRoot,
                        manifest,
                        manifestEntries,
                        files,
                        catalogVersion);
                    if (!string.IsNullOrWhiteSpace(manifestSaveError))
                    {
                        return SyncExecutionResult.Fail(
                            $"保存本地同步清单失败: {manifestSaveError}",
                            files.Count,
                            downloadedCount,
                            newDownloadCount,
                            updatedDownloadCount,
                            totalDownloadBytes,
                            skippedCount,
                            protectedSkippedCount);
                    }

                    return SyncExecutionResult.Fail(
                        result,
                        files.Count,
                        downloadedCount,
                        newDownloadCount,
                        updatedDownloadCount,
                        totalDownloadBytes,
                        skippedCount,
                        protectedSkippedCount);
                }

                downloadedCount++;
                if (decision.Action == SyncFileAction.DownloadNew)
                {
                    newDownloadCount++;
                }
                else
                {
                    updatedDownloadCount++;
                }

                completedDownloadBytes += latestFileDownloadedBytes > 0
                    ? latestFileDownloadedBytes
                    : decision.File.Size ?? 0;

                onProgress?.Invoke(new SyncProgressInfo(
                    decision.Action,
                    decision.File,
                    index + 1,
                    decisions.Count,
                    completedDownloadBytes,
                    totalDownloadBytes,
                    latestFileDownloadedBytes,
                    latestFileTotalBytes,
                    CalculateBytesPerSecond(completedDownloadBytes, stopwatch)));

                UpsertManifestEntry(manifestEntries, decision.File, localPath);
            }

            string? finalManifestSaveError = TrySaveManifest(
                resolvedDownloadRoot,
                manifest,
                manifestEntries,
                files,
                catalogVersion);
            if (!string.IsNullOrWhiteSpace(finalManifestSaveError))
            {
                return SyncExecutionResult.Fail(
                    $"保存本地同步清单失败: {finalManifestSaveError}",
                    files.Count,
                    downloadedCount,
                    newDownloadCount,
                    updatedDownloadCount,
                    totalDownloadBytes,
                    skippedCount,
                    protectedSkippedCount);
            }

            return SyncExecutionResult.CreateSuccess(
                files.Count,
                downloadedCount,
                newDownloadCount,
                updatedDownloadCount,
                totalDownloadBytes,
                skippedCount,
                protectedSkippedCount);
        }

        public async Task<string> DownloadFileAsync(
            RemoteFileItem file,
            string? downloadRoot = null,
            CancellationToken cancellationToken = default)
        {
            string resolvedDownloadRoot = string.IsNullOrWhiteSpace(downloadRoot)
                ? GetDefaultDownloadRoot()
                : downloadRoot;

            return await DownloadFileInternalAsync(file, resolvedDownloadRoot, null, cancellationToken);
        }

        private static async Task<string> DownloadFileInternalAsync(
            RemoteFileItem file,
            string downloadRoot,
            Action<DownloadByteProgress>? onProgress,
            CancellationToken cancellationToken)
        {
            string? tempPath = null;

            try
            {
                string downloadUrl = file.DownloadUrl;
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    SignedUrlResult signResult = await GetSignedUrlAsync(file.Key, cancellationToken);
                    if (!signResult.Success)
                    {
                        return signResult.ErrorMessage;
                    }

                    downloadUrl = signResult.DownloadUrl;
                }

                using HttpResponseMessage response = await HttpClient.GetAsync(
                    downloadUrl,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return $"下载文件失败 (HTTP {(int)response.StatusCode})";
                }

                string localPath = BuildLocalPath(downloadRoot, file.Key);
                string? directory = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrWhiteSpace(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                tempPath = localPath + ".thuai9download";
                await using Stream networkStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                long? totalBytes = response.Content.Headers.ContentLength ?? file.Size;
                byte[] buffer = new byte[81920];
                long downloadedBytes = 0;

                await using (FileStream fileStream = File.Create(tempPath))
                {
                    while (true)
                    {
                        int bytesRead = await networkStream.ReadAsync(buffer, cancellationToken);
                        if (bytesRead <= 0)
                        {
                            break;
                        }

                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        downloadedBytes += bytesRead;
                        onProgress?.Invoke(new DownloadByteProgress(downloadedBytes, totalBytes));
                    }

                    await fileStream.FlushAsync(cancellationToken);
                }

                File.Move(tempPath, localPath, true);
                tempPath = null;
                ApplyRemoteLastWriteTime(localPath, file.LastModified);
                return "成功";
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tempPath);
                throw;
            }
            catch (Exception ex)
            {
                TryDeleteFile(tempPath);
                return "下载失败: " + ex.Message;
            }
        }

        private static void TryDeleteFile(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // 清理临时文件失败不应覆盖真正的下载错误。
            }
        }

        private static async Task<SignedUrlResult> GetSignedUrlAsync(string fileKey, CancellationToken cancellationToken)
        {
            if (!TryBuildUri(
                    $"{CloudFunctionConfig.SignFilePath.TrimStart('/')}?key={Uri.EscapeDataString(fileKey)}",
                    out Uri? requestUri,
                    out string configError))
            {
                return SignedUrlResult.Fail(configError);
            }

            try
            {
                using HttpResponseMessage response = await HttpClient.GetAsync(requestUri, cancellationToken);
                string body = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return SignedUrlResult.Fail(
                        $"获取下载链接失败 (HTTP {(int)response.StatusCode})",
                        $"endpoint: {requestUri}",
                        $"response: {SafeBody(body)}");
                }

                string? downloadUrl = ParseSignedUrl(body);
                if (string.IsNullOrWhiteSpace(downloadUrl))
                {
                    return SignedUrlResult.Fail(
                        "云函数返回里没有 downloadUrl/url 字段",
                        $"endpoint: {requestUri}");
                }

                return SignedUrlResult.CreateSuccess(downloadUrl);
            }
            catch (Exception ex)
            {
                return SignedUrlResult.Fail(
                    "获取下载链接时发生异常",
                    $"endpoint: {requestUri}",
                    $"message: {ex.Message}");
            }
        }

        private static RemoteFileCatalog ParseCatalog(string body)
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            JsonElement fileArray;
            if (root.ValueKind == JsonValueKind.Array)
            {
                fileArray = root;
            }
            else if (root.TryGetProperty("files", out JsonElement filesElement))
            {
                fileArray = filesElement;
            }
            else if (root.TryGetProperty("items", out JsonElement itemsElement))
            {
                fileArray = itemsElement;
            }
            else
            {
                throw new InvalidOperationException("云函数返回中没有 files/items 数组。");
            }

            List<RemoteFileItem> files = new List<RemoteFileItem>();
            foreach (JsonElement item in fileArray.EnumerateArray())
            {
                RemoteFileItem? file = ParseFileItem(item);
                if (file != null && !string.IsNullOrWhiteSpace(file.Key))
                {
                    files.Add(file);
                }
            }

            string? version = root.ValueKind == JsonValueKind.Object
                ? TryReadString(root, "version") ?? TryReadString(root, "catalogVersion")
                : null;
            string? generatedAt = root.ValueKind == JsonValueKind.Object
                ? TryReadString(root, "generatedAt")
                : null;

            return new RemoteFileCatalog(version, generatedAt, files);
        }

        private static RemoteFileItem? ParseFileItem(JsonElement item)
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                string? key = item.GetString();
                return string.IsNullOrWhiteSpace(key)
                    ? null
                    : new RemoteFileItem(key, null, null, null, null, null);
            }

            string? fileKey = TryReadString(item, "key")
                ?? TryReadString(item, "path")
                ?? TryReadString(item, "name");

            if (string.IsNullOrWhiteSpace(fileKey))
            {
                return null;
            }

            long? size = TryReadLong(item, "size");
            string? lastModified = TryReadString(item, "lastModified")
                ?? TryReadString(item, "updatedAt");
            string? downloadUrl = TryReadString(item, "downloadUrl")
                ?? TryReadString(item, "url");
            string? hash = TryReadString(item, "hash")
                ?? TryReadString(item, "etag");
            string? hashAlgorithm = TryReadString(item, "hashAlgorithm");

            return new RemoteFileItem(fileKey, size, lastModified, downloadUrl, hash, hashAlgorithm);
        }

        private static string? ParseSignedUrl(string body)
        {
            using JsonDocument document = JsonDocument.Parse(body);
            JsonElement root = document.RootElement;

            if (root.ValueKind == JsonValueKind.String)
            {
                return root.GetString();
            }

            return TryReadString(root, "downloadUrl")
                ?? TryReadString(root, "url");
        }

        private static bool TryBuildUri(string path, out Uri? requestUri, out string errorMessage)
        {
            string baseUrl = CloudFunctionConfig.ApiBaseUrl.Trim();
            if (string.IsNullOrWhiteSpace(baseUrl)
                || baseUrl.Contains("replace-with-your-cloud-function-domain", StringComparison.OrdinalIgnoreCase))
            {
                requestUri = null;
                errorMessage = "还没有配置云函数地址，请先修改 CloudFunctionConfig.ApiBaseUrl。";
                return false;
            }

            try
            {
                Uri baseUri = new Uri(baseUrl.EndsWith("/", StringComparison.Ordinal) ? baseUrl : baseUrl + "/");
                requestUri = new Uri(baseUri, path.TrimStart('/'));
                errorMessage = string.Empty;
                return true;
            }
            catch (Exception ex)
            {
                requestUri = null;
                errorMessage = "云函数地址配置无效: " + ex.Message;
                return false;
            }
        }

        internal static string BuildLocalPath(string rootDirectory, string fileKey)
        {
            string[] segments = fileKey
                .Split('/', StringSplitOptions.RemoveEmptyEntries)
                .Select(SanitizePathSegment)
                .ToArray();

            if (segments.Length == 0)
            {
                segments = new[] { "download.bin" };
            }

            return Path.Combine(new[] { rootDirectory }.Concat(segments).ToArray());
        }

        public static string GetDefaultDownloadRoot()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                GetDefaultDownloadFolderName());
        }

        public static string GetDefaultDownloadFolderName()
        {
            return "CosDownloads";
        }

        private static double CalculateBytesPerSecond(long downloadedBytes, Stopwatch stopwatch)
        {
            if (downloadedBytes <= 0 || stopwatch.Elapsed.TotalSeconds <= 0.05)
            {
                return 0;
            }

            return downloadedBytes / stopwatch.Elapsed.TotalSeconds;
        }

        private static SyncDecision CreateSkipDecision(RemoteFileItem file, string reason)
        {
            return new SyncDecision(file, SyncFileAction.Skip, reason);
        }

        private static SyncDecision CreateDownloadDecision(RemoteFileItem file, SyncFileAction action, string reason)
        {
            return new SyncDecision(file, action, reason);
        }

        private static async Task<SyncDecision> CreateSyncDecisionAsync(
            RemoteFileItem file,
            IReadOnlyDictionary<string, LocalSyncManifestEntry> manifestEntries,
            string downloadRoot,
            IReadOnlyCollection<ProtectedSyncItem> protectedItems,
            CancellationToken cancellationToken)
        {
            string localPath = BuildLocalPath(downloadRoot, file.Key);
            bool localExists = File.Exists(localPath);

            if (ProtectedSyncMatcher.IsProtected(file.Key, false, protectedItems) && localExists)
            {
                return CreateSkipDecision(file, "protected-by-user");
            }

            if (!localExists)
            {
                return CreateDownloadDecision(file, SyncFileAction.DownloadNew, "本地缺少文件");
            }

            FileInfo localFile = new FileInfo(localPath);
            manifestEntries.TryGetValue(file.Key, out LocalSyncManifestEntry? manifestEntry);

            string? remoteHash = NormalizeHash(file.Hash);
            string remoteAlgorithm = NormalizeHashAlgorithm(file.HashAlgorithm, remoteHash);

            if (!string.IsNullOrWhiteSpace(remoteHash)
                && manifestEntry != null
                && HashesEqual(manifestEntry.Hash, remoteHash)
                && string.Equals(
                    NormalizeHashAlgorithm(manifestEntry.HashAlgorithm, manifestEntry.Hash),
                    remoteAlgorithm,
                    StringComparison.OrdinalIgnoreCase)
                && MatchesLocalSnapshot(manifestEntry, localFile))
            {
                return CreateSkipDecision(file, "manifest-hash-match");
            }

            if (!string.IsNullOrWhiteSpace(remoteHash)
                && TryGetComparableHashAlgorithm(remoteHash, remoteAlgorithm, out string comparableAlgorithm))
            {
                string localHash = await ComputeFileHashAsync(localPath, comparableAlgorithm, cancellationToken);
                if (HashesEqual(localHash, remoteHash))
                {
                    return CreateSkipDecision(file, "content-hash-match");
                }
            }

            if (manifestEntry != null
                && MatchesRemoteMetadata(manifestEntry, file)
                && MatchesLocalSnapshot(manifestEntry, localFile))
            {
                return CreateSkipDecision(file, "manifest-metadata-match");
            }

            if (file.Size.HasValue && localFile.Length != file.Size.Value)
            {
                return CreateDownloadDecision(file, SyncFileAction.DownloadUpdated, "文件大小已变化");
            }

            if (TryParseRemoteTimestamp(file.LastModified, out DateTimeOffset remoteTimestamp)
                && Math.Abs((localFile.LastWriteTimeUtc - remoteTimestamp.UtcDateTime).TotalSeconds) < 2)
            {
                return CreateSkipDecision(file, "timestamp-match");
            }

            return CreateDownloadDecision(file, SyncFileAction.DownloadUpdated, "云端文件有更新");
        }

        private static void UpsertManifestEntry(
            IDictionary<string, LocalSyncManifestEntry> manifestEntries,
            RemoteFileItem file,
            string localPath)
        {
            FileInfo localFile = new FileInfo(localPath);
            if (!localFile.Exists)
            {
                return;
            }

            manifestEntries[file.Key] = new LocalSyncManifestEntry
            {
                Key = file.Key,
                Hash = NormalizeHash(file.Hash),
                HashAlgorithm = NormalizeHashAlgorithm(file.HashAlgorithm, file.Hash),
                RemoteSize = file.Size,
                RemoteLastModified = file.LastModified,
                LocalSize = localFile.Length,
                LocalLastWriteTimeUtc = localFile.LastWriteTimeUtc.ToString("O")
            };
        }

        private static string? TrySaveManifest(
            string downloadRoot,
            LocalSyncManifest manifest,
            IDictionary<string, LocalSyncManifestEntry> manifestEntries,
            IEnumerable<RemoteFileItem> currentRemoteFiles,
            string? catalogVersion)
        {
            try
            {
                manifest.CatalogVersion = catalogVersion;
                manifest.UpdatedAt = DateTimeOffset.UtcNow.ToString("O");
                manifest.Files = BuildManifestFileList(manifestEntries, currentRemoteFiles);
                LocalSyncManifestStore.Save(downloadRoot, manifest);
                return null;
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
        }

        private static List<LocalSyncManifestEntry> BuildManifestFileList(
            IDictionary<string, LocalSyncManifestEntry> manifestEntries,
            IEnumerable<RemoteFileItem> currentRemoteFiles)
        {
            HashSet<string> remoteKeys = currentRemoteFiles
                .Where(file => !file.IsDirectory)
                .Select(file => file.Key)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return manifestEntries.Values
                .Where(entry => remoteKeys.Contains(entry.Key))
                .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool MatchesRemoteMetadata(LocalSyncManifestEntry manifestEntry, RemoteFileItem file)
        {
            return manifestEntry.RemoteSize == file.Size
                && string.Equals(manifestEntry.RemoteLastModified, file.LastModified, StringComparison.Ordinal);
        }

        private static bool MatchesLocalSnapshot(LocalSyncManifestEntry manifestEntry, FileInfo localFile)
        {
            if (!localFile.Exists)
            {
                return false;
            }

            if (manifestEntry.LocalSize != localFile.Length)
            {
                return false;
            }

            if (!DateTimeOffset.TryParse(manifestEntry.LocalLastWriteTimeUtc, out DateTimeOffset savedTimestamp))
            {
                return false;
            }

            return Math.Abs((savedTimestamp.UtcDateTime - localFile.LastWriteTimeUtc).TotalSeconds) < 2;
        }

        private static bool TryGetComparableHashAlgorithm(string remoteHash, string remoteAlgorithm, out string algorithm)
        {
            algorithm = string.Empty;

            if (string.IsNullOrWhiteSpace(remoteHash))
            {
                return false;
            }

            if (string.Equals(remoteAlgorithm, "sha256", StringComparison.OrdinalIgnoreCase))
            {
                algorithm = "sha256";
                return true;
            }

            if (string.Equals(remoteAlgorithm, "md5", StringComparison.OrdinalIgnoreCase))
            {
                algorithm = "md5";
                return true;
            }

            if (string.Equals(remoteAlgorithm, "etag", StringComparison.OrdinalIgnoreCase)
                && IsLikelyMd5(remoteHash))
            {
                algorithm = "md5";
                return true;
            }

            return false;
        }

        private static async Task<string> ComputeFileHashAsync(
            string localPath,
            string algorithm,
            CancellationToken cancellationToken)
        {
            await using FileStream stream = File.OpenRead(localPath);

            using HashAlgorithm hasher = algorithm.ToLowerInvariant() switch
            {
                "md5" => MD5.Create(),
                _ => SHA256.Create()
            };

            byte[] hashBytes = await hasher.ComputeHashAsync(stream, cancellationToken);
            return Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        private static void ApplyRemoteLastWriteTime(string localPath, string? remoteLastModified)
        {
            if (!TryParseRemoteTimestamp(remoteLastModified, out DateTimeOffset remoteTimestamp))
            {
                return;
            }

            try
            {
                File.SetLastWriteTimeUtc(localPath, remoteTimestamp.UtcDateTime);
            }
            catch
            {
            }
        }

        private static bool TryParseRemoteTimestamp(string? value, out DateTimeOffset remoteTimestamp)
        {
            if (!string.IsNullOrWhiteSpace(value)
                && DateTimeOffset.TryParse(value, out remoteTimestamp))
            {
                return true;
            }

            remoteTimestamp = default;
            return false;
        }

        private static string NormalizeHashAlgorithm(string? hashAlgorithm, string? hash)
        {
            string normalizedHash = NormalizeHash(hash) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(hashAlgorithm))
            {
                return hashAlgorithm.Trim().ToLowerInvariant();
            }

            if (normalizedHash.Length == 64)
            {
                return "sha256";
            }

            if (IsLikelyMd5(normalizedHash))
            {
                return "md5";
            }

            return string.Empty;
        }

        private static string? NormalizeHash(string? hash)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return null;
            }

            return hash.Trim().Trim('"').ToLowerInvariant();
        }

        private static bool HashesEqual(string? left, string? right)
        {
            return string.Equals(NormalizeHash(left), NormalizeHash(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsLikelyMd5(string hash)
        {
            string normalized = NormalizeHash(hash) ?? string.Empty;
            return normalized.Length == 32
                && normalized.All(character => Uri.IsHexDigit(character));
        }

        private static string SanitizePathSegment(string segment)
        {
            segment = segment.Trim();

            foreach (char invalidChar in Path.GetInvalidFileNameChars())
            {
                segment = segment.Replace(invalidChar, '_');
            }

            segment = segment.TrimEnd('.', ' ');

            if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == "..")
            {
                return "_";
            }

            if (ReservedWindowsFileNames.Contains(segment))
            {
                return "_" + segment;
            }

            return segment;
        }

        private static string? TryReadString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number => value.GetRawText(),
                JsonValueKind.True => bool.TrueString,
                JsonValueKind.False => bool.FalseString,
                _ => null
            };
        }

        private static long? TryReadLong(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out long result))
            {
                return result;
            }

            if (value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out result))
            {
                return result;
            }

            return null;
        }

        private static string SafeBody(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
            {
                return "<empty>";
            }

            const int maxLength = 300;
            return body.Length <= maxLength ? body : body.Substring(0, maxLength) + "...";
        }
    }

    internal sealed class RemoteFileCatalog
    {
        public RemoteFileCatalog(string? version, string? generatedAt, List<RemoteFileItem> files)
        {
            Version = version;
            GeneratedAt = generatedAt;
            Files = files;
        }

        public string? Version { get; }

        public string? GeneratedAt { get; }

        public List<RemoteFileItem> Files { get; }
    }

    internal sealed class SyncDecision
    {
        public SyncDecision(RemoteFileItem file, SyncFileAction action, string reason)
        {
            File = file;
            Action = action;
            Reason = reason;
        }

        public RemoteFileItem File { get; }

        public SyncFileAction Action { get; }

        public string Reason { get; }
    }

    public sealed class RemoteFileItem
    {
        public RemoteFileItem(
            string key,
            long? size,
            string? lastModified,
            string? downloadUrl,
            string? hash,
            string? hashAlgorithm)
        {
            Key = key;
            Size = size;
            LastModified = lastModified;
            DownloadUrl = downloadUrl ?? string.Empty;
            Hash = hash;
            HashAlgorithm = hashAlgorithm;
        }

        public string Key { get; }

        public long? Size { get; }

        public string? LastModified { get; }

        public string DownloadUrl { get; }

        public string? Hash { get; }

        public string? HashAlgorithm { get; }

        public bool IsDirectory => Key.EndsWith("/", StringComparison.Ordinal);

        public string FileName
        {
            get
            {
                string trimmedKey = Key.TrimEnd('/');
                if (string.IsNullOrWhiteSpace(trimmedKey))
                {
                    return Key;
                }

                return Path.GetFileName(trimmedKey);
            }
        }

        public override string ToString()
        {
            if (Size is null)
            {
                return Key;
            }

            return $"{Key} ({FormatSize(Size.Value)})";
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

    public sealed class FileListResult
    {
        private FileListResult(
            bool success,
            List<RemoteFileItem> files,
            string? catalogVersion,
            string? generatedAt,
            List<string> messages)
        {
            Success = success;
            Files = files;
            CatalogVersion = catalogVersion;
            GeneratedAt = generatedAt;
            Messages = messages;
        }

        public bool Success { get; }

        public List<RemoteFileItem> Files { get; }

        public string? CatalogVersion { get; }

        public string? GeneratedAt { get; }

        public List<string> Messages { get; }

        public static FileListResult CreateSuccess(
            List<RemoteFileItem> files,
            string? catalogVersion,
            string? generatedAt,
            params string[] messages)
        {
            return new FileListResult(true, files, catalogVersion, generatedAt, messages.ToList());
        }

        public static FileListResult Fail(params string[] messages)
        {
            return new FileListResult(false, new List<RemoteFileItem>(), null, null, messages.ToList());
        }
    }

    public sealed class SignedUrlResult
    {
        private SignedUrlResult(bool success, string downloadUrl, string errorMessage)
        {
            Success = success;
            DownloadUrl = downloadUrl;
            ErrorMessage = errorMessage;
        }

        public bool Success { get; }

        public string DownloadUrl { get; }

        public string ErrorMessage { get; }

        public static SignedUrlResult CreateSuccess(string downloadUrl)
        {
            return new SignedUrlResult(true, downloadUrl, string.Empty);
        }

        public static SignedUrlResult Fail(params string[] messages)
        {
            return new SignedUrlResult(false, string.Empty, string.Join(Environment.NewLine, messages));
        }
    }

    public enum SyncFileAction
    {
        Skip,
        DownloadNew,
        DownloadUpdated
    }

    public sealed class SyncProgressInfo
    {
        public SyncProgressInfo(
            SyncFileAction action,
            RemoteFileItem file,
            int currentIndex,
            int totalCount,
            long downloadedBytes,
            long totalDownloadBytes,
            long currentFileDownloadedBytes,
            long? currentFileTotalBytes,
            double bytesPerSecond)
        {
            Action = action;
            File = file;
            CurrentIndex = currentIndex;
            TotalCount = totalCount;
            DownloadedBytes = downloadedBytes;
            TotalDownloadBytes = totalDownloadBytes;
            CurrentFileDownloadedBytes = currentFileDownloadedBytes;
            CurrentFileTotalBytes = currentFileTotalBytes;
            BytesPerSecond = bytesPerSecond;
        }

        public SyncFileAction Action { get; }

        public RemoteFileItem File { get; }

        public int CurrentIndex { get; }

        public int TotalCount { get; }

        public long DownloadedBytes { get; }

        public long TotalDownloadBytes { get; }

        public long CurrentFileDownloadedBytes { get; }

        public long? CurrentFileTotalBytes { get; }

        public double BytesPerSecond { get; }
    }

    public sealed class DownloadByteProgress
    {
        public DownloadByteProgress(long downloadedBytes, long? totalBytes)
        {
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
        }

        public long DownloadedBytes { get; }

        public long? TotalBytes { get; }
    }

    public sealed class SyncExecutionResult
    {
        private SyncExecutionResult(
            bool success,
            string errorMessage,
            int totalFileCount,
            int downloadedCount,
            int newDownloadCount,
            int updatedDownloadCount,
            long totalDownloadBytes,
            int skippedCount,
            int protectedSkippedCount)
        {
            Success = success;
            ErrorMessage = errorMessage;
            TotalFileCount = totalFileCount;
            DownloadedCount = downloadedCount;
            NewDownloadCount = newDownloadCount;
            UpdatedDownloadCount = updatedDownloadCount;
            TotalDownloadBytes = totalDownloadBytes;
            SkippedCount = skippedCount;
            ProtectedSkippedCount = protectedSkippedCount;
        }

        public bool Success { get; }

        public string ErrorMessage { get; }

        public int TotalFileCount { get; }

        public int DownloadedCount { get; }

        public int NewDownloadCount { get; }

        public int UpdatedDownloadCount { get; }

        public long TotalDownloadBytes { get; }

        public int SkippedCount { get; }

        public int ProtectedSkippedCount { get; }

        public static SyncExecutionResult CreateSuccess(
            int totalFileCount,
            int downloadedCount,
            int newDownloadCount,
            int updatedDownloadCount,
            long totalDownloadBytes,
            int skippedCount,
            int protectedSkippedCount)
        {
            return new SyncExecutionResult(
                true,
                string.Empty,
                totalFileCount,
                downloadedCount,
                newDownloadCount,
                updatedDownloadCount,
                totalDownloadBytes,
                skippedCount,
                protectedSkippedCount);
        }

        public static SyncExecutionResult Fail(
            string errorMessage,
            int totalFileCount,
            int downloadedCount,
            int newDownloadCount,
            int updatedDownloadCount,
            long totalDownloadBytes,
            int skippedCount,
            int protectedSkippedCount)
        {
            return new SyncExecutionResult(
                false,
                errorMessage,
                totalFileCount,
                downloadedCount,
                newDownloadCount,
                updatedDownloadCount,
                totalDownloadBytes,
                skippedCount,
                protectedSkippedCount);
        }
    }
}
