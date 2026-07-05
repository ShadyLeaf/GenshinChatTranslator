namespace GenshinChatTranslator.App.Models;

public readonly record struct ScreenBox(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;

    public int Height => Bottom - Top;

    public int Area => Width * Height;

    public ScreenBox Pad(int value, ScreenBox limit)
    {
        return new ScreenBox(
            Math.Max(limit.Left, Left - value),
            Math.Max(limit.Top, Top - value),
            Math.Min(limit.Right, Right + value),
            Math.Min(limit.Bottom, Bottom + value));
    }

    public ScreenBox Shrink(int x, int y)
    {
        if (Width <= x * 2 || Height <= y * 2)
        {
            return this;
        }

        return new ScreenBox(Left + x, Top + y, Right - x, Bottom - y);
    }

    public int[] ToArray()
    {
        return [Left, Top, Right, Bottom];
    }

    public override string ToString()
    {
        return $"[{Left}, {Top}, {Right}, {Bottom}]";
    }
}
