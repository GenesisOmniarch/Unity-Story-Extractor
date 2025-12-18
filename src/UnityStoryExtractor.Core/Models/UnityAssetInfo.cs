namespace UnityStoryExtractor.Core.Models;

/// <summary>
/// Unityアセットの情報を表すクラス
/// </summary>
public class UnityAssetInfo
{
    /// <summary>
    /// アセットの一意識別子
    /// </summary>
    public long PathId { get; set; }

    /// <summary>
    /// アセット名
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// アセットタイプ（TextAsset, MonoBehaviour など）
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// アセットのタイプID
    /// </summary>
    public int TypeId { get; set; }

    /// <summary>
    /// ファイル内のオフセット
    /// </summary>
    public long Offset { get; set; }

    /// <summary>
    /// データサイズ
    /// </summary>
    public long Size { get; set; }

    /// <summary>
    /// 親ファイルのパス
    /// </summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>
    /// 関連する.resSファイルのパス（存在する場合）
    /// </summary>
    public string? ResSFilePath { get; set; }

    /// <summary>
    /// 暗号化されているかどうか
    /// </summary>
    public bool IsEncrypted { get; set; }

    /// <summary>
    /// 検出された暗号化タイプ
    /// </summary>
    public EncryptionType EncryptionType { get; set; } = EncryptionType.None;

    public override string ToString()
    {
        return $"{Name} ({TypeName}) - {Size} bytes";
    }
}

/// <summary>
/// 暗号化タイプの列挙
/// </summary>
public enum EncryptionType
{
    None,
    XOR,
    AES,
    Base64,
    Custom,
    Unknown
}
