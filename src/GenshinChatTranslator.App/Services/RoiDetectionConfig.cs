namespace GenshinChatTranslator.App.Services;

public sealed record RoiDetectionConfig(
    ProcessingConfig Processing,
    MessageRoiConfig MessageRoi,
    MaskConfig Masks,
    CandidateFilterConfig CandidateFilter,
    TextBoxConfig TextBox,
    TextSeedFallbackConfig TextSeedFallback,
    ComponentRefineConfig ComponentRefine,
    TextColorGuardConfig TextColorGuard,
    TextValidationConfig TextValidation)
{
    public static RoiDetectionConfig Load(string path)
    {
        var root = SimpleYamlReader.ReadMapping(path);
        var processing = SimpleYamlReader.Section(root, "processing");
        var messageRoi = SimpleYamlReader.Section(root, "message_roi");
        var masks = SimpleYamlReader.Section(root, "masks");
        var selfLightMask = SimpleYamlReader.Section(masks, "self_light");
        var otherDarkMask = SimpleYamlReader.Section(masks, "other_dark");
        var candidateFilter = SimpleYamlReader.Section(root, "candidate_filter");
        var textBox = SimpleYamlReader.Section(root, "text_box");
        var textSeedFallback = SimpleYamlReader.Section(root, "text_seed_fallback");
        var componentRefine = SimpleYamlReader.Section(root, "component_refine");
        var textGuard = SimpleYamlReader.Section(root, "text_color_guard");
        var textGuardColors = SimpleYamlReader.Section(textGuard, "colors");
        var textValidation = SimpleYamlReader.Section(root, "text_validation");
        var selfLightValidation = SimpleYamlReader.Section(textValidation, "self_light");
        var otherDarkValidation = SimpleYamlReader.Section(textValidation, "other_dark");

        return new RoiDetectionConfig(
            new ProcessingConfig(SimpleYamlReader.Int(processing, "scale")),
            new MessageRoiConfig(
                SimpleYamlReader.Double(messageRoi, "left_ratio"),
                SimpleYamlReader.Double(messageRoi, "top_ratio"),
                SimpleYamlReader.Double(messageRoi, "right_ratio"),
                SimpleYamlReader.Double(messageRoi, "bottom_ratio")),
            new MaskConfig(
                new SelfLightMaskConfig(
                    SimpleYamlReader.Int(selfLightMask, "red_min"),
                    SimpleYamlReader.Int(selfLightMask, "green_min"),
                    SimpleYamlReader.Int(selfLightMask, "blue_min"),
                    SimpleYamlReader.Int(selfLightMask, "red_blue_delta_min")),
                new OtherDarkMaskConfig(
                    SimpleYamlReader.IntArray(otherDarkMask, "target_rgb"),
                    SimpleYamlReader.IntArray(otherDarkMask, "tolerance_rgb"))),
            new CandidateFilterConfig(
                SimpleYamlReader.Int(candidateFilter, "min_width"),
                SimpleYamlReader.Int(candidateFilter, "min_height"),
                SimpleYamlReader.Int(candidateFilter, "max_width"),
                SimpleYamlReader.Int(candidateFilter, "max_height"),
                SimpleYamlReader.Double(candidateFilter, "min_aspect_ratio"),
                SimpleYamlReader.Double(candidateFilter, "min_bubble_fill_ratio"),
                SimpleYamlReader.Int(candidateFilter, "self_light_min_color_area"),
                SimpleYamlReader.Int(candidateFilter, "other_dark_min_color_area"),
                SimpleYamlReader.Double(candidateFilter, "edge_margin_ratio"),
                SimpleYamlReader.Double(candidateFilter, "self_right_anchor_ratio"),
                SimpleYamlReader.Double(candidateFilter, "other_left_anchor_min_ratio"),
                SimpleYamlReader.Double(candidateFilter, "other_left_anchor_max_ratio"),
                SimpleYamlReader.Double(candidateFilter, "duplicate_overlap_ratio")),
            new TextBoxConfig(
                SimpleYamlReader.Int(textBox, "shrink_x_min"),
                SimpleYamlReader.Int(textBox, "shrink_x_max"),
                SimpleYamlReader.Int(textBox, "shrink_x_divisor"),
                SimpleYamlReader.Int(textBox, "shrink_y_min"),
                SimpleYamlReader.Int(textBox, "shrink_y_max"),
                SimpleYamlReader.Int(textBox, "shrink_y_divisor")),
            new TextSeedFallbackConfig(
                SimpleYamlReader.Bool(textSeedFallback, "enabled", false),
                SimpleYamlReader.Double(textSeedFallback, "min_seed_x_offset_ratio"),
                SimpleYamlReader.Int(textSeedFallback, "row_min_pixels"),
                SimpleYamlReader.Int(textSeedFallback, "min_exact_pixels"),
                SimpleYamlReader.Int(textSeedFallback, "min_text_height"),
                SimpleYamlReader.Int(textSeedFallback, "short_text_width_threshold"),
                SimpleYamlReader.Double(textSeedFallback, "short_text_max_fill_ratio"),
                SimpleYamlReader.Int(textSeedFallback, "max_row_gap"),
                SimpleYamlReader.Int(textSeedFallback, "padding_left"),
                SimpleYamlReader.Int(textSeedFallback, "padding_right"),
                SimpleYamlReader.Int(textSeedFallback, "padding_top"),
                SimpleYamlReader.Int(textSeedFallback, "padding_bottom"),
                SimpleYamlReader.Int(textSeedFallback, "min_width"),
                SimpleYamlReader.Int(textSeedFallback, "min_height")),
            new ComponentRefineConfig(
                SimpleYamlReader.Int(componentRefine, "scale_padding"),
                SimpleYamlReader.Double(componentRefine, "already_dense_fill_ratio"),
                SimpleYamlReader.Int(componentRefine, "min_max_row_count"),
                SimpleYamlReader.Int(componentRefine, "dense_row_threshold_min"),
                SimpleYamlReader.Double(componentRefine, "dense_row_threshold_ratio"),
                SimpleYamlReader.Int(componentRefine, "row_top_padding"),
                SimpleYamlReader.Int(componentRefine, "row_bottom_padding"),
                SimpleYamlReader.Int(componentRefine, "min_max_col_count"),
                SimpleYamlReader.Int(componentRefine, "dense_col_threshold_min"),
                SimpleYamlReader.Double(componentRefine, "dense_col_threshold_ratio"),
                SimpleYamlReader.Int(componentRefine, "col_left_padding"),
                SimpleYamlReader.Int(componentRefine, "col_right_padding")),
            new TextColorGuardConfig(
                SimpleYamlReader.Bool(textGuard, "enabled", true),
                SimpleYamlReader.Int(textGuard, "padding"),
                SimpleYamlReader.Bool(textGuard, "seed_expansion_enabled", false),
                SimpleYamlReader.Int(textGuard, "seed_expansion_radius"),
                SimpleYamlReader.Int(textGuard, "seed_expansion_min_base_pixels"),
                SimpleYamlReader.IntArray(textGuardColors, "self_light"),
                SimpleYamlReader.IntArray(textGuardColors, "other_dark")),
            new TextValidationConfig(
                new SelfLightTextValidationConfig(
                    SimpleYamlReader.Int(selfLightValidation, "red_max"),
                    SimpleYamlReader.Int(selfLightValidation, "green_max"),
                    SimpleYamlReader.Int(selfLightValidation, "blue_max"),
                    SimpleYamlReader.Int(selfLightValidation, "max_channel_delta"),
                    SimpleYamlReader.Int(selfLightValidation, "min_pixels"),
                    SimpleYamlReader.Int(selfLightValidation, "area_divisor"),
                    SimpleYamlReader.Int(selfLightValidation, "exact_min_pixels")),
                new OtherDarkTextValidationConfig(
                    SimpleYamlReader.Int(otherDarkValidation, "red_min"),
                    SimpleYamlReader.Int(otherDarkValidation, "green_min"),
                    SimpleYamlReader.Int(otherDarkValidation, "blue_min"),
                    SimpleYamlReader.Int(otherDarkValidation, "max_channel_delta"),
                    SimpleYamlReader.Int(otherDarkValidation, "min_pixels"),
                    SimpleYamlReader.Int(otherDarkValidation, "area_divisor"),
                    SimpleYamlReader.Int(otherDarkValidation, "exact_min_pixels"))));
    }
}

public sealed record ProcessingConfig(int Scale);

public sealed record MessageRoiConfig(double LeftRatio, double TopRatio, double RightRatio, double BottomRatio);

public sealed record MaskConfig(SelfLightMaskConfig SelfLight, OtherDarkMaskConfig OtherDark);

public sealed record SelfLightMaskConfig(int RedMin, int GreenMin, int BlueMin, int RedBlueDeltaMin);

public sealed record OtherDarkMaskConfig(int[] TargetRgb, int[] ToleranceRgb);

public sealed record CandidateFilterConfig(
    int MinWidth,
    int MinHeight,
    int MaxWidth,
    int MaxHeight,
    double MinAspectRatio,
    double MinBubbleFillRatio,
    int SelfLightMinColorArea,
    int OtherDarkMinColorArea,
    double EdgeMarginRatio,
    double SelfRightAnchorRatio,
    double OtherLeftAnchorMinRatio,
    double OtherLeftAnchorMaxRatio,
    double DuplicateOverlapRatio);

public sealed record TextBoxConfig(
    int ShrinkXMin,
    int ShrinkXMax,
    int ShrinkXDivisor,
    int ShrinkYMin,
    int ShrinkYMax,
    int ShrinkYDivisor);

public sealed record TextSeedFallbackConfig(
    bool Enabled,
    double MinSeedXOffsetRatio,
    int RowMinPixels,
    int MinExactPixels,
    int MinTextHeight,
    int ShortTextWidthThreshold,
    double ShortTextMaxFillRatio,
    int MaxRowGap,
    int PaddingLeft,
    int PaddingRight,
    int PaddingTop,
    int PaddingBottom,
    int MinWidth,
    int MinHeight);

public sealed record ComponentRefineConfig(
    int ScalePadding,
    double AlreadyDenseFillRatio,
    int MinMaxRowCount,
    int DenseRowThresholdMin,
    double DenseRowThresholdRatio,
    int RowTopPadding,
    int RowBottomPadding,
    int MinMaxColCount,
    int DenseColThresholdMin,
    double DenseColThresholdRatio,
    int ColLeftPadding,
    int ColRightPadding);

public sealed record TextColorGuardConfig(
    bool Enabled,
    int Padding,
    bool SeedExpansionEnabled,
    int SeedExpansionRadius,
    int SeedExpansionMinBasePixels,
    int[] SelfLight,
    int[] OtherDark);

public sealed record TextValidationConfig(SelfLightTextValidationConfig SelfLight, OtherDarkTextValidationConfig OtherDark);

public sealed record SelfLightTextValidationConfig(
    int RedMax,
    int GreenMax,
    int BlueMax,
    int MaxChannelDelta,
    int MinPixels,
    int AreaDivisor,
    int ExactMinPixels);

public sealed record OtherDarkTextValidationConfig(
    int RedMin,
    int GreenMin,
    int BlueMin,
    int MaxChannelDelta,
    int MinPixels,
    int AreaDivisor,
    int ExactMinPixels);
