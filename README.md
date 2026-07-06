# Genshin Chat Translator

[English](README.en.md) | [日本語](README.ja.md)

Genshin Chat Translator 是一个非官方 Windows 桌面工具，用于识别原神聊天窗口里的文字，并把翻译结果显示在覆盖层上。

## 使用

1. 从 GitHub Releases 下载 `GenshinChatTranslator-Setup-*.exe`。
2. 安装后启动 `Genshin Chat Translator`。
3. 在应用里选择目标语言和翻译方式。
4. 打开原神，进入聊天界面。
5. 保持游戏窗口可见，应用会捕获聊天区域、识别文字并显示翻译覆盖层。

## 支持范围

- 系统：Windows 10 19041 或更高版本。
- 游戏窗口：目前只保证 16:9 画面比例下的效果。
- 其它比例：也许能凑合用，但目前没有专门适配。
- 输入布局：支持键鼠、Xbox 手柄、DualSense 手柄布局。
- OCR：release 包通常会包含 WeChat OCR 和 PaddleOCR 相关运行时/模型文件，源码仓库不包含这些二进制文件。

## Windows 11 BitBlt 兼容修复

本工具使用 BitBlt 捕获游戏窗口内容。Windows 11 的窗口化优化可能导致 BitBlt 截到黑屏或旧帧，因此应用默认启用“自动关闭 Windows 11 窗口化优化以支持 BitBlt”。

该选项会写入当前用户的 DirectX 偏好：

```text
HKCU\Software\Microsoft\DirectX\UserGpuPreferences
DirectXUserGlobalSettings = SwapEffectUpgradeEnable=0;
```

如果修改该选项时游戏已经在运行，需要重启游戏后才会生效。关闭该选项只会停止后续自动写入；如需恢复 Windows 图形偏好，可使用应用内“手动设置”按钮打开 Windows 的高级图形设置页面。

## 限制

- 这是一个面向个人使用的原型工具，不保证所有分辨率、UI 缩放、滤镜、语言组合和游戏版本都稳定可用。
- OCR 和翻译结果可能出错，尤其是背景复杂、聊天气泡被遮挡、字体很小或网络翻译服务不可用时。
- WeChat OCR 相关上游项目没有明确开源许可证；release 中若包含相关二进制，它们不属于本项目 MIT 许可证覆盖范围。
- PaddleOCR 使用 Apache License 2.0；相关二进制和模型文件会按其上游许可证单独处理。

## 配置

应用会在用户目录保存配置。请不要把包含 API key 的本地配置公开分享。

OpenAI-compatible LLM 翻译可通过界面配置，也可以使用环境变量：

```powershell
$env:OPENAI_API_KEY = "your-key"
$env:OPENAI_COMPATIBLE_API_KEY = "your-key"
```

## 许可证

本项目源码使用 [MIT License](LICENSE)。

第三方组件和 release 二进制的许可证说明见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。开发、构建和贡献说明见 [CONTRIBUTE.md](CONTRIBUTE.md)。
