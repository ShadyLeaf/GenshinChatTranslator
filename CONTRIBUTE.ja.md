# Contribute

[中文](CONTRIBUTE.md) | [English](CONTRIBUTE.en.md)

この文書は開発者向けです。通常の使い方は [README.ja.md](README.ja.md) を参照してください。

## 環境

- Windows 10 19041 以降。
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)。
- 任意: Visual Studio 2022 または Rider。
- 任意: インストーラー作成には [Inno Setup 6](https://jrsoftware.org/isinfo.php)。

NuGet 依存関係は `dotnet restore` で取得されます。

- `WeChatOcr` 1.0.5。
- `Microsoft.ML.OnnxRuntime` 1.20.1。

## ビルド

```powershell
dotnet restore src\GenshinChatTranslator.App\GenshinChatTranslator.App.csproj
dotnet build src\GenshinChatTranslator.App\GenshinChatTranslator.App.csproj -c Release
```

Windows x64 の自己完結ディレクトリを作成:

```powershell
.\scripts\package.ps1 -SkipInstaller
```

インストーラーを作成:

```powershell
.\scripts\package.ps1
```

## OCR リソース

### WeChat OCR

このプロジェクトは `WeChatOcr` NuGet パッケージ経由で WeChat OCR を利用します。上流リポジトリは [ZGGSONG/WeChatOcr](https://github.com/ZGGSONG/WeChatOcr) です。現時点では明示的なオープンソースライセンスファイルがなく、README では学習用途であることが示されています。

WeChat OCR 関連バイナリをソースリポジトリにコミットしないでください。release パッケージに含める場合は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を同梱し、それらが本プロジェクトの MIT License の対象外であることを明記してください。

### PaddleOCR ONNX

Paddle のモデルファイルをソースリポジトリにコミットしないでください。[PaddleOCR 公式リポジトリ](https://github.com/PaddlePaddle/PaddleOCR)、または公式の PaddleX / ModelScope / HuggingFace 配布から PP-OCR 認識モデルを取得し、ONNX 形式でエクスポートまたはダウンロードしてください。

現在のアプリは認識モデルと辞書のみを読み込みます。

```text
models/
  ocr/
    paddle/
      ppocr_v5_mobile/
        rec.onnx
        dict.txt
        info.json
```

手順:

1. PP-OCRv5 mobile recognition モデルを取得し、ONNX として `rec.onnx` という名前で保存します。
2. 対応する文字辞書、たとえば PaddleOCR の `ppocr_keys_v5.txt` を取得し、`dict.txt` としてコピーします。
3. 任意で `info.json` を追加します。例: `{"languages":["zh","en","cht","ja"],"name":"PP-OCRv5_mobile"}`。
4. [config/ocr.yml](config/ocr.yml) が `models/ocr/paddle/ppocr_v5_mobile/rec.onnx` と `dict.txt` を指していることを確認します。

注意: 現在の Paddle エンジンは `det.onnx` を読み込みません。チャット吹き出し検出はこのアプリ側の ROI 検出器で行います。

## 設定と秘密情報

既定設定は [config](config) にあります。初回起動時に、アプリは既定設定をユーザー設定ディレクトリへコピーします。

既定設定の `api_key` は必ず空のままにしてください。開発時は環境変数を優先してください。

```powershell
$env:OPENAI_API_KEY = "your-key"
$env:OPENAI_COMPATIBLE_API_KEY = "your-key"
```

`.env`、ローカル設定、証明書、秘密鍵、スクリーンショットサンプル、モデルファイル、API key を含むファイルをコミットしないでください。

## ライセンスと release チェック

本プロジェクトのソースコードは MIT License です。[LICENSE](LICENSE) を参照してください。

release パッケージに第三者バイナリまたはモデルファイルを含める場合、次のファイルも同梱してください。

- [LICENSE](LICENSE)。
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
- PaddleOCR 用の [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt)。
- ONNX Runtime 用の [licenses/ONNXRuntime-MIT.txt](licenses/ONNXRuntime-MIT.txt)。

WeChatOcr は上流でオープンソースライセンスを明示していません。release に関連バイナリを含める場合、それらが本プロジェクトの MIT License では許諾されないことを release notes でも再度明記してください。
