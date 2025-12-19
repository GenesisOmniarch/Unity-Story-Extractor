using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using UnityStoryExtractor.Core.Extractor;
using UnityStoryExtractor.Core.Loader;
using UnityStoryExtractor.Core.Models;
using UnityStoryExtractor.Core.Output;
using UnityStoryExtractor.Core.Parser;

namespace UnityStoryExtractor.GUI.ViewModels;

/// <summary>
/// メインウィンドウのViewModel - 抜本的に改善
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IAssetLoader _loader;
    private readonly IStoryExtractor _extractor;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly Dispatcher _dispatcher;

    // タイムアウト設定
    private const int LoadTimeoutSeconds = 30;
    private const int ExtractTimeoutSeconds = 300; // 5分

    [ObservableProperty]
    private ObservableCollection<FileTreeNodeViewModel> _fileTreeNodes = new();

    [ObservableProperty]
    private ObservableCollection<ExtractedText> _extractedResults = new();

    [ObservableProperty]
    private ObservableCollection<ExtractedText> _filteredResults = new();

    [ObservableProperty]
    private ExtractedText? _selectedResult;

    [ObservableProperty]
    private FileTreeNodeViewModel? _selectedTreeNode;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _statusText = "準備完了";

    [ObservableProperty]
    private string _unityVersion = "不明";

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private double _progress;

    [ObservableProperty]
    private bool _isExtracting;

    [ObservableProperty]
    private bool _isLoadingPreview;

    [ObservableProperty]
    private bool _hasResults;

    [ObservableProperty]
    private bool _hasNoFiles = true;

    [ObservableProperty]
    private bool _canExtract;

    [ObservableProperty]
    private ExtractionStatistics _statistics = new();

    [ObservableProperty]
    private ObservableCollection<string> _filterSources = new()
    {
        "すべて", "TextAsset", "MonoBehaviour", "Assembly", "Binary"
    };

    [ObservableProperty]
    private string _selectedFilterSource = "すべて";

    [ObservableProperty]
    private ExtractionOptions _options = new();

    // 出力フォルダ（App.OutputFolderと同期）
    [ObservableProperty]
    private string _outputFolderPath = string.Empty;

    [ObservableProperty]
    private string _lastExportedFilePath = string.Empty;

    // アセットプレビュー
    [ObservableProperty]
    private string _assetPreviewContent = string.Empty;

    [ObservableProperty]
    private string _assetPreviewTitle = "アセットを選択してください";

    [ObservableProperty]
    private ObservableCollection<AssetContentItem> _assetContents = new();

    // ログ
    [ObservableProperty]
    private ObservableCollection<LogEntry> _logEntries = new();

    [ObservableProperty]
    private string _logText = string.Empty;

    // メモリ監視
    [ObservableProperty]
    private string _memoryUsage = "0 MB";

    private string _currentPath = string.Empty;
    private System.Timers.Timer? _memoryMonitorTimer;

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _loader = new UnityAssetLoader();
        _extractor = new StoryExtractor();

        // 出力フォルダのパスを設定
        OutputFolderPath = App.OutputFolder;
        EnsureOutputFolderExists();

        // プロパティ変更時のフィルタリング
        PropertyChanged += OnPropertyChanged;

        // メモリ監視タイマー開始
        StartMemoryMonitor();

        AddLog(LogLevel.Info, $"アプリケーション初期化完了");
        AddLog(LogLevel.Info, $"出力フォルダ: {OutputFolderPath}");
    }

    private void EnsureOutputFolderExists()
    {
        try
        {
            if (!Directory.Exists(OutputFolderPath))
            {
                Directory.CreateDirectory(OutputFolderPath);
                AddLog(LogLevel.Info, $"出力フォルダを作成: {OutputFolderPath}");
            }
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"出力フォルダ作成エラー: {ex.Message}");
        }
    }

    private void StartMemoryMonitor()
    {
        _memoryMonitorTimer = new System.Timers.Timer(2000);
        _memoryMonitorTimer.Elapsed += (s, e) =>
        {
            var memoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            SafeInvoke(() =>
            {
                MemoryUsage = $"{memoryMB:F1} MB";

                // 1.5GB超えたらGC警告
                if (memoryMB > 1500)
                {
                    AddLog(LogLevel.Warning, $"メモリ使用量が高くなっています: {memoryMB:F0} MB");
                    GC.Collect(2, GCCollectionMode.Optimized);
                }
            });
        };
        _memoryMonitorTimer.Start();
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FilterText) or nameof(SelectedFilterSource))
        {
            ApplyFilter();
        }
        else if (e.PropertyName == nameof(SelectedTreeNode))
        {
            _ = LoadAssetPreviewAsync();
        }
    }

    // === ログ機能 ===
    public void AddLog(LogLevel level, string message, string? details = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            Details = details
        };

        SafeInvoke(() =>
        {
            LogEntries.Add(entry);
            LogText += $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] {message}\n";
            if (!string.IsNullOrEmpty(details))
            {
                LogText += $"  詳細: {details}\n";
            }
        });

        // ファイルにも保存
        SaveLogToFile(entry);

        // App.xaml.csにも転送（エラーログ統合）
        if (level == LogLevel.Error)
        {
            App.WriteLog($"[GUI] {message}");
        }
    }

    private void SaveLogToFile(LogEntry entry)
    {
        try
        {
            var logFilePath = Path.Combine(OutputFolderPath, "extraction_log.txt");
            var logLine = $"[{entry.Timestamp:yyyy-MM-dd HH:mm:ss}] [{entry.Level}] {entry.Message}";
            if (!string.IsNullOrEmpty(entry.Details))
            {
                logLine += $" | 詳細: {entry.Details}";
            }
            File.AppendAllText(logFilePath, logLine + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // ログ書き込みエラーは無視
        }
    }

    private void SafeInvoke(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.Invoke(action);
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
        LogText = string.Empty;
        AddLog(LogLevel.Info, "ログをクリアしました");
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            if (Directory.Exists(OutputFolderPath))
            {
                Process.Start("explorer.exe", OutputFolderPath);
                AddLog(LogLevel.Info, $"出力フォルダを開きました: {OutputFolderPath}");
            }
            else
            {
                MessageBox.Show($"出力フォルダが存在しません:\n{OutputFolderPath}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"フォルダを開けませんでした: {ex.Message}");
        }
    }

    // === アセットプレビュー機能（問題2対応） ===
    private async Task LoadAssetPreviewAsync()
    {
        if (SelectedTreeNode == null || SelectedTreeNode.IsDirectory)
        {
            AssetPreviewTitle = "アセットを選択してください";
            AssetPreviewContent = string.Empty;
            AssetContents.Clear();
            return;
        }

        var nodeName = SelectedTreeNode.Name;
        var nodePath = SelectedTreeNode.FullPath;
        var nodeType = SelectedTreeNode.NodeType;

        try
        {
            IsLoadingPreview = true;
            AssetPreviewTitle = $"読み込み中: {nodeName}...";
            AssetPreviewContent = "解析中...";
            AssetContents.Clear();

            AddLog(LogLevel.Info, $"アセットプレビュー開始: {nodeName}");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(LoadTimeoutSeconds));

            var contents = new List<AssetContentItem>();
            var previewText = new StringBuilder();

            await Task.Run(async () =>
            {
                try
                {
                    var fileInfo = new FileInfo(nodePath);
                    previewText.AppendLine($"=== ファイル情報 ===");
                    previewText.AppendLine($"パス: {nodePath}");
                    previewText.AppendLine($"サイズ: {FormatFileSize(fileInfo.Length)}");
                    previewText.AppendLine($"種別: {nodeType}");
                    previewText.AppendLine();

                    // ファイルサイズが大きすぎる場合は警告
                    if (fileInfo.Length > 500 * 1024 * 1024) // 500MB
                    {
                        previewText.AppendLine("[警告] ファイルサイズが大きいため、プレビューは制限されます");
                        previewText.AppendLine();
                    }

                    // 小さいファイル（100MB以下）のみ完全解析
                    if (fileInfo.Length <= 100 * 1024 * 1024)
                    {
                        var parser = new TextAssetParser();
                        var result = await parser.ParseAsync(nodePath, Options, cts.Token);

                        if (result.Success && result.Assets.Count > 0)
                        {
                            previewText.AppendLine($"=== 抽出されたテキスト ({result.Assets.Count} 件) ===");
                            previewText.AppendLine();

                            int count = 0;
                            foreach (var asset in result.Assets.Take(50)) // 最大50件
                            {
                                cts.Token.ThrowIfCancellationRequested();

                                contents.Add(new AssetContentItem
                                {
                                    Name = asset.Name,
                                    TypeName = asset.TypeName,
                                    Size = asset.TextContent.Sum(t => t.Length),
                                    Preview = string.Join(" ", asset.TextContent.Take(2)).Truncate(200)
                                });

                                foreach (var text in asset.TextContent.Take(5))
                                {
                                    previewText.AppendLine($"--- [{asset.Name}] ---");
                                    previewText.AppendLine(text.Truncate(2000));
                                    previewText.AppendLine();
                                }

                                count++;
                                if (count >= 20) break;
                            }

                            if (result.Assets.Count > 50)
                            {
                                previewText.AppendLine($"... 他 {result.Assets.Count - 50} 件のテキストがあります");
                            }
                        }
                        else if (result.Errors.Count > 0)
                        {
                            previewText.AppendLine("[エラー] 解析に失敗しました:");
                            foreach (var error in result.Errors)
                            {
                                previewText.AppendLine($"  - {error}");
                            }
                        }
                        else
                        {
                            previewText.AppendLine("[情報] テキストデータが見つかりませんでした");
                        }
                    }
                    else
                    {
                        // 大きなファイルはヘッダー情報のみ
                        previewText.AppendLine("[情報] ファイルが大きいため、抽出処理で解析してください");

                        using var stream = File.OpenRead(nodePath);
                        var header = new byte[Math.Min(1024, fileInfo.Length)];
                        await stream.ReadAsync(header, cts.Token);

                        previewText.AppendLine();
                        previewText.AppendLine("=== ヘッダー (HEX) ===");
                        previewText.AppendLine(BitConverter.ToString(header.Take(256).ToArray()).Replace("-", " "));
                    }
                }
                catch (OperationCanceledException)
                {
                    previewText.Clear();
                    previewText.AppendLine("[タイムアウト] プレビューの読み込みがタイムアウトしました");
                    previewText.AppendLine("抽出処理で解析してください");
                }
                catch (Exception ex)
                {
                    previewText.Clear();
                    previewText.AppendLine($"[エラー] {ex.GetType().Name}: {ex.Message}");
                }
            }, cts.Token);

            SafeInvoke(() =>
            {
                AssetPreviewTitle = $"{nodeName} ({contents.Count} アイテム)";
                AssetPreviewContent = previewText.ToString();
                foreach (var item in contents)
                {
                    AssetContents.Add(item);
                }
            });

            AddLog(LogLevel.Info, $"プレビュー完了: {nodeName} - {contents.Count} アイテム");
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"プレビューエラー: {ex.Message}");
            AssetPreviewTitle = "エラー";
            AssetPreviewContent = $"プレビューの読み込みに失敗しました:\n{ex.Message}";
        }
        finally
        {
            IsLoadingPreview = false;
        }
    }

    [RelayCommand]
    private async Task OpenFolderAsync()
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Unityゲームフォルダを選択",
            Multiselect = false
        };

        if (dialog.ShowDialog() == true)
        {
            await LoadPathAsync(dialog.FolderName);
        }
    }

    public async void LoadPath(string path)
    {
        await LoadPathAsync(path);
    }

    private async Task LoadPathAsync(string path)
    {
        if (string.IsNullOrEmpty(path)) return;

        try
        {
            _currentPath = path;
            StatusText = $"読み込み中: {path}";
            IsExtracting = true;
            CanExtract = false;
            FileTreeNodes.Clear();
            AddLog(LogLevel.Info, $"フォルダスキャン開始: {path}");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();

            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                ProgressText = $"スキャン中: {p.ProcessedFiles}/{p.TotalFiles}";
                if (p.ProcessedFiles % 50 == 0)
                {
                    StatusText = $"スキャン中: {Path.GetFileName(p.CurrentFile)}";
                }
            });

            // Unityバージョン検出
            AddLog(LogLevel.Info, "Unityバージョンを検出中...");
            var version = await _loader.DetectUnityVersionAsync(path, _cancellationTokenSource.Token);
            UnityVersion = version ?? "不明";
            AddLog(LogLevel.Info, $"Unityバージョン: {UnityVersion}");

            // ディレクトリスキャン
            var stopwatch = Stopwatch.StartNew();
            var rootNode = await _loader.ScanDirectoryAsync(path, progress, _cancellationTokenSource.Token);
            stopwatch.Stop();

            // ViewModelに変換
            var viewModel = CreateTreeViewModel(rootNode, null);
            viewModel.IsExpanded = true;
            FileTreeNodes.Add(viewModel);

            HasNoFiles = false;
            CanExtract = true;
            var nodeCount = CountNodes(viewModel);
            StatusText = $"読み込み完了: {nodeCount} アイテム ({stopwatch.ElapsedMilliseconds}ms)";
            AddLog(LogLevel.Info, $"スキャン完了: {nodeCount} アイテム, {stopwatch.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException)
        {
            StatusText = "読み込みがキャンセルされました";
            AddLog(LogLevel.Warning, "スキャンがキャンセルされました");
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
            AddLog(LogLevel.Error, $"スキャンエラー: {ex.Message}", ex.StackTrace);
            App.WriteErrorLog("LoadPathAsync", ex);
            MessageBox.Show($"フォルダの読み込みに失敗しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsExtracting = false;
            Progress = 0;
        }
    }

    private FileTreeNodeViewModel CreateTreeViewModel(FileTreeNode model, FileTreeNodeViewModel? parent)
    {
        var vm = new FileTreeNodeViewModel(model, parent);
        vm.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(FileTreeNodeViewModel.IsSelected) && vm.IsSelected)
            {
                SelectedTreeNode = vm;
            }
        };
        return vm;
    }

    // === 抽出処理（問題3対応：抜本的改善） ===
    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (string.IsNullOrEmpty(_currentPath) || FileTreeNodes.Count == 0)
        {
            MessageBox.Show("フォルダを選択してください", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            IsExtracting = true;
            CanExtract = false;
            ExtractedResults.Clear();
            FilteredResults.Clear();

            AddLog(LogLevel.Info, "========================================");
            AddLog(LogLevel.Info, $"抽出処理を開始: {_currentPath}");
            AddLog(LogLevel.Info, $"並列処理: {(Options.UseParallelProcessing ? $"有効 (並列度:{Options.MaxDegreeOfParallelism})" : "無効")}");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();

            // タイムアウト設定
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(ExtractTimeoutSeconds));

            var stopwatch = Stopwatch.StartNew();
            int totalProcessed = 0;
            int totalErrors = 0;
            int totalExtracted = 0;

            var progress = new Progress<ExtractionProgress>(p =>
            {
                Progress = p.Percentage;
                ProgressText = $"{p.ProcessedFiles}/{p.TotalFiles} - {p.CurrentOperation}";
                StatusText = $"抽出中: {Path.GetFileName(p.CurrentFile)}";

                // 定期ログ
                if (p.ProcessedFiles > 0 && p.ProcessedFiles % 20 == 0)
                {
                    AddLog(LogLevel.Info, $"進捗: {p.ProcessedFiles}/{p.TotalFiles} ({p.Percentage:F0}%)");
                }

                // メモリチェック
                var memoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                if (memoryMB > 2000)
                {
                    AddLog(LogLevel.Warning, "メモリ使用量が高いためGCを実行");
                    GC.Collect(2, GCCollectionMode.Forced);
                    GC.WaitForPendingFinalizers();
                }
            });

            var result = await _extractor.ExtractFromDirectoryAsync(
                _currentPath,
                Options,
                progress,
                linkedCts.Token);

            stopwatch.Stop();

            // 結果を表示
            foreach (var text in result.ExtractedTexts)
            {
                ExtractedResults.Add(text);
            }

            Statistics = result.Statistics;
            HasResults = result.ExtractedTexts.Count > 0;
            totalExtracted = result.TotalExtracted;
            totalProcessed = result.ProcessedFiles;
            totalErrors = result.Errors.Count;

            ApplyFilter();

            // ログ出力
            AddLog(LogLevel.Info, "========================================");
            AddLog(LogLevel.Info, $"抽出完了!");
            AddLog(LogLevel.Info, $"  処理ファイル数: {totalProcessed}");
            AddLog(LogLevel.Info, $"  抽出アイテム数: {totalExtracted}");
            AddLog(LogLevel.Info, $"  エラー数: {totalErrors}");
            AddLog(LogLevel.Info, $"  処理時間: {stopwatch.Elapsed.TotalSeconds:F1} 秒");
            AddLog(LogLevel.Info, $"  成功率: {(totalProcessed > 0 ? (totalProcessed - totalErrors) * 100.0 / totalProcessed : 0):F1}%");

            if (result.Errors.Count > 0)
            {
                AddLog(LogLevel.Warning, $"エラーがありました ({result.Errors.Count} 件):");
                foreach (var error in result.Errors.Take(10))
                {
                    AddLog(LogLevel.Error, $"  - {Path.GetFileName(error.File)}: {error.Message}");
                }
                if (result.Errors.Count > 10)
                {
                    AddLog(LogLevel.Warning, $"  ... 他 {result.Errors.Count - 10} 件のエラー");
                }
            }

            StatusText = $"抽出完了: {totalExtracted} アイテム ({stopwatch.Elapsed.TotalSeconds:F1}秒)";

            // 結果がある場合は自動保存
            if (HasResults)
            {
                await AutoSaveResultsAsync(result);
            }
            else
            {
                AddLog(LogLevel.Warning, "抽出可能なテキストが見つかりませんでした");
                MessageBox.Show(
                    "抽出可能なテキストが見つかりませんでした。\n\n" +
                    "考えられる原因:\n" +
                    "- ゲームデータが暗号化されている\n" +
                    "- サポートされていない形式のファイル\n" +
                    "- テキストデータが含まれていない\n\n" +
                    "詳細はログタブを確認してください。",
                    "結果なし",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "抽出がキャンセル/タイムアウトしました";
            AddLog(LogLevel.Warning, "抽出処理がキャンセルまたはタイムアウトしました");
        }
        catch (OutOfMemoryException ex)
        {
            StatusText = "メモリ不足エラー";
            AddLog(LogLevel.Error, $"メモリ不足: {ex.Message}");
            App.WriteErrorLog("ExtractAsync - OOM", ex);

            GC.Collect(2, GCCollectionMode.Forced);
            GC.WaitForPendingFinalizers();

            MessageBox.Show(
                "メモリ不足が発生しました。\n\n対処法:\n" +
                "- 設定で並列度を下げる\n" +
                "- 他のアプリケーションを閉じる\n" +
                "- より大きなメモリのPCで実行",
                "メモリ不足",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
            AddLog(LogLevel.Error, $"抽出エラー: {ex.GetType().Name} - {ex.Message}", ex.StackTrace);
            App.WriteErrorLog("ExtractAsync", ex);
            MessageBox.Show($"抽出に失敗しました:\n{ex.Message}\n\n詳細はログを確認してください。", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsExtracting = false;
            CanExtract = true;
            Progress = 0;
        }
    }

    private async Task AutoSaveResultsAsync(ExtractionResult result)
    {
        try
        {
            EnsureOutputFolderExists();

            var folderName = Path.GetFileName(_currentPath) ?? "extracted";
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"story_from_{SanitizeFileName(folderName)}_{timestamp}.json";
            var outputPath = Path.Combine(OutputFolderPath, fileName);

            var writer = OutputWriterFactory.Create(OutputFormat.Json);
            await writer.WriteAsync(result, outputPath);

            LastExportedFilePath = outputPath;
            AddLog(LogLevel.Info, $"自動保存完了: {outputPath}");
            StatusText = $"保存完了: {fileName}";

            MessageBox.Show(
                $"抽出結果を保存しました:\n{outputPath}\n\n" +
                $"抽出アイテム数: {result.TotalExtracted}\n" +
                $"処理ファイル数: {result.ProcessedFiles}",
                "保存完了",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AddLog(LogLevel.Error, $"自動保存エラー: {ex.Message}");
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new StringBuilder(fileName);
        foreach (var c in invalidChars)
        {
            sanitized.Replace(c, '_');
        }
        return sanitized.ToString();
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        double len = bytes;
        int order = 0;
        while (len >= 1024 && order < sizes.Length - 1)
        {
            order++;
            len /= 1024;
        }
        return $"{len:0.##} {sizes[order]}";
    }

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        AddLog(LogLevel.Warning, "キャンセルが要求されました");
        StatusText = "キャンセル中...";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!HasResults) return;

        var dialog = new SaveFileDialog
        {
            Title = "抽出結果を保存",
            Filter = "JSON (*.json)|*.json|テキスト (*.txt)|*.txt|CSV (*.csv)|*.csv|XML (*.xml)|*.xml",
            DefaultExt = ".json",
            FileName = $"extracted_{DateTime.Now:yyyyMMdd_HHmmss}",
            InitialDirectory = OutputFolderPath
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                var format = Path.GetExtension(dialog.FileName).ToLowerInvariant() switch
                {
                    ".json" => OutputFormat.Json,
                    ".txt" => OutputFormat.Text,
                    ".csv" => OutputFormat.Csv,
                    ".xml" => OutputFormat.Xml,
                    _ => OutputFormat.Json
                };

                var result = new ExtractionResult
                {
                    SourcePath = _currentPath,
                    UnityVersion = UnityVersion,
                    ExtractedTexts = ExtractedResults.ToList(),
                    Statistics = Statistics,
                    Success = true,
                    StartTime = DateTime.UtcNow,
                    EndTime = DateTime.UtcNow
                };

                var writer = OutputWriterFactory.Create(format);
                await writer.WriteAsync(result, dialog.FileName);

                LastExportedFilePath = dialog.FileName;
                StatusText = $"保存完了: {dialog.FileName}";
                AddLog(LogLevel.Info, $"ファイル保存: {dialog.FileName}");

                MessageBox.Show($"保存しました:\n{dialog.FileName}", "保存完了",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AddLog(LogLevel.Error, $"保存エラー: {ex.Message}");
                MessageBox.Show($"保存に失敗しました:\n{ex.Message}", "エラー",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void Settings()
    {
        var settingsWindow = new SettingsWindow(Options, OutputFolderPath);
        if (settingsWindow.ShowDialog() == true)
        {
            Options = settingsWindow.Options;
            if (!string.IsNullOrEmpty(settingsWindow.OutputFolderPath))
            {
                OutputFolderPath = settingsWindow.OutputFolderPath;
                AddLog(LogLevel.Info, $"出力フォルダを変更: {OutputFolderPath}");
            }
        }
    }

    private void ApplyFilter()
    {
        FilteredResults.Clear();

        var filtered = ExtractedResults.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var filter = FilterText.ToLowerInvariant();
            filtered = filtered.Where(r =>
                r.Content.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                r.AssetName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedFilterSource != "すべて")
        {
            filtered = filtered.Where(r => r.Source.ToString() == SelectedFilterSource);
        }

        foreach (var item in filtered.Take(1000)) // 表示上限
        {
            FilteredResults.Add(item);
        }
    }

    private static int CountNodes(FileTreeNodeViewModel node)
    {
        return 1 + node.Children.Sum(CountNodes);
    }
}

// === 補助クラス ===

public enum LogLevel { Info, Warning, Error }

public class LogEntry
{
    public DateTime Timestamp { get; set; }
    public LogLevel Level { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? Details { get; set; }
    public string LevelIcon => Level switch
    {
        LogLevel.Info => "ℹ️",
        LogLevel.Warning => "⚠️",
        LogLevel.Error => "❌",
        _ => "•"
    };
    public string FormattedTime => Timestamp.ToString("HH:mm:ss");
}

public class AssetContentItem
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public int Size { get; set; }
    public string Preview { get; set; } = string.Empty;
}

public class FileTreeNodeViewModel : INotifyPropertyChanged
{
    private readonly FileTreeNode _model;
    private bool _isExpanded;
    private bool _isSelected;
    private ObservableCollection<FileTreeNodeViewModel>? _children;

    public FileTreeNodeViewModel(FileTreeNode model, FileTreeNodeViewModel? parent = null)
    {
        _model = model;
    }

    public string Name => _model.Name;
    public string FullPath => _model.FullPath;
    public bool IsDirectory => _model.IsDirectory;
    public FileNodeType NodeType => _model.NodeType;
    public long FileSize => _model.FileSize;
    public FileTreeNode Model => _model;

    public string Icon => NodeType switch
    {
        FileNodeType.Directory => "📁",
        FileNodeType.AssetsFile => "📄",
        FileNodeType.AssetBundle => "📦",
        FileNodeType.ResourcesAssets => "🗃️",
        FileNodeType.ResSFile => "🖼️",
        FileNodeType.Assembly => "⚙️",
        FileNodeType.GlobalGameManagers => "🔧",
        _ => "📄"
    };

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (_isExpanded != value) { _isExpanded = value; OnPropertyChanged(); } }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
    }

    public ObservableCollection<FileTreeNodeViewModel> Children
    {
        get
        {
            _children ??= new ObservableCollection<FileTreeNodeViewModel>(
                _model.Children.Select(c => new FileTreeNodeViewModel(c, this)));
            return _children;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

// 文字列拡張メソッド
public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
