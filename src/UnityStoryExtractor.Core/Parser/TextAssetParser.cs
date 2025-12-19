using System.Text;
using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Parser;

/// <summary>
/// TextAssetパーサー - 文字化け修正・全テキスト形式対応版
/// </summary>
public class TextAssetParser : IAssetParser
{
    // 問題5対応: 対応ファイル形式を拡張
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Unity関連
        ".assets", ".bundle", ".unity3d", ".ab", ".ress", ".asset",
        // テキスト系
        ".txt", ".json", ".xml", ".yaml", ".yml", ".csv", ".tsv",
        ".lua", ".py", ".js", ".cs", ".html", ".htm", ".md",
        // ゲーム固有
        ".bytes", ".prefab", ".scene", ".meta", ".manifest",
        // バイナリ内テキスト検索対象
        ".dll", ".so", ".dat", ".bin"
    };

    public IEnumerable<FileNodeType> SupportedTypes => new[] 
    { 
        FileNodeType.AssetsFile, 
        FileNodeType.ResourcesAssets,
        FileNodeType.AssetBundle,
        FileNodeType.ResSFile,
        FileNodeType.Other  // 問題5: その他のファイルも対応
    };

    public async Task<ParseResult> ParseAsync(string filePath, ExtractionOptions options, CancellationToken cancellationToken = default)
    {
        var result = new ParseResult { SourcePath = filePath };

        try
        {
            if (!File.Exists(filePath))
            {
                result.Errors.Add($"ファイルが見つかりません: {filePath}");
                return result;
            }

            var fileInfo = new FileInfo(filePath);
            
            // 問題5: 対応拡張子チェック
            var ext = Path.GetExtension(filePath).ToLowerInvariant();
            if (!TextExtensions.Contains(ext) && fileInfo.Length > 50 * 1024 * 1024)
            {
                // 未知の拡張子で50MB以上はスキップ
                return result;
            }

            if (fileInfo.Length > options.StreamingChunkSize)
            {
                using var stream = File.OpenRead(filePath);
                return await ParseStreamAsync(stream, filePath, options, cancellationToken);
            }

            var data = await File.ReadAllBytesAsync(filePath, cancellationToken);
            return await ParseBinaryAsync(data, filePath, options, cancellationToken);
        }
        catch (Exception ex)
        {
            result.Errors.Add($"解析エラー: {ex.Message}");
        }

        return result;
    }

    public async Task<ParseResult> ParseBinaryAsync(byte[] data, string sourcePath, ExtractionOptions options, CancellationToken cancellationToken = default)
    {
        var result = new ParseResult 
        { 
            SourcePath = sourcePath,
            Success = true 
        };

        await Task.Run(() =>
        {
            try
            {
                var textAssets = FindTextAssets(data, sourcePath, options, cancellationToken);
                result.Assets.AddRange(textAssets);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"TextAsset解析エラー: {ex.Message}");
            }
        }, cancellationToken);

        return result;
    }

    public async Task<ParseResult> ParseStreamAsync(Stream stream, string sourcePath, ExtractionOptions options, CancellationToken cancellationToken = default)
    {
        var result = new ParseResult 
        { 
            SourcePath = sourcePath,
            Success = true 
        };

        await Task.Run(() =>
        {
            try
            {
                var buffer = new byte[options.StreamingChunkSize];
                int assetIndex = 0;
                int bytesRead;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var textAssets = FindTextAssets(buffer[..bytesRead], sourcePath, options, cancellationToken);
                    foreach (var asset in textAssets)
                    {
                        asset.Name = $"TextAsset_{assetIndex++}";
                        result.Assets.Add(asset);
                    }

                    // 最大アセット数制限
                    if (result.Assets.Count > 5000) break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"ストリーム解析エラー: {ex.Message}");
            }
        }, cancellationToken);

        return result;
    }

    private List<ParsedAsset> FindTextAssets(byte[] data, string sourcePath, ExtractionOptions options, CancellationToken cancellationToken)
    {
        var assets = new List<ParsedAsset>();

        // 問題3修正: エンコーディング検出を改善
        var textChunks = FindTextChunks(data, options, cancellationToken);
        
        int index = 0;
        foreach (var chunk in textChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(chunk.Text))
                continue;

            if (chunk.Text.Length < options.MinTextLength)
                continue;

            if (chunk.Text.Length > options.MaxTextLength)
                continue;

            // キーワードフィルタ
            if (options.Keywords.Count > 0)
            {
                bool matches = options.Keywords.Any(k => 
                    chunk.Text.Contains(k, StringComparison.OrdinalIgnoreCase));
                if (!matches) continue;
            }

            var asset = new ParsedAsset
            {
                PathId = index++,
                Name = $"Text_{index}",
                TypeName = "TextAsset",
                TextContent = new List<string> { chunk.Text },
                Properties = new Dictionary<string, object>
                {
                    { "Encoding", chunk.Encoding },
                    { "Offset", chunk.Offset },
                    { "Length", chunk.Length }
                }
            };

            assets.Add(asset);

            // 最大数制限
            if (assets.Count >= 2000) break;
        }

        return assets;
    }

    private List<TextChunk> FindTextChunks(byte[] data, ExtractionOptions options, CancellationToken cancellationToken)
    {
        var chunks = new List<TextChunk>();

        // 問題3修正: まずファイル全体のエンコーディングを検出
        var detectedEncoding = DetectEncoding(data);
        
        // 検出したエンコーディングで全体を試行
        var wholeText = TryDecodeWhole(data, detectedEncoding);
        if (!string.IsNullOrEmpty(wholeText) && IsValidText(wholeText) && wholeText.Length >= options.MinTextLength)
        {
            // JSON/XML/YAMLなど構造化テキストの場合は全体を返す
            if (LooksLikeStructuredText(wholeText))
            {
                chunks.Add(new TextChunk
                {
                    Text = wholeText,
                    Encoding = detectedEncoding.EncodingName,
                    Offset = 0,
                    Length = data.Length
                });
                return chunks;
            }
        }

        // UTF-8テキストを検索
        var utf8Chunks = FindUtf8Strings(data, options.MinTextLength, cancellationToken);
        chunks.AddRange(utf8Chunks);

        // UTF-16テキストを検索（日本語など）
        var utf16Chunks = FindUtf16Strings(data, options.MinTextLength, cancellationToken);
        chunks.AddRange(utf16Chunks);

        // Shift-JIS検索（日本語ゲーム用）
        if (options.PrioritizeJapaneseText)
        {
            var sjisChunks = FindShiftJisStrings(data, options.MinTextLength, cancellationToken);
            chunks.AddRange(sjisChunks);
        }

        // 日本語優先の場合、日本語テキストを先頭に
        if (options.PrioritizeJapaneseText)
        {
            chunks = chunks
                .OrderByDescending(c => ContainsJapanese(c.Text))
                .ThenByDescending(c => c.Text.Length)
                .ToList();
        }

        // 重複除去
        chunks = chunks
            .GroupBy(c => c.Text.Trim())
            .Select(g => g.First())
            .ToList();

        return chunks;
    }

    /// <summary>
    /// 問題3修正: エンコーディング検出の改善
    /// </summary>
    private static Encoding DetectEncoding(byte[] data)
    {
        if (data.Length < 2) return Encoding.UTF8;

        // BOMチェック
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
            return Encoding.UTF8;
        
        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
            return Encoding.Unicode; // UTF-16 LE
        
        if (data.Length >= 2 && data[0] == 0xFE && data[1] == 0xFF)
            return Encoding.BigEndianUnicode; // UTF-16 BE
        
        if (data.Length >= 4 && data[0] == 0x00 && data[1] == 0x00 && data[2] == 0xFE && data[3] == 0xFF)
            return Encoding.UTF32;

        // BOMなし：UTF-8を試行
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            utf8.GetString(data);
            
            // ASCII以外の文字が含まれていればUTF-8と判定
            if (data.Any(b => b > 0x7F))
            {
                // 有効なUTF-8マルチバイトシーケンスがあるか確認
                int i = 0;
                while (i < data.Length)
                {
                    if (data[i] < 0x80) { i++; continue; }
                    if ((data[i] & 0xE0) == 0xC0 && i + 1 < data.Length && (data[i+1] & 0xC0) == 0x80) { i += 2; continue; }
                    if ((data[i] & 0xF0) == 0xE0 && i + 2 < data.Length && (data[i+1] & 0xC0) == 0x80 && (data[i+2] & 0xC0) == 0x80) { i += 3; continue; }
                    if ((data[i] & 0xF8) == 0xF0 && i + 3 < data.Length && (data[i+1] & 0xC0) == 0x80 && (data[i+2] & 0xC0) == 0x80 && (data[i+3] & 0xC0) == 0x80) { i += 4; continue; }
                    break;
                }
                if (i == data.Length) return Encoding.UTF8;
            }
            else
            {
                // ASCII範囲内のみならUTF-8
                return Encoding.UTF8;
            }
        }
        catch { }

        // Shift-JISを試行（日本語ゲーム対応）
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sjis = Encoding.GetEncoding("shift_jis");
            var text = sjis.GetString(data);
            
            // 日本語文字が含まれていればShift-JIS
            if (ContainsJapanese(text))
            {
                return sjis;
            }
        }
        catch { }

        return Encoding.UTF8;
    }

    private static string TryDecodeWhole(byte[] data, Encoding encoding)
    {
        try
        {
            // BOMをスキップ
            int offset = 0;
            if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF)
                offset = 3;
            else if (data.Length >= 2 && ((data[0] == 0xFF && data[1] == 0xFE) || (data[0] == 0xFE && data[1] == 0xFF)))
                offset = 2;

            return encoding.GetString(data, offset, data.Length - offset);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool LooksLikeStructuredText(string text)
    {
        var trimmed = text.TrimStart();
        return trimmed.StartsWith("{") || trimmed.StartsWith("[") || 
               trimmed.StartsWith("<") || trimmed.StartsWith("---");
    }

    /// <summary>
    /// Shift-JIS文字列検索（日本語ゲーム対応）
    /// </summary>
    private List<TextChunk> FindShiftJisStrings(byte[] data, int minLength, CancellationToken cancellationToken)
    {
        var chunks = new List<TextChunk>();

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var sjis = Encoding.GetEncoding("shift_jis");

            int startIndex = -1;
            var currentBytes = new List<byte>();

            for (int i = 0; i < data.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                byte b = data[i];

                // Shift-JIS範囲チェック
                bool isValidSjis = false;
                if (b >= 0x20 && b <= 0x7E) // ASCII印刷可能
                {
                    isValidSjis = true;
                }
                else if (b == 0x09 || b == 0x0A || b == 0x0D) // 制御文字
                {
                    isValidSjis = true;
                }
                else if ((b >= 0x81 && b <= 0x9F) || (b >= 0xE0 && b <= 0xFC)) // 2バイト文字先頭
                {
                    if (i + 1 < data.Length)
                    {
                        byte b2 = data[i + 1];
                        if ((b2 >= 0x40 && b2 <= 0x7E) || (b2 >= 0x80 && b2 <= 0xFC))
                        {
                            currentBytes.Add(b);
                            currentBytes.Add(b2);
                            if (startIndex == -1) startIndex = i;
                            i++;
                            continue;
                        }
                    }
                }
                else if (b >= 0xA1 && b <= 0xDF) // 半角カナ
                {
                    isValidSjis = true;
                }

                if (isValidSjis)
                {
                    if (startIndex == -1) startIndex = i;
                    currentBytes.Add(b);
                }
                else if (currentBytes.Count > 0)
                {
                    ProcessSjisChunk(currentBytes, startIndex, sjis, minLength, chunks);
                    currentBytes.Clear();
                    startIndex = -1;
                }
            }

            if (currentBytes.Count > 0)
            {
                ProcessSjisChunk(currentBytes, startIndex, sjis, minLength, chunks);
            }
        }
        catch { }

        return chunks;
    }

    private static void ProcessSjisChunk(List<byte> bytes, int startIndex, Encoding sjis, int minLength, List<TextChunk> chunks)
    {
        try
        {
            var text = sjis.GetString(bytes.ToArray());
            if (text.Length >= minLength && IsValidText(text) && ContainsJapanese(text))
            {
                chunks.Add(new TextChunk
                {
                    Text = text,
                    Encoding = "Shift-JIS",
                    Offset = startIndex,
                    Length = bytes.Count
                });
            }
        }
        catch { }
    }

    private List<TextChunk> FindUtf8Strings(byte[] data, int minLength, CancellationToken cancellationToken)
    {
        var chunks = new List<TextChunk>();
        int startIndex = -1;
        var currentString = new List<byte>();

        for (int i = 0; i < data.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            byte b = data[i];

            if (IsPrintableUtf8Byte(b) || IsUtf8Continuation(data, i))
            {
                if (startIndex == -1)
                    startIndex = i;
                currentString.Add(b);
            }
            else if (currentString.Count > 0)
            {
                string text = TryDecodeUtf8(currentString.ToArray());
                if (text.Length >= minLength && IsValidText(text))
                {
                    chunks.Add(new TextChunk
                    {
                        Text = text,
                        Encoding = "UTF-8",
                        Offset = startIndex,
                        Length = currentString.Count
                    });
                }

                currentString.Clear();
                startIndex = -1;
            }
        }

        if (currentString.Count > 0)
        {
            string text = TryDecodeUtf8(currentString.ToArray());
            if (text.Length >= minLength && IsValidText(text))
            {
                chunks.Add(new TextChunk
                {
                    Text = text,
                    Encoding = "UTF-8",
                    Offset = startIndex,
                    Length = currentString.Count
                });
            }
        }

        return chunks;
    }

    private List<TextChunk> FindUtf16Strings(byte[] data, int minLength, CancellationToken cancellationToken)
    {
        var chunks = new List<TextChunk>();

        // UTF-16LE検索
        chunks.AddRange(FindUtf16StringsInternal(data, minLength, false, cancellationToken));

        // UTF-16BE検索
        chunks.AddRange(FindUtf16StringsInternal(data, minLength, true, cancellationToken));

        return chunks;
    }

    private List<TextChunk> FindUtf16StringsInternal(byte[] data, int minLength, bool bigEndian, CancellationToken cancellationToken)
    {
        var chunks = new List<TextChunk>();
        var currentChars = new List<char>();
        int startIndex = -1;

        for (int i = 0; i < data.Length - 1; i += 2)
        {
            cancellationToken.ThrowIfCancellationRequested();

            char c = bigEndian 
                ? (char)((data[i] << 8) | data[i + 1])
                : (char)(data[i] | (data[i + 1] << 8));

            if (IsPrintableChar(c))
            {
                if (startIndex == -1)
                    startIndex = i;
                currentChars.Add(c);
            }
            else if (currentChars.Count > 0)
            {
                string text = new string(currentChars.ToArray());
                if (text.Length >= minLength && IsValidText(text))
                {
                    chunks.Add(new TextChunk
                    {
                        Text = text,
                        Encoding = bigEndian ? "UTF-16BE" : "UTF-16LE",
                        Offset = startIndex,
                        Length = currentChars.Count * 2
                    });
                }

                currentChars.Clear();
                startIndex = -1;
            }
        }

        if (currentChars.Count > 0)
        {
            string text = new string(currentChars.ToArray());
            if (text.Length >= minLength && IsValidText(text))
            {
                chunks.Add(new TextChunk
                {
                    Text = text,
                    Encoding = bigEndian ? "UTF-16BE" : "UTF-16LE",
                    Offset = startIndex,
                    Length = currentChars.Count * 2
                });
            }
        }

        return chunks;
    }

    private static bool IsPrintableUtf8Byte(byte b)
    {
        return (b >= 0x20 && b <= 0x7E) || b == 0x09 || b == 0x0A || b == 0x0D;
    }

    private static bool IsUtf8Continuation(byte[] data, int index)
    {
        if (index >= data.Length) return false;
        byte b = data[index];

        if ((b & 0xE0) == 0xC0 && index + 1 < data.Length) return true;
        if ((b & 0xF0) == 0xE0 && index + 2 < data.Length) return true;
        if ((b & 0xF8) == 0xF0 && index + 3 < data.Length) return true;
        if ((b & 0xC0) == 0x80) return true;

        return false;
    }

    private static bool IsPrintableChar(char c)
    {
        if (c >= 0x20 && c <= 0x7E) return true;
        if (c == '\t' || c == '\n' || c == '\r') return true;
        if (c >= 0x3000 && c <= 0x9FFF) return true; // CJK
        if (c >= 0x3040 && c <= 0x30FF) return true; // ひらがな・カタカナ
        if (c >= 0xFF00 && c <= 0xFFEF) return true; // 全角
        if (c >= 0xAC00 && c <= 0xD7AF) return true; // ハングル

        return false;
    }

    private static string TryDecodeUtf8(byte[] bytes)
    {
        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return Encoding.ASCII.GetString(bytes);
        }
    }

    private static bool IsValidText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        
        int controlCount = text.Count(c => char.IsControl(c) && c != '\t' && c != '\n' && c != '\r');
        if ((double)controlCount / text.Length > 0.1) return false;

        if (text.All(c => char.IsDigit(c) || char.IsWhiteSpace(c))) return false;

        return true;
    }

    private static bool ContainsJapanese(string text)
    {
        return text.Any(c => 
            (c >= 0x3040 && c <= 0x309F) ||  // ひらがな
            (c >= 0x30A0 && c <= 0x30FF) ||  // カタカナ
            (c >= 0x4E00 && c <= 0x9FFF));   // 漢字
    }

    private class TextChunk
    {
        public string Text { get; set; } = string.Empty;
        public string Encoding { get; set; } = string.Empty;
        public int Offset { get; set; }
        public int Length { get; set; }
    }
}
