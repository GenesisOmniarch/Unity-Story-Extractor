# Unity Story Extractor

Unity 2021.3.43f1 対応ストーリー抽出ツール

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Unity](https://img.shields.io/badge/Unity-2021.3.43f1-000000)](https://unity.com/)

## 概要

Unity Story Extractorは、Unity 2021.3.43f1でビルドされたゲームからストーリー関連データ（テキスト、ダイアログ、イベントシーケンスなど）を抽出するためのC#ベースのアプリケーションです。

### 主な機能

- 📁 **アセットファイル解析**: .assets, AssetBundle, resources.assets, .resSファイルの読み込み
- 📝 **テキスト抽出**: TextAsset、MonoBehaviour、ScriptableObjectからの文字列抽出
- 🔧 **アセンブリデコンパイル**: Assembly-CSharp.dllからハードコードされた文字列を抽出
- 🔐 **暗号化対応**: XOR、AES、Base64などの一般的な暗号化の検出と復号
- 🌍 **日本語対応**: UTF-8、Shift-JIS、UTF-16エンコーディングの自動検出
- 🖥️ **GUIインターフェース**: WPFベースのモダンUI（Material Design）
- 💻 **CLIモード**: バッチ処理対応のコマンドラインインターフェース
- ⚡ **高速処理**: 並列処理とストリーミング読み込みによる大容量ファイル対応

## ⚠️ 著作権に関する注意

このツールは**教育・研究目的**で設計されています。ゲームデータの抽出は、著作権法で保護されているコンテンツに対して行う場合、法的問題が発生する可能性があります。

- 使用者は、自身の行為に対する法的責任を負います
- 正規にライセンスされたコンテンツのみを対象としてください
- 著作権者の権利を尊重してください

## システム要件

- **OS**: Windows 10/11（64bit）
- **ランタイム**: .NET 8.0 Runtime
- **メモリ**: 4GB以上（推奨: 8GB以上）
- **ストレージ**: 100MB以上の空き容量

## インストール

### 前提条件

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### ビルド方法

```bash
# リポジトリのクローン
git clone https://github.com/yourusername/UnityStoryExtractor.git
cd UnityStoryExtractor

# ビルド
dotnet build

# テスト実行
dotnet test

# 公開ビルド（Windows用）
dotnet publish src/UnityStoryExtractor.GUI -c Release -r win-x64 --self-contained
```

## 使用方法

### GUIモード

1. `UnityStoryExtractor.exe`を起動
2. 著作権警告を確認して同意
3. 「フォルダを開く」またはドラッグ＆ドロップでゲームのDataフォルダを選択
4. ファイルツリーから対象を選択
5. 「抽出開始」をクリック
6. 結果をプレビューし、「保存」でエクスポート

### CLIモード

```bash
# 基本的な使用方法
UnityStoryExtractor.CLI -i "C:\Games\GameName\Data" -o output.json

# オプション一覧
UnityStoryExtractor.CLI --help

# 詳細オプション付き
UnityStoryExtractor.CLI \
  -i "C:\Games\GameName\Data" \
  -o output.json \
  -f json \
  -k "dialogue,story" \
  -m 5 \
  -j \
  -v
```

#### CLIオプション

| オプション | 短縮形 | 説明 |
|-----------|--------|------|
| `--input` | `-i` | 入力パス（必須） |
| `--output` | `-o` | 出力ファイルパス（必須） |
| `--format` | `-f` | 出力形式（json, txt, csv, xml） |
| `--keywords` | `-k` | キーワードフィルタ |
| `--min-length` | `-m` | 最小テキスト長 |
| `--parallel` | `-p` | 並列処理の使用 |
| `--verbose` | `-v` | 詳細出力 |
| `--japanese` | `-j` | 日本語テキストを優先 |
| `--decrypt-key` | `-d` | 復号キー（Base64） |

## 出力形式

### JSON
```json
{
  "sourcePath": "/path/to/game",
  "unityVersion": "2021.3.43f1",
  "extractedTexts": [
    {
      "assetName": "Dialogue_01",
      "assetType": "TextAsset",
      "content": "こんにちは、冒険者よ。",
      "source": "TextAsset"
    }
  ]
}
```

### Text
```
================================================================================
Unity Story Extractor - 抽出結果
================================================================================
ソースパス: /path/to/game
抽出アイテム数: 100
...
```

## プロジェクト構造

```
UnityStoryExtractor/
├── src/
│   ├── UnityStoryExtractor.Core/      # コアライブラリ
│   │   ├── Models/                    # データモデル
│   │   ├── Loader/                    # ファイルローダー
│   │   ├── Parser/                    # アセットパーサー
│   │   ├── Extractor/                 # テキスト抽出器
│   │   ├── Decryptor/                 # 復号モジュール
│   │   └── Output/                    # 出力ライター
│   ├── UnityStoryExtractor.GUI/       # WPF GUIアプリケーション
│   └── UnityStoryExtractor.CLI/       # CLIアプリケーション
├── tests/
│   └── UnityStoryExtractor.Tests/     # ユニット/統合テスト
└── README.md
```

## アーキテクチャ

### モジュール構成

1. **Loader**: ファイルの読み込みとディレクトリスキャン
2. **Parser**: アセットファイルの解析
3. **Extractor**: テキストコンテンツの抽出
4. **Decryptor**: 暗号化データの復号
5. **Outputter**: 結果の出力とフォーマット

### データフロー

```
入力フォルダ → Loader → Parser → Extractor → Decryptor → Outputter → 出力ファイル
                ↓
            TreeView表示
```

## 対応ファイル形式

| 形式 | 説明 | 対応状況 |
|------|------|----------|
| `.assets` | Unityアセットファイル | ✅ |
| `sharedassets*.assets` | 共有アセット | ✅ |
| `resources.assets` | ビルトインリソース | ✅ |
| `.resS` | リソースストリーム | ✅ |
| `.bundle` / `.unity3d` / `.ab` | AssetBundle | ✅ |
| `Assembly-CSharp.dll` | ゲームアセンブリ | ✅ |
| `globalgamemanagers` | ゲーム設定 | ✅ |
| `level*` | レベルファイル | ✅ |

## 暗号化対応

| 暗号化方式 | 自動検出 | 復号 |
|-----------|----------|------|
| XOR | ✅ | ✅ |
| Base64 | ✅ | ✅ |
| AES | ✅ | ⚠️ キー必要 |
| カスタム | ❌ | ⚠️ プラグイン |

## 拡張機能

### カスタム復号プラグイン

```csharp
public class CustomDecryptor : IDecryptor
{
    public string Name => "CustomDecryptor";
    public EncryptionType EncryptionType => EncryptionType.Custom;

    public byte[] Decrypt(byte[] data, byte[]? key = null)
    {
        // カスタム復号ロジック
    }

    public bool CanDecrypt(byte[] data) => true;
    public EncryptionDetectionResult Detect(byte[] data) => ...;
}
```

## トラブルシューティング

### よくある問題

**Q: ファイルが読み込めない**
- ファイルパスに日本語やスペースが含まれている場合は、パスを引用符で囲んでください
- ファイルが他のプロセスで使用中でないか確認してください

**Q: テキストが文字化けする**
- エンコーディングが自動検出できない場合があります
- UTF-8、Shift-JIS、UTF-16が自動検出対象です

**Q: 抽出結果が空**
- キーワードフィルタが厳しすぎる可能性があります
- 最小テキスト長を下げてみてください

## ライセンス

MIT License

## 謝辞

- [AssetsTools.NET](https://github.com/nesrak1/AssetsTools.NET) - Unityアセット解析
- [ILSpy](https://github.com/icsharpcode/ILSpy) - C#デコンパイル
- [Material Design In XAML](https://github.com/MaterialDesignInXAML/MaterialDesignInXamlToolkit) - UIフレームワーク

## 更新履歴

### v1.0.0
- 初回リリース
- 基本的なアセット解析機能
- GUI/CLI両対応
- 日本語ローカライズ

---

**注意**: このツールは非公式のサードパーティツールです。Unity Technologies とは関係ありません。
