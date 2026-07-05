namespace GenshinChatTranslator.App.Models;

public sealed record RgbFrame(int Width, int Height, byte[] Pixels)
{
    public int PixelOffset(int x, int y)
    {
        return ((y * Width) + x) * 3;
    }
}
