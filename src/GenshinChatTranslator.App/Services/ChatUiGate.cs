using GenshinChatTranslator.App.Models;

namespace GenshinChatTranslator.App.Services;

public sealed class ChatUiGate
{
    private const int SampleStep = 3;

    public ChatUiGateResult Detect(RgbFrame frame)
    {
        var keyboardMouse = DetectKeyboardMouseLayout(frame);
        if (keyboardMouse.IsChatInterface)
        {
            return keyboardMouse;
        }

        var xboxController = DetectXboxControllerLayout(frame);
        if (xboxController.IsChatInterface)
        {
            return xboxController;
        }

        var playStationController = DetectPlayStationControllerLayout(frame);
        if (playStationController.IsChatInterface)
        {
            return playStationController;
        }

        return keyboardMouse with { Layout = ChatUiLayoutKind.None };
    }

    private static ChatUiGateResult DetectKeyboardMouseLayout(RgbFrame frame)
    {
        var back = DetectIconButton(
            frame,
            RatioBox(frame, 0.005, 0.010, 0.050, 0.085),
            lightThreshold: 0.28,
            darkThreshold: 0.025);
        var more = DetectIconButton(
            frame,
            RatioBox(frame, 0.565, 0.010, 0.625, 0.085),
            lightThreshold: 0.18,
            darkThreshold: 0.006);
        var send = DetectIconButton(
            frame,
            RatioBox(frame, 0.485, 0.895, 0.620, 0.975),
            lightThreshold: 0.30,
            darkThreshold: 0.012);
        var menu = DetectIconButton(
            frame,
            RatioBox(frame, 0.155, 0.895, 0.195, 0.975),
            lightThreshold: 0.18,
            darkThreshold: 0.018);

        var average = (back.Score + more.Score + send.Score + menu.Score) / 4.0;
        var isChatInterface =
            back.Score >= 0.70 &&
            more.Score >= 0.85 &&
            send.Score >= 0.85 &&
            menu.Score >= 0.85 &&
            average >= 0.90;

        return new ChatUiGateResult(
            isChatInterface,
            average,
            ChatUiLayoutKind.KeyboardMouse,
            back,
            more,
            send,
            menu);
    }

    private static ChatUiGateResult DetectXboxControllerLayout(RgbFrame frame)
    {
        var first = DetectControllerIconButton(
            frame,
            RatioBox(frame, 0.041, 0.852, 0.064, 0.879));
        var second = DetectControllerIconButton(
            frame,
            RatioBox(frame, 0.041, 0.900, 0.064, 0.934));

        var firstScore = WeightedScore(first.WhiteRatio, 0.20, first.DarkRatio, 0.40);
        var secondScore = WeightedScore(second.YellowRatio, 0.025, second.DarkRatio, 0.70);
        var average = (firstScore + secondScore) / 2.0;
        var isChatInterface =
            first.WhiteRatio >= 0.20 &&
            first.DarkRatio >= 0.40 &&
            second.YellowRatio >= 0.025 &&
            second.DarkRatio >= 0.70 &&
            second.WhiteRatio <= 0.12 &&
            average >= 0.85;

        return new ChatUiGateResult(
            isChatInterface,
            average,
            ChatUiLayoutKind.XboxController,
            new ChatUiGatePointScore(firstScore, first.WhiteRatio, first.DarkRatio),
            ChatUiGatePointScore.Empty,
            ChatUiGatePointScore.Empty,
            new ChatUiGatePointScore(secondScore, second.YellowRatio, second.DarkRatio));
    }

    private static ChatUiGateResult DetectPlayStationControllerLayout(RgbFrame frame)
    {
        var first = DetectControllerIconButton(
            frame,
            RatioBox(frame, 0.041, 0.852, 0.064, 0.879));
        var second = DetectControllerIconButton(
            frame,
            RatioBox(frame, 0.041, 0.900, 0.064, 0.934));

        var firstScore = WeightedScore(first.WhiteRatio, 0.28, first.DarkRatio, 0.35);
        var secondScore = WeightedScore(second.WhiteRatio, 0.28, second.DarkRatio, 0.35);
        var average = (firstScore + secondScore) / 2.0;
        var isChatInterface =
            first.WhiteRatio >= 0.28 &&
            first.DarkRatio >= 0.35 &&
            second.WhiteRatio >= 0.28 &&
            second.DarkRatio >= 0.35 &&
            second.YellowRatio <= 0.02 &&
            average >= 0.85;

        return new ChatUiGateResult(
            isChatInterface,
            average,
            ChatUiLayoutKind.PlayStationController,
            new ChatUiGatePointScore(firstScore, first.WhiteRatio, first.DarkRatio),
            ChatUiGatePointScore.Empty,
            ChatUiGatePointScore.Empty,
            new ChatUiGatePointScore(secondScore, second.WhiteRatio, second.DarkRatio));
    }

    private static ChatUiGatePointScore DetectIconButton(
        RgbFrame frame,
        ScreenBox box,
        double lightThreshold,
        double darkThreshold)
    {
        var sampleCount = 0;
        var lightCount = 0;
        var darkCount = 0;
        for (var y = box.Top; y < box.Bottom; y += SampleStep)
        {
            for (var x = box.Left; x < box.Right; x += SampleStep)
            {
                var offset = frame.PixelOffset(x, y);
                var red = frame.Pixels[offset];
                var green = frame.Pixels[offset + 1];
                var blue = frame.Pixels[offset + 2];
                sampleCount++;

                if (IsWarmLightUiPixel(red, green, blue))
                {
                    lightCount++;
                }
                else if (IsDarkUiPixel(red, green, blue))
                {
                    darkCount++;
                }
            }
        }

        if (sampleCount == 0)
        {
            return new ChatUiGatePointScore(0, 0, 0);
        }

        var lightRatio = lightCount / (double)sampleCount;
        var darkRatio = darkCount / (double)sampleCount;
        var lightScore = Math.Min(1.0, lightRatio / lightThreshold);
        var darkScore = Math.Min(1.0, darkRatio / darkThreshold);
        var score = (lightScore * 0.75) + (darkScore * 0.25);
        return new ChatUiGatePointScore(score, lightRatio, darkRatio);
    }

    private static ControllerIconMetrics DetectControllerIconButton(RgbFrame frame, ScreenBox box)
    {
        var sampleCount = 0;
        var whiteCount = 0;
        var yellowCount = 0;
        var darkCount = 0;
        for (var y = box.Top; y < box.Bottom; y += SampleStep)
        {
            for (var x = box.Left; x < box.Right; x += SampleStep)
            {
                var offset = frame.PixelOffset(x, y);
                var red = frame.Pixels[offset];
                var green = frame.Pixels[offset + 1];
                var blue = frame.Pixels[offset + 2];
                sampleCount++;

                if (IsWhiteControllerPixel(red, green, blue))
                {
                    whiteCount++;
                }
                else if (IsYellowControllerPixel(red, green, blue))
                {
                    yellowCount++;
                }
                else if (IsDarkUiPixel(red, green, blue))
                {
                    darkCount++;
                }
            }
        }

        if (sampleCount == 0)
        {
            return new ControllerIconMetrics(0, 0, 0);
        }

        return new ControllerIconMetrics(
            whiteCount / (double)sampleCount,
            yellowCount / (double)sampleCount,
            darkCount / (double)sampleCount);
    }

    private static double WeightedScore(double iconRatio, double iconThreshold, double darkRatio, double darkThreshold)
    {
        var iconScore = Math.Min(1.0, iconRatio / iconThreshold);
        var darkScore = Math.Min(1.0, darkRatio / darkThreshold);
        return (iconScore * 0.80) + (darkScore * 0.20);
    }

    private static bool IsWarmLightUiPixel(int red, int green, int blue)
    {
        if (red >= 220 && green >= 215 && blue >= 200)
        {
            return true;
        }

        return red >= 188 &&
            green >= 178 &&
            blue >= 148 &&
            red >= blue + 12 &&
            green >= blue + 4 &&
            red - green <= 35;
    }

    private static bool IsDarkUiPixel(int red, int green, int blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        return red >= 20 &&
            green >= 20 &&
            blue >= 20 &&
            max <= 120 &&
            max - min <= 55;
    }

    private static bool IsWhiteControllerPixel(int red, int green, int blue)
    {
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        return red >= 210 &&
            green >= 210 &&
            blue >= 205 &&
            max - min <= 50;
    }

    private static bool IsYellowControllerPixel(int red, int green, int blue)
    {
        return red >= 140 &&
            green >= 110 &&
            blue <= 100 &&
            red >= green + 5 &&
            green >= blue + 35;
    }

    private static ScreenBox RatioBox(
        RgbFrame frame,
        double left,
        double top,
        double right,
        double bottom)
    {
        return new ScreenBox(
            Clamp((int)(frame.Width * left), 0, frame.Width),
            Clamp((int)(frame.Height * top), 0, frame.Height),
            Clamp((int)(frame.Width * right), 0, frame.Width),
            Clamp((int)(frame.Height * bottom), 0, frame.Height));
    }

    private static int Clamp(int value, int min, int max)
    {
        return Math.Min(max, Math.Max(min, value));
    }

    private sealed record ControllerIconMetrics(
        double WhiteRatio,
        double YellowRatio,
        double DarkRatio);
}

public enum ChatUiLayoutKind
{
    None,
    KeyboardMouse,
    XboxController,
    PlayStationController,
}

public sealed record ChatUiGateResult(
    bool IsChatInterface,
    double Score,
    ChatUiLayoutKind Layout,
    ChatUiGatePointScore BackButton,
    ChatUiGatePointScore MoreButton,
    ChatUiGatePointScore SendButton,
    ChatUiGatePointScore InputMenuButton);

public sealed record ChatUiGatePointScore(
    double Score,
    double LightRatio,
    double DarkRatio)
{
    public static ChatUiGatePointScore Empty { get; } = new(0, 0, 0);
}
