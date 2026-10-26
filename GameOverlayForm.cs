using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace IntervalTimer;

internal sealed record OverlayTimerRow(string Name, int RemainingSeconds);

/// <summary>Only timer names and times; alpha pixels, no panel, no focus or mouse input.</summary>
internal sealed class GameOverlayForm : Form
{
    private string signature = "";
    private Rectangle lastBounds;
    private GameOverlayAppearance? lastAppearance;
    public GameOverlayForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        Text = "타이머신 게임 오버레이";
    }
    protected override bool ShowWithoutActivation => true;
    protected override void OnPaintBackground(PaintEventArgs e) { }
    protected override void OnPaint(PaintEventArgs e) { }
    protected override CreateParams CreateParams {
        get { var value = base.CreateParams; value.ExStyle |= 0x00080000 | 0x00000020 | 0x00000080 | 0x08000000; return value; }
    }
    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0084) { message.Result = (nint)(-1); return; } // HTTRANSPARENT
        if (message.Msg == 0x0021) { message.Result = (nint)3; return; } // MA_NOACTIVATE
        base.WndProc(ref message);
    }

    public void Present(Rectangle gameBounds, int dpi, IReadOnlyList<OverlayTimerRow> rows,
        GameOverlayAppearance? appearance = null)
    {
        if (rows.Count == 0 || gameBounds.Width < 160 || gameBounds.Height < 100) { Hide(); return; }
        appearance = (appearance ?? new()).Normalized();
        var nextSignature = dpi + ":" + string.Join("\u001e", rows.Select(row => row.Name + "\u001f" + row.RemainingSeconds));
        if (signature != nextSignature || lastBounds != gameBounds || lastAppearance != appearance || !Visible)
        {
            using var bitmap = Render(gameBounds.Size, dpi, rows, appearance);
            var destination = LocationFor(gameBounds, bitmap.Size, dpi, appearance);
            SetBitmap(bitmap, destination);
            signature = nextSignature; lastBounds = gameBounds; lastAppearance = appearance;
        }
        if (!Visible) Show();
    }

    internal static Point LocationFor(Rectangle gameBounds, Size overlaySize, int dpi, GameOverlayAppearance appearance)
    {
        appearance = appearance.Normalized();
        var margin = Math.Max(4, (int)Math.Round(18 * dpi / 96d));
        var horizontalInset = Math.Min(margin, Math.Max(0, (gameBounds.Width - overlaySize.Width) / 2));
        var verticalInset = Math.Min(margin, Math.Max(0, (gameBounds.Height - overlaySize.Height) / 2));
        var travelX = Math.Max(0, gameBounds.Width - overlaySize.Width - horizontalInset * 2);
        var travelY = Math.Max(0, gameBounds.Height - overlaySize.Height - verticalInset * 2);
        return new Point(gameBounds.Left + horizontalInset + (int)Math.Round(travelX * appearance.HorizontalPosition / 100d),
            gameBounds.Top + verticalInset + (int)Math.Round(travelY * appearance.VerticalPosition / 100d));
    }

    internal static Bitmap Render(Size gameSize, int dpi, IReadOnlyList<OverlayTimerRow> rows,
        GameOverlayAppearance? appearance = null)
    {
        appearance = (appearance ?? new()).Normalized();
        using var measureBitmap = new Bitmap(1, 1);
        measureBitmap.SetResolution(96, 96);
        using var measure = Graphics.FromImage(measureBitmap);
        using var designNameFont = TimerTypography.Create(appearance.NameFontSize, FontStyle.Bold);
        using var designTimeFont = TimerTypography.CreateTime(appearance.TimeFontSize);
        // Keep both independent font sizes legible, then shrink the whole group
        // only if its content cannot fit inside this game's visible area.
        var nameHeight = Math.Max(1, appearance.NameFontSize * (4f / 3) * 1.35f + 4);
        var timeHeight = Math.Max(1, appearance.TimeFontSize * (4f / 3) * 1.2f + .4f);
        var designRowHeight = nameHeight + timeHeight + 7;
        var timeWidth = measure.MeasureString("99:59:59", designTimeFont).Width;
        var nameWidth = rows.Count == 0 ? 0 : rows.Max(row => measure.MeasureString(row.Name, designNameFont).Width);
        var margin = Math.Max(4, (int)Math.Round(18 * dpi / 96d));
        var availableWidth = Math.Max(1, gameSize.Width - margin * 2);
        var availableHeight = Math.Max(1, gameSize.Height - margin * 2);
        var designWidth = Math.Max(timeWidth, Math.Min(nameWidth, 400))
            + Math.Max(0, Math.Min(nameWidth, 400) - timeWidth) + 8;
        var scale = Math.Min(Math.Clamp(dpi / 96f, 1, 4),
            Math.Min(availableHeight / Math.Max(1, rows.Count * designRowHeight), availableWidth / designWidth));
        var names = new List<Bitmap>();
        var times = new List<Bitmap>();
        try
        {
            // Measure the rendered ink, not DrawString's padded layout rectangles.
            // Reserve every alignment's extent so moving the name never moves the time.
            var padding = 1; var shadowOffset = 1; var timeRight = 1; var width = 1;
            for (var attempt = 0; attempt < 4; attempt++)
            {
                foreach (var mask in names.Concat(times)) mask.Dispose();
                names.Clear(); times.Clear();
                using var nameFont = TimerTypography.Create(Math.Max(1, appearance.NameFontSize * scale), FontStyle.Bold);
                using var timeFont = TimerTypography.CreateTime(Math.Max(1, appearance.TimeFontSize * scale));
                foreach (var row in rows)
                {
                    names.Add(RenderTextInk(row.Name, nameFont, Math.Max(1, (int)Math.Ceiling(400 * scale))));
                    times.Add(RenderTextInk(TimerSession.Format(row.RemainingSeconds), timeFont, availableWidth));
                }
                padding = Math.Max(1, (int)Math.Ceiling(scale));
                shadowOffset = Math.Max(1, (int)Math.Round(scale));
                var leftExtent = rows.Count == 0 ? 1 : Enumerable.Range(0, rows.Count).Max(i => Math.Max(names[i].Width, times[i].Width));
                var rightExtent = rows.Count == 0 ? 0 : Enumerable.Range(0, rows.Count).Max(i => Math.Max(0, names[i].Width - times[i].Width));
                timeRight = padding + leftExtent;
                width = timeRight + rightExtent + padding + shadowOffset;
                if (width <= availableWidth || attempt == 3) break;
                scale *= availableWidth / (float)width * .98f;
            }
            var rowHeight = Math.Max(1, (int)Math.Floor(designRowHeight * scale));
            var bitmap = new Bitmap(Math.Max(1, Math.Min(availableWidth, width)),
                Math.Max(1, Math.Min(availableHeight, rowHeight * rows.Count)), PixelFormat.Format32bppPArgb);
            bitmap.SetResolution(96, 96);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.Clear(Color.Transparent);
            for (var i = 0; i < rows.Count; i++)
            {
                var timeLeft = timeRight - times[i].Width;
                var nameLeft = timeLeft + (int)Math.Round((times[i].Width - names[i].Width)
                    * appearance.NameAlignmentPercent!.Value / 100d);
                var nameTop = i * rowHeight + (int)Math.Round((nameHeight * scale - names[i].Height) / 2);
                var timeTop = i * rowHeight + (int)Math.Round((nameHeight - 2) * scale
                    + (timeHeight * scale - times[i].Height) / 2);
                Draw(names[i], nameLeft, nameTop, TimerTheme.Foreground, Alpha(186));
                Draw(times[i], timeLeft, timeTop, TimerTheme.Accent, Alpha(202));
            }
            return bitmap;

            void Draw(Bitmap mask, int x, int y, Color color, int alpha)
            {
                Tint(mask, x + shadowOffset, y + shadowOffset, Color.Black,
                    (int)Math.Round(145 * (100 - appearance.Transparency) / 80d));
                Tint(mask, x, y, color, alpha);
            }
            void Tint(Bitmap mask, int x, int y, Color color, int alpha)
            {
                using var attributes = new ImageAttributes();
                attributes.SetColorMatrix(new ColorMatrix {
                    Matrix00 = color.R / 255f, Matrix11 = color.G / 255f, Matrix22 = color.B / 255f,
                    Matrix33 = Math.Clamp(alpha, 0, 255) / 255f
                });
                graphics.DrawImage(mask, new Rectangle(x, y, mask.Width, mask.Height),
                    0, 0, mask.Width, mask.Height, GraphicsUnit.Pixel, attributes);
            }
        }
        finally
        {
            foreach (var mask in names.Concat(times)) mask.Dispose();
        }
        // Preserve the translucent text palette, with truly opaque text at 0%.
        int Alpha(int original) => (int)Math.Round(appearance.Transparency <= 20
            ? 255 - (255 - original) * appearance.Transparency / 20d
            : original * (100 - appearance.Transparency) / 80d);
    }

    private static Bitmap RenderTextInk(string text, Font font, int maximumWidth)
    {
        using var probe = new Bitmap(1, 1);
        probe.SetResolution(96, 96);
        using var measure = Graphics.FromImage(probe);
        using var format = (StringFormat)StringFormat.GenericTypographic.Clone();
        format.FormatFlags |= StringFormatFlags.NoWrap;
        format.Trimming = StringTrimming.EllipsisCharacter;
        var width = Math.Max(1, Math.Min(maximumWidth, (int)Math.Ceiling(measure.MeasureString(text, font, int.MaxValue, format).Width) + 2));
        var height = Math.Max(1, (int)Math.Ceiling(font.GetHeight(measure) * 1.6));
        const int inset = 8;
        using var mask = new Bitmap(width + inset * 2, height + inset * 2, PixelFormat.Format32bppPArgb);
        mask.SetResolution(96, 96);
        using (var graphics = Graphics.FromImage(mask))
        {
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.DrawString(text, font, Brushes.White, new RectangleF(inset, inset, width, height), format);
        }
        var data = mask.LockBits(new Rectangle(Point.Empty, mask.Size), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        var left = mask.Width; var right = -1; var top = mask.Height; var bottom = -1;
        try
        {
            var pixels = new byte[data.Stride * data.Height];
            Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
            for (var y = 0; y < mask.Height; y++)
                for (var x = 0; x < mask.Width; x++)
                {
                    if (pixels[y * data.Stride + x * 4 + 3] < 16) continue;
                    left = Math.Min(left, x); right = Math.Max(right, x);
                    top = Math.Min(top, y); bottom = Math.Max(bottom, y);
                }
        }
        finally { mask.UnlockBits(data); }
        return right < left ? new Bitmap(1, 1, PixelFormat.Format32bppPArgb)
            : mask.Clone(Rectangle.FromLTRB(left, top, right + 1, bottom + 1), PixelFormat.Format32bppPArgb);
    }

    private void SetBitmap(Bitmap bitmap, Point destination)
    {
        var screen = GetDC(0);
        var memory = CreateCompatibleDC(screen);
        nint image = 0, previous = 0;
        try
        {
            image = bitmap.GetHbitmap(Color.FromArgb(0));
            previous = SelectObject(memory, image);
            var size = bitmap.Size; var origin = Point.Empty;
            var blend = new Blend { Operation = 0, Alpha = 255, Format = 1 };
            _ = Handle;
            if (!UpdateLayeredWindow(Handle, screen, ref destination, ref size, memory, ref origin, 0, ref blend, 2))
                throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            SetWindowPos(Handle, (nint)(-1), 0, 0, 0, 0, 0x0013); // TOPMOST, no move/resize/activate
        }
        finally
        {
            if (previous != 0) SelectObject(memory, previous);
            if (image != 0) DeleteObject(image);
            if (memory != 0) DeleteDC(memory);
            if (screen != 0) ReleaseDC(0, screen);
        }
    }
    [StructLayout(LayoutKind.Sequential, Pack = 1)] private struct Blend { public byte Operation, Flags, Alpha, Format; }
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint image);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint image);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UpdateLayeredWindow(nint window, nint destinationDC,
        ref Point destination, ref Size size, nint sourceDC, ref Point source, uint key, ref Blend blend, uint flags);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
