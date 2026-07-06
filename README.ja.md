# Genshin Chat Translator

[中文](README.md) | [English](README.en.md)

Genshin Chat Translator は、原神のチャットウィンドウ内の文字を認識し、翻訳結果をオーバーレイ表示する非公式 Windows デスクトップツールです。

## 使い方

1. GitHub Releases から `GenshinChatTranslator-Setup-*.exe` をダウンロードします。
2. インストールして `Genshin Chat Translator` を起動します。
3. アプリで翻訳先言語と翻訳方式を選択します。
4. 原神を起動し、チャット画面を開きます。
5. ゲームウィンドウを表示したままにすると、アプリがチャット領域をキャプチャし、文字を認識して翻訳オーバーレイを表示します。

## 対応範囲

- OS: Windows 10 19041 以降。
- ゲーム画面: 現在は 16:9 レイアウトのみ動作を保証しています。
- その他のアスペクト比: 動く可能性はありますが、まだ個別の適配はしていません。
- 入力レイアウト: キーボード/マウス、Xbox コントローラー、DualSense コントローラーのレイアウトに対応します。
- OCR: release パッケージには通常 WeChat OCR と PaddleOCR 関連のランタイム/モデルバイナリが含まれます。これらのバイナリはソースリポジトリには含まれません。

## Windows 11 BitBlt 互換修正

本ツールは BitBlt でゲームウィンドウをキャプチャします。Windows 11 のウィンドウ最適化により BitBlt が黒画面や古いフレームを返す場合があるため、アプリは「BitBlt のため Windows 11 のウィンドウ最適化を自動的に無効化」をデフォルトで有効にしています。

このオプションは現在のユーザーの DirectX 設定に次の値を書き込みます。

```text
HKCU\Software\Microsoft\DirectX\UserGpuPreferences
DirectXUserGlobalSettings = SwapEffectUpgradeEnable=0;
```

ゲームが起動中にこの設定を変更した場合は、反映するためにゲームを再起動してください。このオプションをオフにしても以後の自動書き込みを停止するだけです。Windows のグラフィック設定を手動で戻したい場合は、アプリの「手動設定」ボタンから詳細グラフィック設定を開いてください。

## 制限

- このツールは個人利用向けのプロトタイプです。すべての解像度、UI スケール、フィルター、言語組み合わせ、ゲームバージョンで安定動作する保証はありません。
- 背景が複雑な場合、チャット吹き出しが隠れている場合、文字が小さい場合、翻訳サービスが利用できない場合などは、OCR や翻訳が誤ることがあります。
- WeChat OCR 関連の上流プロジェクトには明示的なオープンソースライセンスがありません。release に関連バイナリが含まれる場合でも、それらは本プロジェクトの MIT License の対象外です。
- PaddleOCR は Apache License 2.0 です。関連バイナリとモデルファイルは上流ライセンスに従って扱われます。

## 設定

アプリはユーザープロファイル内に設定を保存します。API key を含むローカル設定ファイルを公開しないでください。

OpenAI 互換 LLM 翻訳は UI または環境変数で設定できます。

```powershell
$env:OPENAI_API_KEY = "your-key"
$env:OPENAI_COMPATIBLE_API_KEY = "your-key"
```

## ライセンス

本プロジェクトのソースコードは [MIT License](LICENSE) で提供されます。

第三者コンポーネントと release バイナリの注意事項は [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) を参照してください。開発、ビルド、貢献に関する説明は [CONTRIBUTE.ja.md](CONTRIBUTE.ja.md) にあります。
