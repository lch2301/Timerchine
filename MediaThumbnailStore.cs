using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Forms;

namespace IntervalTimer;

internal readonly record struct MediaPreview(string Title, string ThumbnailPath);

internal static class MediaThumbnailStore
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(25) };
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new(StringComparer.Ordinal);
    private static readonly SemaphoreSlim DecodeSlots = new(2);
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IntervalTimer", "Thumbnails");

    public static bool IsCachedImage(string path)
    {
        try
        {
            return string.Equals(Path.GetDirectoryName(Path.GetFullPath(path)), Folder, StringComparison.OrdinalIgnoreCase)
                && Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase)
                && Path.GetFileNameWithoutExtension(path) is { Length: 64 } name && name.All(Uri.IsHexDigit)
                && new FileInfo(path) is { Exists: true, Length: > 0 and < 4_000_000 };
        }
        catch { return false; }
    }

    public static async Task<MediaPreview> LoadAsync(string source, bool external, CancellationToken cancellation)
    {
        string title = "", imageUrl = "", key;
        if (external)
        {
            if (!ExternalLinkPolicy.IsEnabled || !ExternalLinkPolicy.UsageConfirmed || !OnlineAlarmSource.IsLink(source)) return new("", "");
            var details = await OnlineAlarmSource.GetDetailsAsync(source, cancellation).ConfigureAwait(false);
            title = details.Title;
            imageUrl = details.ThumbnailUrl;
            key = "link:" + source;
        }
        else
        {
            var file = new FileInfo(Path.GetFullPath(source));
            if (!file.Exists) return new("", "");
            source = file.FullName;
            key = $"file:{file.FullName}:{file.Length}:{file.LastWriteTimeUtc.Ticks}";
        }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        var gate = Locks.GetOrAdd(hash, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellation).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Folder);
            var target = Path.Combine(Folder, hash + ".png");
            if (IsCachedImage(target)) return new(title, target);
            if (external && !OnlineAlarmSource.IsLink(imageUrl)) return new(title, "");
            var input = Path.Combine(Folder, $".image-{Guid.NewGuid():N}.download");
            var output = Path.Combine(Folder, $".image-{Guid.NewGuid():N}.png");
            try
            {
                if (external)
                {
                    // Only the registered thumbnail is fetched; audio download behavior is unchanged.
                    using var response = await Client.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
                    response.EnsureSuccessStatusCode();
                    const int maximumImageBytes = 16 * 1024 * 1024;
                    if (response.Content.Headers.ContentLength > maximumImageBytes) return new(title, "");
                    await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
                    await using (var file = new FileStream(input, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                    {
                        var buffer = new byte[81920];
                        var total = 0;
                        int read;
                        while ((read = await stream.ReadAsync(buffer, cancellation).ConfigureAwait(false)) > 0)
                        {
                            total += read;
                            if (total > maximumImageBytes) return new(title, "");
                            await file.WriteAsync(buffer.AsMemory(0, read), cancellation).ConfigureAwait(false);
                        }
                    }
                }
                await DecodeSlots.WaitAsync(cancellation).ConfigureAwait(false);
                try
                {
                    if (!await ExtractAsync(external ? input : source, output, cancellation).ConfigureAwait(false)) return new(title, "");
                }
                finally { DecodeSlots.Release(); }
                cancellation.ThrowIfCancellationRequested();
                File.Move(output, target, overwrite: true);
                return new(title, target);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch { return new(title, ""); }
            finally
            {
                if (File.Exists(input)) File.Delete(input);
                if (File.Exists(output)) File.Delete(output);
            }
        }
        finally { gate.Release(); }
    }

    private static async Task<bool> ExtractAsync(string input, string output, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var token = timeout.Token;
        var start = new ProcessStartInfo(AudioDecoder.ExecutablePath) {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in new[] {
            "-nostdin", "-hide_banner", "-loglevel", "error", "-protocol_whitelist", "file,pipe,crypto,data",
            "-i", input, "-map", "0:v:0", "-an", "-sn", "-dn", "-frames:v", "1",
            "-vf", "scale=480:270:force_original_aspect_ratio=decrease", "-threads", "1", "-c:v", "png", "-y", output
        }) start.ArgumentList.Add(argument);
        using var process = new Process { StartInfo = start };
        token.ThrowIfCancellationRequested();
        if (!process.Start()) return false;
        using var registration = token.Register(() => {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
        });
        await Task.WhenAll(process.StandardOutput.ReadToEndAsync(token), process.StandardError.ReadToEndAsync(token),
            process.WaitForExitAsync(token)).ConfigureAwait(false);
        return process.ExitCode == 0 && new FileInfo(output) is { Exists: true, Length: > 0 and < 4_000_000 };
    }
}

internal sealed class MediaThumbnailView : Control
{
    private Image? image;
    public MediaThumbnailView()
    {
        Size = new Size(112, 64);
        BackColor = TimerTheme.Elevated;
        ForeColor = TimerTheme.Muted;
        Font = TimerTypography.Create(9);
        TabStop = false;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    public void SetImage(string path)
    {
        image?.Dispose();
        image = null;
        try
        {
            if (MediaThumbnailStore.IsCachedImage(path))
            {
                using var source = Image.FromFile(path);
                image = new Bitmap(source);
            }
        }
        catch { /* Missing or invalid thumbnails use the same placeholder. */ }
        AccessibleName = image is null ? "No Data" : "파일 섬네일";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (Width < 2 || Height < 2) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = TimerTheme.Rounded(new RectangleF(.5F, .5F, Width - 1, Height - 1), 7 * DeviceDpi / 96F);
        using var background = new SolidBrush(BackColor);
        e.Graphics.FillPath(background, shape);
        if (image is null)
            TextRenderer.DrawText(e.Graphics, "No Data", Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        else
        {
            var state = e.Graphics.Save();
            e.Graphics.SetClip(shape);
            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var scale = Math.Min(Width / (double)image.Width, Height / (double)image.Height);
            var size = new SizeF((float)(image.Width * scale), (float)(image.Height * scale));
            e.Graphics.DrawImage(image, (Width - size.Width) / 2, (Height - size.Height) / 2, size.Width, size.Height);
            e.Graphics.Restore(state);
        }
        using var border = new Pen(TimerTheme.Border);
        e.Graphics.DrawPath(border, shape);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { image?.Dispose(); image = null; }
        base.Dispose(disposing);
    }
}
