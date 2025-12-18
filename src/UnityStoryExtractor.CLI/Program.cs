using System.CommandLine;
using System.Text;
using UnityStoryExtractor.Core.Extractor;
using UnityStoryExtractor.Core.Models;
using UnityStoryExtractor.Core.Output;

namespace UnityStoryExtractor.CLI;

/// <summary>
/// Unity Story Extractor CLIエントリポイント
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        // 日本語エンコーディングサポート
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Console.OutputEncoding = Encoding.UTF8;

        // 著作権警告
        ShowCopyrightWarning();

        // ルートコマンド
        var rootCommand = new RootCommand("Unity Story Extractor - Unityゲームからストーリーデータを抽出するツール");

        // 入力パスオプション
        var inputOption = new Option<string>(
            aliases: new[] { "-i", "--input" },
            description: "入力パス（ゲームのDataフォルダまたはファイル）")
        {
            IsRequired = true
        };

        // 出力パスオプション
        var outputOption = new Option<string>(
            aliases: new[] { "-o", "--output" },
            description: "出力ファイルパス")
        {
            IsRequired = true
        };

        // 出力形式オプション
        var formatOption = new Option<string>(
            aliases: new[] { "-f", "--format" },
            getDefaultValue: () => "json",
            description: "出力形式 (json, txt, csv, xml)");

        // キーワードフィルタオプション
        var keywordsOption = new Option<string[]>(
            aliases: new[] { "-k", "--keywords" },
            description: "抽出するキーワード（カンマ区切り）")
        {
            AllowMultipleArgumentsPerToken = true
        };

        // 最小テキスト長オプション
        var minLengthOption = new Option<int>(
            aliases: new[] { "-m", "--min-length" },
            getDefaultValue: () => 2,
            description: "最小テキスト長");

        // 並列処理オプション
        var parallelOption = new Option<bool>(
            aliases: new[] { "-p", "--parallel" },
            getDefaultValue: () => true,
            description: "並列処理を使用");

        // 詳細出力オプション
        var verboseOption = new Option<bool>(
            aliases: new[] { "-v", "--verbose" },
            getDefaultValue: () => false,
            description: "詳細な出力を表示");

        // 日本語優先オプション
        var japaneseOption = new Option<bool>(
            aliases: new[] { "-j", "--japanese" },
            getDefaultValue: () => true,
            description: "日本語テキストを優先");

        // 復号キーオプション
        var decryptKeyOption = new Option<string?>(
            aliases: new[] { "-d", "--decrypt-key" },
            description: "復号キー（Base64エンコード）");

        rootCommand.AddOption(inputOption);
        rootCommand.AddOption(outputOption);
        rootCommand.AddOption(formatOption);
        rootCommand.AddOption(keywordsOption);
        rootCommand.AddOption(minLengthOption);
        rootCommand.AddOption(parallelOption);
        rootCommand.AddOption(verboseOption);
        rootCommand.AddOption(japaneseOption);
        rootCommand.AddOption(decryptKeyOption);

        rootCommand.SetHandler(async (context) =>
        {
            var input = context.ParseResult.GetValueForOption(inputOption)!;
            var output = context.ParseResult.GetValueForOption(outputOption)!;
            var format = context.ParseResult.GetValueForOption(formatOption)!;
            var keywords = context.ParseResult.GetValueForOption(keywordsOption) ?? Array.Empty<string>();
            var minLength = context.ParseResult.GetValueForOption(minLengthOption);
            var parallel = context.ParseResult.GetValueForOption(parallelOption);
            var verbose = context.ParseResult.GetValueForOption(verboseOption);
            var japanese = context.ParseResult.GetValueForOption(japaneseOption);
            var decryptKey = context.ParseResult.GetValueForOption(decryptKeyOption);

            await RunExtractionAsync(input, output, format, keywords, minLength, parallel, verbose, japanese, decryptKey);
        });

        return await rootCommand.InvokeAsync(args);
    }

    static void ShowCopyrightWarning()
    {
        Console.WriteLine("================================================================================");
        Console.WriteLine("Unity Story Extractor - 著作権に関する注意");
        Console.WriteLine("================================================================================");
        Console.WriteLine();
        Console.WriteLine("このツールは教育・研究目的で設計されています。");
        Console.WriteLine("ゲームデータの抽出は、著作権法で保護されているコンテンツに対して");
        Console.WriteLine("行う場合、法的問題が発生する可能性があります。");
        Console.WriteLine();
        Console.WriteLine("使用者は、自身の行為に対する法的責任を負います。");
        Console.WriteLine("正規にライセンスされたコンテンツのみを対象とし、");
        Console.WriteLine("著作権者の権利を尊重してください。");
        Console.WriteLine();
        Console.WriteLine("================================================================================");
        Console.WriteLine();
    }

    static async Task RunExtractionAsync(
        string input,
        string output,
        string format,
        string[] keywords,
        int minLength,
        bool parallel,
        bool verbose,
        bool japanese,
        string? decryptKey)
    {
        Console.WriteLine($"入力パス: {input}");
        Console.WriteLine($"出力ファイル: {output}");
        Console.WriteLine($"出力形式: {format}");
        Console.WriteLine();

        // 入力パスの確認
        if (!Directory.Exists(input) && !File.Exists(input))
        {
            Console.WriteLine($"エラー: 入力パスが見つかりません: {input}");
            return;
        }

        // 抽出オプション
        var options = new ExtractionOptions
        {
            Keywords = keywords.ToList(),
            MinTextLength = minLength,
            UseParallelProcessing = parallel,
            VerboseLogging = verbose,
            PrioritizeJapaneseText = japanese,
            OutputFormat = ParseOutputFormat(format)
        };

        if (!string.IsNullOrEmpty(decryptKey))
        {
            try
            {
                options.DecryptionKey = Convert.FromBase64String(decryptKey);
            }
            catch
            {
                Console.WriteLine("警告: 無効な復号キーが指定されました");
            }
        }

        // 抽出実行
        var extractor = new StoryExtractor();
        var progress = new Progress<ExtractionProgress>(p =>
        {
            Console.Write($"\r抽出中: {p.ProcessedFiles}/{p.TotalFiles} ({p.Percentage:F1}%) - {Path.GetFileName(p.CurrentFile)}".PadRight(80));
        });

        Console.WriteLine("抽出を開始します...");
        Console.WriteLine();

        try
        {
            ExtractionResult result;

            if (Directory.Exists(input))
            {
                result = await extractor.ExtractFromDirectoryAsync(input, options, progress);
            }
            else
            {
                result = await extractor.ExtractFromFileAsync(input, options);
            }

            Console.WriteLine();
            Console.WriteLine();
            Console.WriteLine("================================================================================");
            Console.WriteLine("抽出結果");
            Console.WriteLine("================================================================================");
            Console.WriteLine($"処理ファイル数: {result.ProcessedFiles}");
            Console.WriteLine($"抽出アイテム数: {result.TotalExtracted}");
            Console.WriteLine($"処理時間: {result.DurationMs}ms");
            Console.WriteLine($"Unityバージョン: {result.UnityVersion}");
            Console.WriteLine();
            Console.WriteLine("統計情報:");
            Console.WriteLine($"  TextAsset: {result.Statistics.TextAssetCount}");
            Console.WriteLine($"  MonoBehaviour: {result.Statistics.MonoBehaviourCount}");
            Console.WriteLine($"  Assembly文字列: {result.Statistics.AssemblyStringCount}");
            Console.WriteLine($"  バイナリテキスト: {result.Statistics.BinaryTextCount}");
            Console.WriteLine($"  合計バイト数: {result.Statistics.TotalBytes}");
            Console.WriteLine();

            if (result.Errors.Count > 0)
            {
                Console.WriteLine($"エラー数: {result.Errors.Count}");
                if (verbose)
                {
                    foreach (var error in result.Errors)
                    {
                        Console.WriteLine($"  - {error.File}: {error.Message}");
                    }
                }
            }

            // 出力
            var writer = OutputWriterFactory.Create(options.OutputFormat);
            await writer.WriteAsync(result, output);

            Console.WriteLine($"出力ファイルを保存しました: {output}");
            Console.WriteLine();

            if (verbose && result.ExtractedTexts.Count > 0)
            {
                Console.WriteLine("抽出されたテキスト (最初の10件):");
                Console.WriteLine("--------------------------------------------------------------------------------");
                foreach (var text in result.ExtractedTexts.Take(10))
                {
                    Console.WriteLine($"[{text.AssetName}] ({text.Source})");
                    var preview = text.Content.Length > 100 
                        ? text.Content[..100] + "..." 
                        : text.Content;
                    Console.WriteLine($"  {preview.Replace("\n", " ").Replace("\r", "")}");
                    Console.WriteLine();
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"エラー: {ex.Message}");
            if (verbose)
            {
                Console.WriteLine(ex.StackTrace);
            }
        }
    }

    static OutputFormat ParseOutputFormat(string format)
    {
        return format.ToLowerInvariant() switch
        {
            "json" => OutputFormat.Json,
            "txt" or "text" => OutputFormat.Text,
            "csv" => OutputFormat.Csv,
            "xml" => OutputFormat.Xml,
            _ => OutputFormat.Json
        };
    }
}
