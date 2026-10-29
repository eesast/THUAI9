using Avalonia.Controls;
using Avalonia.Controls.Selection;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CosDownloader
{
    public partial class MainWindow : Window
    {
        private readonly CosService cosService = new CosService();
        private readonly List<RemoteFileItem> currentFiles = new List<RemoteFileItem>();
        private LocalSyncProtectionSettings protectionSettings = new LocalSyncProtectionSettings();
        private string downloadRoot = CosService.GetDefaultDownloadRoot();
        private string? currentCatalogVersion;
        private CancellationTokenSource? downloadCancellation;
        private bool canContinueDownload;
        private bool isBusy;
        private long progressCarryDownloadedBytes;
        private long progressCarryTotalBytes;
        private long lastDisplayedDownloadedBytes;
        private long lastDisplayedTotalBytes;
        private double lastDisplayedProgressValue;

        public MainWindow()
        {
            InitializeComponent();

            BtnGetList.Click += GetList_Click;
            BtnDownloadAll.Click += DownloadAll_Click;
            BtnPauseDownload.Click += PauseDownload_Click;
            BtnChooseFolder.Click += ChooseFolder_Click;
            BtnProtectSelection.Click += ProtectSelection_Click;
            BtnUnprotectSelection.Click += UnprotectSelection_Click;
            FileTreeView.SelectionChanged += FileTreeView_SelectionChanged;

            DownloadPathText.Text = downloadRoot;
            LoadProtectionSettings();
            UpdateProtectionSummary();
            UpdateSelectionActionButtons();
            ResetDownloadProgress();

            SetStatus("准备就绪", "先刷新云端文件列表，再开始下载。");
            SummaryText.Text = "还没有获取到任何文件。";
            StatsText.Text = BuildCatalogStatsText(0, 0, 0);
        }

        private async void GetList_Click(object? sender, RoutedEventArgs e)
        {
            canContinueDownload = false;
            SetBusy(true);
            BtnGetList.Content = "正在刷新...";
            ResetDownloadProgress();
            SetStatus("正在同步", "正在向云函数请求最新文件列表，请稍候。");

            try
            {
                FileListResult result = await cosService.GetFileListAsync();
                currentFiles.Clear();
                currentCatalogVersion = result.CatalogVersion;

                if (!result.Success)
                {
                    FileTreeView.ItemsSource = null;
                    UpdateSelectionActionButtons();
                    SetStatus("获取失败", "云函数请求失败，请检查网络或接口状态。");
                    SummaryText.Text = "获取文件列表时发生异常。";
                    StatsText.Text = BuildCatalogStatsText(0, 0, 0);
                    return;
                }

                currentFiles.AddRange(result.Files);
                RefreshTree();

                (int fileCount, int folderCount, long totalBytes) = GetCatalogMetrics();
                SetStatus("列表已更新", "已拿到最新文件列表，可以开始增量同步。");
                SummaryText.Text = folderCount > 0
                    ? $"当前共有 {fileCount} 个文件、{folderCount} 个目录，总大小 {FormatSize(totalBytes)}。"
                    : $"当前共有 {fileCount} 个可下载文件，总大小 {FormatSize(totalBytes)}。";
                StatsText.Text = BuildCatalogStatsText(fileCount, folderCount, totalBytes);
            }
            catch (Exception ex)
            {
                currentFiles.Clear();
                currentCatalogVersion = null;
                FileTreeView.ItemsSource = null;
                UpdateSelectionActionButtons();
                SetStatus("获取失败", "云函数请求失败，请检查网络或接口状态。");
                SummaryText.Text = "获取文件列表时发生异常。";
                StatusDetailText.Text = Shorten(ex.Message, 90);
                StatsText.Text = BuildCatalogStatsText(0, 0, 0);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void DownloadAll_Click(object? sender, RoutedEventArgs e)
        {
            await StartDownloadAsync();
        }

        private async void PauseDownload_Click(object? sender, RoutedEventArgs e)
        {
            if (isBusy)
            {
                if (downloadCancellation == null || downloadCancellation.IsCancellationRequested)
                {
                    return;
                }

                BtnPauseDownload.IsEnabled = false;
                BtnPauseDownload.Content = "正在暂停";
                downloadCancellation.Cancel();
                SetStatus("正在暂停", "正在停止当前下载，已完成的文件会保留。");
                DownloadSpeedText.Text = "0 B/s";
                return;
            }

            if (canContinueDownload)
            {
                await StartDownloadAsync();
            }
        }

        private async Task StartDownloadAsync()
        {
            if (currentFiles.Count == 0)
            {
                canContinueDownload = false;
                SetStatus("无法下载", "当前还没有文件列表，请先执行一次刷新。");
                SummaryText.Text = "请先刷新文件列表。";
                return;
            }

            List<RemoteFileItem> downloadableFiles = currentFiles.FindAll(file => !file.IsDirectory);
            if (downloadableFiles.Count == 0)
            {
                canContinueDownload = false;
                SetStatus("没有可下载文件", "当前列表里没有可下载的文件项。");
                SummaryText.Text = "列表中没有可下载文件。";
                return;
            }

            bool resumeFromPause = canContinueDownload && lastDisplayedTotalBytes > 0;
            if (resumeFromPause)
            {
                progressCarryDownloadedBytes = lastDisplayedDownloadedBytes;
                progressCarryTotalBytes = lastDisplayedTotalBytes;
            }
            else
            {
                ResetProgressSession();
            }

            canContinueDownload = false;
            downloadCancellation?.Dispose();
            downloadCancellation = new CancellationTokenSource();

            SetBusy(true);
            BtnDownloadAll.Content = "正在同步...";
            BtnPauseDownload.Content = "⏸ 暂停";
            BtnPauseDownload.IsEnabled = true;
            ShowDownloadProgress();

            if (!resumeFromPause)
            {
                DownloadProgressBar.Value = 0;
                DownloadProgressText.Text = "0 B";
                DownloadTotalText.Text = "计算中";
            }

            DownloadSpeedText.Text = "计算中";
            SetStatus("正在比对", "正在检查本地文件与云端版本清单的差异。");

            try
            {
                SyncExecutionResult result = await cosService.SyncFilesAsync(
                    currentFiles,
                    downloadRoot,
                    currentCatalogVersion,
                    protectionSettings.Items,
                    UpdateSyncProgress,
                    downloadCancellation.Token);

                (int fileCount, int folderCount, long totalBytes) = GetCatalogMetrics();
                int unchangedSkippedCount = Math.Max(0, result.SkippedCount - result.ProtectedSkippedCount);

                if (!result.Success)
                {
                    canContinueDownload = false;
                    SetStatus("同步失败", Shorten(result.ErrorMessage, 60));
                    SummaryText.Text = $"已下载 {result.DownloadedCount} 个，跳过 {result.SkippedCount} 个。";
                    StatsText.Text = BuildSyncStatsText(result, totalBytes);
                    DownloadProgressText.Text = "同步中断";
                    DownloadTotalText.Text = FormatWholeSize(result.TotalDownloadBytes);
                    DownloadSpeedText.Text = "0 B/s";
                    return;
                }

                DownloadProgressBar.Value = 100;

                if (result.DownloadedCount == 0)
                {
                    canContinueDownload = false;
                    string detail = result.ProtectedSkippedCount > 0
                        ? $"未变更 {unchangedSkippedCount} 个，保护 {result.ProtectedSkippedCount} 个。"
                        : "本地和云端一致，无需下载。";

                    SetStatus("已经是最新版本", detail);
                    SummaryText.Text = result.ProtectedSkippedCount > 0
                        ? $"跳过 {result.SkippedCount} 个，保护 {result.ProtectedSkippedCount} 个。"
                        : $"跳过 {result.SkippedCount} 个未变更文件，总大小 {FormatSize(totalBytes)}。";
                    StatsText.Text = BuildCatalogStatsText(fileCount, folderCount, totalBytes);
                    DownloadProgressText.Text = "无需下载";
                    DownloadTotalText.Text = FormatWholeSize(totalBytes);
                    DownloadSpeedText.Text = "0 B/s";
                    return;
                }

                canContinueDownload = false;
                SetStatus(
                    "同步完成",
                    $"新增 {result.NewDownloadCount} 个，更新 {result.UpdatedDownloadCount} 个，保护 {result.ProtectedSkippedCount} 个。");
                SummaryText.Text = $"本次下载 {result.DownloadedCount} 个，未变更不重复下载。";
                StatsText.Text = BuildSyncStatsText(result, totalBytes);
                DownloadProgressText.Text = FormatWholeSize(result.TotalDownloadBytes);
                DownloadTotalText.Text = FormatWholeSize(result.TotalDownloadBytes);
                DownloadSpeedText.Text = "完成";
            }
            catch (OperationCanceledException)
            {
                canContinueDownload = true;
                SetStatus("已暂停", "点击 ▶ 继续可接着同步，已完成文件会保留。");
                SummaryText.Text = "已暂停，可继续。";
                DownloadProgressBar.Value = lastDisplayedProgressValue;
                DownloadSpeedText.Text = "0 B/s";
            }
            catch (Exception ex)
            {
                canContinueDownload = false;
                SetStatus("同步失败", "下载过程中出现异常。");
                SummaryText.Text = Shorten(ex.Message, 60);
                DownloadProgressText.Text = "同步失败";
                DownloadTotalText.Text = "请重试";
                DownloadSpeedText.Text = "0 B/s";
            }
            finally
            {
                downloadCancellation?.Dispose();
                downloadCancellation = null;
                SetBusy(false);
            }
        }

        private async void ChooseFolder_Click(object? sender, RoutedEventArgs e)
        {
            IStorageFolder? suggestedStartLocation = null;
            if (Directory.Exists(downloadRoot))
            {
                suggestedStartLocation = await StorageProvider.TryGetFolderFromPathAsync(downloadRoot);
            }

            IReadOnlyList<IStorageFolder> folders = await StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions
                {
                    Title = "选择下载位置",
                    AllowMultiple = false,
                    SuggestedStartLocation = suggestedStartLocation
                });

            IStorageFolder? folder = folders.FirstOrDefault();
            if (folder == null)
            {
                return;
            }

            string? localPath = folder.TryGetLocalPath();
            if (string.IsNullOrWhiteSpace(localPath))
            {
                SetStatus("选择失败", "当前只支持选择本机磁盘目录。");
                SummaryText.Text = "请重新选择一个本地文件夹。";
                return;
            }

            string resolvedDownloadRoot = ResolveDownloadRootFromSelectedFolder(localPath);

            try
            {
                Directory.CreateDirectory(resolvedDownloadRoot);
            }
            catch (Exception ex)
            {
                SetStatus("选择失败", "无法创建下载文件夹，请换一个位置。");
                SummaryText.Text = Shorten(ex.Message, 60);
                return;
            }

            downloadRoot = resolvedDownloadRoot;
            canContinueDownload = false;
            ResetDownloadProgress();
            DownloadPathText.Text = downloadRoot;
            LoadProtectionSettings();
            UpdateProtectionSummary();
            RefreshTree();

            SetStatus("下载位置已更新", $"文件会保存到 {CosService.GetDefaultDownloadFolderName()} 文件夹中。");
            SummaryText.Text = $"当前下载目录：{downloadRoot}";
        }

        private static string ResolveDownloadRootFromSelectedFolder(string selectedPath)
        {
            string fullPath = Path.GetFullPath(selectedPath);
            string selectedFolderName = new DirectoryInfo(fullPath).Name;
            string downloadFolderName = CosService.GetDefaultDownloadFolderName();

            if (string.Equals(selectedFolderName, downloadFolderName, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }

            return Path.Combine(fullPath, downloadFolderName);
        }

        private void ProtectSelection_Click(object? sender, RoutedEventArgs e)
        {
            ExplorerNode? selectedNode = GetSelectedNode();
            if (selectedNode == null)
            {
                return;
            }

            if (ProtectedSyncMatcher.HasExactRule(selectedNode.KeyPath, selectedNode.IsDirectory, protectionSettings.Items))
            {
                UpdateSelectionActionButtons();
                return;
            }

            protectionSettings.Items.Add(new ProtectedSyncItem
            {
                Key = ProtectedSyncMatcher.NormalizeKey(selectedNode.KeyPath, selectedNode.IsDirectory),
                IsDirectory = selectedNode.IsDirectory
            });

            SaveProtectionSettings();
            RefreshTree();
            UpdateProtectionSummary();

            SetStatus(
                "已保护选中项",
                selectedNode.IsDirectory
                    ? $"目录 {selectedNode.Name} 下的本地文件后续不会被自动覆盖。"
                    : $"文件 {selectedNode.Name} 后续不会被自动覆盖。");
        }

        private void UnprotectSelection_Click(object? sender, RoutedEventArgs e)
        {
            ExplorerNode? selectedNode = GetSelectedNode();
            if (selectedNode == null)
            {
                return;
            }

            int removedCount = protectionSettings.Items.RemoveAll(item =>
                item.IsDirectory == selectedNode.IsDirectory
                && string.Equals(
                    ProtectedSyncMatcher.NormalizeKey(item.Key, item.IsDirectory),
                    ProtectedSyncMatcher.NormalizeKey(selectedNode.KeyPath, selectedNode.IsDirectory),
                    StringComparison.OrdinalIgnoreCase));

            if (removedCount == 0)
            {
                UpdateSelectionActionButtons();
                return;
            }

            SaveProtectionSettings();
            RefreshTree();
            UpdateProtectionSummary();

            SetStatus(
                "已取消保护",
                selectedNode.IsDirectory
                    ? $"目录 {selectedNode.Name} 之后会重新参与更新。"
                    : $"文件 {selectedNode.Name} 之后会重新参与更新。");
        }

        private void FileTreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        {
            UpdateSelectionActionButtons();
        }

        private void UpdateSyncProgress(SyncProgressInfo progress)
        {
            ShowDownloadProgress();

            string title = progress.Action switch
            {
                SyncFileAction.Skip => "正在检查",
                SyncFileAction.DownloadNew => "正在下载新文件",
                SyncFileAction.DownloadUpdated => "正在更新文件",
                _ => "正在同步"
            };

            string shortFileName = Shorten(progress.File.FileName, 28);
            SetStatus(title, $"进度 {progress.CurrentIndex}/{progress.TotalCount} | {shortFileName}");

            SummaryText.Text = progress.Action switch
            {
                SyncFileAction.Skip => $"检查：{shortFileName}",
                SyncFileAction.DownloadNew => $"新增：{shortFileName}",
                SyncFileAction.DownloadUpdated => $"更新：{shortFileName}",
                _ => shortFileName
            };

            (_, _, long catalogBytes) = GetCatalogMetrics();
            long displayedTotalBytes = progressCarryTotalBytes > 0
                ? progressCarryTotalBytes
                : progress.TotalDownloadBytes;
            long displayedDownloadedBytes = progressCarryDownloadedBytes + progress.DownloadedBytes;
            if (displayedTotalBytes > 0)
            {
                displayedDownloadedBytes = Math.Clamp(displayedDownloadedBytes, 0, displayedTotalBytes);
            }

            StatsText.Text = displayedTotalBytes > 0
                ? $"进度 {progress.CurrentIndex}/{progress.TotalCount} | 本次 {FormatWholeSize(displayedTotalBytes)}\n云端总大小 {FormatWholeSize(catalogBytes)}"
                : $"进度 {progress.CurrentIndex}/{progress.TotalCount} | 本次 0 B\n云端总大小 {FormatWholeSize(catalogBytes)}";

            double overallPercent = displayedTotalBytes > 0
                ? Math.Clamp(displayedDownloadedBytes * 100d / displayedTotalBytes, 0, 100)
                : Math.Clamp(progress.CurrentIndex * 100d / Math.Max(progress.TotalCount, 1), 0, 100);

            DownloadProgressBar.Value = overallPercent;
            DownloadProgressText.Text = displayedTotalBytes > 0
                ? FormatWholeSize(displayedDownloadedBytes)
                : $"{progress.CurrentIndex}/{progress.TotalCount}";
            DownloadTotalText.Text = displayedTotalBytes > 0
                ? FormatWholeSize(displayedTotalBytes)
                : $"{progress.TotalCount} 项";
            DownloadSpeedText.Text = FormatSpeed(progress.BytesPerSecond);

            lastDisplayedDownloadedBytes = displayedDownloadedBytes;
            lastDisplayedTotalBytes = displayedTotalBytes;
            lastDisplayedProgressValue = overallPercent;
        }

        private void SetBusy(bool busy)
        {
            isBusy = busy;

            BtnGetList.IsEnabled = !busy;
            BtnDownloadAll.IsEnabled = !busy && currentFiles.Any(file => !file.IsDirectory);
            BtnPauseDownload.IsEnabled = busy && downloadCancellation != null && !downloadCancellation.IsCancellationRequested;
            BtnChooseFolder.IsEnabled = !busy;

            if (!busy)
            {
                BtnGetList.Content = "刷新文件列表";
                BtnDownloadAll.Content = "下载全部文件";
                BtnPauseDownload.Content = canContinueDownload ? "▶ 继续" : "⏸ 暂停";
                BtnPauseDownload.IsEnabled = canContinueDownload && currentFiles.Any(file => !file.IsDirectory);
            }
            else
            {
                BtnPauseDownload.Content = "⏸ 暂停";
            }

            UpdateSelectionActionButtons();
        }

        private void SetStatus(string title, string detail)
        {
            StatusTitleText.Text = title;
            StatusDetailText.Text = detail;
        }

        private void LoadProtectionSettings()
        {
            protectionSettings = LocalSyncProtectionStore.Load(downloadRoot);
        }

        private void SaveProtectionSettings()
        {
            LocalSyncProtectionStore.Save(downloadRoot, protectionSettings);
        }

        private void RefreshTree()
        {
            FileTreeView.ItemsSource = currentFiles.Count == 0
                ? null
                : ExplorerNode.BuildTree(currentFiles, protectionSettings.Items);

            UpdateSelectionActionButtons();
        }

        private void UpdateProtectionSummary()
        {
            int fileRules = protectionSettings.Items.Count(item => !item.IsDirectory);
            int directoryRules = protectionSettings.Items.Count(item => item.IsDirectory);
            int totalRules = fileRules + directoryRules;

            ProtectedItemsText.Text = totalRules == 0
                ? "0 个文件/目录"
                : $"{totalRules} 个条目（{fileRules} 个文件，{directoryRules} 个目录）";
        }

        private void UpdateSelectionActionButtons()
        {
            ExplorerNode? selectedNode = GetSelectedNode();
            bool hasSelection = selectedNode != null;
            bool hasExactRule = hasSelection && ProtectedSyncMatcher.HasExactRule(
                selectedNode!.KeyPath,
                selectedNode.IsDirectory,
                protectionSettings.Items);

            BtnProtectSelection.IsEnabled = !isBusy && hasSelection && !hasExactRule;
            BtnUnprotectSelection.IsEnabled = !isBusy && hasSelection && hasExactRule;
        }

        private ExplorerNode? GetSelectedNode()
        {
            return FileTreeView.SelectedItem as ExplorerNode;
        }

        private (int FileCount, int FolderCount, long TotalBytes) GetCatalogMetrics()
        {
            int folderCount = currentFiles.Count(file => file.IsDirectory);
            int fileCount = currentFiles.Count - folderCount;
            long totalBytes = currentFiles
                .Where(file => !file.IsDirectory && file.Size.HasValue)
                .Sum(file => file.Size!.Value);

            return (fileCount, folderCount, totalBytes);
        }

        private void ResetDownloadProgress()
        {
            ResetProgressSession();
            DownloadProgressBar.Value = 0;
            DownloadProgressText.Text = "0 B";
            DownloadTotalText.Text = "等待开始";
            DownloadSpeedText.Text = "0 B/s";
            DownloadProgressPanel.IsVisible = true;
            DownloadProgressPanel.Opacity = 0.72;
        }

        private void ResetProgressSession()
        {
            progressCarryDownloadedBytes = 0;
            progressCarryTotalBytes = 0;
            lastDisplayedDownloadedBytes = 0;
            lastDisplayedTotalBytes = 0;
            lastDisplayedProgressValue = 0;
        }

        private void ShowDownloadProgress()
        {
            DownloadProgressPanel.IsVisible = true;
            DownloadProgressPanel.Opacity = 1;
        }

        private static string BuildCatalogStatsText(int fileCount, int folderCount, long totalBytes)
        {
            return $"文件 {fileCount} 个 | 目录 {folderCount} 个\n总大小 {FormatWholeSize(totalBytes)}";
        }

        private static string BuildSyncStatsText(SyncExecutionResult result, long totalBytes)
        {
            int unchangedSkippedCount = Math.Max(0, result.SkippedCount - result.ProtectedSkippedCount);
            return $"新增 {result.NewDownloadCount} | 更新 {result.UpdatedDownloadCount} | 跳过 {unchangedSkippedCount}\n保护 {result.ProtectedSkippedCount} | 本次 {FormatWholeSize(result.TotalDownloadBytes)} | 云端 {FormatWholeSize(totalBytes)}";
        }

        private static string Shorten(string text, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.ReplaceLineEndings(" ");
            return normalized.Length <= maxLength
                ? normalized
                : normalized.Substring(0, maxLength) + "...";
        }

        private static string FormatSpeed(double bytesPerSecond)
        {
            return bytesPerSecond <= 0.5 ? "0 B/s" : $"{FormatWholeSize((long)Math.Round(bytesPerSecond))}/s";
        }

        private static string FormatWholeSize(long size)
        {
            string[] units = { "B", "KB", "MB", "GB" };
            double value = Math.Max(0, size);
            int unitIndex = 0;

            while (value >= 1024 && unitIndex < units.Length - 1)
            {
                value /= 1024;
                unitIndex++;
            }

            return $"{value:0} {units[unitIndex]}";
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
