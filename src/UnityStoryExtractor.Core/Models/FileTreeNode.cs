namespace UnityStoryExtractor.Core.Models;

/// <summary>
/// ファイルツリーのノードを表すクラス
/// </summary>
public class FileTreeNode
{
    /// <summary>
    /// ノード名（ファイル名またはディレクトリ名）
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// フルパス
    /// </summary>
    public string FullPath { get; set; } = string.Empty;

    /// <summary>
    /// ディレクトリかどうか
    /// </summary>
    public bool IsDirectory { get; set; }

    /// <summary>
    /// ファイルタイプ
    /// </summary>
    public FileNodeType NodeType { get; set; }

    /// <summary>
    /// ファイルサイズ（バイト）
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// 子ノード
    /// </summary>
    public List<FileTreeNode> Children { get; set; } = new();

    /// <summary>
    /// アセット情報（解析済みの場合）
    /// </summary>
    public List<UnityAssetInfo> Assets { get; set; } = new();

    /// <summary>
    /// 展開済みフラグ（遅延ロード用）
    /// </summary>
    public bool IsExpanded { get; set; }

    /// <summary>
    /// ロード済みフラグ
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// 選択状態
    /// </summary>
    public bool IsSelected { get; set; }

    /// <summary>
    /// 関連する.resSファイルのノード
    /// </summary>
    public FileTreeNode? AssociatedResS { get; set; }

    public override string ToString()
    {
        return IsDirectory ? $"[{Name}]" : Name;
    }
}

/// <summary>
/// ファイルノードタイプの列挙
/// </summary>
public enum FileNodeType
{
    Directory,
    AssetsFile,
    AssetBundle,
    ResourcesAssets,
    ResSFile,
    Assembly,
    GlobalGameManagers,
    Other
}
