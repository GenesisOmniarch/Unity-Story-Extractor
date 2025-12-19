using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Xml.Linq;
using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Output;

/// <summary>
/// 出力ライターファクトリー
/// </summary>
public static class OutputWriterFactory
{
    public static IOutputWriter Create(OutputFormat format)
    {
        return format switch
        {
            OutputFormat.Json => new JsonOutputWriter(),
            OutputFormat.Text => new TextOutputWriter(),
            OutputFormat.Csv => new CsvOutputWriter(),
            OutputFormat.Xml => new XmlOutputWriter(),
            _ => throw new ArgumentException($"サポートされていない出力形式: {format}")
        };
    }
}

/// <summary>
/// JSON出力ライター
/// </summary>
public class JsonOutputWriter : IOutputWriter
{
    public OutputFormat Format => OutputFormat.Json;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task WriteAsync(ExtractionResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        var json = await ToStringAsync(result, cancellationToken);
        await File.WriteAllTextAsync(outputPath, json, Encoding.UTF8, cancellationToken);
    }

    public Task<string> ToStringAsync(ExtractionResult result, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        return Task.FromResult(json);
    }
}

/// <summary>
/// テキスト出力ライター
/// </summary>
public class TextOutputWriter : IOutputWriter
{
    public OutputFormat Format => OutputFormat.Text;

    public async Task WriteAsync(ExtractionResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        var text = await ToStringAsync(result, cancellationToken);
        await File.WriteAllTextAsync(outputPath, text, Encoding.UTF8, cancellationToken);
    }

    public Task<string> ToStringAsync(ExtractionResult result, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        sb.AppendLine("================================================================================");
        sb.AppendLine("Unity Story Extractor - 抽出結果");
        sb.AppendLine("================================================================================");
        sb.AppendLine();
        sb.AppendLine($"ソースパス: {result.SourcePath}");
        sb.AppendLine($"Unityバージョン: {result.UnityVersion}");
        sb.AppendLine($"処理ファイル数: {result.ProcessedFiles}");
        sb.AppendLine($"抽出アイテム数: {result.TotalExtracted}");
        sb.AppendLine($"処理時間: {result.DurationMs}ms");
        sb.AppendLine();
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine("統計情報");
        sb.AppendLine("--------------------------------------------------------------------------------");
        sb.AppendLine($"  TextAsset: {result.Statistics.TextAssetCount}");
        sb.AppendLine($"  MonoBehaviour: {result.Statistics.MonoBehaviourCount}");
        sb.AppendLine($"  Assembly文字列: {result.Statistics.AssemblyStringCount}");
        sb.AppendLine($"  バイナリテキスト: {result.Statistics.BinaryTextCount}");
        sb.AppendLine($"  合計バイト数: {result.Statistics.TotalBytes}");
        sb.AppendLine();

        if (result.Errors.Count > 0)
        {
            sb.AppendLine("--------------------------------------------------------------------------------");
            sb.AppendLine("エラー");
            sb.AppendLine("--------------------------------------------------------------------------------");
            foreach (var error in result.Errors)
            {
                sb.AppendLine($"  [{error.File}] {error.Message}");
            }
            sb.AppendLine();
        }

        sb.AppendLine("================================================================================");
        sb.AppendLine("抽出テキスト");
        sb.AppendLine("================================================================================");
        sb.AppendLine();

        int index = 1;
        foreach (var text in result.ExtractedTexts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            sb.AppendLine($"--- [{index}] {text.AssetName} ({text.AssetType}) ---");
            sb.AppendLine($"ソース: {text.SourceFile}");
            sb.AppendLine($"抽出元: {text.Source}");
            sb.AppendLine();
            sb.AppendLine(text.Content);
            sb.AppendLine();
            index++;
        }

        return Task.FromResult(sb.ToString());
    }
}

/// <summary>
/// CSV出力ライター
/// </summary>
public class CsvOutputWriter : IOutputWriter
{
    public OutputFormat Format => OutputFormat.Csv;

    public async Task WriteAsync(ExtractionResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        var csv = await ToStringAsync(result, cancellationToken);
        await File.WriteAllTextAsync(outputPath, csv, Encoding.UTF8, cancellationToken);
    }

    public Task<string> ToStringAsync(ExtractionResult result, CancellationToken cancellationToken = default)
    {
        var sb = new StringBuilder();

        // ヘッダー
        sb.AppendLine("\"AssetName\",\"AssetType\",\"SourceFile\",\"ExtractionSource\",\"Content\"");

        foreach (var text in result.ExtractedTexts)
        {
            cancellationToken.ThrowIfCancellationRequested();

            sb.AppendLine(
                $"\"{EscapeCsv(text.AssetName)}\"," +
                $"\"{EscapeCsv(text.AssetType)}\"," +
                $"\"{EscapeCsv(text.SourceFile)}\"," +
                $"\"{text.Source}\"," +
                $"\"{EscapeCsv(text.Content)}\"");
        }

        return Task.FromResult(sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        return value.Replace("\"", "\"\"").Replace("\n", "\\n").Replace("\r", "\\r");
    }
}

/// <summary>
/// XML出力ライター
/// </summary>
public class XmlOutputWriter : IOutputWriter
{
    public OutputFormat Format => OutputFormat.Xml;

    public async Task WriteAsync(ExtractionResult result, string outputPath, CancellationToken cancellationToken = default)
    {
        var xml = await ToStringAsync(result, cancellationToken);
        await File.WriteAllTextAsync(outputPath, xml, Encoding.UTF8, cancellationToken);
    }

    public Task<string> ToStringAsync(ExtractionResult result, CancellationToken cancellationToken = default)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", "yes"),
            new XElement("ExtractionResult",
                new XElement("SourcePath", result.SourcePath),
                new XElement("UnityVersion", result.UnityVersion),
                new XElement("ProcessedFiles", result.ProcessedFiles),
                new XElement("TotalExtracted", result.TotalExtracted),
                new XElement("DurationMs", result.DurationMs),
                new XElement("Statistics",
                    new XElement("TextAssetCount", result.Statistics.TextAssetCount),
                    new XElement("MonoBehaviourCount", result.Statistics.MonoBehaviourCount),
                    new XElement("AssemblyStringCount", result.Statistics.AssemblyStringCount),
                    new XElement("BinaryTextCount", result.Statistics.BinaryTextCount),
                    new XElement("TotalBytes", result.Statistics.TotalBytes)
                ),
                new XElement("ExtractedTexts",
                    result.ExtractedTexts.Select(t => new XElement("Text",
                        new XElement("AssetName", t.AssetName),
                        new XElement("AssetType", t.AssetType),
                        new XElement("SourceFile", t.SourceFile),
                        new XElement("ExtractionSource", t.Source.ToString()),
                        new XElement("Content", new XCData(t.Content))
                    ))
                ),
                new XElement("Errors",
                    result.Errors.Select(e => new XElement("Error",
                        new XElement("File", e.File),
                        new XElement("Message", e.Message)
                    ))
                )
            )
        );

        // XML宣言を含めて出力
        var xml = doc.Declaration?.ToString() + Environment.NewLine + doc.ToString();
        return Task.FromResult(xml);
    }
}
