using System;
using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;

namespace LMFOOLS_Project;

internal static class DpiScaling
{
    private static double? _cachedScaleFactor;

    /// <summary>
    /// Scales a Window's dimensions and content to compensate for DPI scaling
    /// on Linux (e.g. KDE Plasma Wayland) where Avalonia may not detect
    /// the compositor's scale factor via XWayland.
    /// Call this in the constructor after InitializeComponent().
    /// </summary>
    internal static void Apply(Window window)
    {
        if (!OperatingSystem.IsLinux())
            return;

        var scaleFactor = GetScaleFactor();
        if (scaleFactor <= 1.0)
            return;

        window.Width *= scaleFactor;
        window.Height *= scaleFactor;
        window.MinWidth *= scaleFactor;
        window.MinHeight *= scaleFactor;

        var originalContent = (Control)window.Content!;
        window.Content = null; // Detach before reparenting.
        var ltc = new LayoutTransformControl
        {
            LayoutTransform = new ScaleTransform(scaleFactor, scaleFactor),
            Child = originalContent
        };
        window.Content = ltc;
    }

    private static double GetScaleFactor()
    {
        if (_cachedScaleFactor.HasValue)
            return _cachedScaleFactor.Value;

        _cachedScaleFactor = DetectLinuxScaleFactor();
        return _cachedScaleFactor.Value;
    }

    private static double DetectLinuxScaleFactor()
    {
        // Check QT_SCALE_FACTOR (KDE Plasma).
        var qtScale = Environment.GetEnvironmentVariable("QT_SCALE_FACTOR");
        if (!string.IsNullOrEmpty(qtScale) &&
            double.TryParse(qtScale, NumberStyles.Float, CultureInfo.InvariantCulture, out var scale) &&
            scale > 1.0)
            return scale;

        // Check GDK_SCALE (GNOME).
        var gdkScale = Environment.GetEnvironmentVariable("GDK_SCALE");
        if (!string.IsNullOrEmpty(gdkScale) &&
            double.TryParse(gdkScale, NumberStyles.Float, CultureInfo.InvariantCulture, out scale) &&
            scale > 1.0)
            return scale;

        // Read Xft.dpi from the X resource database. KDE Plasma sets this to
        // reflect the user's scaling (e.g. 144 for 150%, 192 for 200%). Baseline is 96.
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "xrdb",
                Arguments = "-query",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (proc != null)
            {
                var output = proc.StandardOutput.ReadToEnd();
                proc.WaitForExit();
                foreach (var line in output.Split('\n'))
                {
                    if (line.StartsWith("Xft.dpi:", StringComparison.OrdinalIgnoreCase))
                    {
                        var dpiStr = line.Substring("Xft.dpi:".Length).Trim();
                        if (double.TryParse(dpiStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var dpi) &&
                            dpi > 96)
                            return dpi / 96.0;
                    }
                }
            }
        }
        catch
        {
            // xrdb not available.
        }

        return 1.0;
    }
}
