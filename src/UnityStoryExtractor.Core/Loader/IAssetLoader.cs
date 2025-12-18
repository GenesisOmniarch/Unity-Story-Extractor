using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Loader;

/// <summary>
/// アセットローダーのインターフェース
/// </summary>
public interface IAssetLoader
{
    /// <summary>
    /// ディレクトリをスキャンしてファイルツリーを構築
    /// </summary>
    Task<FileTreeNode> ScanDirectoryAsync(string path, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// 単一ファイルをロード
    /// </summary>
    Task<LoadedAssetFile> LoadFileAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// アセットファイルからアセット一覧を取得
    /// </summary>
    Task<List<UnityAssetInfo>> GetAssetsAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unityバージョンを検出
    /// </summary>
    Task<string?> DetectUnityVersionAsync(string dataFolderPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// .resSファイルを関連付け
    /// </summary>
    void LinkResSFiles(FileTreeNode rootNode);

    /// <summary>
    /// サポートされているファイルかどうかを判定
    /// </summary>
    bool IsSupportedFile(string path);
}

/// <summary>
/// スキャン進捗情報
/// </summary>
public class ScanProgress
{
    public int TotalFiles { get; set; }
    public int ProcessedFiles { get; set; }
    public string CurrentFile { get; set; } = string.Empty;
    public double Percentage => TotalFiles > 0 ? (double)ProcessedFiles / TotalFiles * 100 : 0;
}

/// <summary>
/// ロードされたアセットファイル
/// </summary>
public class LoadedAssetFile : IDisposable
{
    public string FilePath { get; set; } = string.Empty;
    public FileNodeType FileType { get; set; }
    public string? UnityVersion { get; set; }
    public List<UnityAssetInfo> Assets { get; set; } = new();
    public Stream? DataStream { get; set; }
    public byte[]? RawData { get; set; }
    public bool IsLoaded { get; set; }
    public string? Error { get; set; }

    public void Dispose()
    {
        DataStream?.Dispose();
        RawData = null;
        GC.SuppressFinalize(this);
    }
}
