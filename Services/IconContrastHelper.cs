using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MediaColor = System.Windows.Media.Color;

namespace CastDecoy.Services;

public static class IconContrastHelper
{
    private static readonly SolidColorBrush DefaultLightBg = new(MediaColor.FromRgb(0xf2, 0xf2, 0xf7));
    private static readonly SolidColorBrush DefaultLightBorder = new(MediaColor.FromRgb(0xe5, 0xe5, 0xea));

    private static readonly SolidColorBrush DarkContrastBg = new(MediaColor.FromRgb(0x18, 0x18, 0x1b));
    private static readonly SolidColorBrush DarkContrastBorder = new(MediaColor.FromRgb(0x3f, 0x3f, 0x46));

    static IconContrastHelper()
    {
        DefaultLightBg.Freeze();
        DefaultLightBorder.Freeze();
        DarkContrastBg.Freeze();
        DarkContrastBorder.Freeze();
    }

    public static (Brush Background, Brush Border) GetAdaptiveBrushes(ImageSource? icon)
    {
        if (icon == null)
            return (DefaultLightBg, DefaultLightBorder);

        try
        {
            BitmapSource? bmpSource = icon as BitmapSource;
            if (bmpSource == null)
            {
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawImage(icon, new Rect(0, 0, 24, 24));
                }
                var rtb = new RenderTargetBitmap(24, 24, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                bmpSource = rtb;
            }

            var formatted = new FormatConvertedBitmap(bmpSource, PixelFormats.Bgra32, null, 0);
            int width = Math.Min(32, formatted.PixelWidth);
            int height = Math.Min(32, formatted.PixelHeight);

            int stride = width * 4;
            byte[] pixels = new byte[height * stride];
            formatted.CopyPixels(new Int32Rect(0, 0, width, height), pixels, stride, 0);

            long totalLum = 0;
            int visibleCount = 0;
            int lightPixelCount = 0;
            int totalPixels = width * height;

            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte b = pixels[i];
                byte g = pixels[i + 1];
                byte r = pixels[i + 2];
                byte a = pixels[i + 3];

                if (a > 30)
                {
                    visibleCount++;
                    int lum = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    totalLum += lum;
                    if (lum > 170)
                    {
                        lightPixelCount++;
                    }
                }
            }

            if (visibleCount > 0)
            {
                double avgLum = (double)totalLum / visibleCount;
                double lightRatio = (double)lightPixelCount / visibleCount;
                bool isTransparent = visibleCount < (totalPixels * 0.94);

                if (isTransparent && (avgLum > 160 || lightRatio > 0.50))
                {
                    return (DarkContrastBg, DarkContrastBorder);
                }
            }
        }
        catch { }

        return (DefaultLightBg, DefaultLightBorder);
    }
}
