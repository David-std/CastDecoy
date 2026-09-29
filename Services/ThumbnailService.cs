using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace CastDecoy;

public static class ThumbnailService
{
    public static BitmapSource? CaptureThumbnail(IntPtr hWnd, int targetWidth = 560, int targetHeight = 315)
    {
        try
        {
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd))
                return null;

            // 1. Obtener dimensiones precisas de la ventana usando DWM o WindowPlacement
            int srcWidth = 0;
            int srcHeight = 0;
            RECT rect = default;

            bool isMinimized = NativeMethods.IsIconic(hWnd);
            if (isMinimized)
            {
                var wp = new NativeMethods.WINDOWPLACEMENT { length = Marshal.SizeOf<NativeMethods.WINDOWPLACEMENT>() };
                if (NativeMethods.GetWindowPlacement(hWnd, ref wp))
                {
                    srcWidth = wp.rcNormalPosition.Width;
                    srcHeight = wp.rcNormalPosition.Height;
                    rect = wp.rcNormalPosition;
                }
            }

            if (srcWidth <= 16 || srcHeight <= 16)
            {
                int hr = NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf<RECT>());
                if (hr == 0 && rect.Width > 16 && rect.Height > 16)
                {
                    srcWidth = rect.Width;
                    srcHeight = rect.Height;
                }
                else if (NativeMethods.GetWindowRect(hWnd, out rect))
                {
                    srcWidth = rect.Width;
                    srcHeight = rect.Height;
                }
            }

            if (srcWidth <= 16 || srcHeight <= 16)
                return null;

            srcWidth = Math.Min(srcWidth, 3840);
            srcHeight = Math.Min(srcHeight, 2160);

            using var srcBmp = new Bitmap(srcWidth, srcHeight, PixelFormat.Format32bppArgb);
            bool captured = false;

            // 2. Intentar capturar con PrintWindow sobre la ventana raíz
            using (var g = Graphics.FromImage(srcBmp))
            {
                IntPtr hdc = g.GetHdc();
                try
                {
                    // PW_RENDERFULLCONTENT (2)
                    captured = NativeMethods.PrintWindow(hWnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);

                    // Flag 3: PW_CLIENTONLY | PW_RENDERFULLCONTENT (para DirectComposition / WinUI)
                    if (!captured || IsBitmapBlank(srcBmp, srcWidth, srcHeight))
                    {
                        captured = NativeMethods.PrintWindow(hWnd, hdc, NativeMethods.PW_CLIENTONLY | NativeMethods.PW_RENDERFULLCONTENT);
                    }

                    // Flag 0 (GDI standard)
                    if (!captured || IsBitmapBlank(srcBmp, srcWidth, srcHeight))
                    {
                        captured = NativeMethods.PrintWindow(hWnd, hdc, 0);
                    }

                    // 3. Fallback para apps UWP y apps aceleradas por hardware:
                    // Buscar ventanas hijas de composición (Windows.UI.Core.CoreWindow, Chrome_RenderWidgetHostHWND, etc.)
                    if (!captured || IsBitmapBlank(srcBmp, srcWidth, srcHeight))
                    {
                        var childHandles = new List<IntPtr>();
                        NativeMethods.EnumChildWindows(hWnd, (childHwnd, _) =>
                        {
                            if (NativeMethods.IsWindowVisible(childHwnd))
                            {
                                childHandles.Add(childHwnd);
                            }
                            return childHandles.Count < 10;
                        }, IntPtr.Zero);

                        foreach (var childHwnd in childHandles)
                        {
                            bool childCap = NativeMethods.PrintWindow(childHwnd, hdc, NativeMethods.PW_RENDERFULLCONTENT);
                            if (!childCap)
                            {
                                childCap = NativeMethods.PrintWindow(childHwnd, hdc, 0);
                            }

                            if (childCap && !IsBitmapBlank(srcBmp, srcWidth, srcHeight))
                            {
                                captured = true;
                                break;
                            }
                        }
                    }

                    // 4. Fallback BitBlt desde el escritorio si la ventana no está minimizada y está en pantalla
                    if ((!captured || IsBitmapBlank(srcBmp, srcWidth, srcHeight)) && !isMinimized)
                    {
                        if (rect.Left > -4000 && rect.Top > -4000 && rect.Right > 0 && rect.Bottom > 0)
                        {
                            IntPtr hDesktopDc = NativeMethods.GetDC(IntPtr.Zero);
                            if (hDesktopDc != IntPtr.Zero)
                            {
                                try
                                {
                                    bool bltOk = NativeMethods.BitBlt(hdc, 0, 0, srcWidth, srcHeight, hDesktopDc, rect.Left, rect.Top, NativeMethods.SRCCOPY);
                                    if (bltOk && !IsBitmapBlank(srcBmp, srcWidth, srcHeight))
                                    {
                                        captured = true;
                                    }
                                }
                                finally
                                {
                                    NativeMethods.ReleaseDC(IntPtr.Zero, hDesktopDc);
                                }
                            }
                        }
                    }
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }

            if (!captured || IsBitmapBlank(srcBmp, srcWidth, srcHeight))
                return null;

            // Escalar al tamaño deseado para la tarjeta
            float scale = Math.Min((float)targetWidth / srcWidth, (float)targetHeight / srcHeight);
            int thumbW = Math.Max(1, (int)(srcWidth * scale));
            int thumbH = Math.Max(1, (int)(srcHeight * scale));

            using var thumbBmp = new Bitmap(thumbW, thumbH, PixelFormat.Format32bppArgb);
            using (var gThumb = Graphics.FromImage(thumbBmp))
            {
                gThumb.InterpolationMode = InterpolationMode.HighQualityBicubic;
                gThumb.SmoothingMode = SmoothingMode.HighQuality;
                gThumb.PixelOffsetMode = PixelOffsetMode.HighQuality;
                gThumb.CompositingQuality = CompositingQuality.HighQuality;
                gThumb.DrawImage(srcBmp, 0, 0, thumbW, thumbH);
            }

            // DirectComposition y PrintWindow a menudo dejan el canal Alpha en 0x00.
            // WPF trata Alpha=0 como completamente transparente, lo que ocultaba la miniatura.
            // Forzar Alpha = 255 (opaco) para que WPF la renderice visible siempre.
            var data = thumbBmp.LockBits(
                new Rectangle(0, 0, thumbW, thumbH),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb);
            try
            {
                unsafe
                {
                    byte* ptr = (byte*)data.Scan0;
                    int stride = Math.Abs(data.Stride);
                    for (int y = 0; y < thumbH; y++)
                    {
                        byte* row = ptr + (y * stride);
                        for (int x = 0; x < thumbW; x++)
                        {
                            row[x * 4 + 3] = 255;
                        }
                    }
                }
            }
            finally
            {
                thumbBmp.UnlockBits(data);
            }

            var hBitmap = thumbBmp.GetHbitmap();
            try
            {
                var wpfBitmap = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                wpfBitmap.Freeze();
                return wpfBitmap;
            }
            finally
            {
                NativeMethods.DeleteObject(hBitmap);
            }
        }
        catch
        {
            return null;
        }
    }

    private static bool IsBitmapBlank(Bitmap bmp, int w, int h)
    {
        try
        {
            int stepX = Math.Max(1, w / 8);
            int stepY = Math.Max(1, h / 8);
            int nonDarkCount = 0;

            for (int x = stepX; x < w; x += stepX)
            {
                for (int y = stepY; y < h; y += stepY)
                {
                    var c = bmp.GetPixel(x, y);
                    if (c.R > 4 || c.G > 4 || c.B > 4)
                    {
                        nonDarkCount++;
                        if (nonDarkCount >= 2) return false;
                    }
                }
            }

            return nonDarkCount < 2;
        }
        catch
        {
            return false;
        }
    }
}
