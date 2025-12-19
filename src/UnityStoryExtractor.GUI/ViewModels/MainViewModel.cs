using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
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
/// メインウィンドウのViewModel
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IAssetLoader _loader;
    private readonly IStoryExtractor _extractor;
    private CancellationTokenSource? _cancellationTokenSource;

    // パーサーをキャッシュ（プレビュー用）
    private readonly TextAssetParser _textAssetParser = new();
    private readonly MonoBehaviourParser _monoBehaviourParser = new();
    private readonly AssemblyParser _assemblyParser = new();

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

    // === 問題1: 出力先フォルダ関連 ===
    [ObservableProperty]
    private string _outputFolderPath = string.Empty;

    [ObservableProperty]
    private string _lastExportedFilePath = string.Empty;

    // === 問題2: アセットプレビュー関連 ===
    [ObservableProperty]
    private string _assetPreviewContent = string.Empty;

    [ObservableProperty]
    private string _assetPreviewTitle = "アセットを選択してください";

    [ObservableProperty]
    private bool _isLoadingPreview;

    [ObservableProperty]
    private ObservableCollection<AssetContentItem> _assetContents = new();

    // === 問題3: ログ機能強化 ===
    [ObservableProperty]
    private ObservableCollection<LogEntry> _logEntries = new();

    [ObservableProperty]
    private string _logText = string.Empty;

    // === 問題4: メモリ監視 ===
    [ObservableProperty]
    private string _memoryUsage = "0 MB";

    private string _currentPath = string.Empty;
    private System.Timers.Timer? _memoryMonitorTimer;

    public MainViewModel()
    {
        _loader = new UnityAssetLoader();
        _extractor = new StoryExtractor();

        // 出力フォルダのデフォルトパスを設定
        InitializeOutputFolder();

        // プロパティ変更時のフィルタリング
        PropertyChanged += OnPropertyChanged;

        // メモリ監視タイマー開始
        StartMemoryMonitor();
    }

    private void InitializeOutputFolder()
    {
        // アプリケーション実行フォルダ/Output をデフォルトに
        var appFolder = AppDomain.CurrentDomain.BaseDirectory;
        OutputFolderPath = Path.Combine(appFolder, "Output");

        // フォルダがなければ作成
        if (!Directory.Exists(OutputFolderPath))
        {
            Directory.CreateDirectory(OutputFolderPath);
            AddLog(LogLevel.Info, $"出力フォルダを作成しました: {OutputFolderPath}");
        }
    }

    private void StartMemoryMonitor()
    {
        _memoryMonitorTimer = new System.Timers.Timer(1000);
        _memoryMonitorTimer.Elapsed += (s, e) =>
        {
            var memoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                MemoryUsage = $"{memoryMB:F1} MB";

                // 2GB超えたらGC強制実行
                if (memoryMB > 2000)
                {
                    AddLog(LogLevel.Warning, "メモリ使用量が2GBを超えました。ガベージコレクションを実行します。");
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
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

    // === ログ機能（問題3） ===
    public void AddLog(LogLevel level, string message, string? details = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTime.Now,
            Level = level,
            Message = message,
            Details = details
        };

        Application.Current?.Dispatcher?.Invoke(() =>
        {
            LogEntries.Add(entry);
            LogText += $"[{entry.Timestamp:HH:mm:ss}] [{entry.Level}] {message}\n";
            if (!string.IsNullOrEmpty(details))
            {
                LogText += $"  詳細: {details}\n";
            }

            // ログをファイルにも保存
            SaveLogToFile(entry);
        });
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
            // ログファイル書き込みエラーは無視
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
        if (Directory.Exists(OutputFolderPath))
        {
            System.Diagnostics.Process.Start("explorer.exe", OutputFolderPath);
        }
        else
        {
            MessageBox.Show("出力フォルダが存在しません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // === アセットプレビュー機能（問題2） ===
    private async Task LoadAssetPreviewAsync()
    {
        if (SelectedTreeNode == null || SelectedTreeNode.IsDirectory)
        {
            AssetPreviewTitle = "アセットを選択してください";
            AssetPreviewContent = string.Empty;
            AssetContents.Clear();
            return;
        }

        try
        {
            IsLoadingPreview = true;
            AssetPreviewTitle = $"読み込み中: {SelectedTreeNode.Name}";
            AssetPreviewContent = string.Empty;
            AssetContents.Clear();

            AddLog(LogLevel.Info, $"アセットプレビュー読み込み開始: {SelectedTreeNode.Name}");

            var nodeType = SelectedTreeNode.NodeType;
            var filePath = SelectedTreeNode.FullPath;

            await Task.Run(async () =>
            {
                var contents = new List<AssetContentItem>();
                var previewText = new StringBuilder();

                try
                {
                    switch (nodeType)
                    {
                        case FileNodeType.AssetsFile:
                        case FileNodeType.ResourcesAssets:
                        case FileNodeType.AssetBundle:
                            var textResult = await _textAssetParser.ParseAsync(filePath, Options, CancellationToken.None);
                            foreach (var asset in textResult.Assets)
                            {
                                contents.Add(new AssetContentItem
                                {
                                    Name = asset.Name,
                                    TypeName = asset.TypeName,
                                    Size = asset.TextContent.Sum(t => t.Length),
                                    Preview = string.Join("\n", asset.TextContent.Take(3))
                                });

                                foreach (var text in asset.TextContent.Take(10))
                                {
                                    previewText.AppendLine($"--- {asset.Name} ({asset.TypeName}) ---");
                                    previewText.AppendLine(text.Length > 1000 ? text[..1000] + "..." : text);
                                    previewText.AppendLine();
                                }
                            }
                            break;

                        case FileNodeType.Assembly:
                            var asmResult = await _assemblyParser.ParseAsync(filePath, Options, CancellationToken.None);
                            foreach (var asset in asmResult.Assets)
                            {
                                contents.Add(new AssetContentItem
                                {
                                    Name = asset.Name,
                                    TypeName = "Assembly String",
                                    Size = asset.TextContent.Sum(t => t.Length),
                                    Preview = string.Join("; ", asset.TextContent.Take(5))
                                });

                                previewText.AppendLine($"=== {asset.Name} ===");
                                foreach (var text in asset.TextContent.Take(50))
                                {
                                    previewText.AppendLine($"  • {text}");
                                }
                            }
                            break;

                        default:
                            // バイナリファイルとして読み込み
                            if (File.Exists(filePath))
                            {
                                var bytes = await File.ReadAllBytesAsync(filePath);
                                var textContent = TryDecodeText(bytes);
                                if (!string.IsNullOrWhiteSpace(textContent))
                                {
                                    previewText.AppendLine(textContent.Length > 5000 ? textContent[..5000] + "..." : textContent);
                                    contents.Add(new AssetContentItem
                                    {
                                        Name = Path.GetFileName(filePath),
                                        TypeName = "Binary/Text",
                                        Size = bytes.Length,
                                        Preview = textContent.Length > 100 ? textContent[..100] + "..." : textContent
                                    });
                                }
                                else
                                {
                                    previewText.AppendLine($"[バイナリデータ: {bytes.Length:N0} bytes]");
                                    previewText.AppendLine(BitConverter.ToString(bytes.Take(256).ToArray()).Replace("-", " "));
                                }
                            }
                            break;
                    }
                }
                catch (Exception ex)
                {
                    previewText.AppendLine($"[エラー] アセット解析に失敗: {ex.Message}");
                }

                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    AssetPreviewTitle = $"{SelectedTreeNode?.Name} ({contents.Count} アイテム)";
                    AssetPreviewContent = previewText.ToString();
                    foreach (var item in contents)
                    {
                        AssetContents.Add(item);
                    }
                });
            });

            AddLog(LogLevel.Info, $"アセットプレビュー読み込み完了: {AssetContents.Count} アイテム");
        }
        catch (Exception ex)
        {
            AssetPreviewTitle = "エラー";
            AssetPreviewContent = $"プレビューの読み込みに失敗しました:\n{ex.Message}";
            AddLog(LogLevel.Error, $"プレビュー読み込みエラー: {ex.Message}");
        }
        finally
        {
            IsLoadingPreview = false;
        }
    }

    private static string TryDecodeText(byte[] bytes)
    {
        // BOMチェック
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        // UTF-8として試行
        try
        {
            var text = Encoding.UTF8.GetString(bytes);
            // テキストとして妥当かチェック
            var printableRatio = text.Count(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t') / (double)text.Length;
            if (printableRatio > 0.8)
                return text;
        }
        catch { }

        // Shift-JIS試行
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sjis = Encoding.GetEncoding("shift_jis");
            var text = sjis.GetString(bytes);
            var printableRatio = text.Count(c => !char.IsControl(c) || c == '\n' || c == '\r' || c == '\t') / (double)text.Length;
            if (printableRatio > 0.8)
                return text;
        }
        catch { }

        return string.Empty;
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
            FileTreeNodes.Clear();
            AddLog(LogLevel.Info, $"フォルダを読み込み開始: {path}");

            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                ProgressText = $"{p.ProcessedFiles}/{p.TotalFiles} ファイル";
            });

            _cancellationTokenSource = new CancellationTokenSource();

            // Unityバージョン検出
            var version = await _loader.DetectUnityVersionAsync(path, _cancellationTokenSource.Token);
            UnityVersion = version ?? "不明";
            AddLog(LogLevel.Info, $"Unityバージョン検出: {UnityVersion}");

            // ディレクトリスキャン
            var rootNode = await _loader.ScanDirectoryAsync(path, progress, _cancellationTokenSource.Token);

            // ViewModelに変換
            var viewModel = CreateTreeViewModel(rootNode, null);
            viewModel.IsExpanded = true;
            FileTreeNodes.Add(viewModel);

            HasNoFiles = false;
            CanExtract = true;
            var nodeCount = CountNodes(viewModel);
            StatusText = $"読み込み完了: {nodeCount} アイテム";
            AddLog(LogLevel.Info, $"スキャン完了: {nodeCount} アイテム発見");
        }
        catch (OperationCanceledException)
        {
            StatusText = "読み込みがキャンセルされました";
            AddLog(LogLevel.Warning, "読み込みがキャンセルされました");
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
            AddLog(LogLevel.Error, $"フォルダ読み込みエラー: {ex.Message}", ex.StackTrace);
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

    [RelayCommand]
    private async Task ExtractAsync()
    {
        if (string.IsNullOrEmpty(_currentPath) || FileTreeNodes.Count == 0)
            return;

        try
        {
            IsExtracting = true;
            CanExtract = false;
            ExtractedResults.Clear();
            FilteredResults.Clear();
            AddLog(LogLevel.Info, "抽出処理を開始しました");

            _cancellationTokenSource = new CancellationTokenSource();

            var progress = new Progress<ExtractionProgress>(p =>
            {
                Progress = p.Percentage;
                ProgressText = $"{p.ProcessedFiles}/{p.TotalFiles} - {p.CurrentOperation}";
                StatusText = $"抽出中: {Path.GetFileName(p.CurrentFile)}";

                // 定期的にログ出力
                if (p.ProcessedFiles % 10 == 0)
                {
                    AddLog(LogLevel.Info, $"処理中... {p.ProcessedFiles}/{p.TotalFiles} ファイル完了");
                }
            });

            var result = await _extractor.ExtractFromDirectoryAsync(
                _currentPath,
                Options,
                progress,
                _cancellationTokenSource.Token);

            // 結果を表示
            foreach (var text in result.ExtractedTexts)
            {
                ExtractedResults.Add(text);
            }

            Statistics = result.Statistics;
            HasResults = result.ExtractedTexts.Count > 0;

            ApplyFilter();

            // ログ更新
            AddLog(LogLevel.Info, $"抽出完了: {result.TotalExtracted} アイテム抽出");
            AddLog(LogLevel.Info, $"処理ファイル数: {result.ProcessedFiles}, 処理時間: {result.DurationMs}ms");

            foreach (var error in result.Errors)
            {
                AddLog(LogLevel.Error, $"抽出エラー: {error.File}", error.Message);
            }

            foreach (var warning in result.Warnings)
            {
                AddLog(LogLevel.Warning, warning);
            }

            StatusText = $"抽出完了: {result.TotalExtracted} アイテム";

            // 自動保存オプション: Outputフォルダに自動保存
            if (HasResults)
            {
                await AutoSaveResultsAsync(result);
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "抽出がキャンセルされました";
            AddLog(LogLevel.Warning, "抽出がキャンセルされました");
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
            AddLog(LogLevel.Error, $"抽出エラー: {ex.Message}", ex.StackTrace);
            MessageBox.Show($"抽出に失敗しました:\n{ex.Message}", "エラー",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsExtracting = false;
            CanExtract = true;
            Progress = 0;
        }
    }

    // === 問題1: 自動保存機能 ===
    private async Task AutoSaveResultsAsync(ExtractionResult result)
    {
        try
        {
            // Outputフォルダ確認・作成
            if (!Directory.Exists(OutputFolderPath))
            {
                Directory.CreateDirectory(OutputFolderPath);
            }

            // ファイル名生成（入力フォルダ名＋タイムスタンプ）
            var folderName = Path.GetFileName(_currentPath) ?? "extracted";
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var fileName = $"story_from_{SanitizeFileName(folderName)}_{timestamp}.json";
            var outputPath = Path.Combine(OutputFolderPath, fileName);

            // 同名ファイルが存在する場合の確認
            if (File.Exists(outputPath))
            {
                var msgResult = MessageBox.Show(
                    $"ファイルが既に存在します:\n{outputPath}\n\n上書きしますか？",
                    "上書き確認",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (msgResult != MessageBoxResult.Yes)
                {
                    AddLog(LogLevel.Info, "自動保存がキャンセルされました");
                    return;
                }
            }

            // JSON形式で保存
            var writer = OutputWriterFactory.Create(OutputFormat.Json);
            await writer.WriteAsync(result, outputPath);

            LastExportedFilePath = outputPath;
            AddLog(LogLevel.Info, $"自動保存完了: {outputPath}");
            StatusText = $"保存完了: {fileName}";

            // ユーザーに通知
            MessageBox.Show(
                $"抽出結果を自動保存しました:\n{outputPath}\n\n抽出アイテム数: {result.TotalExtracted}",
                "自動保存完了",
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

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
        AddLog(LogLevel.Warning, "キャンセルが要求されました");
    }

    [RelayCommand]
    private async Task ExportAsync()
    {
        if (!HasResults) return;

        var dialog = new SaveFileDialog
        {
            Title = "抽出結果を保存",
            Filter = "JSON ファイル (*.json)|*.json|テキスト ファイル (*.txt)|*.txt|CSV ファイル (*.csv)|*.csv|XML ファイル (*.xml)|*.xml",
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
                AddLog(LogLevel.Info, $"ファイル保存完了: {dialog.FileName}");

                MessageBox.Show($"ファイルを保存しました:\n{dialog.FileName}", "保存完了",
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
        // 設定ダイアログを表示
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

        // テキストフィルタ
        if (!string.IsNullOrWhiteSpace(FilterText))
        {
            var filter = FilterText.ToLowerInvariant();
            filtered = filtered.Where(r =>
                r.Content.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                r.AssetName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }

        // ソースフィルタ
        if (SelectedFilterSource != "すべて")
        {
            filtered = filtered.Where(r => r.Source.ToString() == SelectedFilterSource);
        }

        foreach (var item in filtered)
        {
            FilteredResults.Add(item);
        }
    }

    private static int CountNodes(FileTreeNodeViewModel node)
    {
        return 1 + node.Children.Sum(CountNodes);
    }
}

/// <summary>
/// ログレベル
/// </summary>
public enum LogLevel
{
    Info,
    Warning,
    Error
}

/// <summary>
/// ログエントリ
/// </summary>
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

/// <summary>
/// アセット内容アイテム
/// </summary>
public class AssetContentItem
{
    public string Name { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public int Size { get; set; }
    public string Preview { get; set; } = string.Empty;
}

/// <summary>
/// ファイルツリーノードのViewModel
/// </summary>
public class FileTreeNodeViewModel : INotifyPropertyChanged
{
    private readonly FileTreeNode _model;
    private readonly FileTreeNodeViewModel? _parent;
    private bool _isExpanded;
    private bool _isSelected;
    private ObservableCollection<FileTreeNodeViewModel>? _children;

    public FileTreeNodeViewModel(FileTreeNode model, FileTreeNodeViewModel? parent = null)
    {
        _model = model;
        _parent = parent;
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
        set
        {
            if (_isExpanded != value)
            {
                _isExpanded = value;
                OnPropertyChanged();
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected != value)
            {
                _isSelected = value;
                OnPropertyChanged();
            }
        }
    }

    public ObservableCollection<FileTreeNodeViewModel> Children
    {
        get
        {
            if (_children == null)
            {
                _children = new ObservableCollection<FileTreeNodeViewModel>(
                    _model.Children.Select(c => new FileTreeNodeViewModel(c, this)));
            }
            return _children;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
