using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using GenshinChatTranslator.App.Localization;
using GenshinChatTranslator.App.Models;
using GenshinChatTranslator.App.Ocr;
using GenshinChatTranslator.App.Services;
using GenshinChatTranslator.App.Translation;
using GenshinChatTranslator.App.Win32;

namespace GenshinChatTranslator.App;

public sealed class OverlayWindow : Window
{
    private const double ReferenceOverlayWidth = 2560d;
    private const double ReferenceOverlayHeight = 1440d;
    private const double OverlayTextBaseFontSize = 16d;
    private const double OverlayTextBaseMaxWidth = 420d;
    private const double OverlayTextVerticalShiftRatio = 0.015d;

    private readonly Canvas _canvas = new();
    private IntPtr _hwnd;

    public OverlayWindow()
    {
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Content = _canvas;
        ResizeMode = ResizeMode.NoResize;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        WindowStyle = WindowStyle.None;
        Width = 1;
        Height = 1;

        SourceInitialized += (_, _) =>
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            ApplyClickThroughStyles();
        };
    }

    public void ShowRois(
        ScreenBox clientBox,
        ScreenBox messageRoi,
        IReadOnlyList<ChatBubbleRoi> rois,
        IReadOnlyList<ChatOcrItem> ocrResults,
        IReadOnlyList<ChatTranslationItem> translationResults,
        bool showDebugRois)
    {
        if (!IsVisible)
        {
            Show();
        }

        var deviceToDip = GetDeviceToDipTransform();
        var dipSize = deviceToDip.Transform(new Point(clientBox.Width, clientBox.Height));
        Width = Math.Max(1, dipSize.X);
        Height = Math.Max(1, dipSize.Y);
        _canvas.Width = Width;
        _canvas.Height = Height;
        _canvas.Children.Clear();
        var overlayScale = CalculateOverlayScale(clientBox.Width, clientBox.Height);
        var textVerticalShift = clientBox.Height * OverlayTextVerticalShiftRatio * deviceToDip.M22;

        if (showDebugRois)
        {
            AddBox(ToDipBox(messageRoi, deviceToDip), Color.FromRgb(80, 180, 255), 2, LocalizationManager.Text("OverlayMessageRoi"));
        }

        for (var index = 0; index < rois.Count; index++)
        {
            var roi = rois[index];
            var color = roi.Kind == ChatRoiDetector.SelfLightKind
                ? Color.FromRgb(255, 80, 80)
                : Color.FromRgb(80, 255, 120);
            var textBox = ToDipBox(roi.TextBox, deviceToDip);
            var translationItem = translationResults.FirstOrDefault(item => item.Index == index + 1);
            var ocrText = ocrResults.FirstOrDefault(item => item.Index == index + 1)?.Result.Text;
            var displayText = ResolveDisplayText(translationItem, ocrText);

            if (showDebugRois)
            {
                var kindLabel = roi.Kind == ChatRoiDetector.SelfLightKind
                    ? LocalizationManager.Text("RoiKindSelfLight")
                    : LocalizationManager.Text("RoiKindOtherDark");
                AddBox(ToDipBox(roi.BubbleBox, deviceToDip), color, 3, $"{index + 1}:{kindLabel}");
                AddBox(textBox, Colors.White, 1, null);
            }

            if (!string.IsNullOrWhiteSpace(displayText))
            {
                AddOverlayText(textBox, displayText, color, overlayScale, textVerticalShift);
            }
        }

        ApplyClickThroughStyles();
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                _hwnd,
                NativeMethods.HwndTopMost,
                clientBox.Left,
                clientBox.Top,
                clientBox.Width,
                clientBox.Height,
                NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow);
            NativeMethods.ShowWindow(_hwnd, NativeMethods.SwShownoactivate);
        }
    }

    private static string? ResolveDisplayText(ChatTranslationItem? translationItem, string? ocrText)
    {
        if (!string.IsNullOrWhiteSpace(translationItem?.Result.Text))
        {
            return translationItem.Result.Text;
        }

        if (!string.IsNullOrWhiteSpace(translationItem?.SourceText))
        {
            return translationItem.SourceText;
        }

        return string.IsNullOrWhiteSpace(ocrText) ? null : ocrText;
    }

    private void AddOverlayText(Rect box, string text, Color accent, double overlayScale, double verticalShift)
    {
        var border = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(210, 17, 24, 39)),
            BorderBrush = new SolidColorBrush(accent),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 4, 8, 5),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brushes.White,
                FontSize = OverlayTextBaseFontSize * overlayScale,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = OverlayTextBaseMaxWidth * overlayScale,
            },
        };
        Canvas.SetLeft(border, box.Left);
        Canvas.SetTop(border, Math.Max(0, box.Bottom + 4 - verticalShift));
        _canvas.Children.Add(border);
    }

    private static double CalculateOverlayScale(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return 1d;
        }

        var widthScale = width / ReferenceOverlayWidth;
        var heightScale = height / ReferenceOverlayHeight;
        return Math.Max(0.1d, Math.Min(widthScale, heightScale));
    }

    private void AddBox(Rect box, Color color, double thickness, string? label)
    {
        var brush = new SolidColorBrush(color);
        var rectangle = new Rectangle
        {
            Width = Math.Max(1, box.Width),
            Height = Math.Max(1, box.Height),
            Stroke = brush,
            StrokeThickness = thickness,
            Fill = Brushes.Transparent,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(rectangle, box.Left);
        Canvas.SetTop(rectangle, box.Top);
        _canvas.Children.Add(rectangle);

        if (label is null)
        {
            return;
        }

        var text = new TextBlock
        {
            Text = label,
            Foreground = brush,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            IsHitTestVisible = false,
        };
        Canvas.SetLeft(text, box.Left + 6);
        Canvas.SetTop(text, Math.Max(0, box.Top - 20));
        _canvas.Children.Add(text);
    }

    private Matrix GetDeviceToDipTransform()
    {
        return PresentationSource.FromVisual(this)?.CompositionTarget?.TransformFromDevice
            ?? Matrix.Identity;
    }

    private static Rect ToDipBox(ScreenBox box, Matrix deviceToDip)
    {
        var topLeft = deviceToDip.Transform(new Point(box.Left, box.Top));
        var bottomRight = deviceToDip.Transform(new Point(box.Right, box.Bottom));
        return new Rect(topLeft, bottomRight);
    }

    private void ApplyClickThroughStyles()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        var current = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle).ToInt64();
        var updated = current |
            NativeMethods.WsExTransparent |
            NativeMethods.WsExLayered |
            NativeMethods.WsExToolWindow |
            NativeMethods.WsExNoActivate;
        NativeMethods.SetWindowLongPtr(_hwnd, NativeMethods.GwlExStyle, new IntPtr(updated));
    }
}
