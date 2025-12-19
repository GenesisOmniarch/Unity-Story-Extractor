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
/// メインウィンドウのViewModel - フリーズ問題修正版
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IAssetLoader _loader;
    private readonly IStoryExtractor _extractor;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly Dispatcher _dispatcher;

    // タイムアウト設定
    private const int LoadTimeoutSeconds = 60;
    private const int ExtractTimeoutSeconds = 600; // 10分

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
    private ExtractionOptions _options = new()
    {
        // デフォルト並列度を2に下げる（フリーズ防止）
        MaxDegreeOfParallelism = 2,
        UseParallelProcessing = true
    };

    [ObservableProperty]
    private string _outputFolderPath = string.Empty;

    [ObservableProperty]
    private string _lastExportedFilePath = string.Empty;

    [ObservableProperty]
    private string _assetPreviewContent = string.Empty;

    [ObservableProperty]
    private string _assetPreviewTitle = "アセットを選択してください";

    [ObservableProperty]
    private ObservableCollection<AssetContentItem> _assetContents = new();

    [ObservableProperty]
    private ObservableCollection<LogEntry> _logEntries = new();

    [ObservableProperty]
    private string _logText = string.Empty;

    [ObservableProperty]
    private string _memoryUsage = "0 MB";

    private string _currentPath = string.Empty;
    private System.Timers.Timer? _memoryMonitorTimer;
    private int _lastProgressReport = 0;

    public MainViewModel()
    {
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        _loader = new UnityAssetLoader();
        _extractor = new StoryExtractor();

        OutputFolderPath = App.OutputFolder;
        EnsureOutputFolderExists();

        PropertyChanged += OnPropertyChanged;
        StartMemoryMonitor();

        AddLogAsync(LogLevel.Info, "アプリケーション初期化完了");
        AddLogAsync(LogLevel.Info, $"出力フォルダ: {OutputFolderPath}");
    }

    private void EnsureOutputFolderExists()
    {
        try
        {
            if (!Directory.Exists(OutputFolderPath))
            {
                Directory.CreateDirectory(OutputFolderPath);
            }
        }
        catch { }
    }

    private void StartMemoryMonitor()
    {
        _memoryMonitorTimer = new System.Timers.Timer(3000);
        _memoryMonitorTimer.Elapsed += async (s, e) =>
        {
            var memoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            await SafeInvokeAsync(() => MemoryUsage = $"{memoryMB:F1} MB");

            if (memoryMB > 1500)
            {
                GC.Collect(2, GCCollectionMode.Optimized);
            }
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

    // === 問題1修正: Dispatcher.Invoke → InvokeAsync ===
    private async Task SafeInvokeAsync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            await _dispatcher.InvokeAsync(action, DispatcherPriority.Background);
        }
    }

    private void SafeInvokeSync(Action action)
    {
        if (_dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            _dispatcher.BeginInvoke(action, DispatcherPriority.Background);
        }
    }

    // === 問題4修正: ログの整理（非同期化） ===
    public async void AddLogAsync(LogLevel level, string message, string? details = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            Details = details
        };

        await SafeInvokeAsync(() =>
        {
            LogEntries.Add(entry);
            // ログテキストは最新100件のみ保持
            if (LogEntries.Count > 100)
            {
                LogEntries.RemoveAt(0);
            }
            LogText = string.Join("\n", LogEntries.TakeLast(50).Select(e => 
                $"[{e.Timestamp:HH:mm:ss}] [{e.Level}] {e.Message}"));
        });

        SaveLogToFile(entry);

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
            File.AppendAllText(logFilePath, logLine + Environment.NewLine, Encoding.UTF8);
        }
        catch { }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        LogEntries.Clear();
        LogText = string.Empty;
        AddLogAsync(LogLevel.Info, "ログをクリアしました");
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        try
        {
            if (Directory.Exists(OutputFolderPath))
            {
                Process.Start("explorer.exe", OutputFolderPath);
            }
        }
        catch { }
    }

    // === 問題2対応: アセットプレビュー ===
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

        try
        {
            IsLoadingPreview = true;
            AssetPreviewTitle = $"読み込み中: {nodeName}...";
            AssetPreviewContent = "解析中...";
            AssetContents.Clear();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(LoadTimeoutSeconds));

            var contents = new List<AssetContentItem>();
            var previewText = new StringBuilder();

            await Task.Run(async () =>
            {
                try
                {
                    if (!File.Exists(nodePath)) return;

                    var fileInfo = new FileInfo(nodePath);
                    previewText.AppendLine($"=== ファイル情報 ===");
                    previewText.AppendLine($"パス: {nodePath}");
                    previewText.AppendLine($"サイズ: {FormatFileSize(fileInfo.Length)}");
                    previewText.AppendLine();

                    if (fileInfo.Length > 100 * 1024 * 1024)
                    {
                        previewText.AppendLine("[情報] ファイルが大きいため、抽出処理で解析してください");
                        return;
                    }

                    var parser = new TextAssetParser();
                    var result = await parser.ParseAsync(nodePath, Options, cts.Token);

                    if (result.Success && result.Assets.Count > 0)
                    {
                        previewText.AppendLine($"=== 抽出テキスト ({result.Assets.Count} 件) ===");
                        previewText.AppendLine();

                        foreach (var asset in result.Assets.Take(30))
                        {
                            cts.Token.ThrowIfCancellationRequested();

                            contents.Add(new AssetContentItem
                            {
                                Name = asset.Name,
                                TypeName = asset.TypeName,
                                Size = asset.TextContent.Sum(t => t.Length),
                                Preview = string.Join(" ", asset.TextContent.Take(2)).Truncate(150)
                            });

                            foreach (var text in asset.TextContent.Take(3))
                            {
                                previewText.AppendLine($"--- [{asset.Name}] ---");
                                previewText.AppendLine(text.Truncate(1500));
                                previewText.AppendLine();
                            }
                        }
                    }
                    else
                    {
                        previewText.AppendLine("[情報] テキストデータが見つかりませんでした");
                    }
                }
                catch (OperationCanceledException)
                {
                    previewText.Clear();
                    previewText.AppendLine("[タイムアウト]");
                }
                catch (Exception ex)
                {
                    previewText.Clear();
                    previewText.AppendLine($"[エラー] {ex.Message}");
                }
            }, cts.Token);

            await SafeInvokeAsync(() =>
            {
                AssetPreviewTitle = $"{nodeName} ({contents.Count} アイテム)";
                AssetPreviewContent = previewText.ToString();
                AssetContents.Clear();
                foreach (var item in contents)
                {
                    AssetContents.Add(item);
                }
            });
        }
        catch (Exception ex)
        {
            AssetPreviewTitle = "エラー";
            AssetPreviewContent = ex.Message;
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

            AddLogAsync(LogLevel.Info, $"フォルダスキャン開始: {path}");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();

            var progress = new Progress<ScanProgress>(p =>
            {
                // UIスレッドブロック防止：頻繁な更新を間引く
                if (p.ProcessedFiles - _lastProgressReport >= 10 || p.ProcessedFiles == p.TotalFiles)
                {
                    _lastProgressReport = p.ProcessedFiles;
                    SafeInvokeSync(() =>
                    {
                        Progress = p.Percentage;
                        ProgressText = $"スキャン: {p.ProcessedFiles}/{p.TotalFiles}";
                    });
                }
            });

            var version = await Task.Run(() => 
                _loader.DetectUnityVersionAsync(path, _cancellationTokenSource.Token));
            UnityVersion = version ?? "不明";

            var stopwatch = Stopwatch.StartNew();
            var rootNode = await Task.Run(() => 
                _loader.ScanDirectoryAsync(path, progress, _cancellationTokenSource.Token));
            stopwatch.Stop();

            // UIスレッドでツリー構築
            await SafeInvokeAsync(() =>
            {
                var viewModel = CreateTreeViewModel(rootNode, null);
                viewModel.IsExpanded = true;
                FileTreeNodes.Clear();
                FileTreeNodes.Add(viewModel);

                HasNoFiles = false;
                CanExtract = true;
                var nodeCount = CountNodes(viewModel);
                StatusText = $"完了: {nodeCount} アイテム ({stopwatch.ElapsedMilliseconds}ms)";
            });

            AddLogAsync(LogLevel.Info, $"スキャン完了: {stopwatch.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException)
        {
            StatusText = "キャンセルされました";
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
            AddLogAsync(LogLevel.Error, $"スキャンエラー: {ex.Message}");
        }
        finally
        {
            IsExtracting = false;
            Progress = 0;
            _lastProgressReport = 0;
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

    // === 問題1修正: 抽出処理の非同期化強化 ===
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
            _lastProgressReport = 0;

            AddLogAsync(LogLevel.Info, "========================================");
            AddLogAsync(LogLevel.Info, $"抽出開始: {_currentPath}");
            AddLogAsync(LogLevel.Info, $"並列度: {Options.MaxDegreeOfParallelism}");

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource = new CancellationTokenSource();

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource.Token);
            linkedCts.CancelAfter(TimeSpan.FromSeconds(ExtractTimeoutSeconds));

            var stopwatch = Stopwatch.StartNew();

            // 問題4修正: 冗長なログを削除、進捗のみ更新
            var progress = new Progress<ExtractionProgress>(p =>
            {
                // 頻繁な更新を間引く（10ファイルごと、または完了時）
                if (p.ProcessedFiles - _lastProgressReport >= 10 || p.ProcessedFiles == p.TotalFiles)
                {
                    _lastProgressReport = p.ProcessedFiles;
                    SafeInvokeSync(() =>
                    {
                        Progress = p.Percentage;
                        ProgressText = $"{p.ProcessedFiles}/{p.TotalFiles}";
                        StatusText = $"抽出中... {p.Percentage:F0}%";
                    });
                }
            });

            var result = await Task.Run(() => 
                _extractor.ExtractFromDirectoryAsync(_currentPath, Options, progress, linkedCts.Token),
                linkedCts.Token);

            stopwatch.Stop();

            // 結果をUIに反映
            await SafeInvokeAsync(() =>
            {
                foreach (var text in result.ExtractedTexts)
                {
                    ExtractedResults.Add(text);
                }
                Statistics = result.Statistics;
                HasResults = result.ExtractedTexts.Count > 0;
                ApplyFilter();
            });

            // 最終結果のみログ出力（問題4対応）
            AddLogAsync(LogLevel.Info, "========================================");
            AddLogAsync(LogLevel.Info, $"抽出完了!");
            AddLogAsync(LogLevel.Info, $"  処理: {result.ProcessedFiles} ファイル");
            AddLogAsync(LogLevel.Info, $"  抽出: {result.TotalExtracted} アイテム");
            AddLogAsync(LogLevel.Info, $"  エラー: {result.Errors.Count} 件");
            AddLogAsync(LogLevel.Info, $"  時間: {stopwatch.Elapsed.TotalSeconds:F1} 秒");

            StatusText = $"完了: {result.TotalExtracted} アイテム ({stopwatch.Elapsed.TotalSeconds:F1}秒)";

            if (HasResults)
            {
                await AutoSaveResultsAsync(result);
            }
            else
            {
                MessageBox.Show("抽出可能なテキストが見つかりませんでした。", "結果なし",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "キャンセル/タイムアウト";
            AddLogAsync(LogLevel.Warning, "抽出がキャンセルまたはタイムアウトしました");
        }
        catch (OutOfMemoryException ex)
        {
            StatusText = "メモリ不足";
            AddLogAsync(LogLevel.Error, $"メモリ不足: {ex.Message}");
            GC.Collect(2, GCCollectionMode.Forced);
            MessageBox.Show("メモリ不足です。並列度を下げて再試行してください。", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
            AddLogAsync(LogLevel.Error, $"抽出エラー: {ex.Message}");
        }
        finally
        {
            IsExtracting = false;
            CanExtract = true;
            Progress = 0;
            _lastProgressReport = 0;
        }
    }

    private async Task AutoSaveResultsAsync(ExtractionResult result)
    {
        try
        {
            EnsureOutputFolderExists();

            var folderName = Path.GetFileName(_currentPath) ?? "extracted";
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"story_{SanitizeFileName(folderName)}_{timestamp}.json";
            var outputPath = Path.Combine(OutputFolderPath, fileName);

            var writer = OutputWriterFactory.Create(OutputFormat.Json);
            await writer.WriteAsync(result, outputPath);

            LastExportedFilePath = outputPath;
            AddLogAsync(LogLevel.Info, $"保存完了: {fileName}");

            MessageBox.Show($"保存しました:\n{outputPath}\n\n抽出: {result.TotalExtracted} アイテム",
                "完了", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            AddLogAsync(LogLevel.Error, $"保存エラー: {ex.Message}");
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
        StatusText = "キャンセル中...";
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!HasResults) return;

        var dialog = new SaveFileDialog
        {
            Title = "抽出結果を保存",
            Filter = "JSON (*.json)|*.json|テキスト (*.txt)|*.txt|CSV (*.csv)|*.csv",
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
                    ".txt" => OutputFormat.Text,
                    ".csv" => OutputFormat.Csv,
                    _ => OutputFormat.Json
                };

                var result = new ExtractionResult
                {
                    SourcePath = _currentPath,
                    UnityVersion = UnityVersion,
                    ExtractedTexts = ExtractedResults.ToList(),
                    Statistics = Statistics,
                    Success = true
                };

                var writer = OutputWriterFactory.Create(format);
                await writer.WriteAsync(result, dialog.FileName);

                MessageBox.Show($"保存しました:\n{dialog.FileName}", "完了",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失敗:\n{ex.Message}", "エラー",
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

        foreach (var item in filtered.Take(500))
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

public static class StringExtensions
{
    public static string Truncate(this string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }
}
