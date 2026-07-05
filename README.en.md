# Genshin Chat Translator

[中文](README.md) | [日本語](README.ja.md)

Genshin Chat Translator is an unofficial Windows desktop tool that recognizes text in the Genshin Impact chat window and displays translated text in an overlay.

## Usage

1. Download `GenshinChatTranslator-Setup-*.exe` from GitHub Releases.
2. Install and launch `Genshin Chat Translator`.
3. Select the target language and translation method in the app.
4. Open Genshin Impact and enter the chat screen.
5. Keep the game window visible. The app captures the chat area, recognizes text, and shows translated overlay text.

## Supported Scope

- OS: Windows 10 19041 or later.
- Game window: only 16:9 layouts are currently guaranteed.
- Other aspect ratios: they may work, but are not specifically adapted yet.
- Input layouts: keyboard/mouse, Xbox controller, and DualSense controller layouts.
- OCR: release packages usually include WeChat OCR and PaddleOCR runtime/model binaries. These binaries are not stored in the source repository.

## Limitations

- This is a personal-use prototype. It is not guaranteed to work reliably across all resolutions, UI scales, filters, language combinations, or game versions.
- OCR and translation can be wrong, especially with complex backgrounds, covered chat bubbles, small text, or unavailable translation services.
- The upstream WeChat OCR project does not declare an explicit open-source license. If related binaries are included in a release, they are not covered by this project's MIT License.
- PaddleOCR uses the Apache License 2.0. Related binaries and model files are handled under their upstream license.

## Configuration

The app stores user configuration in the user profile. Do not publicly share local config files that contain API keys.

OpenAI-compatible LLM translation can be configured in the UI or through environment variables:

```powershell
$env:OPENAI_API_KEY = "your-key"
$env:OPENAI_COMPATIBLE_API_KEY = "your-key"
```

## License

This project's source code is licensed under the [MIT License](LICENSE).

Third-party component and release-binary notices are in [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md). Development, build, and contribution notes are in [CONTRIBUTE.en.md](CONTRIBUTE.en.md).
