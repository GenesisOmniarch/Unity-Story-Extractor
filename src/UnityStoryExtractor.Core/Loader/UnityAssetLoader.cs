using System.Text;
using System.Text.RegularExpressions;
using UnityStoryExtractor.Core.Models;

namespace UnityStoryExtractor.Core.Loader;

/// <summary>
/// Unityアセットファイルのローダー実装
/// </summary>
public partial class UnityAssetLoader : IAssetLoader
{
    private readonly Dictionary<string, string[]> _fileExtensions = new()
    {
        { "assets", new[] { ".assets", ".asset" } },
        { "bundle", new[] { ".bundle", ".unity3d", ".ab" } },
        { "resources", new[] { "resources.assets" } },
        { "ress", new[] { ".resS", ".ress" } },
        { "assembly", new[] { ".dll" } }
    };

    private static readonly string[] ExcludedDirectories = 
    {
        "Mono", "MonoBleedingEdge", "il2cpp_data", "Plugins"
    };

    public async Task<FileTreeNode> ScanDirectoryAsync(string path, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException($"ディレクトリが見つかりません: {path}");

        var rootNode = new FileTreeNode
        {
            Name = Path.GetFileName(path) ?? path,
            FullPath = path,
            IsDirectory = true,
            NodeType = FileNodeType.Directory
        };

        // ファイル数をカウント
        var allFiles = await Task.Run(() => 
            Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(f => IsSupportedFile(f))
                .ToList(), cancellationToken);

        var scanProgress = new ScanProgress { TotalFiles = allFiles.Count };
        int processedCount = 0;

        await Task.Run(() =>
        {
            ScanDirectoryRecursive(rootNode, path, allFiles, ref processedCount, progress, scanProgress, cancellationToken);
        }, cancellationToken);

        // .resSファイルをリンク
        LinkResSFiles(rootNode);

        return rootNode;
    }

    private void ScanDirectoryRecursive(
        FileTreeNode parentNode, 
        string path, 
        List<string> allSupportedFiles,
        ref int processedCount,
        IProgress<ScanProgress>? progress,
        ScanProgress scanProgress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            // サブディレクトリを処理
            var directories = Directory.GetDirectories(path)
                .Where(d => !ExcludedDirectories.Contains(Path.GetFileName(d)))
                .OrderBy(d => d);

            foreach (var dir in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var dirNode = new FileTreeNode
                {
                    Name = Path.GetFileName(dir),
                    FullPath = dir,
                    IsDirectory = true,
                    NodeType = FileNodeType.Directory
                };

                ScanDirectoryRecursive(dirNode, dir, allSupportedFiles, ref processedCount, progress, scanProgress, cancellationToken);

                // 子要素がある場合のみ追加
                if (dirNode.Children.Count > 0)
                {
                    parentNode.Children.Add(dirNode);
                }
            }

            // ファイルを処理
            var files = Directory.GetFiles(path)
                .Where(IsSupportedFile)
                .OrderBy(f => f);

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var fileInfo = new FileInfo(file);
                var fileNode = new FileTreeNode
                {
                    Name = fileInfo.Name,
                    FullPath = file,
                    IsDirectory = false,
                    NodeType = DetermineFileType(file),
                    FileSize = fileInfo.Length
                };

                parentNode.Children.Add(fileNode);

                processedCount++;
                scanProgress.ProcessedFiles = processedCount;
                scanProgress.CurrentFile = file;
                progress?.Report(scanProgress);
            }
        }
        catch (UnauthorizedAccessException)
        {
            // アクセス権限がない場合はスキップ
        }
        catch (IOException)
        {
            // I/Oエラーの場合はスキップ
        }
    }

    public async Task<LoadedAssetFile> LoadFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var result = new LoadedAssetFile
        {
            FilePath = path,
            FileType = DetermineFileType(path)
        };

        try
        {
            if (!File.Exists(path))
            {
                result.Error = $"ファイルが見つかりません: {path}";
                return result;
            }

            var fileInfo = new FileInfo(path);
            
            // 大容量ファイルはストリーミング
            if (fileInfo.Length > 100 * 1024 * 1024) // 100MB以上
            {
                result.DataStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 
                    bufferSize: 64 * 1024, useAsync: true);
            }
            else
            {
                result.RawData = await File.ReadAllBytesAsync(path, cancellationToken);
            }

            // Unityバージョンを検出
            result.UnityVersion = DetectUnityVersionFromFile(path);
            
            // アセット情報を取得
            result.Assets = await GetAssetsAsync(path, cancellationToken);
            result.IsLoaded = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        return result;
    }

    public async Task<List<UnityAssetInfo>> GetAssetsAsync(string path, CancellationToken cancellationToken = default)
    {
        var assets = new List<UnityAssetInfo>();

        try
        {
            var fileType = DetermineFileType(path);

            switch (fileType)
            {
                case FileNodeType.AssetsFile:
                case FileNodeType.ResourcesAssets:
                    assets = await ParseAssetsFileAsync(path, cancellationToken);
                    break;

                case FileNodeType.AssetBundle:
                    assets = await ParseAssetBundleAsync(path, cancellationToken);
                    break;

                case FileNodeType.ResSFile:
                    assets = await ParseResSFileAsync(path, cancellationToken);
                    break;

                case FileNodeType.Assembly:
                    assets.Add(new UnityAssetInfo
                    {
                        Name = Path.GetFileName(path),
                        TypeName = "Assembly",
                        SourceFile = path,
                        Size = new FileInfo(path).Length
                    });
                    break;
            }
        }
        catch (Exception ex)
        {
            // エラーログを記録
            System.Diagnostics.Debug.WriteLine($"アセット解析エラー: {path} - {ex.Message}");
        }

        return assets;
    }

    private async Task<List<UnityAssetInfo>> ParseAssetsFileAsync(string path, CancellationToken cancellationToken)
    {
        var assets = new List<UnityAssetInfo>();

        await Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);

                // Unity SerializedFileヘッダーを読み取り
                var header = ReadSerializedFileHeader(reader);
                
                if (header.IsValid)
                {
                    // オブジェクト情報を読み取り
                    foreach (var obj in header.Objects)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        
                        assets.Add(new UnityAssetInfo
                        {
                            PathId = obj.PathId,
                            Name = obj.Name ?? $"Object_{obj.PathId}",
                            TypeName = obj.TypeName,
                            TypeId = obj.TypeId,
                            Offset = obj.Offset,
                            Size = obj.Size,
                            SourceFile = path
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Assets解析エラー: {ex.Message}");
            }
        }, cancellationToken);

        return assets;
    }

    private async Task<List<UnityAssetInfo>> ParseAssetBundleAsync(string path, CancellationToken cancellationToken)
    {
        var assets = new List<UnityAssetInfo>();

        await Task.Run(() =>
        {
            try
            {
                using var stream = File.OpenRead(path);
                using var reader = new BinaryReader(stream);

                // UnityFS/UnityWebヘッダーを確認
                var signature = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0');
                
                if (signature is "UnityFS" or "UnityWeb" or "UnityRaw")
                {
                    // バンドルヘッダーを解析
                    var bundleInfo = ParseUnityFSBundle(reader, path);
                    assets.AddRange(bundleInfo);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Bundle解析エラー: {ex.Message}");
            }
        }, cancellationToken);

        return assets;
    }

    private List<UnityAssetInfo> ParseUnityFSBundle(BinaryReader reader, string path)
    {
        var assets = new List<UnityAssetInfo>();

        try
        {
            // UnityFSヘッダー構造を解析
            reader.BaseStream.Position = 0;
            var signature = Encoding.ASCII.GetString(reader.ReadBytes(8)).TrimEnd('\0');
            var version = reader.ReadInt32BE();
            var unityVersion = ReadNullTerminatedString(reader);
            var unityRevision = ReadNullTerminatedString(reader);

            // バンドル全体をアセットとして登録
            assets.Add(new UnityAssetInfo
            {
                Name = Path.GetFileName(path),
                TypeName = "AssetBundle",
                SourceFile = path,
                Size = reader.BaseStream.Length
            });
        }
        catch
        {
            // 解析失敗時は空のリストを返す
        }

        return assets;
    }

    private async Task<List<UnityAssetInfo>> ParseResSFileAsync(string path, CancellationToken cancellationToken)
    {
        var assets = new List<UnityAssetInfo>();

        await Task.Run(() =>
        {
            // .resSファイルはヘッダーレスなので、関連する.assetsファイルとペアで処理
            var fileInfo = new FileInfo(path);
            
            assets.Add(new UnityAssetInfo
            {
                Name = fileInfo.Name,
                TypeName = "ResourceStream",
                SourceFile = path,
                Size = fileInfo.Length
            });
        }, cancellationToken);

        return assets;
    }

    public async Task<string?> DetectUnityVersionAsync(string dataFolderPath, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            // globalgamemanagersからバージョンを検出
            var ggmPath = Path.Combine(dataFolderPath, "globalgamemanagers");
            if (File.Exists(ggmPath))
            {
                return DetectUnityVersionFromFile(ggmPath);
            }

            // data.unityまたはmainDataからバージョンを検出
            var dataPath = Path.Combine(dataFolderPath, "data.unity3d");
            if (File.Exists(dataPath))
            {
                return DetectUnityVersionFromFile(dataPath);
            }

            var mainDataPath = Path.Combine(dataFolderPath, "mainData");
            if (File.Exists(mainDataPath))
            {
                return DetectUnityVersionFromFile(mainDataPath);
            }

            // level0から検出
            var level0Path = Path.Combine(dataFolderPath, "level0");
            if (File.Exists(level0Path))
            {
                return DetectUnityVersionFromFile(level0Path);
            }

            return null;
        }, cancellationToken);
    }

    private string? DetectUnityVersionFromFile(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);

            // ファイルの最初の部分を読み取り
            var buffer = new byte[Math.Min(4096, stream.Length)];
            reader.Read(buffer, 0, buffer.Length);

            // バージョン文字列を検索 (例: "2021.3.43f1")
            var content = Encoding.ASCII.GetString(buffer);
            var match = UnityVersionRegex().Match(content);
            
            if (match.Success)
            {
                return match.Value;
            }

            // UTF-8でも試行
            content = Encoding.UTF8.GetString(buffer);
            match = UnityVersionRegex().Match(content);
            
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }

    public void LinkResSFiles(FileTreeNode rootNode)
    {
        var resSFiles = new Dictionary<string, FileTreeNode>();
        var assetsFiles = new List<FileTreeNode>();

        // .resSファイルと.assetsファイルを収集
        CollectFiles(rootNode, resSFiles, assetsFiles);

        // リンクを作成
        foreach (var assetsNode in assetsFiles)
        {
            var baseName = Path.GetFileNameWithoutExtension(assetsNode.FullPath);
            var resSName = baseName + ".resS";
            
            // 同じディレクトリ内の.resSを検索
            var dir = Path.GetDirectoryName(assetsNode.FullPath) ?? "";
            var resSPath = Path.Combine(dir, resSName);

            if (resSFiles.TryGetValue(resSPath.ToLowerInvariant(), out var resSNode))
            {
                assetsNode.AssociatedResS = resSNode;
            }
        }
    }

    private void CollectFiles(FileTreeNode node, Dictionary<string, FileTreeNode> resSFiles, List<FileTreeNode> assetsFiles)
    {
        if (node.IsDirectory)
        {
            foreach (var child in node.Children)
            {
                CollectFiles(child, resSFiles, assetsFiles);
            }
        }
        else
        {
            if (node.NodeType == FileNodeType.ResSFile)
            {
                resSFiles[node.FullPath.ToLowerInvariant()] = node;
            }
            else if (node.NodeType is FileNodeType.AssetsFile or FileNodeType.ResourcesAssets)
            {
                assetsFiles.Add(node);
            }
        }
    }

    public bool IsSupportedFile(string path)
    {
        var fileName = Path.GetFileName(path).ToLowerInvariant();
        var extension = Path.GetExtension(path).ToLowerInvariant();

        // globalgamemanagers は特別扱い
        if (fileName is "globalgamemanagers" or "globalgamemanagers.assets")
            return true;

        // level* ファイル
        if (fileName.StartsWith("level") && !fileName.Contains('.'))
            return true;

        // mainData
        if (fileName == "maindata")
            return true;

        // sharedassets
        if (fileName.StartsWith("sharedassets") && fileName.EndsWith(".assets"))
            return true;

        // resources.assets
        if (fileName == "resources.assets")
            return true;

        // 拡張子でチェック
        foreach (var exts in _fileExtensions.Values)
        {
            if (exts.Any(e => extension.Equals(e, StringComparison.OrdinalIgnoreCase) || 
                             fileName.EndsWith(e, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private FileNodeType DetermineFileType(string path)
    {
        var fileName = Path.GetFileName(path).ToLowerInvariant();
        var extension = Path.GetExtension(path).ToLowerInvariant();

        if (fileName == "globalgamemanagers" || fileName == "globalgamemanagers.assets")
            return FileNodeType.GlobalGameManagers;

        if (fileName == "resources.assets")
            return FileNodeType.ResourcesAssets;

        if (extension is ".ress" || fileName.EndsWith(".ress"))
            return FileNodeType.ResSFile;

        if (extension == ".dll")
            return FileNodeType.Assembly;

        if (extension is ".bundle" or ".unity3d" or ".ab")
            return FileNodeType.AssetBundle;

        if (extension == ".assets" || fileName.StartsWith("sharedassets") || 
            fileName.StartsWith("level") || fileName == "maindata")
            return FileNodeType.AssetsFile;

        return FileNodeType.Other;
    }

    private SerializedFileHeader ReadSerializedFileHeader(BinaryReader reader)
    {
        var header = new SerializedFileHeader();

        try
        {
            // メタデータサイズを読み取り
            header.MetadataSize = reader.ReadInt32BE();
            header.FileSize = reader.ReadInt32BE();
            header.Version = reader.ReadInt32BE();
            header.DataOffset = reader.ReadInt32BE();

            // エンディアン
            if (header.Version >= 9)
            {
                header.IsBigEndian = reader.ReadByte() != 0;
                reader.ReadBytes(3); // reserved
            }

            // バージョン22以降は追加ヘッダーあり
            if (header.Version >= 22)
            {
                header.MetadataSize = reader.ReadInt32();
                header.FileSize = reader.ReadInt64();
                header.DataOffset = reader.ReadInt64();
                reader.ReadInt64(); // unknown
            }

            header.IsValid = header.MetadataSize > 0 && header.FileSize > 0;
        }
        catch
        {
            header.IsValid = false;
        }

        return header;
    }

    private static string ReadNullTerminatedString(BinaryReader reader)
    {
        var bytes = new List<byte>();
        byte b;
        while ((b = reader.ReadByte()) != 0)
        {
            bytes.Add(b);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }

    [GeneratedRegex(@"\d{4}\.\d+\.\d+[a-zA-Z]\d+")]
    private static partial Regex UnityVersionRegex();

    private class SerializedFileHeader
    {
        public int MetadataSize { get; set; }
        public long FileSize { get; set; }
        public int Version { get; set; }
        public long DataOffset { get; set; }
        public bool IsBigEndian { get; set; }
        public bool IsValid { get; set; }
        public List<SerializedObject> Objects { get; set; } = new();
    }

    private class SerializedObject
    {
        public long PathId { get; set; }
        public string? Name { get; set; }
        public string TypeName { get; set; } = "Unknown";
        public int TypeId { get; set; }
        public long Offset { get; set; }
        public long Size { get; set; }
    }
}

/// <summary>
/// BinaryReader拡張メソッド
/// </summary>
public static class BinaryReaderExtensions
{
    public static int ReadInt32BE(this BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToInt32(bytes, 0);
    }

    public static long ReadInt64BE(this BinaryReader reader)
    {
        var bytes = reader.ReadBytes(8);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToInt64(bytes, 0);
    }

    public static uint ReadUInt32BE(this BinaryReader reader)
    {
        var bytes = reader.ReadBytes(4);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return BitConverter.ToUInt32(bytes, 0);
    }
}
