# UnityStoryExtractor 引継ぎ情報

## 1. プロジェクト概要

### 目的
Unity 2021.3.43f1で作成されたゲームから、ストーリー関連データ（テキスト、ダイアログ、イベントシーケンス）を抽出するツール。

### 技術スタック
- **言語**: C# (.NET 8.0)
- **GUI**: WPF (Windows Presentation Foundation)
- **CLI**: System.CommandLine
- **ターゲット**: Windows x64 (self-contained)

---

## 2. プロジェクト構造

```
/workspace/
├── src/
│   ├── UnityStoryExtractor.Core/     # コアライブラリ
│   │   ├── Models/                   # データモデル
│   │   ├── Loader/                   # アセットローダー
│   │   ├── Parser/                   # パーサー
│   │   ├── Extractor/                # 抽出ロジック
│   │   ├── Decryptor/                # 復号化
│   │   ├── Output/                   # 出力フォーマッター
│   │   └── Resources/                # 日本語リソース
│   ├── UnityStoryExtractor.GUI/      # WPF GUIアプリ
│   │   ├── Views/                    # XAML画面
│   │   ├── ViewModels/               # MVVM ViewModel
│   │   └── Converters/               # 値コンバーター
│   └── UnityStoryExtractor.CLI/      # CLIアプリ
├── tests/
│   └── UnityStoryExtractor.Tests/    # ユニットテスト
├── publish/                          # ビルド出力
├── UnityStoryExtractor.sln           # ソリューションファイル
├── Directory.Build.props             # 共通ビルド設定
└── README.md                         # ユーザー向けドキュメント
```

---

## 3. 主要コンポーネント

### 3.1 Core ライブラリ

#### Models (`src/UnityStoryExtractor.Core/Models/`)
| ファイル | 説明 |
|---------|------|
| `UnityAssetInfo.cs` | Unityアセット情報 |
| `ExtractedText.cs` | 抽出テキストデータ |
| `FileTreeNode.cs` | ファイルツリー構造 |
| `ExtractionResult.cs` | 抽出結果・統計 |
| `ExtractionOptions.cs` | 抽出オプション設定 |

#### Loader (`src/UnityStoryExtractor.Core/Loader/`)
| ファイル | 説明 |
|---------|------|
| `IAssetLoader.cs` | ローダーインターフェース |
| `UnityAssetLoader.cs` | ディレクトリスキャン、バージョン検出 |

#### Parser (`src/UnityStoryExtractor.Core/Parser/`)
| ファイル | 説明 |
|---------|------|
| `IAssetParser.cs` | パーサーインターフェース |
| `TextAssetParser.cs` | TextAsset解析（UTF-8/UTF-16検索） |
| `MonoBehaviourParser.cs` | MonoBehaviour解析 |
| `AssemblyParser.cs` | DLL文字列抽出 |

#### Extractor (`src/UnityStoryExtractor.Core/Extractor/`)
| ファイル | 説明 |
|---------|------|
| `IStoryExtractor.cs` | 抽出インターフェース |
| `StoryExtractor.cs` | **メイン抽出ロジック**（並列処理、ストリーミング） |

#### Decryptor (`src/UnityStoryExtractor.Core/Decryptor/`)
| ファイル | 説明 |
|---------|------|
| `IDecryptor.cs` | 復号インターフェース |
| `DecryptorManager.cs` | XOR/Base64/AES復号 |

#### Output (`src/UnityStoryExtractor.Core/Output/`)
| ファイル | 説明 |
|---------|------|
| `IOutputWriter.cs` | 出力インターフェース |
| `OutputWriterFactory.cs` | JSON/TXT/CSV/XML出力 |

### 3.2 GUI アプリ

#### 主要ファイル
| ファイル | 説明 |
|---------|------|
| `App.xaml.cs` | アプリケーションエントリ、グローバルエラーハンドリング |
| `Views/MainWindow.xaml` | メインUI（XAML） |
| `Views/MainWindow.xaml.cs` | コードビハインド |
| `ViewModels/MainViewModel.cs` | **メインロジック**（MVVM） |
| `Views/SettingsWindow.xaml` | 設定ダイアログ |

---

## 4. ビルド方法

### 前提条件
- .NET 8.0 SDK

### ビルドコマンド
```bash
# デバッグビルド
dotnet build

# リリースビルド
dotnet build -c Release

# Windows x64向けパブリッシュ（self-contained）
dotnet publish src/UnityStoryExtractor.GUI -c Release -r win-x64 --self-contained -o ./publish/GUI
dotnet publish src/UnityStoryExtractor.CLI -c Release -r win-x64 --self-contained -o ./publish/CLI

# ZIP作成
cd publish
zip -r ../UnityStoryExtractor-GUI-win-x64.zip GUI/
zip -r ../UnityStoryExtractor-CLI-win-x64.zip CLI/
```

### Linux環境でのビルド
WPFはWindows専用のため、`.csproj`に以下が必要：
```xml
<EnableWindowsTargeting>true</EnableWindowsTargeting>
```

---

## 5. 設計上の重要なポイント

### 5.1 非同期処理
- すべてのI/O処理は`async/await`
- 大量ファイル処理は`Parallel.ForEachAsync`
- UIスレッドへの更新は`Dispatcher.Invoke`

### 5.2 メモリ管理
- 大容量ファイル（200MB+）はストリーミング処理
- メモリ1.5GB超でGC自動実行
- `OutOfMemoryException`は自動スキップ

### 5.3 エラーハンドリング
- 各ファイル処理は独立してtry-catch
- エラー発生時はログ記録してスキップ（全体処理は継続）
- タイムアウト設定（プレビュー30秒、抽出5分）

### 5.4 出力先
- すべての出力は `[アプリ実行フォルダ]/Output/` に統一
- `App.OutputFolder` で参照可能
- ログファイル: `UnityStoryExtractor_Error.log`, `extraction_log.txt`

---

## 6. 対応ファイル形式

| 形式 | 拡張子/ファイル名 | 対応状況 |
|-----|-----------------|---------|
| Assets | `.assets`, `sharedassets*.assets` | ✅ |
| AssetBundle | `.bundle`, `.unity3d`, `.ab` | ✅ |
| Resources | `resources.assets` | ✅ |
| ResS | `.resS` | ✅ |
| Assembly | `.dll` | ✅ |
| GlobalGameManagers | `globalgamemanagers` | ✅（バージョン検出用） |

---

## 7. 既知の課題・制限事項

### 7.1 未完全対応
1. **暗号化データ**: 一部ゲームはカスタム暗号化を使用しており、自動復号できない場合がある
2. **IL2CPP**: `libil2cpp.so`の完全解析は未実装
3. **AssetBundle圧縮**: LZ4/LZMA圧縮の完全展開は`AssetsTools.NET`に依存

### 7.2 パフォーマンス
- 10GB+のDataフォルダでは処理に数分かかる場合がある
- 並列度はデフォルト4（設定で変更可能）

### 7.3 テスト
- 実際のゲームデータでの検証が必要
- `tests/`のユニットテストは基本機能のみ

---

## 8. 今後の改善案

### 短期
1. **AssetsTools.NET統合強化**: 現在は文字列検索ベースだが、正式なUnityアセット解析を追加
2. **プレビュー機能強化**: キーワードハイライト、検索機能
3. **バッチ処理**: 複数フォルダの一括処理

### 中期
1. **プラグインシステム**: カスタム復号プラグインの読み込み
2. **翻訳支援**: 抽出テキストの翻訳ワークフロー統合
3. **差分比較**: バージョン間のテキスト差分検出

### 長期
1. **クロスプラットフォーム**: Avalonia UIへの移行（macOS/Linux対応）
2. **AI連携**: 自動テキスト分類、キャラクター識別

---

## 9. 重要なファイル詳細

### `StoryExtractor.cs` - 抽出エンジン

```csharp
// 主要メソッド
Task<ExtractionResult> ExtractFromDirectoryAsync(
    string directoryPath,
    ExtractionOptions options,
    IProgress<ExtractionProgress>? progress,
    CancellationToken cancellationToken)

// 処理フロー
1. ディレクトリスキャン（UnityAssetLoader）
2. ファイル収集（CollectFiles）
3. 並列処理（Parallel.ForEachAsync）
4. 各ファイル解析（ExtractFromSingleFileAsync）
5. 結果集約（ConcurrentBag）
```

### `MainViewModel.cs` - GUIロジック

```csharp
// 主要プロパティ
ObservableCollection<FileTreeNodeViewModel> FileTreeNodes  // ツリービュー
ObservableCollection<ExtractedText> ExtractedResults       // 抽出結果
string OutputFolderPath                                     // 出力先
bool IsExtracting                                           // 処理中フラグ

// 主要コマンド
OpenFolderCommand    // フォルダ選択
ExtractCommand       // 抽出開始
CancelCommand        // キャンセル
ExportCommand        // 結果保存
```

---

## 10. デバッグ方法

### ログ確認
1. GUI: 「ログ」タブでリアルタイム確認
2. ファイル: `Output/extraction_log.txt`
3. エラー: `Output/UnityStoryExtractor_Error.log`

### Visual Studioデバッグ
```
1. UnityStoryExtractor.slnを開く
2. UnityStoryExtractor.GUIをスタートアッププロジェクトに設定
3. F5でデバッグ実行
```

### ブレークポイント推奨箇所
- `StoryExtractor.ExtractFromSingleFileAsync` - ファイル解析
- `TextAssetParser.FindTextChunks` - テキスト検索
- `MainViewModel.ExtractAsync` - GUI抽出処理

---

## 11. 依存パッケージ

### Core
| パッケージ | バージョン | 用途 |
|-----------|-----------|------|
| AssetsTools.NET | 3.0.0 | Unity asset解析（参照のみ） |
| K4os.Compression.LZ4 | 1.3.8 | LZ4圧縮 |
| ICSharpCode.Decompiler | 8.2.0 | DLL解析 |
| System.Text.Json | 8.0.5 | JSON処理 |
| Newtonsoft.Json | 13.0.3 | JSON処理（互換性） |
| System.Text.Encoding.CodePages | 8.0.0 | Shift-JIS対応 |

### GUI
| パッケージ | バージョン | 用途 |
|-----------|-----------|------|
| CommunityToolkit.Mvvm | 8.4.0 | MVVM フレームワーク |

### CLI
| パッケージ | バージョン | 用途 |
|-----------|-----------|------|
| System.CommandLine | 2.0.0-beta4 | コマンドライン解析 |

---

## 12. 連絡先・リソース

### リポジトリ
- 現在のワークスペース: `/workspace/`

### 配布ファイル
- `/workspace/UnityStoryExtractor-GUI-win-x64.zip` (69 MB)
- `/workspace/UnityStoryExtractor-CLI-win-x64.zip` (34 MB)

### 参考資料
- [Unity Asset File Format](https://github.com/Unity-Technologies/AssetRipper/wiki)
- [AssetsTools.NET Documentation](https://github.com/nesrak1/AssetsTools.NET)
- [WPF MVVM Pattern](https://docs.microsoft.com/en-us/dotnet/architecture/maui/mvvm)

---

## 13. クイックスタート（開発者向け）

```bash
# 1. リポジトリを取得
cd /workspace

# 2. ビルド
dotnet build -c Release

# 3. テスト実行
dotnet test

# 4. GUIをデバッグ実行（Windows）
dotnet run --project src/UnityStoryExtractor.GUI

# 5. CLIを実行
dotnet run --project src/UnityStoryExtractor.CLI -- --help
```

---

**最終更新**: 2024年12月19日
**作成者**: AI Assistant (Claude)
