# Contribute

[中文](CONTRIBUTE.md) | [日本語](CONTRIBUTE.ja.md)

This document is for developers. End-user usage is covered in [README.en.md](README.en.md).

## Environment

- Windows 10 19041 or later.
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0).
- Optional: Visual Studio 2022 or Rider.
- Optional: [Inno Setup 6](https://jrsoftware.org/isinfo.php) for installer builds.

NuGet dependencies are restored by `dotnet restore`:

- `WeChatOcr` 1.0.5.
- `Microsoft.ML.OnnxRuntime` 1.20.1.

## Build

```powershell
dotnet restore src\GenshinChatTranslator.App\GenshinChatTranslator.App.csproj
dotnet build src\GenshinChatTranslator.App\GenshinChatTranslator.App.csproj -c Release
```

Publish a self-contained Windows x64 directory:

```powershell
.\scripts\package.ps1 -SkipInstaller
```

Create an installer:

```powershell
.\scripts\package.ps1
```

## OCR Assets

### WeChat OCR

The project integrates WeChat OCR through the `WeChatOcr` NuGet package. The upstream repository is [ZGGSONG/WeChatOcr](https://github.com/ZGGSONG/WeChatOcr). It currently has no explicit open-source license file, and its README describes the project as being for study/learning purposes.

Do not commit WeChat OCR binaries to the source repository. If release packages include them, keep [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) in the package and make clear that they are not covered by this project's MIT License.

### PaddleOCR ONNX

Do not commit Paddle model files to the source repository. Get a PP-OCR recognition model from the [official PaddleOCR repository](https://github.com/PaddlePaddle/PaddleOCR) or official PaddleX/ModelScope/HuggingFace releases, then export or download it in ONNX format.

The current app loads only the recognition model and dictionary:

```text
models/
  ocr/
    paddle/
      ppocr_v5_mobile/
        rec.onnx
        dict.txt
        info.json
```

Steps:

1. Get the PP-OCRv5 mobile recognition model and export it as ONNX named `rec.onnx`.
2. Get the matching character dictionary, for example PaddleOCR `ppocr_keys_v5.txt`, and copy it as `dict.txt`.
3. Optionally add `info.json`, for example `{"languages":["zh","en","cht","ja"],"name":"PP-OCRv5_mobile"}`.
4. Ensure [config/ocr.yml](config/ocr.yml) still points to `models/ocr/paddle/ppocr_v5_mobile/rec.onnx` and `dict.txt`.

Note: the current Paddle engine does not load `det.onnx`; chat bubble detection is handled by this app's ROI detector.

## Config and Secrets

Default config files live in [config](config). On first run, the app copies them to the user config directory.

Default `api_key` values must stay empty. For development, prefer environment variables:

```powershell
$env:OPENAI_API_KEY = "your-key"
$env:OPENAI_COMPATIBLE_API_KEY = "your-key"
```

Do not commit `.env`, local config, certificates, private keys, screenshot samples, model files, or any file containing API keys.

## License and Release Checklist

This project's source code is licensed under the MIT License. See [LICENSE](LICENSE).

If a release package includes third-party binaries or model files, it must also include:

- [LICENSE](LICENSE).
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
- [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt) for PaddleOCR.
- [licenses/ONNXRuntime-MIT.txt](licenses/ONNXRuntime-MIT.txt) for ONNX Runtime.

WeChatOcr does not declare an upstream open-source license. If a release includes related binaries, repeat in the release notes that those files are not licensed by this project's MIT License.
