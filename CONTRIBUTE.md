# Contribute

[English](CONTRIBUTE.en.md) | [日本語](CONTRIBUTE.ja.md)

本文档面向开发者。普通使用说明见 [README.md](README.md)。

## 环境

- Windows 10 19041 或更高版本。
- [.NET 8 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)。
- 可选：Visual Studio 2022 或 Rider。
- 可选：打安装包需要 [Inno Setup 6](https://jrsoftware.org/isinfo.php)。

NuGet 依赖由 `dotnet restore` 获取：

- `WeChatOcr` 1.0.5。
- `Microsoft.ML.OnnxRuntime` 1.20.1。

## 构建

```powershell
dotnet restore src\GenshinChatTranslator.App\GenshinChatTranslator.App.csproj
dotnet build src\GenshinChatTranslator.App\GenshinChatTranslator.App.csproj -c Release
```

发布自包含 Windows x64 目录：

```powershell
.\scripts\package.ps1 -SkipInstaller
```

生成安装包：

```powershell
.\scripts\package.ps1
```

## OCR 资源

### WeChat OCR

项目通过 NuGet 包 `WeChatOcr` 接入 WeChat OCR。上游仓库是 [ZGGSONG/WeChatOcr](https://github.com/ZGGSONG/WeChatOcr)，目前没有明确开源许可证文件，并且 README 写明偏学习用途。

源码仓库不要提交 WeChat OCR 相关二进制。release 如果包含这些文件，必须在包内保留 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)，并明确它们不属于本项目 MIT License 的授权范围。

### PaddleOCR ONNX

源码仓库不要提交 Paddle 模型文件。需要从 [PaddleOCR 官方仓库](https://github.com/PaddlePaddle/PaddleOCR) 或 PaddleX/ModelScope/HuggingFace 等官方发布渠道获取 PP-OCR 识别模型，并导出或下载 ONNX 格式。

本项目当前只加载识别模型和字典：

```text
models/
  ocr/
    paddle/
      ppocr_v5_mobile/
        rec.onnx
        dict.txt
        info.json
```

步骤：

1. 获取 PP-OCRv5 mobile recognition 模型，导出为 ONNX 后命名为 `rec.onnx`。
2. 获取匹配的字符字典，例如 PaddleOCR 的 `ppocr_keys_v5.txt`，复制为 `dict.txt`。
3. 可选写入 `info.json`，例如 `{"languages":["zh","en","cht","ja"],"name":"PP-OCRv5_mobile"}`。
4. 确认 [config/ocr.yml](config/ocr.yml) 中的路径仍指向 `models/ocr/paddle/ppocr_v5_mobile/rec.onnx` 和 `dict.txt`。

注意：当前 Paddle 引擎不读取 `det.onnx`；聊天气泡检测由本项目自己的 ROI 检测器完成。

## 配置与密钥

默认配置位于 [config](config)。首次运行时应用会把默认配置复制到用户配置目录。

默认配置中的 `api_key` 必须保持为空。开发测试时优先使用环境变量：

```powershell
$env:OPENAI_API_KEY = "your-key"
$env:OPENAI_COMPATIBLE_API_KEY = "your-key"
```

不要提交 `.env`、本地配置、证书、私钥、截图样本、模型文件或任何包含 API key 的文件。

## 许可证与 release 检查

本项目源码使用 MIT License，见 [LICENSE](LICENSE)。

release 包若包含第三方二进制或模型文件，必须同时包含：

- [LICENSE](LICENSE)。
- [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。
- [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt)，用于 PaddleOCR。
- [licenses/ONNXRuntime-MIT.txt](licenses/ONNXRuntime-MIT.txt)，用于 ONNX Runtime。

WeChatOcr 上游未声明开源许可证；如果 release 包含相关二进制，需要在 release 说明中再次提醒用户：这些文件不由本项目 MIT License 授权。
