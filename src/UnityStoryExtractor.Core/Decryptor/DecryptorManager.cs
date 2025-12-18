using System.Security.Cryptography;
using System.Text;
using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Decryptor;

/// <summary>
/// 復号器管理クラス
/// </summary>
public class DecryptorManager
{
    private readonly List<IDecryptor> _decryptors = new();
    private readonly List<IDecryptorPlugin> _plugins = new();

    public DecryptorManager()
    {
        // デフォルトの復号器を登録
        RegisterDecryptor(new XorDecryptor());
        RegisterDecryptor(new Base64Decryptor());
        RegisterDecryptor(new AesDecryptor());
    }

    /// <summary>
    /// 復号器を登録
    /// </summary>
    public void RegisterDecryptor(IDecryptor decryptor)
    {
        _decryptors.Add(decryptor);
    }

    /// <summary>
    /// プラグインを登録
    /// </summary>
    public void RegisterPlugin(IDecryptorPlugin plugin)
    {
        _plugins.Add(plugin);
        if (plugin.Decryptor != null)
        {
            _decryptors.Add(plugin.Decryptor);
        }
    }

    /// <summary>
    /// プラグインディレクトリからプラグインをロード
    /// </summary>
    public void LoadPlugins(string pluginDirectory)
    {
        if (!Directory.Exists(pluginDirectory)) return;

        // TODO: プラグインの動的ロード実装
    }

    /// <summary>
    /// データの暗号化タイプを検出
    /// </summary>
    public EncryptionDetectionResult DetectEncryption(byte[] data)
    {
        foreach (var decryptor in _decryptors)
        {
            var result = decryptor.Detect(data);
            if (result.IsEncrypted && result.Confidence > 0.7)
            {
                return result;
            }
        }

        return new EncryptionDetectionResult
        {
            IsEncrypted = false,
            Type = EncryptionType.None,
            Confidence = 1.0
        };
    }

    /// <summary>
    /// データを復号（自動検出）
    /// </summary>
    public DecryptionResult TryDecrypt(byte[] data, byte[]? key = null)
    {
        var detection = DetectEncryption(data);
        
        if (!detection.IsEncrypted)
        {
            return new DecryptionResult
            {
                Success = false,
                Message = "暗号化されていないデータです",
                OriginalData = data
            };
        }

        var decryptor = _decryptors.FirstOrDefault(d => d.EncryptionType == detection.Type);
        if (decryptor == null)
        {
            return new DecryptionResult
            {
                Success = false,
                Message = $"対応する復号器がありません: {detection.Type}",
                OriginalData = data,
                DetectedType = detection.Type
            };
        }

        try
        {
            var decrypted = decryptor.Decrypt(data, key);
            return new DecryptionResult
            {
                Success = true,
                DecryptedData = decrypted,
                OriginalData = data,
                DetectedType = detection.Type,
                UsedDecryptor = decryptor.Name
            };
        }
        catch (Exception ex)
        {
            return new DecryptionResult
            {
                Success = false,
                Message = $"復号エラー: {ex.Message}",
                OriginalData = data,
                DetectedType = detection.Type
            };
        }
    }

    /// <summary>
    /// 指定した復号器で復号
    /// </summary>
    public DecryptionResult Decrypt(byte[] data, EncryptionType type, byte[]? key = null)
    {
        var decryptor = _decryptors.FirstOrDefault(d => d.EncryptionType == type);
        if (decryptor == null)
        {
            return new DecryptionResult
            {
                Success = false,
                Message = $"復号器が見つかりません: {type}",
                OriginalData = data
            };
        }

        try
        {
            var decrypted = decryptor.Decrypt(data, key);
            return new DecryptionResult
            {
                Success = true,
                DecryptedData = decrypted,
                OriginalData = data,
                DetectedType = type,
                UsedDecryptor = decryptor.Name
            };
        }
        catch (Exception ex)
        {
            return new DecryptionResult
            {
                Success = false,
                Message = $"復号エラー: {ex.Message}",
                OriginalData = data,
                DetectedType = type
            };
        }
    }
}

/// <summary>
/// 復号結果
/// </summary>
public class DecryptionResult
{
    public bool Success { get; set; }
    public byte[]? DecryptedData { get; set; }
    public byte[]? OriginalData { get; set; }
    public EncryptionType DetectedType { get; set; }
    public string? UsedDecryptor { get; set; }
    public string? Message { get; set; }
}

/// <summary>
/// 復号器プラグインインターフェース
/// </summary>
public interface IDecryptorPlugin
{
    string Name { get; }
    string Version { get; }
    IDecryptor? Decryptor { get; }
}

/// <summary>
/// XOR復号器
/// </summary>
public class XorDecryptor : IDecryptor
{
    public string Name => "XOR";
    public EncryptionType EncryptionType => EncryptionType.XOR;

    public byte[] Decrypt(byte[] data, byte[]? key = null)
    {
        if (key == null || key.Length == 0)
        {
            // キーが指定されていない場合は一般的なキーを試行
            key = DetectXorKey(data);
        }

        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
        {
            result[i] = (byte)(data[i] ^ key[i % key.Length]);
        }
        return result;
    }

    public bool CanDecrypt(byte[] data)
    {
        return Detect(data).IsEncrypted;
    }

    public EncryptionDetectionResult Detect(byte[] data)
    {
        if (data.Length < 16) 
        {
            return new EncryptionDetectionResult { IsEncrypted = false, Confidence = 1.0 };
        }

        // XOR暗号化の特徴を検出
        // - 繰り返しパターン
        // - エントロピーの特定範囲

        var entropy = CalculateEntropy(data);
        bool hasRepetition = HasRepetitivePattern(data);

        // 中程度のエントロピーと繰り返しパターンはXORの特徴
        bool likelyXor = entropy > 4.0 && entropy < 7.5 && hasRepetition;

        return new EncryptionDetectionResult
        {
            IsEncrypted = likelyXor,
            Type = EncryptionType.XOR,
            Confidence = likelyXor ? 0.7 : 0.3,
            Details = new Dictionary<string, object>
            {
                { "Entropy", entropy },
                { "HasRepetition", hasRepetition }
            }
        };
    }

    private byte[] DetectXorKey(byte[] data)
    {
        // 単純なXORキー検出（最も頻出するバイトがスペースまたはNULLと仮定）
        var frequency = new int[256];
        foreach (var b in data)
        {
            frequency[b]++;
        }

        int mostFrequent = Array.IndexOf(frequency, frequency.Max());
        
        // スペース（0x20）またはNULL（0x00）と仮定
        byte assumedPlain = (byte)(mostFrequent == 0 ? 0x00 : 0x20);
        byte key = (byte)(mostFrequent ^ assumedPlain);

        return new[] { key };
    }

    private static double CalculateEntropy(byte[] data)
    {
        var frequency = new int[256];
        foreach (var b in data)
        {
            frequency[b]++;
        }

        double entropy = 0;
        double length = data.Length;

        foreach (var count in frequency)
        {
            if (count > 0)
            {
                double p = count / length;
                entropy -= p * Math.Log2(p);
            }
        }

        return entropy;
    }

    private static bool HasRepetitivePattern(byte[] data)
    {
        // 16バイト単位でパターンを検出
        for (int keyLength = 1; keyLength <= 16; keyLength++)
        {
            int matches = 0;
            int total = 0;

            for (int i = keyLength; i < Math.Min(data.Length, 1024); i++)
            {
                total++;
                if (data[i] == data[i % keyLength])
                {
                    matches++;
                }
            }

            if (total > 0 && (double)matches / total > 0.3)
            {
                return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Base64復号器
/// </summary>
public class Base64Decryptor : IDecryptor
{
    public string Name => "Base64";
    public EncryptionType EncryptionType => EncryptionType.Base64;

    public byte[] Decrypt(byte[] data, byte[]? key = null)
    {
        var str = Encoding.ASCII.GetString(data).Trim();
        return Convert.FromBase64String(str);
    }

    public bool CanDecrypt(byte[] data)
    {
        return Detect(data).IsEncrypted;
    }

    public EncryptionDetectionResult Detect(byte[] data)
    {
        try
        {
            var str = Encoding.ASCII.GetString(data).Trim();
            
            // Base64文字のみで構成されているか
            bool isBase64Chars = str.All(c => 
                char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=' || char.IsWhiteSpace(c));

            // 長さが4の倍数か（パディング込み）
            var strippedLength = str.Replace(" ", "").Replace("\n", "").Replace("\r", "").Length;
            bool validLength = strippedLength % 4 == 0;

            // 末尾のパディング
            bool hasPadding = str.TrimEnd().EndsWith('=');

            bool likelyBase64 = isBase64Chars && validLength && (str.Length > 20);

            return new EncryptionDetectionResult
            {
                IsEncrypted = likelyBase64,
                Type = EncryptionType.Base64,
                Confidence = likelyBase64 ? 0.85 : 0.2,
                Details = new Dictionary<string, object>
                {
                    { "IsBase64Chars", isBase64Chars },
                    { "ValidLength", validLength },
                    { "HasPadding", hasPadding }
                }
            };
        }
        catch
        {
            return new EncryptionDetectionResult { IsEncrypted = false, Confidence = 1.0 };
        }
    }
}

/// <summary>
/// AES復号器
/// </summary>
public class AesDecryptor : IDecryptor
{
    public string Name => "AES";
    public EncryptionType EncryptionType => EncryptionType.AES;

    public byte[] Decrypt(byte[] data, byte[]? key = null)
    {
        if (key == null || key.Length < 16)
        {
            throw new ArgumentException("AES復号にはキーが必要です（16, 24, または 32バイト）");
        }

        using var aes = Aes.Create();
        aes.Key = key.Length switch
        {
            16 => key,
            24 => key,
            32 => key,
            _ => key[..16]
        };

        // IVをデータの先頭から取得（一般的なパターン）
        if (data.Length < 16)
        {
            throw new ArgumentException("データが短すぎます");
        }

        aes.IV = data[..16];
        var encryptedData = data[16..];

        using var decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(encryptedData, 0, encryptedData.Length);
    }

    public bool CanDecrypt(byte[] data)
    {
        return data.Length >= 32 && data.Length % 16 == 0;
    }

    public EncryptionDetectionResult Detect(byte[] data)
    {
        // AES暗号化の特徴
        // - データ長が16の倍数
        // - 高いエントロピー
        // - パターンなし

        bool validLength = data.Length >= 32 && data.Length % 16 == 0;
        double entropy = CalculateEntropy(data);
        bool highEntropy = entropy > 7.5;

        bool likelyAes = validLength && highEntropy;

        return new EncryptionDetectionResult
        {
            IsEncrypted = likelyAes,
            Type = EncryptionType.AES,
            Confidence = likelyAes ? 0.6 : 0.1, // AESは確信度低め（キーが必要）
            Details = new Dictionary<string, object>
            {
                { "ValidLength", validLength },
                { "Entropy", entropy }
            }
        };
    }

    private static double CalculateEntropy(byte[] data)
    {
        var frequency = new int[256];
        foreach (var b in data)
        {
            frequency[b]++;
        }

        double entropy = 0;
        double length = data.Length;

        foreach (var count in frequency)
        {
            if (count > 0)
            {
                double p = count / length;
                entropy -= p * Math.Log2(p);
            }
        }

        return entropy;
    }
}
