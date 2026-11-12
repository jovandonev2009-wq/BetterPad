namespace BetterPad.Theme;

/// <summary>
/// The built-in color sets. Light and Sepia keep the syntax palettes from the
/// local rule files; the dark themes supply their own so tokens stay readable.
/// </summary>
public static class ThemeCatalog
{
    public static ThemePalette Light { get; } = new("light", "Light", new Dictionary<string, string>
    {
        ["BgBrush"] = "#F7F8FA",
        ["SurfaceBrush"] = "#FFFFFF",
        ["SurfaceAltBrush"] = "#F3F5F7",
        ["BorderBrush"] = "#E3E6EA",
        ["TextBrush"] = "#1F2933",
        ["MutedBrush"] = "#6B7280",
        ["SecondaryTextBrush"] = "#4B5563",
        ["AccentBrush"] = "#2563EB",
        ["AccentHoverBrush"] = "#1D4ED8",
        ["AccentPressedBrush"] = "#1E40AF",
        ["AccentSoftBrush"] = "#E8F0FE",
        ["DangerBrush"] = "#DC2626",
        ["HoverBrush"] = "#EDF0F4",
        ["PressedBrush"] = "#E2E7ED",
        ["TabStripBrush"] = "#F1F3F6",
        ["TabHoverBrush"] = "#E6E9EE",
        ["InputBgBrush"] = "#FFFFFF",
        ["InputBorderBrush"] = "#D7DCE3",
        ["CheckBorderBrush"] = "#C3C8D0",
        ["CheckBorderHoverBrush"] = "#94A3B8",
        ["ScrollThumbBrush"] = "#CDD3DB",
        ["ScrollThumbHoverBrush"] = "#AAB3BF",
        ["ScrollThumbDragBrush"] = "#94A3B8",
        ["ToolTipBgBrush"] = "#111827",
        ["ToolTipTextBrush"] = "#FFFFFF",
        ["EditorBgBrush"] = "#FFFFFF",
        ["GutterBgBrush"] = "#FAFBFC",
        ["GutterBorderBrush"] = "#ECEEF1",
        ["EditorTextBrush"] = "#1F2937",
        ["EditorCaretBrush"] = "#111827",
        ["SelectionBrush"] = "#3B82F6",
        ["EditorCurrentLineBrush"] = "#F2F6FD",
        ["GutterNumberBrush"] = "#9AA3AF",
        ["GutterNumberActiveBrush"] = "#2563EB"
    });

    public static ThemePalette BlackRed { get; } = new("black-red", "Black & Red", new Dictionary<string, string>
    {
        ["BgBrush"] = "#0B0B0C",
        ["SurfaceBrush"] = "#141416",
        ["SurfaceAltBrush"] = "#1A1A1D",
        ["BorderBrush"] = "#2A2A2E",
        ["TextBrush"] = "#ECECEE",
        ["MutedBrush"] = "#9A9AA0",
        ["SecondaryTextBrush"] = "#C7C7CC",
        ["AccentBrush"] = "#E11D2E",
        ["AccentHoverBrush"] = "#F43F4F",
        ["AccentPressedBrush"] = "#B31424",
        ["AccentSoftBrush"] = "#2B1216",
        ["DangerBrush"] = "#F87171",
        ["HoverBrush"] = "#242427",
        ["PressedBrush"] = "#2D2D31",
        ["TabStripBrush"] = "#101012",
        ["TabHoverBrush"] = "#232326",
        ["InputBgBrush"] = "#19191C",
        ["InputBorderBrush"] = "#333338",
        ["CheckBorderBrush"] = "#3A3A40",
        ["CheckBorderHoverBrush"] = "#7A2A31",
        ["ScrollThumbBrush"] = "#3A3A40",
        ["ScrollThumbHoverBrush"] = "#4A4A52",
        ["ScrollThumbDragBrush"] = "#5A5A64",
        ["ToolTipBgBrush"] = "#26262A",
        ["ToolTipTextBrush"] = "#F5F5F7",
        ["EditorBgBrush"] = "#0E0E10",
        ["GutterBgBrush"] = "#121214",
        ["GutterBorderBrush"] = "#26262A",
        ["EditorTextBrush"] = "#E8E8EA",
        ["EditorCaretBrush"] = "#FF5A66",
        ["SelectionBrush"] = "#E11D2E",
        ["EditorCurrentLineBrush"] = "#18181B",
        ["GutterNumberBrush"] = "#5C5C64",
        ["GutterNumberActiveBrush"] = "#FF5A66",
        ["SyntaxKeywordBrush"] = "#FF4D5A",
        ["SyntaxTypeBrush"] = "#FF8E8E",
        ["SyntaxStringBrush"] = "#FFB38A",
        ["SyntaxCommentBrush"] = "#8A666B",
        ["SyntaxNumberBrush"] = "#FF7A7A",
        ["SyntaxPreprocessorBrush"] = "#A07A80"
    }, overrideSyntaxColors: true);

    public static ThemePalette Midnight { get; } = new("midnight", "Midnight", new Dictionary<string, string>
    {
        ["BgBrush"] = "#0B1220",
        ["SurfaceBrush"] = "#101A2C",
        ["SurfaceAltBrush"] = "#0E1728",
        ["BorderBrush"] = "#22304A",
        ["TextBrush"] = "#E2E8F0",
        ["MutedBrush"] = "#8FA0B8",
        ["SecondaryTextBrush"] = "#C3CEDF",
        ["AccentBrush"] = "#38BDF8",
        ["AccentHoverBrush"] = "#0EA5E9",
        ["AccentPressedBrush"] = "#0284C7",
        ["AccentSoftBrush"] = "#12283C",
        ["DangerBrush"] = "#F87171",
        ["HoverBrush"] = "#172235",
        ["PressedBrush"] = "#1E2C44",
        ["TabStripBrush"] = "#0C1424",
        ["TabHoverBrush"] = "#18243A",
        ["InputBgBrush"] = "#0F192B",
        ["InputBorderBrush"] = "#263750",
        ["CheckBorderBrush"] = "#2C3D58",
        ["CheckBorderHoverBrush"] = "#3B82F6",
        ["ScrollThumbBrush"] = "#2A3B55",
        ["ScrollThumbHoverBrush"] = "#374B69",
        ["ScrollThumbDragBrush"] = "#465D7F",
        ["ToolTipBgBrush"] = "#1B293F",
        ["ToolTipTextBrush"] = "#EAF2FB",
        ["EditorBgBrush"] = "#0A1120",
        ["GutterBgBrush"] = "#0D1626",
        ["GutterBorderBrush"] = "#1D2A42",
        ["EditorTextBrush"] = "#D7E2F0",
        ["EditorCaretBrush"] = "#7DD3FC",
        ["SelectionBrush"] = "#38BDF8",
        ["EditorCurrentLineBrush"] = "#111C30",
        ["GutterNumberBrush"] = "#52698A",
        ["GutterNumberActiveBrush"] = "#7DD3FC",
        ["SyntaxKeywordBrush"] = "#7DD3FC",
        ["SyntaxTypeBrush"] = "#67E8F9",
        ["SyntaxStringBrush"] = "#9AE6B4",
        ["SyntaxCommentBrush"] = "#64748B",
        ["SyntaxNumberBrush"] = "#F0B775",
        ["SyntaxPreprocessorBrush"] = "#8FA0B8"
    }, overrideSyntaxColors: true);

    public static ThemePalette Sepia { get; } = new("sepia", "Sepia", new Dictionary<string, string>
    {
        ["BgBrush"] = "#F2ECE0",
        ["SurfaceBrush"] = "#FAF6EE",
        ["SurfaceAltBrush"] = "#F3EDE0",
        ["BorderBrush"] = "#DDD3C0",
        ["TextBrush"] = "#3B3226",
        ["MutedBrush"] = "#8A7A63",
        ["SecondaryTextBrush"] = "#5F5242",
        ["AccentBrush"] = "#B45309",
        ["AccentHoverBrush"] = "#92400E",
        ["AccentPressedBrush"] = "#78350F",
        ["AccentSoftBrush"] = "#F3E3CC",
        ["DangerBrush"] = "#B91C1C",
        ["HoverBrush"] = "#EDE5D6",
        ["PressedBrush"] = "#E4D9C6",
        ["TabStripBrush"] = "#EDE6D8",
        ["TabHoverBrush"] = "#E4DAC7",
        ["InputBgBrush"] = "#FDFAF4",
        ["InputBorderBrush"] = "#CFC3AD",
        ["CheckBorderBrush"] = "#C4B79F",
        ["CheckBorderHoverBrush"] = "#9C8B70",
        ["ScrollThumbBrush"] = "#CFC3AD",
        ["ScrollThumbHoverBrush"] = "#B8A98D",
        ["ScrollThumbDragBrush"] = "#A0937A",
        ["ToolTipBgBrush"] = "#3B3226",
        ["ToolTipTextBrush"] = "#FBF7EF",
        ["EditorBgBrush"] = "#FBF7EF",
        ["GutterBgBrush"] = "#F5EFE3",
        ["GutterBorderBrush"] = "#E3D9C7",
        ["EditorTextBrush"] = "#3B3226",
        ["EditorCaretBrush"] = "#2E271D",
        ["SelectionBrush"] = "#C58A4A",
        ["EditorCurrentLineBrush"] = "#F2EADB",
        ["GutterNumberBrush"] = "#A3957E",
        ["GutterNumberActiveBrush"] = "#A8541E"
    });

    public static IReadOnlyList<ThemePalette> All { get; } = [Light, BlackRed, Midnight, Sepia];

    public static ThemePalette Find(string? id)
    {
        foreach (ThemePalette theme in All)
        {
            if (string.Equals(theme.Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return theme;
            }
        }

        return Light;
    }
}
