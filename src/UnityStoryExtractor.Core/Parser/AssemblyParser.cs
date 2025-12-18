using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.RegularExpressions;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;
using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Parser;

/// <summary>
/// C#アセンブリパーサー（ILSpyベース）
/// </summary>
public partial class AssemblyParser : IAssetParser
{
    private static readonly string[] StoryRelatedPatterns =
    {
        "dialogue", "dialog", "text", "message", "story", "script",
        "conversation", "speech", "line", "subtitle", "quote",
        "セリフ", "台詞", "テキスト", "メッセージ", "ストーリー", "会話"
    };

    public IEnumerable<FileNodeType> SupportedTypes => new[] { FileNodeType.Assembly };

    public async Task<ParseResult> ParseAsync(string filePath, ExtractionOptions options, CancellationToken cancellationToken = default)
    {
        var result = new ParseResult 
        { 
            SourcePath = filePath,
            Success = true 
        };

        await Task.Run(() =>
        {
            try
            {
                // メタデータから文字列リテラルを抽出
                var metadataStrings = ExtractMetadataStrings(filePath, options, cancellationToken);
                result.Assets.AddRange(metadataStrings);

                // デコンパイルしてハードコードされた文字列を抽出
                if (options.ExtractAssemblyStrings)
                {
                    var decompileStrings = ExtractDecompiledStrings(filePath, options, cancellationToken);
                    result.Assets.AddRange(decompileStrings);
                }
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Errors.Add($"アセンブリ解析エラー: {ex.Message}");
            }
        }, cancellationToken);

        return result;
    }

    public async Task<ParseResult> ParseBinaryAsync(byte[] data, string sourcePath, ExtractionOptions options, CancellationToken cancellationToken = default)
    {
        // 一時ファイルに書き出して処理
        var tempPath = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(tempPath, data, cancellationToken);
            return await ParseAsync(tempPath, options, cancellationToken);
        }
        finally
        {
            try { File.Delete(tempPath); } catch { }
        }
    }

    public async Task<ParseResult> ParseStreamAsync(Stream stream, string sourcePath, ExtractionOptions options, CancellationToken cancellationToken = default)
    {
        using var memoryStream = new MemoryStream();
        await stream.CopyToAsync(memoryStream, cancellationToken);
        return await ParseBinaryAsync(memoryStream.ToArray(), sourcePath, options, cancellationToken);
    }

    private List<ParsedAsset> ExtractMetadataStrings(string filePath, ExtractionOptions options, CancellationToken cancellationToken)
    {
        var assets = new List<ParsedAsset>();

        try
        {
            using var stream = File.OpenRead(filePath);
            using var peReader = new PEReader(stream);

            if (!peReader.HasMetadata) return assets;

            var metadataReader = peReader.GetMetadataReader();
            var strings = new HashSet<string>();

            // User Stringsヒープから抽出（簡易版）
            int heapSize = metadataReader.GetHeapSize(HeapIndex.UserString);
            int offset = 1; // オフセット0はnullなので1から開始

            while (offset < heapSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var handle = MetadataTokens.UserStringHandle(offset);
                    if (handle.IsNil) break;

                    var str = metadataReader.GetUserString(handle);
                    
                    if (!string.IsNullOrEmpty(str) && IsValidStoryString(str, options))
                    {
                        strings.Add(str);
                    }

                    // 次のオフセットへ（文字列長 + ヘッダー + ターミネータ）
                    // User String は UTF-16 なので 2倍 + 1（長さバイト） + 1（ターミネータ）
                    int strByteLength = str.Length * 2 + 1;
                    int lengthBytes = strByteLength < 0x80 ? 1 : (strByteLength < 0x4000 ? 2 : 4);
                    offset += lengthBytes + strByteLength;
                }
                catch
                {
                    offset++; // エラー時は次のバイトへ
                }
            }

            if (strings.Count > 0)
            {
                var asset = new ParsedAsset
                {
                    Name = Path.GetFileName(filePath) + "_Metadata",
                    TypeName = "AssemblyMetadata",
                    TextContent = strings.ToList(),
                    Properties = new Dictionary<string, object>
                    {
                        { "Source", "UserStrings" },
                        { "Count", strings.Count }
                    }
                };
                assets.Add(asset);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"メタデータ抽出エラー: {ex.Message}");
        }

        return assets;
    }

    private List<ParsedAsset> ExtractDecompiledStrings(string filePath, ExtractionOptions options, CancellationToken cancellationToken)
    {
        var assets = new List<ParsedAsset>();

        try
        {
            var settings = new DecompilerSettings
            {
                ThrowOnAssemblyResolveErrors = false
            };

            using var module = new PEFile(filePath);
            var decompiler = new CSharpDecompiler(filePath, settings);

            // 型を列挙してストーリー関連のクラスを探す
            var typeSystem = decompiler.TypeSystem;
            var storyTypes = FindStoryRelatedTypes(typeSystem, cancellationToken);

            foreach (var typeName in storyTypes)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var fullTypeName = new FullTypeName(typeName);
                    var code = decompiler.DecompileTypeAsString(fullTypeName);
                    
                    // 文字列リテラルを抽出
                    var strings = ExtractStringLiterals(code, options);
                    
                    if (strings.Count > 0)
                    {
                        var asset = new ParsedAsset
                        {
                            Name = typeName,
                            TypeName = "DecompiledClass",
                            TextContent = strings,
                            Properties = new Dictionary<string, object>
                            {
                                { "ClassName", typeName },
                                { "StringCount", strings.Count }
                            }
                        };
                        assets.Add(asset);
                    }
                }
                catch
                {
                    // デコンパイルエラーは無視
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"デコンパイルエラー: {ex.Message}");
        }

        return assets;
    }

    private List<string> FindStoryRelatedTypes(IDecompilerTypeSystem typeSystem, CancellationToken cancellationToken)
    {
        var storyTypes = new List<string>();

        try
        {
            foreach (var type in typeSystem.MainModule.TypeDefinitions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var typeName = type.FullName;
                
                // ストーリー関連の名前を含むクラスを検索
                if (StoryRelatedPatterns.Any(p => 
                    typeName.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    storyTypes.Add(typeName);
                }
            }
        }
        catch
        {
            // 型列挙エラーは無視
        }

        return storyTypes;
    }

    private List<string> ExtractStringLiterals(string code, ExtractionOptions options)
    {
        var strings = new List<string>();

        // 文字列リテラルを抽出（"..." または @"..."）
        var matches = StringLiteralRegex().Matches(code);
        
        foreach (Match match in matches)
        {
            var str = match.Groups[1].Value;
            str = UnescapeString(str);

            if (IsValidStoryString(str, options))
            {
                strings.Add(str);
            }
        }

        // 逐語的文字列リテラル
        var verbatimMatches = VerbatimStringRegex().Matches(code);
        foreach (Match match in verbatimMatches)
        {
            var str = match.Groups[1].Value.Replace("\"\"", "\"");
            
            if (IsValidStoryString(str, options))
            {
                strings.Add(str);
            }
        }

        return strings.Distinct().ToList();
    }

    private static string UnescapeString(string str)
    {
        return str
            .Replace("\\n", "\n")
            .Replace("\\r", "\r")
            .Replace("\\t", "\t")
            .Replace("\\\"", "\"")
            .Replace("\\\\", "\\");
    }

    private static bool IsValidStoryString(string str, ExtractionOptions options)
    {
        if (string.IsNullOrWhiteSpace(str)) return false;
        if (str.Length < options.MinTextLength) return false;

        // プログラミング関連の文字列を除外
        if (str.StartsWith("//") || str.StartsWith("/*")) return false;
        if (str.Contains("{0}") && !str.Contains(' ')) return false;
        if (IsCodeString(str)) return false;

        // キーワードフィルタ
        if (options.Keywords.Count > 0)
        {
            if (!options.Keywords.Any(k => str.Contains(k, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        return true;
    }

    private static bool IsCodeString(string str)
    {
        // プログラミング関連のパターン
        if (str.StartsWith("System.") || str.StartsWith("UnityEngine.")) return true;
        if (str.StartsWith("get_") || str.StartsWith("set_")) return true;
        if (str.All(c => char.IsUpper(c) || c == '_')) return true; // CONSTANT_CASE
        if (str.Contains("Exception")) return true;
        if (str.EndsWith(".dll") || str.EndsWith(".exe")) return true;
        if (str.StartsWith("http://") || str.StartsWith("https://")) return true;

        return false;
    }

    [GeneratedRegex(@"""((?:[^""\\]|\\.)*)""")]
    private static partial Regex StringLiteralRegex();

    [GeneratedRegex(@"@""((?:[^""]|"""")*)""")]
    private static partial Regex VerbatimStringRegex();
}
