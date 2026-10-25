using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace IntervalTimer;

internal static class TimerTheme
{
    public static readonly Color Background = Color.FromArgb(14, 18, 24);
    public static readonly Color Card = Color.FromArgb(23, 29, 38);
    public static readonly Color Elevated = Color.FromArgb(31, 39, 50);
    public static readonly Color Border = Color.FromArgb(47, 59, 73);
    public static readonly Color Foreground = Color.FromArgb(237, 244, 249);
    public static readonly Color Muted = Color.FromArgb(139, 156, 174);
    public static readonly Color StopIcon = Color.FromArgb(170, 184, 197);
    public static readonly Color TimeInputHint = Color.FromArgb(38, 53, 66);
    public static readonly Color Accent = Color.FromArgb(112, 224, 197);
    public static readonly Color AccentDark = Color.FromArgb(17, 43, 38);
    public static readonly Color Warm = Color.FromArgb(246, 192, 109);

    public static Button Button(string text, bool primary = false)
    {
        var button = new TimerButton {
            Text = text, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Elevated,
            ForeColor = primary ? AccentDark : Foreground, Primary = primary,
            Padding = new Padding(14, 9, 14, 9), Font = TimerTypography.Create(9.5F, FontStyle.Bold),
            UseVisualStyleBackColor = false, Cursor = Cursors.Hand, Margin = new Padding(4)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    public static Label Caption(string text) => new() {
        Text = text, AutoSize = true, ForeColor = Muted,
        Anchor = AnchorStyles.Left, Margin = new Padding(0, 3, 8, 3)
    };

    public static void SetIcon(Button button, TimerGlyph icon)
    {
        if (button is TimerButton themed && themed.Glyph != icon) { themed.Glyph = icon; themed.Invalidate(); }
    }

    public static void StyleTextBox(TextBoxBase input)
    {
        input.BackColor = Elevated;
        input.ForeColor = Foreground;
        input.BorderStyle = BorderStyle.FixedSingle;
    }

    public static void StyleNumber(NumericUpDown input)
    {
        input.BackColor = Elevated;
        input.ForeColor = Foreground;
        input.BorderStyle = BorderStyle.None;
        foreach (Control child in input.Controls)
        {
            if (child is TextBoxBase text) { StyleTextBox(text); text.BorderStyle = BorderStyle.None; }
            else child.Paint += (_, e) => {
                e.Graphics.Clear(Elevated);
                var middle = child.Height / 2f;
                using var pen = new Pen(input.Enabled ? Muted : Border, Math.Max(1, input.DeviceDpi / 96f)) {
                    StartCap = LineCap.Round, EndCap = LineCap.Round
                };
                var x = child.Width / 2f;
                var d = Math.Max(2, child.Width / 5f);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawLines(pen, [new PointF(x - d, middle / 2 + d / 2), new(x, middle / 2 - d / 2), new(x + d, middle / 2 + d / 2)]);
                e.Graphics.DrawLines(pen, [new PointF(x - d, middle * 1.5f - d / 2), new(x, middle * 1.5f + d / 2), new(x + d, middle * 1.5f - d / 2)]);
            };
        }
    }

    public static GraphicsPath Rounded(RectangleF rectangle, float radius)
    {
        var path = new GraphicsPath();
        radius = Math.Max(.1f, Math.Min(radius, Math.Min(rectangle.Width, rectangle.Height) / 2));
        var diameter = radius * 2;
        path.AddArc(rectangle.Left, rectangle.Top, diameter, diameter, 180, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Top, diameter, diameter, 270, 90);
        path.AddArc(rectangle.Right - diameter, rectangle.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(rectangle.Left, rectangle.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static Color Blend(Color a, Color b, float amount) => Color.FromArgb(
        (int)(a.R + (b.R - a.R) * amount), (int)(a.G + (b.G - a.G) * amount), (int)(a.B + (b.B - a.B) * amount));

    public static void ApplyWindow(Form form)
    {
        void Apply()
        {
            try
            {
                var dark = 1;
                DwmSetWindowAttribute(form.Handle, 20, ref dark, 4);
                var caption = ColorTranslator.ToWin32(Background);
                DwmSetWindowAttribute(form.Handle, 35, ref caption, 4);
                var text = ColorTranslator.ToWin32(Foreground);
                DwmSetWindowAttribute(form.Handle, 36, ref text, 4);
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
        form.HandleCreated += (_, _) => Apply();
        if (form.IsHandleCreated) Apply();
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
}

internal enum TimerGlyph { None, Add, Settings, Play, Pause, Stop, Restart, Bell, Edit, Folder, Keyboard, Check, Close, Delete }

internal sealed class TimerButton : Button
{
    private bool hovered;
    private bool pressed;
    public bool Primary { get; set; }
    public bool Quiet { get; set; }
    public bool IconOnly { get; set; }
    public TimerGlyph Glyph { get; set; }

    public TimerButton() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
        | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

    public override Size GetPreferredSize(Size proposedSize)
    {
        var scale = DeviceDpi / 96f;
        var text = IconOnly ? Size.Empty : TextRenderer.MeasureText(Text, Font, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var icon = Glyph == TimerGlyph.None ? 0 : (int)Math.Round((IconOnly ? 18 : 25) * scale);
        return new Size(Math.Max(MinimumSize.Width, text.Width + icon + Padding.Horizontal + (int)(4 * scale)),
            Math.Max(MinimumSize.Height, Math.Max(text.Height, (int)(18 * scale)) + Padding.Vertical + (int)(4 * scale)));
    }

    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); pressed = false; Invalidate(); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) { pressed = true; Invalidate(); } base.OnKeyDown(e); }
    protected override void OnKeyUp(KeyEventArgs e) { pressed = false; Invalidate(); base.OnKeyUp(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Width < 2 || Height < 2) return;
        var scale = DeviceDpi / 96f;
        var graphics = e.Graphics;
        graphics.Clear(Parent?.BackColor is { A: 255 } parent ? parent : TimerTheme.Card);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var background = Quiet ? TimerTheme.Card : BackColor;
        if (hovered) background = TimerTheme.Blend(background, Color.White, Primary ? .09f : .08f);
        if (pressed) background = TimerTheme.Blend(background, Color.Black, .12f);
        if (!Enabled) background = TimerTheme.Blend(background, TimerTheme.Card, .65f);
        using var shape = TimerTheme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 10 * scale);
        using var fill = new SolidBrush(background);
        graphics.FillPath(fill, shape);
        if (!Primary && !Quiet)
        {
            using var border = new Pen(TimerTheme.Border, Math.Max(1, scale));
            graphics.DrawPath(border, shape);
        }
        var color = Enabled ? ForeColor : TimerTheme.Muted;
        var text = IconOnly ? "" : Text;
        var textSize = TextRenderer.MeasureText(graphics, text, Font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
        var iconWidth = Glyph == TimerGlyph.None ? 0 : 18 * scale;
        var gap = iconWidth > 0 && text.Length > 0 ? 7 * scale : 0;
        var total = textSize.Width + iconWidth + gap;
        var x = Math.Max(Padding.Left, (Width - total) / 2f);
        if (IconOnly) x = (Width - iconWidth) / 2;
        if (Glyph != TimerGlyph.None)
        {
            var iconColor = Glyph == TimerGlyph.Stop && Enabled && !Primary ? TimerTheme.StopIcon : color;
            TimerIcons.Draw(graphics, Glyph, new RectangleF(x, (Height - iconWidth) / 2, iconWidth, iconWidth), iconColor);
        }
        var bounds = new Rectangle((int)(x + iconWidth + gap), 0,
            Math.Max(1, Width - (int)(x + iconWidth + gap) - Padding.Right), Height);
        if (text.Length > 0) TextRenderer.DrawText(graphics, text, Font, bounds, color,
            TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused && ShowFocusCues)
        {
            using var focus = TimerTheme.Rounded(new RectangleF(3 * scale, 3 * scale,
                Width - 6 * scale, Height - 6 * scale), 8 * scale);
            using var pen = new Pen(Primary ? TimerTheme.AccentDark : TimerTheme.Accent, scale);
            graphics.DrawPath(pen, focus);
        }
    }
}

internal static class TimerIcons
{
    public static void Draw(Graphics graphics, TimerGlyph glyph, RectangleF bounds, Color color)
    {
        var saved = graphics.Save();
        graphics.TranslateTransform(bounds.X, bounds.Y);
        graphics.ScaleTransform(bounds.Width / 24, bounds.Height / 24);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(color, 1.7f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        using var brush = new SolidBrush(color);
        switch (glyph)
        {
            case TimerGlyph.Add: graphics.DrawLine(pen, 12, 5, 12, 19); graphics.DrawLine(pen, 5, 12, 19, 12); break;
            case TimerGlyph.Play: graphics.FillPolygon(brush, [new PointF(8, 4.5f), new(20, 12), new(8, 19.5f)]); break;
            case TimerGlyph.Pause: graphics.FillRectangle(brush, 6, 5, 4, 14); graphics.FillRectangle(brush, 14, 5, 4, 14); break;
            case TimerGlyph.Stop: graphics.FillRectangle(brush, 5, 5, 14, 14); break;
            case TimerGlyph.Restart: graphics.DrawArc(pen, 5, 5, 14, 14, -75, 300); graphics.DrawLines(pen, [new PointF(15, 2), new(15, 7), new(20, 7)]); break;
            case TimerGlyph.Edit:
                // Open square and pencil, matching the supplied reference.
                // The combined outline spans x=2..22 and y=2..22.
                using (var square = new GraphicsPath())
                {
                    square.AddLine(13, 4, 4, 4);
                    square.AddBezier(4, 4, 2.9f, 4, 2, 4.9f, 2, 6);
                    square.AddLine(2, 6, 2, 20);
                    square.AddBezier(2, 20, 2, 21.1f, 2.9f, 22, 4, 22);
                    square.AddLine(4, 22, 18, 22);
                    square.AddBezier(18, 22, 19.1f, 22, 20, 21.1f, 20, 20);
                    square.AddLine(20, 20, 20, 12);
                    graphics.DrawPath(pen, square);
                }
                graphics.DrawPolygon(pen, [new PointF(8, 17), new(9.5f, 12),
                    new(19.5f, 2), new(22, 4.5f), new(12, 14.5f)]);
                graphics.DrawLine(pen, 9.5f, 12, 12, 14.5f);
                graphics.DrawLine(pen, 17.6f, 3.9f, 20.1f, 6.4f);
                break;
            case TimerGlyph.Bell: graphics.DrawBezier(pen, 6, 16, 7, 13, 4, 5, 12, 5); graphics.DrawBezier(pen, 12, 5, 20, 5, 17, 13, 18, 16); graphics.DrawLine(pen, 5, 17, 19, 17); graphics.DrawArc(pen, 9, 17, 6, 5, 0, 180); graphics.DrawLine(pen, 12, 3, 12, 5); break;
            case TimerGlyph.Folder: graphics.DrawLines(pen, [new PointF(3, 18), new(3, 6), new(10, 6), new(12, 9), new(21, 9), new(19, 19), new(3, 19), new(6, 11), new(20, 11)]); break;
            case TimerGlyph.Check: graphics.DrawLines(pen, [new PointF(5, 12), new(10, 17), new(20, 6)]); break;
            case TimerGlyph.Close: graphics.DrawLine(pen, 6, 6, 18, 18); graphics.DrawLine(pen, 6, 18, 18, 6); break;
            case TimerGlyph.Delete:
                graphics.DrawLine(pen, 5, 6, 19, 6);
                graphics.DrawLines(pen, [new PointF(9, 6), new(9, 3), new(15, 3), new(15, 6)]);
                graphics.DrawLines(pen, [new PointF(7, 6), new(8, 20), new(16, 20), new(17, 6)]);
                graphics.DrawLine(pen, 10, 10, 10, 16); graphics.DrawLine(pen, 14, 10, 14, 16); break;
            case TimerGlyph.Keyboard: graphics.DrawRectangle(pen, 2, 5, 20, 14); for (var row = 0; row < 2; row++) for (var col = 0; col < 4; col++) graphics.FillRectangle(brush, 5 + col * 4, 8 + row * 4, 1.5f, 1.5f); graphics.DrawLine(pen, 8, 16, 16, 16); break;
            case TimerGlyph.Settings:
                graphics.DrawEllipse(pen, 8, 8, 8, 8);
                for (var i = 0; i < 8; i++) { var a = i * Math.PI / 4; graphics.DrawLine(pen, 12 + (float)Math.Cos(a) * 8, 12 + (float)Math.Sin(a) * 8, 12 + (float)Math.Cos(a) * 10, 12 + (float)Math.Sin(a) * 10); }
                graphics.DrawEllipse(pen, 4.5f, 4.5f, 15, 15); break;
        }
        graphics.Restore(saved);
    }
}

// A keycap is a full-area action, rather than a link restricted to text pixels.
internal sealed class KeycapButton : Label
{
    public KeycapButton()
    {
        AutoSize = true;
        BackColor = TimerTheme.Elevated;
        ForeColor = TimerTheme.Muted;
        AccessibleRole = AccessibleRole.PushButton;
        Padding = new Padding(9, 5, 9, 5);
        Font = TimerTypography.Create(9F);
        Cursor = Cursors.Hand;
        SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor is { A: 255 } parent ? parent : TimerTheme.Card);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = TimerTheme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1), 6 * DeviceDpi / 96f);
        using var brush = new SolidBrush(BackColor);
        using var pen = new Pen(TimerTheme.Border);
        e.Graphics.FillPath(brush, shape);
        e.Graphics.DrawPath(pen, shape);
    }
}
