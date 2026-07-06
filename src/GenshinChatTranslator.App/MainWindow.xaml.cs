using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Navigation;
using System.Windows.Threading;
using GenshinChatTranslator.App.Localization;
using GenshinChatTranslator.App.Models;
using GenshinChatTranslator.App.Ocr;
using GenshinChatTranslator.App.Services;
using GenshinChatTranslator.App.Translation;

namespace GenshinChatTranslator.App;

public partial class MainWindow : Window
{
    private static readonly string[] DefaultTitleKeywords = ["原神", "YuanShen", "Genshin Impact"];
    private const double SupportedAspectRatio = 16d / 9d;
    private const double SupportedAspectRatioTolerance = 0.01d;
    private static readonly Brush SelectedLanguageBackground = new SolidColorBrush(Color.FromRgb(37, 99, 235));
    private static readonly Brush SelectedLanguageForeground = Brushes.White;
    private static readonly Brush NormalLanguageBackground = Brushes.White;
    private static readonly Brush NormalLanguageForeground = new SolidColorBrush(Color.FromRgb(31, 41, 55));
    private static readonly Brush SelectedLanguageBorder = new SolidColorBrush(Color.FromRgb(29, 78, 216));
    private static readonly Brush NormalLanguageBorder = new SolidColorBrush(Color.FromRgb(209, 213, 219));

    private readonly DispatcherTimer _timer;
    private readonly WindowTracker _windowTracker = new();
    private readonly GdiFrameCaptureService _captureService = new();
    private readonly ChatUiGate _snapshotChatUiGate = new();
    private OverlayWindow? _overlayWindow;
    private RoiDetectionConfig? _roiConfig;
    private OcrOptions? _ocrOptions;
    private ChatOcrPipeline? _ocrPipeline;
    private TranslationOptions? _translationOptions;
    private ChatTranslationPipeline? _translationPipeline;
    private ChatRoiDetector? _snapshotDetector;
    private RoiDetectionLoopService? _detectionLoop;
    private UserPreferences _preferences = new();
    private string? _configPath;
    private string? _translationConfigPath;
    private bool _isRunning;
    private bool _isStarting;
    private bool _isDebugMode;
    private bool _isRefreshingLocalizedChoices;
    private DateTime _lastRenderedSnapshotAt = DateTime.MinValue;

    public MainWindow()
    {
        InitializeComponent();

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100),
        };
        _timer.Tick += (_, _) => RefreshOverlay();

        Loaded += (_, _) => Initialize();
        Closed += (_, _) =>
        {
            _detectionLoop?.Dispose();
            _overlayWindow?.Close();
        };
    }

    private void Initialize()
    {
        try
        {
            _preferences = UserPreferencesStore.Load();
            _configPath = WorkspacePaths.GetUserConfigFile("roi_detection.yml");
            _roiConfig = RoiDetectionConfig.Load(_configPath);
            var ocrConfigPath = WorkspacePaths.GetUserConfigFile("ocr.yml");
            _ocrOptions = OcrOptions.Load(ocrConfigPath);
            _ocrPipeline = new ChatOcrPipeline(_ocrOptions);
            _translationConfigPath = WorkspacePaths.GetUserConfigFile("translation.yml");
            _translationOptions = TranslationOptions.Load(_translationConfigPath);
            _translationPipeline = new ChatTranslationPipeline(_translationOptions);
            ApplyPreferencesToPipelines();
            _isRefreshingLocalizedChoices = true;
            try
            {
                InitializeOcrEngineOptions(_ocrPipeline);
                InitializeTranslationOptions(_translationPipeline);
            }
            finally
            {
                _isRefreshingLocalizedChoices = false;
            }

            _snapshotDetector = new ChatRoiDetector(_roiConfig);
            _detectionLoop = new RoiDetectionLoopService(DefaultTitleKeywords, _roiConfig, _ocrPipeline, _translationPipeline);
            _detectionLoop.SetFastDetection(_preferences.FastDetection);
            _overlayWindow = new OverlayWindow();
            DebugModeCheckBox.IsChecked = _isDebugMode;
            _isRefreshingLocalizedChoices = true;
            try
            {
                FastDetectionCheckBox.IsChecked = _preferences.FastDetection;
                AutoFixWin11BitBltCheckBox.IsChecked = _preferences.AutoFixWin11BitBlt;
            }
            finally
            {
                _isRefreshingLocalizedChoices = false;
            }

            ApplyWin11BitBltFixIfEnabled();
            ApplyDebugMode();
            ApplyCultureButtonState();

            TargetTextBlock.Text = LocalizationManager.Text("NoTargetText");
            CaptureTextBlock.Text = "-";
            RoiTextBlock.Text = LocalizationManager.Format("RoiCountFormat", 0);
            OcrTextBlock.Text = LocalizationManager.Format(
                "OcrCountFormat",
                0,
                0,
                GetOcrEngineDisplayName(_ocrPipeline.SelectedEngineKind));
            TranslationTextBlock.Text = LocalizationManager.Format(
                "TranslationCountFormat",
                0,
                0,
                GetTranslationEngineDisplayName(_translationPipeline.SelectedEngineKind),
                GetLanguageDisplayName(_translationPipeline.TargetLanguage));
            RecognitionSummaryTextBlock.Text = LocalizationManager.Text("RecognitionSummaryWaiting");
            UpdateLatencyText(null);
            SetStatus(LocalizationManager.Text("StatusReady"));
        }
        catch (Exception ex)
        {
            SetStatus(LocalizationManager.Format("StatusCaptureFailedFormat", ex.Message));
            ToggleButton.IsEnabled = false;
            SnapshotButton.IsEnabled = false;
            OcrEngineComboBox.IsEnabled = false;
            TranslationEngineComboBox.IsEnabled = false;
            ConfigureLlmButton.IsEnabled = false;
            TranslationTargetLanguageComboBox.IsEnabled = false;
        }
    }

    private void InitializeOcrEngineOptions(ChatOcrPipeline pipeline)
    {
        var options = pipeline.AvailableEngineKinds
            .Select(kind => new OcrEngineChoice(kind, GetOcrEngineDisplayName(kind)))
            .ToList();
        OcrEngineComboBox.ItemsSource = options;
        OcrEngineComboBox.SelectedItem = options.FirstOrDefault(option => option.Kind == pipeline.SelectedEngineKind)
            ?? options.FirstOrDefault();
        OcrEngineComboBox.IsEnabled = options.Count > 0;
    }

    private void InitializeTranslationOptions(ChatTranslationPipeline pipeline)
    {
        var engineOptions = pipeline.AvailableEngineKinds
            .Select(kind => new TranslationEngineChoice(kind, GetTranslationEngineDisplayName(kind)))
            .ToList();
        TranslationEngineComboBox.ItemsSource = engineOptions;
        TranslationEngineComboBox.SelectedItem = engineOptions.FirstOrDefault(option => option.Kind == pipeline.SelectedEngineKind)
            ?? engineOptions.FirstOrDefault();
        TranslationEngineComboBox.IsEnabled = engineOptions.Count > 0;
        UpdateConfigureLlmButtonVisibility();

        var languageOptions = new[]
        {
            ChatLanguage.ChineseSimplified,
            ChatLanguage.Japanese,
            ChatLanguage.English,
        }
            .Select(language => new TranslationLanguageChoice(language, GetLanguageDisplayName(language)))
            .ToList();
        TranslationTargetLanguageComboBox.ItemsSource = languageOptions;
        TranslationTargetLanguageComboBox.SelectedItem = languageOptions.FirstOrDefault(option => option.Language == pipeline.TargetLanguage)
            ?? languageOptions.FirstOrDefault();
        TranslationTargetLanguageComboBox.IsEnabled = languageOptions.Count > 0;
    }

    private void OcrEngineComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isRefreshingLocalizedChoices)
        {
            return;
        }

        if (_ocrPipeline is null || OcrEngineComboBox.SelectedItem is not OcrEngineChoice choice)
        {
            return;
        }

        _ocrPipeline.SelectEngine(choice.Kind);
        SaveUserPreferences();
        OcrTextBlock.Text = LocalizationManager.Format("OcrCountFormat", 0, 0, choice.DisplayName);
        SetStatus(LocalizationManager.Format("StatusOcrEngineChangedFormat", choice.DisplayName));
    }

    private void TranslationEngineComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isRefreshingLocalizedChoices)
        {
            return;
        }

        if (_translationPipeline is null || TranslationEngineComboBox.SelectedItem is not TranslationEngineChoice choice)
        {
            return;
        }

        _translationPipeline.SelectEngine(choice.Kind);
        SaveUserPreferences();
        UpdateConfigureLlmButtonVisibility();
        TranslationTextBlock.Text = LocalizationManager.Format(
            "TranslationCountFormat",
            0,
            0,
            choice.DisplayName,
            GetLanguageDisplayName(_translationPipeline.TargetLanguage));
        SetStatus(LocalizationManager.Format("StatusTranslationEngineChangedFormat", choice.DisplayName));
    }

    private void TranslationTargetLanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isRefreshingLocalizedChoices)
        {
            return;
        }

        if (_translationPipeline is null || TranslationTargetLanguageComboBox.SelectedItem is not TranslationLanguageChoice choice)
        {
            return;
        }

        _translationPipeline.SelectTargetLanguage(choice.Language);
        SaveUserPreferences();
        TranslationTextBlock.Text = LocalizationManager.Format(
            "TranslationCountFormat",
            0,
            0,
            GetTranslationEngineDisplayName(_translationPipeline.SelectedEngineKind),
            choice.DisplayName);
        SetStatus(LocalizationManager.Format("StatusTranslationTargetChangedFormat", choice.DisplayName));
    }

    private void DebugModeCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        _isDebugMode = DebugModeCheckBox.IsChecked == true;
        ApplyDebugMode();
        _lastRenderedSnapshotAt = DateTime.MinValue;
        RefreshOverlay();
    }

    private void AutoFixWin11BitBltCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingLocalizedChoices)
        {
            return;
        }

        _preferences.AutoFixWin11BitBlt = AutoFixWin11BitBltCheckBox.IsChecked == true;
        SaveUserPreferences();
        ApplyWin11BitBltFixIfEnabled();
    }

    private void FastDetectionCheckBox_CheckedChanged(object sender, RoutedEventArgs e)
    {
        if (_isRefreshingLocalizedChoices)
        {
            return;
        }

        var fastDetection = FastDetectionCheckBox.IsChecked == true;
        ApplyFastDetection(fastDetection);
        _preferences.FastDetection = fastDetection;
        SaveUserPreferences();
        SetStatus(LocalizationManager.Text(fastDetection ? "StatusFastDetectionEnabled" : "StatusFastDetectionDisabled"));
    }

    private void OpenDisplayAdvancedGraphicsSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo("ms-settings:display-advancedgraphics")
        {
            UseShellExecute = true,
        });
    }

    private void LanguageButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Tag is not string cultureName)
        {
            return;
        }

        LocalizationManager.LoadCultureResources(CultureInfo.GetCultureInfo(cultureName));
        RefreshLocalizedControls();
        SaveUserPreferences();
        SetStatus(LocalizationManager.Format("StatusUiLanguageChangedFormat", GetUiLanguageDisplayName(cultureName)));
    }

    private void ConfigureLlmButton_Click(object sender, RoutedEventArgs e)
    {
        if (_translationConfigPath is null || _translationOptions is null)
        {
            return;
        }

        var dialog = new LlmConfigurationDialog(_translationConfigPath, _translationOptions)
        {
            Owner = this,
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        ReloadTranslationConfiguration();
        SetStatus(LocalizationManager.Text("StatusLlmConfigSaved"));
    }

    private void ProjectLink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
        {
            UseShellExecute = true,
        });
        e.Handled = true;
    }

    private async void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isRunning)
        {
            StopDetection();
        }
        else
        {
            await StartDetectionAsync();
        }
    }

    private async void SnapshotButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SnapshotButton.IsEnabled = false;
            var result = await CaptureAndDetectAsync(CancellationToken.None);
            if (result is null)
            {
                SetStatus(LocalizationManager.Text("StatusNoTarget"));
                return;
            }

            if (!result.Value.IsChatInterface)
            {
                _overlayWindow?.Hide();
                TargetTextBlock.Text = LocalizationManager.Text("NotChatInterfaceText");
                CaptureTextBlock.Text = LocalizationManager.Format("CaptureFormat", result.Value.Frame.Width, result.Value.Frame.Height, DateTime.Now);
                RoiTextBlock.Text = LocalizationManager.Format("RoiCountFormat", 0);
                OcrTextBlock.Text = LocalizationManager.Format(
                    "OcrCountFormat",
                    0,
                    0,
                    GetOcrEngineDisplayName(_ocrPipeline?.SelectedEngineKind ?? OcrEngineKind.Stub));
                TranslationTextBlock.Text = LocalizationManager.Format(
                    "TranslationCountFormat",
                    0,
                    0,
                    GetTranslationEngineDisplayName(_translationPipeline?.SelectedEngineKind ?? TranslationEngineKind.None),
                    GetLanguageDisplayName(_translationPipeline?.TargetLanguage ?? ChatLanguage.ChineseSimplified));
                RecognitionSummaryTextBlock.Text = LocalizationManager.Text("RecognitionSummaryNotChatInterface");
                SetStatus(LocalizationManager.Text("StatusNotChatInterface"));
                return;
            }

            var outputPath = SnapshotWriter.Write(
                result.Value.Window,
                result.Value.Frame,
                result.Value.MessageRoi,
                result.Value.Rois,
                result.Value.OcrResults,
                result.Value.TranslationResults);
            UpdateStatus(result.Value.Window, result.Value.Frame, result.Value.Rois, result.Value.OcrResults, result.Value.TranslationResults);
            SetStatus(LocalizationManager.Format("StatusSnapshotSavedFormat", outputPath));
        }
        catch (Exception ex)
        {
            _overlayWindow?.Hide();
            SetStatus(LocalizationManager.Format("StatusCaptureFailedFormat", ex.Message));
        }
        finally
        {
            SnapshotButton.IsEnabled = true;
        }
    }

    private async Task StartDetectionAsync()
    {
        if (_isStarting)
        {
            return;
        }

        _isStarting = true;
        ToggleButton.IsEnabled = false;
        try
        {
            if (!await TryValidateLlmBeforeStartAsync())
            {
                return;
            }

            BeginDetection();
        }
        finally
        {
            _isStarting = false;
            ToggleButton.IsEnabled = true;
            ToggleButton.Content = LocalizationManager.Text(_isRunning ? "StopButton" : "StartButton");
        }
    }

    private async Task<bool> TryValidateLlmBeforeStartAsync()
    {
        if (_translationPipeline?.SelectedEngineKind != TranslationEngineKind.OpenAiCompatibleLlm)
        {
            return true;
        }

        SetStatus(LocalizationManager.Text("StatusLlmStartValidationRunning"));
        var validation = await _translationPipeline.ValidateSelectedLlmConnectionAsync(CancellationToken.None);
        if (validation.IsSuccess)
        {
            return true;
        }

        MessageBox.Show(
            this,
            LocalizationManager.Format("LlmStartValidationFailedPromptFormat", validation.ErrorMessage),
            LocalizationManager.Text("LlmStartValidationFailedTitle"),
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        SetStatus(LocalizationManager.Format("StatusLlmStartValidationFailedFormat", validation.ErrorMessage));
        return false;
    }

    private void BeginDetection()
    {
        if (!TryConfirmAspectRatioBeforeStart())
        {
            return;
        }

        _isRunning = true;
        ApplyWin11BitBltFixIfEnabled();
        ToggleButton.Content = LocalizationManager.Text("StopButton");
        RecognitionSummaryTextBlock.Text = LocalizationManager.Text("RecognitionSummaryWaiting");
        UpdateLatencyText(null);
        SetStatus(LocalizationManager.Text("StatusRunning"));
        _detectionLoop?.Start();
        _timer.Start();
        RefreshOverlay();
    }

    private bool TryConfirmAspectRatioBeforeStart()
    {
        var window = _windowTracker.FindTargetWindow(DefaultTitleKeywords);
        if (window is null)
        {
            return true;
        }

        if (!_windowTracker.IsForegroundWindow(window))
        {
            return true;
        }

        var width = window.ClientBox.Width;
        var height = window.ClientBox.Height;
        if (IsSupportedAspectRatio(width, height))
        {
            return true;
        }

        if (!ShowUnsupportedAspectRatioPrompt(window))
        {
            SetStatus(LocalizationManager.Text("StatusUnsupportedAspectRatioCanceled"));
            return false;
        }

        return true;
    }

    private bool ShowUnsupportedAspectRatioPrompt(WindowInfo window)
    {
        var result = MessageBox.Show(
            this,
            LocalizationManager.Format(
                "UnsupportedAspectRatioPromptFormat",
                window.ClientBox.Width,
                window.ClientBox.Height),
            LocalizationManager.Text("UnsupportedAspectRatioPromptTitle"),
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);
        return result == MessageBoxResult.OK;
    }

    private static bool IsSupportedAspectRatio(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return false;
        }

        var actualAspectRatio = (double)width / height;
        var relativeError = Math.Abs(actualAspectRatio - SupportedAspectRatio) / SupportedAspectRatio;
        return relativeError <= SupportedAspectRatioTolerance;
    }

    private void StopDetection()
    {
        _isRunning = false;
        _timer.Stop();
        _detectionLoop?.Stop();
        _overlayWindow?.Hide();
        _lastRenderedSnapshotAt = DateTime.MinValue;
        ToggleButton.Content = LocalizationManager.Text("StartButton");
        RecognitionSummaryTextBlock.Text = LocalizationManager.Text("RecognitionSummaryWaiting");
        UpdateLatencyText(null);
        SetStatus(LocalizationManager.Text("StatusStopped"));
    }

    private void RefreshOverlay()
    {
        var snapshot = _detectionLoop?.Snapshot;
        if (snapshot is null || !snapshot.IsRunning)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(snapshot.ErrorMessage))
        {
            _overlayWindow?.Hide();
            _lastRenderedSnapshotAt = DateTime.MinValue;
            RecognitionSummaryTextBlock.Text = LocalizationManager.Format("RecognitionSummaryFailedFormat", snapshot.ErrorMessage);
            UpdateLatencyText(snapshot.LatencyAverages);
            SetStatus(LocalizationManager.Format("StatusCaptureFailedFormat", snapshot.ErrorMessage));
            return;
        }

        if (snapshot.TargetMissing)
        {
            _overlayWindow?.Hide();
            _lastRenderedSnapshotAt = DateTime.MinValue;
            TargetTextBlock.Text = LocalizationManager.Text("NoTargetText");
            RecognitionSummaryTextBlock.Text = LocalizationManager.Text("RecognitionSummaryNoTarget");
            UpdateLatencyText(snapshot.LatencyAverages);
            SetStatus(LocalizationManager.Text("StatusNoTarget"));
            return;
        }

        if (snapshot.TargetBackground)
        {
            _overlayWindow?.Hide();
            _lastRenderedSnapshotAt = DateTime.MinValue;
            RecognitionSummaryTextBlock.Text = LocalizationManager.Text("RecognitionSummaryWaiting");
            UpdateLatencyText(snapshot.LatencyAverages);
            SetStatus(LocalizationManager.Text("StatusBackground"));
            return;
        }

        if (snapshot.ChatInterfaceMissing || snapshot.Result is null)
        {
            _overlayWindow?.Hide();
            _lastRenderedSnapshotAt = DateTime.MinValue;
            TargetTextBlock.Text = LocalizationManager.Text("NotChatInterfaceText");
            RecognitionSummaryTextBlock.Text = LocalizationManager.Text("RecognitionSummaryNotChatInterface");
            UpdateLatencyText(snapshot.LatencyAverages);
            SetStatus(LocalizationManager.Text("StatusNotChatInterface"));
            return;
        }

        var result = snapshot.Result;
        UpdateStatus(result.Window, result.FrameWidth, result.FrameHeight, result.CapturedAt, result.Rois, result.OcrResults, result.TranslationResults, snapshot.LatencyAverages);
        if (_windowTracker.GetForegroundWindow() != result.Window.Hwnd)
        {
            _overlayWindow?.Hide();
            _lastRenderedSnapshotAt = DateTime.MinValue;
            SetStatus(LocalizationManager.Text("StatusBackground"));
            return;
        }

        if (_lastRenderedSnapshotAt != snapshot.UpdatedAt || _overlayWindow?.IsVisible != true)
        {
            _overlayWindow?.ShowRois(
                result.Window.ClientBox,
                result.MessageRoi,
                result.Rois,
                result.OcrResults,
                result.TranslationResults,
                showDebugRois: _isDebugMode);
            _lastRenderedSnapshotAt = snapshot.UpdatedAt;
        }

        SetStatus(LocalizationManager.Text("StatusRunning"));
    }

    private void SetStatus(string statusText)
    {
        var latestLlmRequest = LlmTranslationRequestStatusStore.Latest;
        if (latestLlmRequest is null)
        {
            StatusTextBlock.Text = statusText;
            return;
        }

        var llmStatusText = LocalizationManager.Format(
            "LlmTranslationRequestStatusFormat",
            latestLlmRequest.Sequence,
            latestLlmRequest.SentAt,
            latestLlmRequest.Model,
            GetLanguageDisplayName(latestLlmRequest.TargetLanguage),
            latestLlmRequest.MessageCount,
            latestLlmRequest.CharacterCount,
            latestLlmRequest.Indices,
            latestLlmRequest.ThinkingType);
        StatusTextBlock.Text = $"{statusText}\n{llmStatusText}";
    }

    private async Task<DetectionResult?> CaptureAndDetectAsync(CancellationToken cancellationToken)
    {
        if (_snapshotDetector is null || _ocrPipeline is null || _translationPipeline is null)
        {
            throw new InvalidOperationException("ROI detector, OCR pipeline, or translation pipeline is not initialized.");
        }

        var window = _windowTracker.FindTargetWindow(DefaultTitleKeywords);
        if (window is null)
        {
            return null;
        }

        var frame = _captureService.Capture(window);
        var chatUiGateResult = _snapshotChatUiGate.Detect(frame);
        if (!chatUiGateResult.IsChatInterface)
        {
            return new DetectionResult(
                window,
                frame,
                new ScreenBox(0, 0, 0, 0),
                Array.Empty<ChatBubbleRoi>(),
                Array.Empty<ChatOcrItem>(),
                Array.Empty<ChatTranslationItem>(),
                IsChatInterface: false);
        }

        var (messageRoi, rois) = _snapshotDetector.Locate(frame);
        var ocrResults = await _ocrPipeline.RecognizeAsync(frame, rois, cancellationToken);
        var translationResults = await _translationPipeline.TranslateAsync(ocrResults, cancellationToken);
        return new DetectionResult(window, frame, messageRoi, rois, ocrResults, translationResults, IsChatInterface: true);
    }

    private void UpdateStatus(
        WindowInfo window,
        RgbFrame frame,
        IReadOnlyList<ChatBubbleRoi> rois,
        IReadOnlyList<ChatOcrItem> ocrResults,
        IReadOnlyList<ChatTranslationItem> translationResults)
    {
        UpdateStatus(window, frame.Width, frame.Height, DateTime.Now, rois, ocrResults, translationResults, null);
    }

    private void UpdateStatus(
        WindowInfo window,
        int frameWidth,
        int frameHeight,
        DateTime capturedAt,
        IReadOnlyList<ChatBubbleRoi> rois,
        IReadOnlyList<ChatOcrItem> ocrResults,
        IReadOnlyList<ChatTranslationItem> translationResults,
        PipelineLatencyAverages? latencyAverages)
    {
        TargetTextBlock.Text = LocalizationManager.Format("TargetFormat", window.Title, window.Hwnd, window.ClientBox);
        CaptureTextBlock.Text = LocalizationManager.Format("CaptureFormat", frameWidth, frameHeight, capturedAt);
        RoiTextBlock.Text = LocalizationManager.Format("RoiCountFormat", rois.Count);
        var successCount = ocrResults.Count(item => item.Result.IsSuccess);
        var engine = _ocrPipeline?.SelectedEngineKind ?? ocrResults.FirstOrDefault()?.Result.Engine ?? OcrEngineKind.Stub;
        OcrTextBlock.Text = LocalizationManager.Format("OcrCountFormat", successCount, ocrResults.Count, GetOcrEngineDisplayName(engine));
        var translationSuccessCount = translationResults.Count(item => item.Result.IsSuccess);
        var translationEngine = _translationPipeline?.SelectedEngineKind ?? translationResults.FirstOrDefault()?.Result.Engine ?? TranslationEngineKind.None;
        var targetLanguage = _translationPipeline?.TargetLanguage ?? translationResults.FirstOrDefault()?.Result.TargetLanguage ?? ChatLanguage.ChineseSimplified;
        TranslationTextBlock.Text = LocalizationManager.Format(
            "TranslationCountFormat",
            translationSuccessCount,
            translationResults.Count,
            GetTranslationEngineDisplayName(translationEngine),
            GetLanguageDisplayName(targetLanguage));
        RecognitionSummaryTextBlock.Text = LocalizationManager.Format(
            "RecognitionSummaryActiveFormat",
            rois.Count,
            successCount,
            ocrResults.Count,
            translationSuccessCount,
            translationResults.Count);
        UpdateLatencyText(latencyAverages);
    }

    private void UpdateLatencyText(PipelineLatencyAverages? latencyAverages)
    {
        LatencyTextBlock.Text = latencyAverages is null
            ? LocalizationManager.Text("LatencyNoSamples")
            : LocalizationManager.Format(
                "LatencyAverageFormat",
                latencyAverages.Count,
                latencyAverages.Capacity,
                latencyAverages.EndToEndMs,
                FormatLatencyStage(latencyAverages.CaptureMs, "F1"),
                FormatLatencyStage(latencyAverages.ChatGateMs, "F3"),
                FormatLatencyStage(latencyAverages.RoiMs, "F1"),
                FormatLatencyStage(latencyAverages.OcrMs, "F1"),
                FormatLatencyStage(latencyAverages.TranslationMs, "F1"));
    }

    private static string FormatLatencyStage(double? milliseconds, string format)
    {
        return milliseconds.HasValue
            ? $"{milliseconds.Value.ToString(format, CultureInfo.CurrentCulture)} ms"
            : "-";
    }

    private void ApplyDebugMode()
    {
        var visibility = _isDebugMode ? Visibility.Visible : Visibility.Collapsed;
        DebugDetailsBorder.Visibility = visibility;
        SnapshotButton.Visibility = visibility;
        StatusTextBlock.Visibility = visibility;
    }

    private void ReloadTranslationConfiguration()
    {
        if (_translationConfigPath is null)
        {
            return;
        }

        var selectedEngine = _translationPipeline?.SelectedEngineKind ?? TranslationEngineKind.OpenAiCompatibleLlm;
        var targetLanguage = _translationPipeline?.TargetLanguage ?? ChatLanguage.ChineseSimplified;
        _translationOptions = TranslationOptions.Load(_translationConfigPath);
        _translationPipeline = new ChatTranslationPipeline(_translationOptions);
        _translationPipeline.SelectEngine(selectedEngine);
        _translationPipeline.SelectTargetLanguage(targetLanguage);
        _detectionLoop?.UpdateTranslationPipeline(_translationPipeline);

        _isRefreshingLocalizedChoices = true;
        try
        {
            InitializeTranslationOptions(_translationPipeline);
        }
        finally
        {
            _isRefreshingLocalizedChoices = false;
        }

        RefreshLocalizedSummaryTexts();
    }

    private void ApplyPreferencesToPipelines()
    {
        if (_ocrPipeline is not null &&
            _preferences.GetOcrEngine() is { } ocrEngine &&
            _ocrPipeline.AvailableEngineKinds.Contains(ocrEngine))
        {
            _ocrPipeline.SelectEngine(ocrEngine);
        }

        ApplyFastDetection(_preferences.FastDetection);

        if (_translationPipeline is null)
        {
            return;
        }

        if (_preferences.GetTranslationEngine() is { } translationEngine &&
            _translationPipeline.AvailableEngineKinds.Contains(translationEngine))
        {
            _translationPipeline.SelectEngine(translationEngine);
        }

        if (_preferences.GetTranslationTargetLanguage() is { } targetLanguage)
        {
            _translationPipeline.SelectTargetLanguage(targetLanguage);
        }
    }

    private void SaveUserPreferences()
    {
        _preferences.UiCultureName = LocalizationManager.CurrentCulture.Name;
        if (_ocrPipeline is not null)
        {
            _preferences.OcrEngine = _ocrPipeline.SelectedEngineKind.ToString();
        }

        _preferences.FastDetection = FastDetectionCheckBox.IsChecked == true;
        if (_translationPipeline is not null)
        {
            _preferences.TranslationEngine = _translationPipeline.SelectedEngineKind.ToString();
            _preferences.TranslationTargetLanguage = _translationPipeline.TargetLanguage.ToString();
        }

        _preferences.AutoFixWin11BitBlt = AutoFixWin11BitBltCheckBox.IsChecked == true;

        try
        {
            UserPreferencesStore.Save(_preferences);
        }
        catch
        {
            // Preferences are a convenience layer; runtime choices still apply in memory if saving fails.
        }
    }

    private void ApplyWin11BitBltFixIfEnabled()
    {
        if (_preferences.AutoFixWin11BitBlt && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        {
            BitBltRegistryHelper.SetDirectXUserGlobalSettings();
        }
    }

    private void ApplyFastDetection(bool enabled)
    {
        _ocrPipeline?.SetSerialOcr(!enabled);
        _detectionLoop?.SetFastDetection(enabled);
    }

    private void UpdateConfigureLlmButtonVisibility()
    {
        var selectedKind = TranslationEngineComboBox.SelectedItem is TranslationEngineChoice choice
            ? choice.Kind
            : _translationPipeline?.SelectedEngineKind;
        ConfigureLlmButton.Visibility = selectedKind == TranslationEngineKind.OpenAiCompatibleLlm
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void RefreshLocalizedControls()
    {
        _isRefreshingLocalizedChoices = true;
        try
        {
            if (_ocrPipeline is not null)
            {
                InitializeOcrEngineOptions(_ocrPipeline);
            }

            if (_translationPipeline is not null)
            {
                InitializeTranslationOptions(_translationPipeline);
            }
        }
        finally
        {
            _isRefreshingLocalizedChoices = false;
        }

        ToggleButton.Content = LocalizationManager.Text(_isRunning ? "StopButton" : "StartButton");
        ApplyDebugMode();
        ApplyCultureButtonState();
        RefreshLocalizedSummaryTexts();
        _lastRenderedSnapshotAt = DateTime.MinValue;
        RefreshOverlay();
    }

    private void RefreshLocalizedSummaryTexts()
    {
        var snapshot = _detectionLoop?.Snapshot;
        var snapshotResult = snapshot?.Result;
        if (snapshotResult is not null)
        {
            UpdateStatus(
                snapshotResult.Window,
                snapshotResult.FrameWidth,
                snapshotResult.FrameHeight,
                snapshotResult.CapturedAt,
                snapshotResult.Rois,
                snapshotResult.OcrResults,
                snapshotResult.TranslationResults,
                snapshot?.LatencyAverages);
            return;
        }

        TargetTextBlock.Text = LocalizationManager.Text("NoTargetText");
        CaptureTextBlock.Text = "-";
        RoiTextBlock.Text = LocalizationManager.Format("RoiCountFormat", 0);
        OcrTextBlock.Text = LocalizationManager.Format(
            "OcrCountFormat",
            0,
            0,
            GetOcrEngineDisplayName(_ocrPipeline?.SelectedEngineKind ?? OcrEngineKind.Stub));
        TranslationTextBlock.Text = LocalizationManager.Format(
            "TranslationCountFormat",
            0,
            0,
            GetTranslationEngineDisplayName(_translationPipeline?.SelectedEngineKind ?? TranslationEngineKind.None),
            GetLanguageDisplayName(_translationPipeline?.TargetLanguage ?? ChatLanguage.ChineseSimplified));
        RecognitionSummaryTextBlock.Text = snapshot?.TargetMissing == true
            ? LocalizationManager.Text("RecognitionSummaryNoTarget")
            : snapshot?.ChatInterfaceMissing == true
                ? LocalizationManager.Text("RecognitionSummaryNotChatInterface")
                : LocalizationManager.Text("RecognitionSummaryWaiting");
        UpdateLatencyText(snapshot?.LatencyAverages);
    }

    private void ApplyCultureButtonState()
    {
        var cultureName = LocalizationManager.CurrentCulture.Name;
        ApplyLanguageButtonState(ChineseLanguageButton, cultureName.Equals("zh-CN", StringComparison.OrdinalIgnoreCase));
        ApplyLanguageButtonState(JapaneseLanguageButton, cultureName.Equals("ja-JP", StringComparison.OrdinalIgnoreCase));
        ApplyLanguageButtonState(EnglishLanguageButton, cultureName.Equals("en-US", StringComparison.OrdinalIgnoreCase));
    }

    private static void ApplyLanguageButtonState(Button button, bool isSelected)
    {
        button.Background = isSelected ? SelectedLanguageBackground : NormalLanguageBackground;
        button.Foreground = isSelected ? SelectedLanguageForeground : NormalLanguageForeground;
        button.BorderBrush = isSelected ? SelectedLanguageBorder : NormalLanguageBorder;
        button.BorderThickness = new Thickness(1);
    }

    private static string GetOcrEngineDisplayName(OcrEngineKind kind)
    {
        return kind switch
        {
            OcrEngineKind.Paddle => LocalizationManager.Text("OcrEnginePaddle"),
            OcrEngineKind.Windows => LocalizationManager.Text("OcrEngineWindows"),
            OcrEngineKind.OpenAiVision => LocalizationManager.Text("OcrEngineOpenAiVision"),
            OcrEngineKind.WeChat => LocalizationManager.Text("OcrEngineWeChat"),
            _ => LocalizationManager.Text("OcrEngineStub"),
        };
    }

    private static string GetTranslationEngineDisplayName(TranslationEngineKind kind)
    {
        return kind switch
        {
            TranslationEngineKind.None => LocalizationManager.Text("TranslationEngineNone"),
            TranslationEngineKind.MicrosoftEdge => LocalizationManager.Text("TranslationEngineMicrosoftEdge"),
            TranslationEngineKind.OpenAiCompatibleLlm => LocalizationManager.Text("TranslationEngineOpenAiCompatibleLlm"),
            _ => kind.ToString(),
        };
    }

    private static string GetLanguageDisplayName(ChatLanguage language)
    {
        return language switch
        {
            ChatLanguage.ChineseSimplified => LocalizationManager.Text("LanguageChineseSimplified"),
            ChatLanguage.English => LocalizationManager.Text("LanguageEnglish"),
            ChatLanguage.Japanese => LocalizationManager.Text("LanguageJapanese"),
            _ => LocalizationManager.Text("LanguageAuto"),
        };
    }

    private static string GetUiLanguageDisplayName(string cultureName)
    {
        return cultureName switch
        {
            "zh-CN" => LocalizationManager.Text("UiLanguageChineseButton"),
            "ja-JP" => LocalizationManager.Text("UiLanguageJapaneseButton"),
            "en-US" => LocalizationManager.Text("UiLanguageEnglishButton"),
            _ => cultureName,
        };
    }

    private readonly record struct DetectionResult(
        WindowInfo Window,
        RgbFrame Frame,
        ScreenBox MessageRoi,
        IReadOnlyList<ChatBubbleRoi> Rois,
        IReadOnlyList<ChatOcrItem> OcrResults,
        IReadOnlyList<ChatTranslationItem> TranslationResults,
        bool IsChatInterface);

    private sealed record OcrEngineChoice(OcrEngineKind Kind, string DisplayName);

    private sealed record TranslationEngineChoice(TranslationEngineKind Kind, string DisplayName);

    private sealed record TranslationLanguageChoice(ChatLanguage Language, string DisplayName);
}
