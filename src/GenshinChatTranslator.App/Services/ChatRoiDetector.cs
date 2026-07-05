using GenshinChatTranslator.App.Models;

namespace GenshinChatTranslator.App.Services;

public sealed class ChatRoiDetector
{
    public const string SelfLightKind = "self_light";
    public const string OtherDarkKind = "other_dark";
    private const int NormalizedWidth = 2560;
    private const int NormalizedHeight = 1440;

    private readonly RoiDetectionConfig _config;

    public ChatRoiDetector(RoiDetectionConfig config)
    {
        _config = config;
    }

    public (ScreenBox MessageRoi, IReadOnlyList<ChatBubbleRoi> Rois) Locate(RgbFrame frame, int? scaleOverride = null)
    {
        if (ShouldNormalizeToBaseline(frame))
        {
            var normalizedFrame = ResizeNearest(frame, NormalizedWidth, NormalizedHeight);
            var (normalizedMessageRoi, normalizedRois) = LocateCore(normalizedFrame, scaleOverride);
            return (
                MapBoxToSource(normalizedMessageRoi, frame.Width, frame.Height),
                normalizedRois
                    .Select(roi => new ChatBubbleRoi(
                        roi.Kind,
                        MapBoxToSource(roi.BubbleBox, frame.Width, frame.Height),
                        MapBoxToSource(roi.TextBox, frame.Width, frame.Height),
                        roi.Confidence))
                    .ToArray());
        }

        return LocateCore(frame, scaleOverride);
    }

    private (ScreenBox MessageRoi, IReadOnlyList<ChatBubbleRoi> Rois) LocateCore(RgbFrame frame, int? scaleOverride = null)
    {
        var scale = scaleOverride ?? _config.Processing.Scale;
        if (scale <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(scaleOverride), scale, "Scale must be positive.");
        }

        var fullBounds = new ScreenBox(0, 0, frame.Width, frame.Height);
        var messageRoi = MakeMessageRoi(frame.Width, frame.Height);
        var scaledMessageRoi = new ScreenBox(
            messageRoi.Left / scale,
            messageRoi.Top / scale,
            messageRoi.Right / scale,
            messageRoi.Bottom / scale);

        var small = Downsample(frame, scale);
        _lastDownsampledSize = new DownsampledSize(small.Width, small.Height);
        var masks = MakeMasks(small, scaledMessageRoi, frame, messageRoi, scale);
        var rois = new List<ChatBubbleRoi>();

        foreach (var (kind, mask) in masks)
        {
            foreach (var component in ConnectedComponents(mask))
            {
                var (pixelArea, box) = RefineComponentBox(kind, frame, mask, component.Box, component.Area, scale, fullBounds);
                var colorArea = pixelArea * scale * scale;
                if (!IsBubbleCandidate(kind, box, colorArea, messageRoi))
                {
                    rois.AddRange(MakeTextSeedFallbackRois(kind, frame, mask, box, scale, fullBounds, messageRoi));
                    continue;
                }

                var textBox = MakeTextBox(box, kind, frame);
                if (!HasEnoughTextColorPixels(frame, kind, textBox))
                {
                    continue;
                }

                var confidence = Math.Min(1.0, colorArea / (double)Math.Max(1, box.Area));
                rois.Add(new ChatBubbleRoi(kind, box, textBox, confidence));
            }
        }

        var uniqueRois = SuppressDuplicateRois(rois);
        uniqueRois.Sort((left, right) =>
        {
            var topCompare = left.BubbleBox.Top.CompareTo(right.BubbleBox.Top);
            return topCompare != 0 ? topCompare : left.BubbleBox.Left.CompareTo(right.BubbleBox.Left);
        });

        return (messageRoi, uniqueRois);
    }

    private static bool ShouldNormalizeToBaseline(RgbFrame frame)
    {
        return (frame.Width != NormalizedWidth || frame.Height != NormalizedHeight) &&
            IsSixteenByNine(frame.Width, frame.Height);
    }

    private static bool IsSixteenByNine(int width, int height)
    {
        return width > 0 && height > 0 && (long)width * 9 == (long)height * 16;
    }

    private static RgbFrame ResizeNearest(RgbFrame frame, int width, int height)
    {
        var pixels = new byte[width * height * 3];
        for (var y = 0; y < height; y++)
        {
            var sourceY = Math.Min(frame.Height - 1, (int)(((long)y * frame.Height + (height / 2L)) / height));
            for (var x = 0; x < width; x++)
            {
                var sourceX = Math.Min(frame.Width - 1, (int)(((long)x * frame.Width + (width / 2L)) / width));
                var sourceOffset = frame.PixelOffset(sourceX, sourceY);
                var targetOffset = ((y * width) + x) * 3;
                pixels[targetOffset] = frame.Pixels[sourceOffset];
                pixels[targetOffset + 1] = frame.Pixels[sourceOffset + 1];
                pixels[targetOffset + 2] = frame.Pixels[sourceOffset + 2];
            }
        }

        return new RgbFrame(width, height, pixels);
    }

    private static ScreenBox MapBoxToSource(ScreenBox box, int sourceWidth, int sourceHeight)
    {
        return new ScreenBox(
            ScaleFloor(box.Left, sourceWidth, NormalizedWidth),
            ScaleFloor(box.Top, sourceHeight, NormalizedHeight),
            ScaleCeiling(box.Right, sourceWidth, NormalizedWidth),
            ScaleCeiling(box.Bottom, sourceHeight, NormalizedHeight));
    }

    private static int ScaleFloor(int value, int numerator, int denominator)
    {
        return (int)((long)value * numerator / denominator);
    }

    private static int ScaleCeiling(int value, int numerator, int denominator)
    {
        return (int)(((long)value * numerator + denominator - 1) / denominator);
    }

    private ScreenBox MakeMessageRoi(int width, int height)
    {
        return new ScreenBox(
            (int)(width * _config.MessageRoi.LeftRatio),
            (int)(height * _config.MessageRoi.TopRatio),
            (int)(width * _config.MessageRoi.RightRatio),
            (int)(height * _config.MessageRoi.BottomRatio));
    }

    private static DownsampledRgb Downsample(RgbFrame frame, int scale)
    {
        if (scale <= 1)
        {
            var values = new short[frame.Pixels.Length];
            for (var index = 0; index < frame.Pixels.Length; index++)
            {
                values[index] = frame.Pixels[index];
            }

            return new DownsampledRgb(frame.Width, frame.Height, values);
        }

        var scaledWidth = frame.Width / scale;
        var scaledHeight = frame.Height / scale;
        var pixels = new short[scaledWidth * scaledHeight * 3];

        for (var sy = 0; sy < scaledHeight; sy++)
        {
            for (var sx = 0; sx < scaledWidth; sx++)
            {
                var red = 0;
                var green = 0;
                var blue = 0;
                for (var dy = 0; dy < scale; dy++)
                {
                    var y = (sy * scale) + dy;
                    for (var dx = 0; dx < scale; dx++)
                    {
                        var x = (sx * scale) + dx;
                        var sourceOffset = frame.PixelOffset(x, y);
                        red += frame.Pixels[sourceOffset];
                        green += frame.Pixels[sourceOffset + 1];
                        blue += frame.Pixels[sourceOffset + 2];
                    }
                }

                var targetOffset = ((sy * scaledWidth) + sx) * 3;
                var divisor = scale * scale;
                pixels[targetOffset] = (short)(red / divisor);
                pixels[targetOffset + 1] = (short)(green / divisor);
                pixels[targetOffset + 2] = (short)(blue / divisor);
            }
        }

        return new DownsampledRgb(scaledWidth, scaledHeight, pixels);
    }

    private IReadOnlyList<(string Kind, bool[] Mask)> MakeMasks(
        DownsampledRgb rgb,
        ScreenBox scaledMessageRoi,
        RgbFrame frame,
        ScreenBox messageRoi,
        int scale)
    {
        var lightMask = new bool[rgb.Width * rgb.Height];
        var darkMask = new bool[rgb.Width * rgb.Height];
        var darkTarget = _config.Masks.OtherDark.TargetRgb;
        var darkTolerance = _config.Masks.OtherDark.ToleranceRgb;

        for (var y = Math.Max(0, scaledMessageRoi.Top); y < Math.Min(rgb.Height, scaledMessageRoi.Bottom); y++)
        {
            for (var x = Math.Max(0, scaledMessageRoi.Left); x < Math.Min(rgb.Width, scaledMessageRoi.Right); x++)
            {
                var pixelIndex = (y * rgb.Width) + x;
                var offset = pixelIndex * 3;
                var red = rgb.Pixels[offset];
                var green = rgb.Pixels[offset + 1];
                var blue = rgb.Pixels[offset + 2];

                lightMask[pixelIndex] =
                    red > _config.Masks.SelfLight.RedMin &&
                    green > _config.Masks.SelfLight.GreenMin &&
                    blue > _config.Masks.SelfLight.BlueMin &&
                    red - blue > _config.Masks.SelfLight.RedBlueDeltaMin;

                darkMask[pixelIndex] =
                    Math.Abs(red - darkTarget[0]) <= darkTolerance[0] &&
                    Math.Abs(green - darkTarget[1]) <= darkTolerance[1] &&
                    Math.Abs(blue - darkTarget[2]) <= darkTolerance[2];
            }
        }

        AddTextSeedExpansionToMask(lightMask, frame, messageRoi, SelfLightKind, scale, rgb.Width, rgb.Height);
        AddTextSeedExpansionToMask(darkMask, frame, messageRoi, OtherDarkKind, scale, rgb.Width, rgb.Height);

        return [(SelfLightKind, lightMask), (OtherDarkKind, darkMask)];
    }

    private void AddTextSeedExpansionToMask(
        bool[] mask,
        RgbFrame frame,
        ScreenBox messageRoi,
        string kind,
        int scale,
        int scaledWidth,
        int scaledHeight)
    {
        if (!_config.TextColorGuard.SeedExpansionEnabled || _config.TextColorGuard.SeedExpansionRadius <= 0)
        {
            return;
        }

        var baseMask = new bool[mask.Length];
        Array.Copy(mask, baseMask, mask.Length);
        var target = kind == SelfLightKind ? _config.TextColorGuard.SelfLight : _config.TextColorGuard.OtherDark;
        if (target.Length != 3)
        {
            return;
        }

        var clipped = new ScreenBox(
            Math.Max(0, messageRoi.Left),
            Math.Max(0, messageRoi.Top),
            Math.Min(frame.Width, messageRoi.Right),
            Math.Min(frame.Height, messageRoi.Bottom));
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return;
        }

        var scaledRadius = Math.Max(1, (_config.TextColorGuard.SeedExpansionRadius + Math.Max(1, scale) - 1) / Math.Max(1, scale));
        for (var y = clipped.Top; y < clipped.Bottom; y++)
        {
            for (var x = clipped.Left; x < clipped.Right; x++)
            {
                var offset = frame.PixelOffset(x, y);
                if (frame.Pixels[offset] != target[0] ||
                    frame.Pixels[offset + 1] != target[1] ||
                    frame.Pixels[offset + 2] != target[2])
                {
                    continue;
                }

                var seedX = x / scale;
                var seedY = y / scale;
                var left = Math.Max(0, seedX - scaledRadius);
                var right = Math.Min(scaledWidth, seedX + scaledRadius + 1);
                var top = Math.Max(0, seedY - scaledRadius);
                var bottom = Math.Min(scaledHeight, seedY + scaledRadius + 1);
                if (CountMaskPixels(baseMask, scaledWidth, left, top, right, bottom) < _config.TextColorGuard.SeedExpansionMinBasePixels)
                {
                    continue;
                }

                for (var expandY = top; expandY < bottom; expandY++)
                {
                    for (var expandX = left; expandX < right; expandX++)
                    {
                        mask[(expandY * scaledWidth) + expandX] = true;
                    }
                }
            }
        }
    }

    private static int CountMaskPixels(bool[] mask, int width, int left, int top, int right, int bottom)
    {
        var count = 0;
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                if (mask[(y * width) + x])
                {
                    count++;
                }
            }
        }

        return count;
    }

    private IReadOnlyList<ChatBubbleRoi> MakeTextSeedFallbackRois(
        string kind,
        RgbFrame frame,
        bool[] mask,
        ScreenBox rejectedBox,
        int scale,
        ScreenBox fullBounds,
        ScreenBox messageRoi)
    {
        var fallback = _config.TextSeedFallback;
        if (kind != OtherDarkKind || !fallback.Enabled)
        {
            return [];
        }

        var target = _config.TextColorGuard.OtherDark;
        if (target.Length != 3)
        {
            return [];
        }

        var clipped = new ScreenBox(
            Math.Max(fullBounds.Left, rejectedBox.Left),
            Math.Max(fullBounds.Top, rejectedBox.Top),
            Math.Min(fullBounds.Right, rejectedBox.Right),
            Math.Min(fullBounds.Bottom, rejectedBox.Bottom));
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return [];
        }

        var exactMask = new bool[clipped.Width * clipped.Height];
        var rowCounts = new int[clipped.Height];
        var minSeedX = messageRoi.Left + (int)(messageRoi.Width * fallback.MinSeedXOffsetRatio);
        for (var y = clipped.Top; y < clipped.Bottom; y++)
        {
            var localY = y - clipped.Top;
            for (var x = Math.Max(clipped.Left, minSeedX); x < clipped.Right; x++)
            {
                var offset = frame.PixelOffset(x, y);
                if (frame.Pixels[offset] != target[0] ||
                    frame.Pixels[offset + 1] != target[1] ||
                    frame.Pixels[offset + 2] != target[2])
                {
                    continue;
                }

                exactMask[(localY * clipped.Width) + x - clipped.Left] = true;
                rowCounts[localY]++;
            }
        }

        var rowClusters = BuildDenseRowClusters(rowCounts, fallback.RowMinPixels, fallback.MaxRowGap);
        if (rowClusters.Count == 0)
        {
            return [];
        }

        var rois = new List<ChatBubbleRoi>();
        foreach (var (clusterTop, clusterBottom) in rowClusters)
        {
            var exactCount = 0;
            var minX = clipped.Width;
            var minY = clipped.Height;
            var maxX = -1;
            var maxY = -1;

            for (var localY = clusterTop; localY <= clusterBottom; localY++)
            {
                for (var localX = 0; localX < clipped.Width; localX++)
                {
                    if (!exactMask[(localY * clipped.Width) + localX])
                    {
                        continue;
                    }

                    exactCount++;
                    minX = Math.Min(minX, localX);
                    minY = Math.Min(minY, localY);
                    maxX = Math.Max(maxX, localX);
                    maxY = Math.Max(maxY, localY);
                }
            }

            if (maxX < minX || maxY < minY)
            {
                continue;
            }

            var textWidth = maxX - minX + 1;
            var textHeight = maxY - minY + 1;
            var exactFillRatio = exactCount / (double)Math.Max(1, textWidth * textHeight);
            if (!IsTextSeedFallbackClusterAllowed(exactCount, textWidth, textHeight, exactFillRatio))
            {
                continue;
            }

            var textBounds = new ScreenBox(
                clipped.Left + minX,
                clipped.Top + minY,
                clipped.Left + maxX + 1,
                clipped.Top + maxY + 1);
            var candidate = ClampTextSeedFallbackBox(
                MakeTextSeedFallbackBox(textBounds, fullBounds),
                textBounds,
                messageRoi);
            var colorArea = CountScaledMaskPixels(mask, candidate, scale) * scale * scale;
            if (!IsBubbleCandidate(kind, candidate, colorArea, messageRoi))
            {
                continue;
            }

            var textBox = MakeTextBox(candidate, kind, frame);
            if (!HasEnoughTextColorPixels(frame, kind, textBox))
            {
                continue;
            }

            var confidence = Math.Min(1.0, colorArea / (double)Math.Max(1, candidate.Area));
            rois.Add(new ChatBubbleRoi(kind, candidate, textBox, confidence));
        }

        return rois;
    }

    private ScreenBox ClampTextSeedFallbackBox(ScreenBox candidate, ScreenBox textBounds, ScreenBox messageRoi)
    {
        var filter = _config.CandidateFilter;
        var textPadding = _config.TextColorGuard.Padding;
        var rightLimit = messageRoi.Right - (int)(messageRoi.Width * filter.EdgeMarginRatio);
        var right = candidate.Right;
        if (textBounds.Right + textPadding <= rightLimit)
        {
            right = Math.Min(right, rightLimit);
        }

        var bottom = candidate.Bottom;
        if (textBounds.Bottom + textPadding <= messageRoi.Bottom)
        {
            bottom = Math.Min(bottom, messageRoi.Bottom);
        }

        return new ScreenBox(candidate.Left, candidate.Top, right, bottom);
    }

    private bool IsTextSeedFallbackClusterAllowed(int exactPixels, int textWidth, int textHeight, double exactFillRatio)
    {
        var fallback = _config.TextSeedFallback;
        if (exactPixels < fallback.MinExactPixels)
        {
            return false;
        }

        if (textHeight < fallback.MinTextHeight)
        {
            return false;
        }

        if (textWidth < fallback.ShortTextWidthThreshold && exactFillRatio > fallback.ShortTextMaxFillRatio)
        {
            return false;
        }

        return true;
    }

    private List<ChatBubbleRoi> SuppressDuplicateRois(IEnumerable<ChatBubbleRoi> rois)
    {
        var overlapRatio = _config.CandidateFilter.DuplicateOverlapRatio;
        var source = rois.ToList();
        if (source.Count < 2 || overlapRatio >= 1.0)
        {
            return source;
        }

        var kept = new List<ChatBubbleRoi>();
        foreach (var roi in source.OrderByDescending(item => item.Confidence))
        {
            if (kept.Any(existing =>
                    roi.Kind == existing.Kind &&
                    OverlapArea(roi.BubbleBox, existing.BubbleBox) / (double)Math.Max(1, Math.Min(roi.BubbleBox.Area, existing.BubbleBox.Area)) >= overlapRatio))
            {
                continue;
            }

            kept.Add(roi);
        }

        return kept;
    }

    private static int OverlapArea(ScreenBox left, ScreenBox right)
    {
        var overlapLeft = Math.Max(left.Left, right.Left);
        var overlapTop = Math.Max(left.Top, right.Top);
        var overlapRight = Math.Min(left.Right, right.Right);
        var overlapBottom = Math.Min(left.Bottom, right.Bottom);
        return Math.Max(0, overlapRight - overlapLeft) * Math.Max(0, overlapBottom - overlapTop);
    }

    private static IReadOnlyList<(int Top, int Bottom)> BuildDenseRowClusters(int[] rowCounts, int rowMinPixels, int maxRowGap)
    {
        var clusters = new List<(int Top, int Bottom)>();
        var start = -1;
        var previous = -1;
        for (var row = 0; row < rowCounts.Length; row++)
        {
            if (rowCounts[row] < rowMinPixels)
            {
                continue;
            }

            if (start < 0)
            {
                start = row;
                previous = row;
                continue;
            }

            if (row - previous <= maxRowGap)
            {
                previous = row;
                continue;
            }

            clusters.Add((start, previous));
            start = row;
            previous = row;
        }

        if (start >= 0)
        {
            clusters.Add((start, previous));
        }

        return clusters;
    }

    private ScreenBox MakeTextSeedFallbackBox(ScreenBox textBounds, ScreenBox fullBounds)
    {
        var fallback = _config.TextSeedFallback;
        var left = textBounds.Left - fallback.PaddingLeft;
        var top = textBounds.Top - fallback.PaddingTop;
        var right = textBounds.Right + fallback.PaddingRight;
        var bottom = textBounds.Bottom + fallback.PaddingBottom;

        if (right - left < fallback.MinWidth)
        {
            right = left + fallback.MinWidth;
        }

        if (bottom - top < fallback.MinHeight)
        {
            bottom = top + fallback.MinHeight;
        }

        return new ScreenBox(
            Math.Max(fullBounds.Left, left),
            Math.Max(fullBounds.Top, top),
            Math.Min(fullBounds.Right, right),
            Math.Min(fullBounds.Bottom, bottom));
    }

    private int CountScaledMaskPixels(bool[] mask, ScreenBox box, int scale)
    {
        var size = _lastDownsampledSize ?? throw new InvalidOperationException("Missing downsampled size.");
        var left = Math.Max(0, box.Left / scale);
        var top = Math.Max(0, box.Top / scale);
        var right = Math.Min(size.Width, (box.Right + scale - 1) / scale);
        var bottom = Math.Min(size.Height, (box.Bottom + scale - 1) / scale);
        if (left >= right || top >= bottom)
        {
            return 0;
        }

        return CountMaskPixels(mask, size.Width, left, top, right, bottom);
    }

    private static IEnumerable<Component> ConnectedComponents(bool[] mask, int width, int height)
    {
        var seen = new bool[mask.Length];
        var queue = new Queue<int>();

        for (var index = 0; index < mask.Length; index++)
        {
            if (seen[index] || !mask[index])
            {
                continue;
            }

            seen[index] = true;
            queue.Enqueue(index);
            var minX = index % width;
            var maxX = minX;
            var minY = index / width;
            var maxY = minY;
            var area = 0;

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                area++;
                var x = current % width;
                var y = current / width;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);

                for (var ny = y - 1; ny <= y + 1; ny++)
                {
                    if (ny < 0 || ny >= height)
                    {
                        continue;
                    }

                    for (var nx = x - 1; nx <= x + 1; nx++)
                    {
                        if (nx < 0 || nx >= width)
                        {
                            continue;
                        }

                        var neighbor = (ny * width) + nx;
                        if (seen[neighbor] || !mask[neighbor])
                        {
                            continue;
                        }

                        seen[neighbor] = true;
                        queue.Enqueue(neighbor);
                    }
                }
            }

            yield return new Component(area, new ScreenBox(minX, minY, maxX + 1, maxY + 1));
        }
    }

    private IEnumerable<Component> ConnectedComponents(bool[] mask)
    {
        var scale = _lastDownsampledSize ?? throw new InvalidOperationException("Missing downsampled size.");
        return ConnectedComponents(mask, scale.Width, scale.Height);
    }

    private (int Area, ScreenBox Box) RefineComponentBox(
        string kind,
        RgbFrame frame,
        bool[] mask,
        ScreenBox smallBox,
        int pixelArea,
        int scale,
        ScreenBox fullBounds)
    {
        var box = ScaleSmallBox(smallBox, scale, fullBounds);
        var textBounds = FindExactTextColorBounds(frame, kind, box);
        var fillRatio = pixelArea * scale * scale / (double)Math.Max(1, box.Area);
        if (fillRatio >= _config.ComponentRefine.AlreadyDenseFillRatio)
        {
            return (pixelArea, box);
        }

        var maskWidth = _lastDownsampledSize?.Width ?? 0;
        if (maskWidth <= 0)
        {
            return (pixelArea, box);
        }

        var subWidth = smallBox.Width;
        var subHeight = smallBox.Height;
        var rowCounts = new int[subHeight];
        for (var y = 0; y < subHeight; y++)
        {
            var sourceY = smallBox.Top + y;
            for (var x = 0; x < subWidth; x++)
            {
                if (mask[(sourceY * maskWidth) + smallBox.Left + x])
                {
                    rowCounts[y]++;
                }
            }
        }

        var maxRowCount = rowCounts.Length == 0 ? 0 : rowCounts.Max();
        if (maxRowCount < _config.ComponentRefine.MinMaxRowCount)
        {
            return (pixelArea, box);
        }

        var rowThreshold = Math.Max(
            _config.ComponentRefine.DenseRowThresholdMin,
            (int)(maxRowCount * _config.ComponentRefine.DenseRowThresholdRatio));
        var denseRows = DenseIndexes(rowCounts, rowThreshold).ToArray();
        if (denseRows.Length == 0)
        {
            return (pixelArea, box);
        }

        var top = Math.Max(0, denseRows[0] - _config.ComponentRefine.RowTopPadding);
        var bottom = Math.Min(subHeight, denseRows[^1] + _config.ComponentRefine.RowBottomPadding);
        var denseHeight = Math.Max(0, bottom - top);
        var colCounts = new int[subWidth];
        for (var y = 0; y < denseHeight; y++)
        {
            var sourceY = smallBox.Top + top + y;
            for (var x = 0; x < subWidth; x++)
            {
                if (mask[(sourceY * maskWidth) + smallBox.Left + x])
                {
                    colCounts[x]++;
                }
            }
        }

        var maxColCount = colCounts.Length == 0 ? 0 : colCounts.Max();
        if (maxColCount < _config.ComponentRefine.MinMaxColCount)
        {
            return (pixelArea, box);
        }

        var colThreshold = Math.Max(
            _config.ComponentRefine.DenseColThresholdMin,
            (int)(maxColCount * _config.ComponentRefine.DenseColThresholdRatio));
        var denseCols = DenseIndexes(colCounts, colThreshold).ToArray();
        if (denseCols.Length == 0)
        {
            return (pixelArea, box);
        }

        var left = Math.Max(0, denseCols[0] - _config.ComponentRefine.ColLeftPadding);
        var right = Math.Min(subWidth, denseCols[^1] + _config.ComponentRefine.ColRightPadding);
        var refinedSmallBox = new ScreenBox(
            smallBox.Left + left,
            smallBox.Top + top,
            smallBox.Left + right,
            smallBox.Top + bottom);

        var refinedArea = 0;
        for (var y = refinedSmallBox.Top; y < refinedSmallBox.Bottom; y++)
        {
            for (var x = refinedSmallBox.Left; x < refinedSmallBox.Right; x++)
            {
                if (mask[(y * maskWidth) + x])
                {
                    refinedArea++;
                }
            }
        }

        var refinedBox = IncludeTextColorBounds(ScaleSmallBox(refinedSmallBox, scale, fullBounds), textBounds, fullBounds);
        return (refinedArea, refinedBox);
    }

    private DownsampledSize? _lastDownsampledSize;

    private ScreenBox ScaleSmallBox(ScreenBox box, int scale, ScreenBox fullBounds)
    {
        return new ScreenBox(box.Left * scale, box.Top * scale, box.Right * scale, box.Bottom * scale)
            .Pad(_config.ComponentRefine.ScalePadding, fullBounds);
    }

    private ScreenBox MakeTextBox(ScreenBox box, string kind, RgbFrame frame)
    {
        var shrinkX = Math.Min(
            _config.TextBox.ShrinkXMax,
            Math.Max(_config.TextBox.ShrinkXMin, box.Width / _config.TextBox.ShrinkXDivisor));
        var shrinkY = Math.Min(
            _config.TextBox.ShrinkYMax,
            Math.Max(_config.TextBox.ShrinkYMin, box.Height / _config.TextBox.ShrinkYDivisor));
        var textBox = box.Shrink(shrinkX, shrinkY);
        var textBounds = FindExactTextColorBounds(frame, kind, box);
        return IncludeTextColorBounds(textBox, textBounds, box);
    }

    private ScreenBox? FindExactTextColorBounds(RgbFrame frame, string kind, ScreenBox box)
    {
        if (!_config.TextColorGuard.Enabled)
        {
            return null;
        }

        var target = kind == SelfLightKind ? _config.TextColorGuard.SelfLight : _config.TextColorGuard.OtherDark;
        if (target.Length != 3)
        {
            return null;
        }

        var clipped = new ScreenBox(
            Math.Max(0, box.Left),
            Math.Max(0, box.Top),
            Math.Min(frame.Width, box.Right),
            Math.Min(frame.Height, box.Bottom));
        if (clipped.Width <= 0 || clipped.Height <= 0)
        {
            return null;
        }

        var found = false;
        var minX = clipped.Right;
        var minY = clipped.Bottom;
        var maxX = clipped.Left;
        var maxY = clipped.Top;
        for (var y = clipped.Top; y < clipped.Bottom; y++)
        {
            for (var x = clipped.Left; x < clipped.Right; x++)
            {
                var offset = frame.PixelOffset(x, y);
                if (frame.Pixels[offset] != target[0] ||
                    frame.Pixels[offset + 1] != target[1] ||
                    frame.Pixels[offset + 2] != target[2])
                {
                    continue;
                }

                found = true;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return found ? new ScreenBox(minX, minY, maxX + 1, maxY + 1) : null;
    }

    private ScreenBox IncludeTextColorBounds(ScreenBox box, ScreenBox? textBounds, ScreenBox fullBounds)
    {
        if (textBounds is null)
        {
            return box;
        }

        var padding = _config.TextColorGuard.Padding;
        return new ScreenBox(
            Math.Max(fullBounds.Left, Math.Min(box.Left, textBounds.Value.Left - padding)),
            Math.Max(fullBounds.Top, Math.Min(box.Top, textBounds.Value.Top - padding)),
            Math.Min(fullBounds.Right, Math.Max(box.Right, textBounds.Value.Right + padding)),
            Math.Min(fullBounds.Bottom, Math.Max(box.Bottom, textBounds.Value.Bottom + padding)));
    }

    private bool HasEnoughTextColorPixels(RgbFrame frame, string kind, ScreenBox textBox)
    {
        if (textBox.Width <= 0 || textBox.Height <= 0)
        {
            return false;
        }

        var count = 0;
        var exactCount = 0;
        var exactTarget = kind == SelfLightKind ? _config.TextColorGuard.SelfLight : _config.TextColorGuard.OtherDark;
        if (exactTarget.Length != 3)
        {
            return false;
        }

        for (var y = textBox.Top; y < textBox.Bottom; y++)
        {
            for (var x = textBox.Left; x < textBox.Right; x++)
            {
                var offset = frame.PixelOffset(x, y);
                var red = frame.Pixels[offset];
                var green = frame.Pixels[offset + 1];
                var blue = frame.Pixels[offset + 2];
                var maxChannel = Math.Max(red, Math.Max(green, blue));
                var minChannel = Math.Min(red, Math.Min(green, blue));
                if (red == exactTarget[0] && green == exactTarget[1] && blue == exactTarget[2])
                {
                    exactCount++;
                }

                if (kind == SelfLightKind)
                {
                    var config = _config.TextValidation.SelfLight;
                    if (red < config.RedMax &&
                        green < config.GreenMax &&
                        blue < config.BlueMax &&
                        maxChannel - minChannel < config.MaxChannelDelta)
                    {
                        count++;
                    }
                }
                else
                {
                    var config = _config.TextValidation.OtherDark;
                    if (red > config.RedMin &&
                        green > config.GreenMin &&
                        blue > config.BlueMin &&
                        maxChannel - minChannel < config.MaxChannelDelta)
                    {
                        count++;
                    }
                }
            }
        }

        var minPixels = kind == SelfLightKind
            ? Math.Max(_config.TextValidation.SelfLight.MinPixels, textBox.Area / _config.TextValidation.SelfLight.AreaDivisor)
            : Math.Max(_config.TextValidation.OtherDark.MinPixels, textBox.Area / _config.TextValidation.OtherDark.AreaDivisor);
        var exactMinPixels = kind == SelfLightKind
            ? _config.TextValidation.SelfLight.ExactMinPixels
            : _config.TextValidation.OtherDark.ExactMinPixels;
        return exactCount >= exactMinPixels && count >= minPixels;
    }

    private bool IsBubbleCandidate(string kind, ScreenBox box, int colorArea, ScreenBox messageRoi)
    {
        var filter = _config.CandidateFilter;
        if (box.Width < filter.MinWidth || box.Height < filter.MinHeight)
        {
            return false;
        }

        if (box.Width > filter.MaxWidth || box.Height > filter.MaxHeight)
        {
            return false;
        }

        if (box.Width / (double)Math.Max(1, box.Height) < filter.MinAspectRatio)
        {
            return false;
        }

        if (colorArea / (double)Math.Max(1, box.Area) < filter.MinBubbleFillRatio)
        {
            return false;
        }

        if (kind == SelfLightKind)
        {
            if (colorArea < filter.SelfLightMinColorArea)
            {
                return false;
            }

            var minLeft = messageRoi.Left + (int)(messageRoi.Width * filter.EdgeMarginRatio);
            var minRight = messageRoi.Left + (int)(messageRoi.Width * filter.SelfRightAnchorRatio);
            return box.Left >= minLeft && box.Right >= minRight;
        }

        if (colorArea < filter.OtherDarkMinColorArea)
        {
            return false;
        }

        var anchorMin = messageRoi.Left + (int)(messageRoi.Width * filter.OtherLeftAnchorMinRatio);
        var anchorMax = messageRoi.Left + (int)(messageRoi.Width * filter.OtherLeftAnchorMaxRatio);
        var maxRight = messageRoi.Right - (int)(messageRoi.Width * filter.EdgeMarginRatio);
        return box.Left >= anchorMin && box.Left <= anchorMax && box.Right <= maxRight;
    }

    private static IEnumerable<int> DenseIndexes(int[] counts, int threshold)
    {
        for (var index = 0; index < counts.Length; index++)
        {
            if (counts[index] >= threshold)
            {
                yield return index;
            }
        }
    }

    private sealed record DownsampledRgb(int Width, int Height, short[] Pixels);

    private sealed record DownsampledSize(int Width, int Height);

    private sealed record Component(int Area, ScreenBox Box);
}
