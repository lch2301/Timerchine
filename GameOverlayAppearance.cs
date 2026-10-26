namespace IntervalTimer;

internal enum OverlayNameAlignment { Left, Center, Right }

// Positions describe travel within the visible game area, including the edge
// margin. The same settings work across monitors, resolutions and DPI changes.
internal sealed record GameOverlayAppearance
{
    public int HorizontalPosition { get; set; } = 100;
    public int VerticalPosition { get; set; } = 30;
    public int Transparency { get; set; } = 50;
    public float TimeFontSize { get; set; } = 15f;
    public float NameFontSize { get; set; } = 13f;
    // Retained for settings written before the continuous alignment control.
    public OverlayNameAlignment NameAlignment { get; set; } = OverlayNameAlignment.Right;
    public int? NameAlignmentPercent { get; set; }

    public GameOverlayAppearance Normalized() => this with {
        HorizontalPosition = Math.Clamp(HorizontalPosition, 0, 100),
        VerticalPosition = Math.Clamp(VerticalPosition, 0, 100),
        Transparency = Math.Clamp(Transparency, 0, 90),
        TimeFontSize = float.IsFinite(TimeFontSize) ? Math.Clamp(TimeFontSize, 12f, 96f) : 15f,
        NameFontSize = float.IsFinite(NameFontSize) ? Math.Clamp(NameFontSize, 8f, 48f) : 13f,
        NameAlignment = Enum.IsDefined(NameAlignment) ? NameAlignment : OverlayNameAlignment.Right,
        NameAlignmentPercent = Math.Clamp(NameAlignmentPercent ?? (NameAlignment switch {
            OverlayNameAlignment.Left => 0,
            OverlayNameAlignment.Center => 50,
            _ => 100
        }), 0, 100)
    };
}
