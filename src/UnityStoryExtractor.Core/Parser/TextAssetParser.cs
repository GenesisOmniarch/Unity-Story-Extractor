using System.Text;
using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Parser;

/// <summary>
/// TextAssetパーサー
/// </summary>
public class TextAssetParser : IAssetParser
{
    public IEnumerable<FileNodeType> SupportedTypes => new[] 
    { 
        FileNodeType.AssetsFile, 
        FileNodeType.ResourcesAssets,
        FileNodeType.AssetBundle 
    };

    public async Task<ParseResult> ParseAsync(string filePath, ExtractionOptions options, CancellationToken cancellationToken = default)
    {
        var result = new ParseResult { SourcePath = filePath };

        try
        {
            var fileInfo = new FileInfo(filePath);
            
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
                // TextAsset構造を検索
                var textAssets = FindTextAssets(data, sourcePath, options, cancellationToken);
                result.Assets.AddRange(textAssets);
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
                var overlap = new byte[4096]; // オーバーラップバッファ
                long position = 0;
                int bytesRead;
                int assetIndex = 0;

                while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // チャンクを処理
                    var textAssets = FindTextAssets(buffer[..bytesRead], sourcePath, options, cancellationToken);
                    foreach (var asset in textAssets)
                    {
                        asset.Name = $"TextAsset_{assetIndex++}";
                        result.Assets.Add(asset);
                    }

                    position += bytesRead;
                }
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

        // UTF-8/UTF-16テキストチャンクを検索
        var textChunks = FindTextChunks(data, options, cancellationToken);
        
        int index = 0;
        foreach (var chunk in textChunks)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(chunk.Text))
                continue;

            if (chunk.Text.Length < options.MinTextLength)
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
        }

        return assets;
    }

    private List<TextChunk> FindTextChunks(byte[] data, ExtractionOptions options, CancellationToken cancellationToken)
    {
        var chunks = new List<TextChunk>();

        // UTF-8テキストを検索
        var utf8Chunks = FindUtf8Strings(data, options.MinTextLength, cancellationToken);
        chunks.AddRange(utf8Chunks);

        // UTF-16テキストを検索（日本語など）
        var utf16Chunks = FindUtf16Strings(data, options.MinTextLength, cancellationToken);
        chunks.AddRange(utf16Chunks);

        // 日本語優先の場合、日本語テキストを先頭に
        if (options.PrioritizeJapaneseText)
        {
            chunks = chunks
                .OrderByDescending(c => ContainsJapanese(c.Text))
                .ThenByDescending(c => c.Text.Length)
                .ToList();
        }

        return chunks;
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

            // 印刷可能文字またはUTF-8マルチバイトシーケンス
            if (IsPrintableUtf8Byte(b) || IsUtf8Continuation(data, i))
            {
                if (startIndex == -1)
                    startIndex = i;
                currentString.Add(b);
            }
            else if (currentString.Count > 0)
            {
                // 文字列終端
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

        // 残りの文字列を処理
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
        var encoding = bigEndian ? Encoding.BigEndianUnicode : Encoding.Unicode;
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
        // ASCII印刷可能文字
        return (b >= 0x20 && b <= 0x7E) || b == 0x09 || b == 0x0A || b == 0x0D;
    }

    private static bool IsUtf8Continuation(byte[] data, int index)
    {
        if (index >= data.Length) return false;
        byte b = data[index];

        // UTF-8マルチバイトシーケンス開始
        if ((b & 0xE0) == 0xC0 && index + 1 < data.Length) return true;  // 2バイト
        if ((b & 0xF0) == 0xE0 && index + 2 < data.Length) return true;  // 3バイト
        if ((b & 0xF8) == 0xF0 && index + 3 < data.Length) return true;  // 4バイト
        if ((b & 0xC0) == 0x80) return true;  // 継続バイト

        return false;
    }

    private static bool IsPrintableChar(char c)
    {
        // ASCII印刷可能
        if (c >= 0x20 && c <= 0x7E) return true;
        // 改行・タブ
        if (c == '\t' || c == '\n' || c == '\r') return true;
        // CJK文字
        if (c >= 0x3000 && c <= 0x9FFF) return true;
        // ひらがな・カタカナ
        if (c >= 0x3040 && c <= 0x30FF) return true;
        // 全角文字
        if (c >= 0xFF00 && c <= 0xFFEF) return true;
        // ハングル
        if (c >= 0xAC00 && c <= 0xD7AF) return true;

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
        
        // 制御文字が多すぎる場合は無効
        int controlCount = text.Count(c => char.IsControl(c) && c != '\t' && c != '\n' && c != '\r');
        if ((double)controlCount / text.Length > 0.1) return false;

        // 数字のみは無効
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
