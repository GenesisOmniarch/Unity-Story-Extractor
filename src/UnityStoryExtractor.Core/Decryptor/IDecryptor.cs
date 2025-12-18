using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Decryptor;

/// <summary>
/// 復号器のインターフェース
/// </summary>
public interface IDecryptor
{
    /// <summary>
    /// 復号器の名前
    /// </summary>
    string Name { get; }

    /// <summary>
    /// 対応する暗号化タイプ
    /// </summary>
    EncryptionType EncryptionType { get; }

    /// <summary>
    /// データを復号
    /// </summary>
    byte[] Decrypt(byte[] data, byte[]? key = null);

    /// <summary>
    /// このデータが復号可能か判定
    /// </summary>
    bool CanDecrypt(byte[] data);

    /// <summary>
    /// 暗号化を検出
    /// </summary>
    EncryptionDetectionResult Detect(byte[] data);
}

/// <summary>
/// 暗号化検出結果
/// </summary>
public class EncryptionDetectionResult
{
    public bool IsEncrypted { get; set; }
    public EncryptionType Type { get; set; }
    public double Confidence { get; set; }
    public Dictionary<string, object> Details { get; set; } = new();
}
