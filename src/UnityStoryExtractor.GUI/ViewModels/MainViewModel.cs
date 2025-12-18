using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using UnityStoryExtractor.Core.Extractor;
using UnityStoryExtractor.Core.Loader;
using UnityStoryExtractor.Core.Models;
using UnityStoryExtractor.Core.Output;

namespace UnityStoryExtractor.GUI.ViewModels;

/// <summary>
/// メインウィンドウのViewModel
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IAssetLoader _loader;
    private readonly IStoryExtractor _extractor;
    private CancellationTokenSource? _cancellationTokenSource;

    [ObservableProperty]
    private ObservableCollection<FileTreeNodeViewModel> _fileTreeNodes = new();

    [ObservableProperty]
    private ObservableCollection<ExtractedText> _extractedResults = new();

    [ObservableProperty]
    private ObservableCollection<ExtractedText> _filteredResults = new();

    [ObservableProperty]
    private ExtractedText? _selectedResult;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    private string _statusText = "準備完了";

    [ObservableProperty]
    private string _unityVersion = "不明";

    [ObservableProperty]
    private string _logText = string.Empty;

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

    private string _currentPath = string.Empty;

    public MainViewModel()
    {
        _loader = new UnityAssetLoader();
        _extractor = new StoryExtractor();

        // プロパティ変更時のフィルタリング
        PropertyChanged += OnPropertyChanged;
    }

    private void OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(FilterText) or nameof(SelectedFilterSource))
        {
            ApplyFilter();
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
            FileTreeNodes.Clear();
            LogText = $"[{DateTime.Now:HH:mm:ss}] フォルダを読み込み中: {path}\n";

            var progress = new Progress<ScanProgress>(p =>
            {
                Progress = p.Percentage;
                ProgressText = $"{p.ProcessedFiles}/{p.TotalFiles} ファイル";
            });

            _cancellationTokenSource = new CancellationTokenSource();

            // Unityバージョン検出
            var version = await _loader.DetectUnityVersionAsync(path, _cancellationTokenSource.Token);
            UnityVersion = version ?? "不明";
            LogText += $"[{DateTime.Now:HH:mm:ss}] Unityバージョン: {UnityVersion}\n";

            // ディレクトリスキャン
            var rootNode = await _loader.ScanDirectoryAsync(path, progress, _cancellationTokenSource.Token);
            
            // ViewModelに変換
            var viewModel = new FileTreeNodeViewModel(rootNode);
            viewModel.IsExpanded = true;
            FileTreeNodes.Add(viewModel);

            HasNoFiles = false;
            CanExtract = true;
            StatusText = $"読み込み完了: {CountNodes(viewModel)} アイテム";
            LogText += $"[{DateTime.Now:HH:mm:ss}] スキャン完了\n";
        }
        catch (OperationCanceledException)
        {
            StatusText = "読み込みがキャンセルされました";
            LogText += $"[{DateTime.Now:HH:mm:ss}] キャンセルされました\n";
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
            LogText += $"[{DateTime.Now:HH:mm:ss}] エラー: {ex.Message}\n";
            MessageBox.Show($"フォルダの読み込みに失敗しました:\n{ex.Message}", "エラー", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsExtracting = false;
            Progress = 0;
        }
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
            LogText += $"[{DateTime.Now:HH:mm:ss}] 抽出開始\n";

            _cancellationTokenSource = new CancellationTokenSource();

            var progress = new Progress<ExtractionProgress>(p =>
            {
                Progress = p.Percentage;
                ProgressText = $"{p.ProcessedFiles}/{p.TotalFiles} - {p.CurrentOperation}";
                StatusText = $"抽出中: {Path.GetFileName(p.CurrentFile)}";
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
            LogText += $"[{DateTime.Now:HH:mm:ss}] 抽出完了\n";
            LogText += $"  処理ファイル数: {result.ProcessedFiles}\n";
            LogText += $"  抽出アイテム数: {result.TotalExtracted}\n";
            LogText += $"  処理時間: {result.DurationMs}ms\n";

            foreach (var error in result.Errors)
            {
                LogText += $"  エラー: {error.File} - {error.Message}\n";
            }

            StatusText = $"抽出完了: {result.TotalExtracted} アイテム";
        }
        catch (OperationCanceledException)
        {
            StatusText = "抽出がキャンセルされました";
            LogText += $"[{DateTime.Now:HH:mm:ss}] キャンセルされました\n";
        }
        catch (Exception ex)
        {
            StatusText = $"エラー: {ex.Message}";
            LogText += $"[{DateTime.Now:HH:mm:ss}] エラー: {ex.Message}\n";
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

    [RelayCommand]
    private void Cancel()
    {
        _cancellationTokenSource?.Cancel();
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
            FileName = $"extracted_{DateTime.Now:yyyyMMdd_HHmmss}"
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

                StatusText = $"保存完了: {dialog.FileName}";
                LogText += $"[{DateTime.Now:HH:mm:ss}] ファイル保存: {dialog.FileName}\n";

                MessageBox.Show($"ファイルを保存しました:\n{dialog.FileName}", "保存完了", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存に失敗しました:\n{ex.Message}", "エラー", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    [RelayCommand]
    private void Settings()
    {
        // 設定ダイアログを表示（将来の実装）
        MessageBox.Show("設定機能は今後実装予定です。", "設定", 
            MessageBoxButton.OK, MessageBoxImage.Information);
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
/// ファイルツリーノードのViewModel
/// </summary>
public class FileTreeNodeViewModel : INotifyPropertyChanged
{
    private readonly FileTreeNode _model;
    private bool _isExpanded;
    private bool _isSelected;
    private ObservableCollection<FileTreeNodeViewModel>? _children;

    public FileTreeNodeViewModel(FileTreeNode model)
    {
        _model = model;
    }

    public string Name => _model.Name;
    public string FullPath => _model.FullPath;
    public bool IsDirectory => _model.IsDirectory;
    public FileNodeType NodeType => _model.NodeType;
    public long FileSize => _model.FileSize;

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
                    _model.Children.Select(c => new FileTreeNodeViewModel(c)));
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
