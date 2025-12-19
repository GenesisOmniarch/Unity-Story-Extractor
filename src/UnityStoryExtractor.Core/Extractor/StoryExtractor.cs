using System.Collections.Concurrent;
using System.Diagnostics;
using UnityStoryExtractor.Core.Decryptor;
using UnityStoryExtractor.Core.Loader;
using UnityStoryExtractor.Core.Models;
using UnityStoryExtractor.Core.Parser;

namespace UnityStoryExtractor.Core.Extractor;

/// <summary>
/// ストーリー抽出器の実装 - 抜本的に改善
/// </summary>
public class StoryExtractor : IStoryExtractor
{
    private readonly IAssetLoader _loader;
    private readonly List<IAssetParser> _parsers;
    private readonly DecryptorManager _decryptorManager;

    // 設定
    private const int MaxFileSizeForFullParse = 200 * 1024 * 1024; // 200MB
    private const int StreamingChunkSize = 8 * 1024 * 1024; // 8MB chunks
    private const int MaxConcurrentFiles = 4;

    public StoryExtractor()
    {
        _loader = new UnityAssetLoader();
        _parsers = new List<IAssetParser>
        {
            new TextAssetParser(),
            new MonoBehaviourParser(),
            new AssemblyParser()
        };
        _decryptorManager = new DecryptorManager();
    }

    public StoryExtractor(IAssetLoader loader, IEnumerable<IAssetParser> parsers, DecryptorManager decryptorManager)
    {
        _loader = loader;
        _parsers = parsers.ToList();
        _decryptorManager = decryptorManager;
    }

    public async Task<ExtractionResult> ExtractFromDirectoryAsync(
        string directoryPath,
        ExtractionOptions options,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ExtractionResult
        {
            SourcePath = directoryPath,
            StartTime = DateTime.UtcNow
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Unityバージョンを検出
            result.UnityVersion = await _loader.DetectUnityVersionAsync(directoryPath, cancellationToken) ?? "Unknown";

            // ディレクトリをスキャン
            var scanProgress = new Progress<ScanProgress>(p =>
            {
                progress?.Report(new ExtractionProgress
                {
                    CurrentOperation = "スキャン中...",
                    CurrentFile = p.CurrentFile,
                    TotalFiles = p.TotalFiles,
                    ProcessedFiles = p.ProcessedFiles
                });
            });

            var rootNode = await _loader.ScanDirectoryAsync(directoryPath, scanProgress, cancellationToken);

            // ファイルを収集
            var files = CollectFiles(rootNode).ToList();
            int totalFiles = files.Count;
            int processedFiles = 0;

            // 結果格納用
            var extractedTexts = new ConcurrentBag<ExtractedText>();
            var errors = new ConcurrentBag<ExtractionError>();

            // 並列処理の設定
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = options.UseParallelProcessing 
                    ? Math.Min(options.MaxDegreeOfParallelism, MaxConcurrentFiles) 
                    : 1,
                CancellationToken = cancellationToken
            };

            // ファイルごとに処理
            await Parallel.ForEachAsync(files, parallelOptions, async (file, ct) =>
            {
                try
                {
                    ct.ThrowIfCancellationRequested();

                    var extracted = await ExtractFromSingleFileAsync(file, options, ct);
                    foreach (var text in extracted)
                    {
                        extractedTexts.Add(text);
                    }

                    var currentProcessed = Interlocked.Increment(ref processedFiles);
                    progress?.Report(new ExtractionProgress
                    {
                        TotalFiles = totalFiles,
                        ProcessedFiles = currentProcessed,
                        CurrentFile = file.FullPath,
                        CurrentOperation = "抽出中...",
                        ExtractedCount = extractedTexts.Count
                    });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (OutOfMemoryException ex)
                {
                    // メモリ不足時はGC実行してスキップ
                    GC.Collect(2, GCCollectionMode.Forced);
                    GC.WaitForPendingFinalizers();
                    
                    errors.Add(new ExtractionError
                    {
                        File = file.FullPath,
                        Message = $"メモリ不足: {ex.Message}",
                        Timestamp = DateTime.UtcNow
                    });
                }
                catch (Exception ex)
                {
                    errors.Add(new ExtractionError
                    {
                        File = file.FullPath,
                        Message = $"{ex.GetType().Name}: {ex.Message}",
                        Timestamp = DateTime.UtcNow
                    });
                }
            });

            result.ExtractedTexts.AddRange(extractedTexts);
            result.Errors.AddRange(errors);
            result.ProcessedFiles = processedFiles;
            result.Success = true;
        }
        catch (OperationCanceledException)
        {
            result.Warnings.Add("抽出がキャンセルされました");
        }
        catch (Exception ex)
        {
            result.Errors.Add(new ExtractionError
            {
                File = directoryPath,
                Message = $"ディレクトリ抽出エラー: {ex.Message}",
                Exception = ex.ToString()
            });
        }

        stopwatch.Stop();
        result.EndTime = DateTime.UtcNow;
        result.TotalExtracted = result.ExtractedTexts.Count;

        // 統計を更新
        UpdateStatistics(result);

        return result;
    }

    public async Task<ExtractionResult> ExtractFromFileAsync(
        string filePath,
        ExtractionOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = new ExtractionResult
        {
            SourcePath = filePath,
            StartTime = DateTime.UtcNow
        };

        try
        {
            var fileNode = new FileTreeNode
            {
                FullPath = filePath,
                Name = Path.GetFileName(filePath),
                IsDirectory = false,
                NodeType = DetermineFileType(filePath),
                FileSize = new FileInfo(filePath).Length
            };

            var extracted = await ExtractFromSingleFileAsync(fileNode, options, cancellationToken);
            result.ExtractedTexts.AddRange(extracted);
            result.ProcessedFiles = 1;
            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Errors.Add(new ExtractionError
            {
                File = filePath,
                Message = $"ファイル抽出エラー: {ex.Message}",
                Exception = ex.ToString()
            });
        }

        result.EndTime = DateTime.UtcNow;
        result.TotalExtracted = result.ExtractedTexts.Count;
        UpdateStatistics(result);
        return result;
    }

    public async Task<ExtractionResult> ExtractFromNodeAsync(
        FileTreeNode node,
        ExtractionOptions options,
        IProgress<ExtractionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new ExtractionResult
        {
            SourcePath = node.FullPath,
            StartTime = DateTime.UtcNow
        };

        try
        {
            if (node.IsDirectory)
            {
                var files = CollectFiles(node).ToList();
                int totalFiles = files.Count;
                int processedFiles = 0;

                foreach (var file in files)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var extracted = await ExtractFromSingleFileAsync(file, options, cancellationToken);
                        result.ExtractedTexts.AddRange(extracted);
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(new ExtractionError
                        {
                            File = file.FullPath,
                            Message = ex.Message
                        });
                    }

                    processedFiles++;
                    progress?.Report(new ExtractionProgress
                    {
                        TotalFiles = totalFiles,
                        ProcessedFiles = processedFiles,
                        CurrentFile = file.FullPath,
                        CurrentOperation = "抽出中...",
                        ExtractedCount = result.ExtractedTexts.Count
                    });
                }

                result.ProcessedFiles = processedFiles;
            }
            else
            {
                var extracted = await ExtractFromSingleFileAsync(node, options, cancellationToken);
                result.ExtractedTexts.AddRange(extracted);
                result.ProcessedFiles = 1;
            }

            result.Success = true;
        }
        catch (OperationCanceledException)
        {
            result.Warnings.Add("抽出がキャンセルされました");
        }
        catch (Exception ex)
        {
            result.Errors.Add(new ExtractionError
            {
                File = node.FullPath,
                Message = $"ノード抽出エラー: {ex.Message}",
                Exception = ex.ToString()
            });
        }

        result.EndTime = DateTime.UtcNow;
        result.TotalExtracted = result.ExtractedTexts.Count;
        UpdateStatistics(result);
        return result;
    }

    /// <summary>
    /// 単一ファイルから抽出 - ロバストな実装
    /// </summary>
    private async Task<List<ExtractedText>> ExtractFromSingleFileAsync(
        FileTreeNode node,
        ExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var texts = new List<ExtractedText>();

        // ファイル存在チェック
        if (!File.Exists(node.FullPath))
        {
            return texts;
        }

        var fileInfo = new FileInfo(node.FullPath);

        // 空ファイルはスキップ
        if (fileInfo.Length == 0)
        {
            return texts;
        }

        // 除外パターンチェック
        if (IsExcludedFile(node.FullPath, options))
        {
            return texts;
        }

        // パーサーを取得
        var parser = _parsers.FirstOrDefault(p => p.SupportedTypes.Contains(node.NodeType));
        
        ParseResult? parseResult = null;

        try
        {
            if (parser != null)
            {
                // 大きなファイルはストリーミング処理
                if (fileInfo.Length > MaxFileSizeForFullParse)
                {
                    parseResult = await ParseLargeFileAsync(node.FullPath, parser, options, cancellationToken);
                }
                else
                {
                    parseResult = await parser.ParseAsync(node.FullPath, options, cancellationToken);
                }
            }
            else
            {
                // パーサーがない場合はフォールバック解析
                parseResult = await FallbackParseAsync(node.FullPath, options, cancellationToken);
            }

            if (parseResult != null && parseResult.Success)
            {
                foreach (var asset in parseResult.Assets)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // 暗号化チェック
                    if (asset.IsEncrypted && options.AttemptDecryption && asset.RawData != null)
                    {
                        var decrypted = await TryDecryptAssetAsync(asset, node.FullPath, parser, options, cancellationToken);
                        if (decrypted.Any())
                        {
                            texts.AddRange(decrypted);
                            continue;
                        }
                    }

                    // 通常の抽出
                    var source = DetermineExtractionSource(node.NodeType, asset.TypeName);
                    AddExtractedTexts(asset, node.FullPath, source, texts, options);
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (IOException)
        {
            // ファイルアクセスエラーは無視（ロックされているなど）
        }
        catch (UnauthorizedAccessException)
        {
            // アクセス権限エラーは無視
        }
        catch (Exception)
        {
            // その他のエラーもスキップして続行
        }

        // .resSファイルの処理
        if (options.ProcessResSFiles && node.AssociatedResS != null)
        {
            try
            {
                var resSTexts = await ExtractFromResSAsync(node.AssociatedResS, options, cancellationToken);
                texts.AddRange(resSTexts);
            }
            catch
            {
                // .resS処理エラーはスキップ
            }
        }

        return texts;
    }

    /// <summary>
    /// 大きなファイルをストリーミング処理
    /// </summary>
    private async Task<ParseResult> ParseLargeFileAsync(
        string filePath,
        IAssetParser parser,
        ExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var result = new ParseResult { SourcePath = filePath, Success = true };

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, 
                FileShare.Read, bufferSize: StreamingChunkSize, useAsync: true);

            var buffer = new byte[StreamingChunkSize];
            long position = 0;
            int chunkIndex = 0;

            while (position < stream.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // メモリチェック
                var memoryMB = GC.GetTotalMemory(false) / (1024.0 * 1024.0);
                if (memoryMB > 1500)
                {
                    GC.Collect(1, GCCollectionMode.Optimized);
                }

                int bytesToRead = (int)Math.Min(StreamingChunkSize, stream.Length - position);
                int bytesRead = await stream.ReadAsync(buffer.AsMemory(0, bytesToRead), cancellationToken);

                if (bytesRead == 0) break;

                // チャンクを解析
                var chunkResult = await parser.ParseBinaryAsync(
                    buffer[..bytesRead], 
                    $"{filePath}#chunk{chunkIndex}", 
                    options, 
                    cancellationToken);

                result.Assets.AddRange(chunkResult.Assets);

                position += bytesRead;
                chunkIndex++;

                // 最大アセット数制限
                if (result.Assets.Count > 10000)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"ストリーミング解析エラー: {ex.Message}");
        }

        return result;
    }

    /// <summary>
    /// フォールバック解析（文字列検索）
    /// </summary>
    private async Task<ParseResult> FallbackParseAsync(
        string filePath,
        ExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var result = new ParseResult { SourcePath = filePath, Success = true };

        try
        {
            var textParser = new TextAssetParser();
            return await textParser.ParseAsync(filePath, options, cancellationToken);
        }
        catch
        {
            result.Success = false;
        }

        return result;
    }

    /// <summary>
    /// 暗号化アセットの復号を試行
    /// </summary>
    private async Task<List<ExtractedText>> TryDecryptAssetAsync(
        ParsedAsset asset,
        string filePath,
        IAssetParser? parser,
        ExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var texts = new List<ExtractedText>();

        if (asset.RawData == null) return texts;

        try
        {
            var decryptResult = _decryptorManager.TryDecrypt(asset.RawData, options.DecryptionKey);
            
            if (decryptResult.Success && decryptResult.DecryptedData != null)
            {
                var targetParser = parser ?? new TextAssetParser();
                var decryptedParse = await targetParser.ParseBinaryAsync(
                    decryptResult.DecryptedData,
                    filePath,
                    options,
                    cancellationToken);

                foreach (var decryptedAsset in decryptedParse.Assets)
                {
                    AddExtractedTexts(decryptedAsset, filePath, ExtractionSource.Binary, texts, options);
                }
            }
        }
        catch
        {
            // 復号失敗はスキップ
        }

        return texts;
    }

    /// <summary>
    /// .resSファイルから抽出
    /// </summary>
    private async Task<List<ExtractedText>> ExtractFromResSAsync(
        FileTreeNode resSNode,
        ExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var texts = new List<ExtractedText>();

        try
        {
            var parser = new TextAssetParser();
            var parseResult = await parser.ParseAsync(resSNode.FullPath, options, cancellationToken);

            foreach (var asset in parseResult.Assets)
            {
                AddExtractedTexts(asset, resSNode.FullPath, ExtractionSource.ResS, texts, options);
            }
        }
        catch
        {
            // エラーは無視
        }

        return texts;
    }

    /// <summary>
    /// 抽出テキストを追加
    /// </summary>
    private void AddExtractedTexts(
        ParsedAsset asset,
        string sourceFile,
        ExtractionSource source,
        List<ExtractedText> texts,
        ExtractionOptions options)
    {
        foreach (var content in asset.TextContent)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;
            if (content.Length < options.MinTextLength) continue;
            if (content.Length > options.MaxTextLength) continue;

            // キーワードフィルタ
            if (options.Keywords.Count > 0)
            {
                bool matches = options.Keywords.Any(k =>
                    content.Contains(k, StringComparison.OrdinalIgnoreCase));
                if (!matches) continue;
            }

            texts.Add(new ExtractedText
            {
                AssetName = asset.Name,
                SourceFile = sourceFile,
                AssetType = asset.TypeName,
                Content = content,
                Source = source,
                Metadata = new Dictionary<string, object>(asset.Properties)
            });
        }
    }

    private bool IsExcludedFile(string path, ExtractionOptions options)
    {
        var fileName = Path.GetFileName(path).ToLowerInvariant();
        var extension = Path.GetExtension(path).ToLowerInvariant();

        foreach (var pattern in options.ExcludePatterns)
        {
            var lowerPattern = pattern.ToLowerInvariant();
            if (lowerPattern.StartsWith("*."))
            {
                if (extension == lowerPattern[1..]) return true;
            }
            else if (fileName.Contains(lowerPattern.Replace("*", "")))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<FileTreeNode> CollectFiles(FileTreeNode node)
    {
        if (!node.IsDirectory)
        {
            yield return node;
            yield break;
        }

        foreach (var child in node.Children)
        {
            foreach (var file in CollectFiles(child))
            {
                yield return file;
            }
        }
    }

    private static FileNodeType DetermineFileType(string path)
    {
        var fileName = Path.GetFileName(path).ToLowerInvariant();
        var extension = Path.GetExtension(path).ToLowerInvariant();

        if (fileName == "globalgamemanagers")
            return FileNodeType.GlobalGameManagers;
        if (fileName == "resources.assets")
            return FileNodeType.ResourcesAssets;
        if (extension is ".ress")
            return FileNodeType.ResSFile;
        if (extension == ".dll")
            return FileNodeType.Assembly;
        if (extension is ".bundle" or ".unity3d" or ".ab")
            return FileNodeType.AssetBundle;
        if (extension == ".assets" || fileName.StartsWith("sharedassets"))
            return FileNodeType.AssetsFile;

        return FileNodeType.Other;
    }

    private static ExtractionSource DetermineExtractionSource(FileNodeType nodeType, string typeName)
    {
        if (typeName.Contains("TextAsset"))
            return ExtractionSource.TextAsset;
        if (typeName.Contains("MonoBehaviour") || typeName.Contains("ScriptableObject"))
            return ExtractionSource.MonoBehaviour;
        if (typeName.Contains("Assembly"))
            return ExtractionSource.Assembly;
        if (nodeType == FileNodeType.ResSFile)
            return ExtractionSource.ResS;

        return ExtractionSource.Binary;
    }

    private static void UpdateStatistics(ExtractionResult result)
    {
        foreach (var text in result.ExtractedTexts)
        {
            result.Statistics.TotalBytes += System.Text.Encoding.UTF8.GetByteCount(text.Content);

            switch (text.Source)
            {
                case ExtractionSource.TextAsset:
                    result.Statistics.TextAssetCount++;
                    break;
                case ExtractionSource.MonoBehaviour:
                case ExtractionSource.ScriptableObject:
                    result.Statistics.MonoBehaviourCount++;
                    break;
                case ExtractionSource.Assembly:
                case ExtractionSource.IL2CPP:
                    result.Statistics.AssemblyStringCount++;
                    break;
                case ExtractionSource.Binary:
                case ExtractionSource.ResS:
                    result.Statistics.BinaryTextCount++;
                    break;
            }
        }
    }
}
