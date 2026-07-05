namespace GenshinChatTranslator.App.Models;

public sealed record ChatBubbleRoi(
    string Kind,
    ScreenBox BubbleBox,
    ScreenBox TextBox,
    double Confidence);
