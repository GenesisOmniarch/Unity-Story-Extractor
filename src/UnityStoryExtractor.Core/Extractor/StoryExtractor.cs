using System.Collections.Concurrent;
using UnityStoryExtractor.Core.Decryptor;
using UnityStoryExtractor.Core.Loader;
using UnityStoryExtractor.Core.Models;
using UnityStoryExtractor.Core.Parser;

namespace UnityStoryExtractor.Core.Extractor;

/// <summary>
/// ストーリー抽出器の実装
/// </summary>
public class StoryExtractor : IStoryExtractor
{
    private readonly IAssetLoader _loader;
    private readonly List<IAssetParser> _parsers;
    private readonly DecryptorManager _decryptorManager;

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

            // 抽出を実行
            await ExtractFromNodeInternalAsync(rootNode, options, result, progress, cancellationToken);

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

        result.EndTime = DateTime.UtcNow;
        result.TotalExtracted = result.ExtractedTexts.Count;
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
            var fileType = DetermineFileType(filePath);
            var parser = _parsers.FirstOrDefault(p => p.SupportedTypes.Contains(fileType));

            if (parser != null)
            {
                var parseResult = await parser.ParseAsync(filePath, options, cancellationToken);
                
                foreach (var asset in parseResult.Assets)
                {
                    ProcessAsset(asset, filePath, options, result);
                }

                result.ProcessedFiles = 1;
            }
            else
            {
                result.Warnings.Add($"サポートされていないファイル形式: {filePath}");
            }

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
            await ExtractFromNodeInternalAsync(node, options, result, progress, cancellationToken);
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
        return result;
    }

    private async Task ExtractFromNodeInternalAsync(
        FileTreeNode node,
        ExtractionOptions options,
        ExtractionResult result,
        IProgress<ExtractionProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (node.IsDirectory)
        {
            // ファイル数をカウント
            int totalFiles = CountFiles(node);
            int processedFiles = 0;

            if (options.UseParallelProcessing)
            {
                // 並列処理
                var files = CollectFiles(node);
                var extractedTexts = new ConcurrentBag<ExtractedText>();
                var errors = new ConcurrentBag<ExtractionError>();

                await Parallel.ForEachAsync(files, new ParallelOptions
                {
                    MaxDegreeOfParallelism = options.MaxDegreeOfParallelism,
                    CancellationToken = cancellationToken
                }, async (file, ct) =>
                {
                    try
                    {
                        var extracted = await ExtractFromSingleFileAsync(file, options, ct);
                        foreach (var text in extracted)
                        {
                            extractedTexts.Add(text);
                        }

                        Interlocked.Increment(ref processedFiles);
                        progress?.Report(new ExtractionProgress
                        {
                            TotalFiles = totalFiles,
                            ProcessedFiles = processedFiles,
                            CurrentFile = file.FullPath,
                            CurrentOperation = "抽出中...",
                            ExtractedCount = extractedTexts.Count
                        });
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new ExtractionError
                        {
                            File = file.FullPath,
                            Message = ex.Message
                        });
                    }
                });

                result.ExtractedTexts.AddRange(extractedTexts);
                result.Errors.AddRange(errors);
                result.ProcessedFiles = processedFiles;
            }
            else
            {
                // 順次処理
                foreach (var file in CollectFiles(node))
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
        }
        else
        {
            // 単一ファイル
            var extracted = await ExtractFromSingleFileAsync(node, options, cancellationToken);
            result.ExtractedTexts.AddRange(extracted);
            result.ProcessedFiles = 1;
        }

        UpdateStatistics(result);
    }

    private async Task<List<ExtractedText>> ExtractFromSingleFileAsync(
        FileTreeNode node,
        ExtractionOptions options,
        CancellationToken cancellationToken)
    {
        var texts = new List<ExtractedText>();

        var parser = _parsers.FirstOrDefault(p => p.SupportedTypes.Contains(node.NodeType));
        if (parser == null) return texts;

        var parseResult = await parser.ParseAsync(node.FullPath, options, cancellationToken);

        foreach (var asset in parseResult.Assets)
        {
            // 暗号化チェック
            if (asset.IsEncrypted && options.AttemptDecryption && asset.RawData != null)
            {
                var decryptResult = _decryptorManager.TryDecrypt(asset.RawData, options.DecryptionKey);
                if (decryptResult.Success && decryptResult.DecryptedData != null)
                {
                    // 復号したデータを再解析
                    var decryptedParse = await parser.ParseBinaryAsync(
                        decryptResult.DecryptedData, 
                        node.FullPath, 
                        options, 
                        cancellationToken);

                    foreach (var decryptedAsset in decryptedParse.Assets)
                    {
                        AddExtractedTexts(decryptedAsset, node.FullPath, ExtractionSource.Binary, texts, options);
                    }
                    continue;
                }
            }

            // 通常の抽出
            var source = DetermineExtractionSource(node.NodeType, asset.TypeName);
            AddExtractedTexts(asset, node.FullPath, source, texts, options);
        }

        // .resSファイルの処理
        if (options.ProcessResSFiles && node.AssociatedResS != null)
        {
            var resSTexts = await ExtractFromResSAsync(node.AssociatedResS, options, cancellationToken);
            texts.AddRange(resSTexts);
        }

        return texts;
    }

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($".resS処理エラー: {ex.Message}");
        }

        return texts;
    }

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

    private void ProcessAsset(ParsedAsset asset, string sourceFile, ExtractionOptions options, ExtractionResult result)
    {
        var source = ExtractionSource.Unknown;
        
        if (asset.TypeName.Contains("TextAsset"))
            source = ExtractionSource.TextAsset;
        else if (asset.TypeName.Contains("MonoBehaviour"))
            source = ExtractionSource.MonoBehaviour;
        else if (asset.TypeName.Contains("Assembly"))
            source = ExtractionSource.Assembly;

        foreach (var content in asset.TextContent)
        {
            if (string.IsNullOrWhiteSpace(content)) continue;
            if (content.Length < options.MinTextLength) continue;

            result.ExtractedTexts.Add(new ExtractedText
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

    private static int CountFiles(FileTreeNode node)
    {
        if (!node.IsDirectory) return 1;
        return node.Children.Sum(c => CountFiles(c));
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
