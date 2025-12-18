using System.Text;
using System.Text.RegularExpressions;
using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Parser;

/// <summary>
/// MonoBehaviourパーサー
/// </summary>
public partial class MonoBehaviourParser : IAssetParser
{
    private static readonly string[] StoryRelatedFieldNames = 
    {
        "dialogue", "dialog", "text", "message", "story", "script",
        "conversation", "speech", "line", "subtitle", "caption",
        "content", "description", "narrative", "quote", "saying",
        // 日本語フィールド名
        "セリフ", "台詞", "テキスト", "メッセージ", "ストーリー",
        "会話", "台本", "字幕", "説明", "内容"
    };

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
                // MonoBehaviour構造を検索
                var monoBehaviours = FindMonoBehaviours(data, sourcePath, options, cancellationToken);
                result.Assets.AddRange(monoBehaviours);
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"MonoBehaviour解析エラー: {ex.Message}");
            }
        }, cancellationToken);

        return result;
    }

    public async Task<ParseResult> ParseStreamAsync(Stream stream, string sourcePath, ExtractionOptions options, CancellationToken cancellationToken = default)
    {
        // ストリームをメモリに読み込んで処理
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return await ParseBinaryAsync(memoryStream.ToArray(), sourcePath, options, cancellationToken);
    }

    private List<ParsedAsset> FindMonoBehaviours(byte[] data, string sourcePath, ExtractionOptions options, CancellationToken cancellationToken)
    {
        var assets = new List<ParsedAsset>();

        // シリアライズされた文字列配列パターンを検索
        var stringArrays = FindSerializedStringArrays(data, options, cancellationToken);
        
        int index = 0;
        foreach (var array in stringArrays)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (array.Strings.Count == 0)
                continue;

            // ストーリー関連フィールドかチェック
            bool isStoryRelated = IsStoryRelatedField(array.FieldName);
            
            if (options.Keywords.Count > 0 && !isStoryRelated)
            {
                bool matches = array.Strings.Any(s => 
                    options.Keywords.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase)));
                if (!matches) continue;
            }

            var asset = new ParsedAsset
            {
                PathId = index++,
                Name = array.FieldName ?? $"MonoBehaviour_{index}",
                TypeName = "MonoBehaviour",
                TextContent = array.Strings,
                Properties = new Dictionary<string, object>
                {
                    { "FieldName", array.FieldName ?? "Unknown" },
                    { "StringCount", array.Strings.Count },
                    { "Offset", array.Offset }
                }
            };

            assets.Add(asset);
        }

        return assets;
    }

    private List<SerializedStringArray> FindSerializedStringArrays(byte[] data, ExtractionOptions options, CancellationToken cancellationToken)
    {
        var arrays = new List<SerializedStringArray>();

        using var stream = new MemoryStream(data);
        using var reader = new BinaryReader(stream);

        int position = 0;
        while (position < data.Length - 8)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                stream.Position = position;

                // 配列サイズを読み取り（0-1000の範囲で合理的な値のみ）
                int arraySize = reader.ReadInt32();
                
                if (arraySize > 0 && arraySize < 1000)
                {
                    var strings = new List<string>();
                    bool valid = true;
                    long arrayStart = stream.Position;

                    for (int i = 0; i < arraySize && valid; i++)
                    {
                        if (stream.Position >= stream.Length - 4)
                        {
                            valid = false;
                            break;
                        }

                        int stringLength = reader.ReadInt32();
                        
                        // 文字列長の検証
                        if (stringLength < 0 || stringLength > 10000 || stream.Position + stringLength > stream.Length)
                        {
                            valid = false;
                            break;
                        }

                        var stringBytes = reader.ReadBytes(stringLength);
                        string text = Encoding.UTF8.GetString(stringBytes);

                        // テキストの検証
                        if (IsValidDialogueText(text))
                        {
                            strings.Add(text);
                        }

                        // アライメントをスキップ
                        int alignment = (4 - (stringLength % 4)) % 4;
                        if (stream.Position + alignment <= stream.Length)
                        {
                            stream.Position += alignment;
                        }
                    }

                    if (valid && strings.Count >= 2 && strings.Count >= arraySize / 2)
                    {
                        // フィールド名を逆検索
                        string? fieldName = FindFieldName(data, position);

                        arrays.Add(new SerializedStringArray
                        {
                            FieldName = fieldName,
                            Strings = strings,
                            Offset = position
                        });
                    }
                }
            }
            catch
            {
                // 解析エラーは無視して続行
            }

            position++;
        }

        return arrays;
    }

    private string? FindFieldName(byte[] data, int position)
    {
        // 配列の前方でフィールド名を検索
        int searchStart = Math.Max(0, position - 200);
        int searchLength = position - searchStart;

        if (searchLength <= 0) return null;

        var searchData = new byte[searchLength];
        Array.Copy(data, searchStart, searchData, 0, searchLength);

        string content = Encoding.UTF8.GetString(searchData);

        // ストーリー関連フィールド名を検索
        foreach (var fieldName in StoryRelatedFieldNames)
        {
            if (content.Contains(fieldName, StringComparison.OrdinalIgnoreCase))
            {
                return fieldName;
            }
        }

        // キャメルケースのフィールド名パターン
        var match = FieldNameRegex().Match(content);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }

    private static bool IsStoryRelatedField(string? fieldName)
    {
        if (string.IsNullOrEmpty(fieldName)) return false;
        
        return StoryRelatedFieldNames.Any(n => 
            fieldName.Contains(n, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsValidDialogueText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (text.Length < 2) return false;

        // 制御文字が多すぎる
        int controlCount = text.Count(c => char.IsControl(c) && c != '\t' && c != '\n' && c != '\r');
        if ((double)controlCount / text.Length > 0.1) return false;

        // 数字のみ
        if (text.All(char.IsDigit)) return false;

        // 単一の特殊文字
        if (text.All(c => !char.IsLetterOrDigit(c))) return false;

        // バイナリデータっぽい
        if (text.Contains('\0')) return false;

        return true;
    }

    [GeneratedRegex(@"([a-zA-Z_][a-zA-Z0-9_]*(?:Text|Dialog|Message|Story|Line|Content))\x00")]
    private static partial Regex FieldNameRegex();

    private class SerializedStringArray
    {
        public string? FieldName { get; set; }
        public List<string> Strings { get; set; } = new();
        public int Offset { get; set; }
    }
}
