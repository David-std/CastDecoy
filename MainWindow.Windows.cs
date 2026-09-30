using CastDecoy.Services;
using FlashCap;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;
using MediaColor = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using System.Xml.Linq;

namespace CastDecoy;

public partial class MainWindow : Window
{
    private string? _selectedBrowserKey = null;
    private string? _selectedBrowserName = null;
    private string? _selectedBrowserPath = null;
    private Process? _protectedBrowserProc = null;
    private IntPtr _protectedBrowserHwnd = IntPtr.Zero;
    private string? _activeProtectedBrowserKey = null;
    private string? _chromePath = null;
    private string? _edgePath = null;
    private string? _bravePath = null;
    private string? _operaPath = null;
    private string? _firefoxPath = null;

    
    private class RawWindow
    {
        public IntPtr Handle { get; set; }
        public string Title { get; set; } = string.Empty;
        public string ProcessName { get; set; } = string.Empty;
        public uint ProcessId { get; set; }
        public bool IsSelf { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool IsMinimized { get; set; }
    }

    
    private void RefreshWindowsSilent()
    {
        if (_isExiting) return;
        RefreshWindowsInternal(false);
    }

    
    public void RefreshWindows()
    {
        RefreshWindowsInternal(true);
    }

    
    private void RefreshWindowsInternal(bool captureThumbnails)
    {
        var shellWindow = NativeMethods.GetShellWindow();
        var rawList = new List<RawWindow>();

        NativeMethods.EnumWindows((hWnd, _) =>
        {
            if (!NativeMethods.IsWindowVisible(hWnd)) return true;
            if (hWnd == shellWindow) return true;

            int titleLen = NativeMethods.GetWindowTextLength(hWnd);
            if (titleLen == 0) return true;

            if (NativeMethods.DwmGetWindowAttribute(hWnd, NativeMethods.DWMWA_CLOAKED,
                out bool isCloaked, Marshal.SizeOf<bool>()) == 0 && isCloaked)
                return true;

            var classNameBuilder = new StringBuilder(256);
            NativeMethods.GetClassName(hWnd, classNameBuilder, 256);
            string className = classNameBuilder.ToString();
            if (ExcludedClasses.Contains(className)) return true;

            int exStyle = NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);
            bool isToolWindow = (exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0;
            bool isAppWindow = (exStyle & NativeMethods.WS_EX_APPWINDOW) != 0;
            if (isToolWindow && !isAppWindow) return true;

            IntPtr owner = NativeMethods.GetWindow(hWnd, NativeMethods.GW_OWNER);
            if (owner != IntPtr.Zero && !isAppWindow) return true;

            if (!NativeMethods.GetWindowRect(hWnd, out var rect)) return true;
            int width = rect.Width;
            int height = rect.Height;

            bool isMinimized = NativeMethods.IsIconic(hWnd);
            if (!isMinimized && (width <= 80 || height <= 80)) return true;

            var titleBuilder = new StringBuilder(titleLen + 1);
            NativeMethods.GetWindowText(hWnd, titleBuilder, titleBuilder.Capacity);
            string title = titleBuilder.ToString().Trim();
            if (string.IsNullOrWhiteSpace(title)) return true;

            NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
            string processName = "unknown";
            try
            {
                using var proc = Process.GetProcessById((int)pid);
                processName = proc.ProcessName;
            }
            catch { }

            rawList.Add(new RawWindow
            {
                Handle = hWnd,
                Title = title,
                ProcessName = processName,
                ProcessId = pid,
                IsSelf = pid == _selfPid,
                Width = width,
                Height = height,
                IsMinimized = isMinimized
            });

            return true;
        }, IntPtr.Zero);

        var grouped = rawList
            .GroupBy(w => new { w.ProcessId, w.Title })
            .Select(g => g.OrderByDescending(w => w.Width * w.Height).First())
            .ToList();

        var pids = grouped.Select(w => w.ProcessId).Distinct().ToList();
        var deduped = new List<RawWindow>();

        foreach (var pid in pids)
        {
            var procWindows = grouped.Where(w => w.ProcessId == pid).ToList();
            if (procWindows.Count > 1)
            {
                var genericStubs = procWindows.Where(w =>
                    w.Title.Equals(w.ProcessName, StringComparison.OrdinalIgnoreCase)).ToList();

                if (genericStubs.Count > 0 && genericStubs.Count < procWindows.Count)
                {
                    deduped.AddRange(procWindows.Except(genericStubs));
                    continue;
                }
            }
            deduped.AddRange(procWindows);
        }

        var currentMap = _allWindows.ToDictionary(w => w.Handle, w => w);
        var newList = new List<WindowInfo>();

        foreach (var item in deduped)
        {
            NativeMethods.GetWindowDisplayAffinity(item.Handle, out uint aff);
            bool isGhosted = aff == NativeMethods.WDA_EXCLUDEFROMCAPTURE;

            ImageSource? icon = null;
            BitmapSource? existingThumb = null;

            if (currentMap.TryGetValue(item.Handle, out var existing) && existing.Icon != null)
            {
                icon = existing.Icon;
                existingThumb = existing.Thumbnail;
            }
            else
            {
                try
                {
                    using var proc = Process.GetProcessById((int)item.ProcessId);
                    icon = ExtractWindowIcon(item.Handle, proc, item.Title);
                }
                catch { }
                if (existing != null && existingThumb == null)
                {
                    existingThumb = existing.Thumbnail;
                }
            }

            var winInfo = new WindowInfo
            {
                Handle = item.Handle,
                Title = item.Title,
                ProcessName = item.ProcessName,
                ProcessId = item.ProcessId,
                IsSelf = item.IsSelf,
                WindowWidth = item.Width,
                WindowHeight = item.Height,
                IsMinimized = item.IsMinimized,
                IsGhosted = isGhosted,
                StatusText = isGhosted ? "Oculta" : "Visible",
                Icon = icon,
                Thumbnail = existingThumb
            };

            newList.Add(winInfo);
        }

        var sorted = newList.OrderByDescending(w => w.IsSelf).ThenBy(w => w.Title).ToList();

        bool countChanged = _allWindows.Count != sorted.Count;
        bool handlesChanged = !_allWindows.Select(w => w.Handle).SequenceEqual(sorted.Select(w => w.Handle));

        if (countChanged || handlesChanged)
        {
            _allWindows.Clear();
            _allWindows.AddRange(sorted);

            var prevSelected = WindowListBox.SelectedItem as WindowInfo;
            _displayedWindows.Clear();
            foreach (var win in _allWindows)
            {
                _displayedWindows.Add(win);
            }

            if (prevSelected != null && _displayedWindows.Any(w => w.Handle == prevSelected.Handle))
            {
                WindowListBox.SelectedItem = _displayedWindows.First(w => w.Handle == prevSelected.Handle);
            }
            else if (_displayedWindows.Count > 0 && WindowListBox.SelectedItem == null)
            {
                WindowListBox.SelectedIndex = 0;
            }

            var selfWin = _allWindows.FirstOrDefault(w => w.IsSelf);
            if (selfWin != null)
            {
                SelfHideSwitch.IsChecked = selfWin.IsGhosted;
            }

            UpdateStatusInfo();
            UpdateActionButtons();
            BuildTrayContextMenu();
            SyncAppShortcuts();
            UpdateLiveDiagnosticStatus();
        }
        else
        {
            // Sincronizar propiedades en las instancias activas ya enlazadas a la UI
            foreach (var item in sorted)
            {
                var existing = _allWindows.FirstOrDefault(w => w.Handle == item.Handle);
                if (existing != null)
                {
                    if (existing.Title != item.Title) existing.Title = item.Title;
                    if (existing.IsMinimized != item.IsMinimized) existing.IsMinimized = item.IsMinimized;
                    if (existing.WindowWidth != item.WindowWidth) existing.WindowWidth = item.WindowWidth;
                    if (existing.WindowHeight != item.WindowHeight) existing.WindowHeight = item.WindowHeight;
                    if (existing.IsGhosted != item.IsGhosted)
                    {
                        existing.IsGhosted = item.IsGhosted;
                        existing.StatusText = item.StatusText;
                    }
                    if (existing.Thumbnail == null && item.Thumbnail != null)
                    {
                        existing.Thumbnail = item.Thumbnail;
                    }
                }
            }
        }

        if (captureThumbnails)
        {
            // Apuntar directamente a las instancias visibles en la UI (_displayedWindows)
            var targets = _displayedWindows.ToList();
            Task.Run(() =>
            {
                foreach (var win in targets)
                {
                    if (_isExiting) break;
                    var thumb = ThumbnailService.CaptureThumbnail(win.Handle);
                    if (thumb != null)
                    {
                        Dispatcher.InvokeAsync(() => win.Thumbnail = thumb);
                    }
                }
            });
        }
    }

    
    private static ImageSource? _defaultAppIcon;

    private static ImageSource GetDefaultAppIcon()
    {
        if (_defaultAppIcon != null) return _defaultAppIcon;

        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRoundedRectangle(
                new SolidColorBrush(MediaColor.FromRgb(0x47, 0x55, 0x69)),
                null,
                new Rect(0, 0, 32, 32),
                6, 6);
            dc.DrawRoundedRectangle(
                new SolidColorBrush(MediaColor.FromRgb(0xf8, 0xfa, 0xfc)),
                null,
                new Rect(3, 8, 26, 21),
                3, 3);
            dc.DrawRectangle(
                new SolidColorBrush(MediaColor.FromRgb(0x94, 0xa3, 0xb8)),
                null,
                new Rect(6, 12, 20, 2));
            dc.DrawRectangle(
                new SolidColorBrush(MediaColor.FromRgb(0xcb, 0xd5, 0xe1)),
                null,
                new Rect(6, 17, 14, 2));
        }
        var rtb = new RenderTargetBitmap(32, 32, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        rtb.Render(dv);
        rtb.Freeze();
        _defaultAppIcon = rtb;
        return _defaultAppIcon;
    }

    private static ImageSource? LoadBitmapFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;

            var uri = new Uri(path, UriKind.Absolute);
            try
            {
                var decoder = BitmapDecoder.Create(uri, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count > 0)
                {
                    var frame = decoder.Frames
                        .OrderBy(f => Math.Abs(f.PixelWidth - 32))
                        .FirstOrDefault() ?? decoder.Frames[0];
                    frame.Freeze();
                    return frame;
                }
            }
            catch { }

            if (path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase))
            {
                using var ico = new System.Drawing.Icon(path);
                var src = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = uri;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private static ImageSource? TryExtractPackagedAppIcon(string path)
    {
        try
        {
            string? dir = File.Exists(path) ? System.IO.Path.GetDirectoryName(path) : path;
            while (!string.IsNullOrEmpty(dir))
            {
                string manifestPath = System.IO.Path.Combine(dir, "AppxManifest.xml");
                if (File.Exists(manifestPath))
                {
                    try
                    {
                        string xml = File.ReadAllText(manifestPath);
                        var doc = XDocument.Parse(xml);

                        foreach (var el in doc.Descendants())
                        {
                            var attr = el.Attribute("Square44x44Logo")
                                    ?? el.Attribute("Logo")
                                    ?? el.Attribute("Square150x150Logo")
                                    ?? el.Attribute("SmallTile");

                            if (attr != null && !string.IsNullOrWhiteSpace(attr.Value))
                            {
                                string relative = attr.Value.Replace('/', '\\');
                                string candidate = System.IO.Path.Combine(dir, relative);
                                if (File.Exists(candidate))
                                {
                                    var img = LoadBitmapFromFile(candidate);
                                    if (img != null) return img;
                                }

                                string targetDir = System.IO.Path.GetDirectoryName(candidate) ?? dir;
                                string fileNoExt = System.IO.Path.GetFileNameWithoutExtension(candidate);
                                if (Directory.Exists(targetDir))
                                {
                                    var matched = Directory.GetFiles(targetDir, fileNoExt + "*.png");
                                    if (matched.Length > 0)
                                    {
                                        var best = matched.OrderBy(m =>
                                        {
                                            if (m.Contains("targetsize-48", StringComparison.OrdinalIgnoreCase)) return 1;
                                            if (m.Contains("targetsize-44", StringComparison.OrdinalIgnoreCase)) return 2;
                                            if (m.Contains("targetsize-32", StringComparison.OrdinalIgnoreCase)) return 3;
                                            if (m.Contains("targetsize-256", StringComparison.OrdinalIgnoreCase)) return 4;
                                            if (m.Contains("scale-100", StringComparison.OrdinalIgnoreCase)) return 5;
                                            return 10;
                                        }).First();
                                        var img = LoadBitmapFromFile(best);
                                        if (img != null) return img;
                                    }
                                }
                            }
                        }
                    }
                    catch { }

                    string assetsDir = System.IO.Path.Combine(dir, "Assets");
                    if (Directory.Exists(assetsDir))
                    {
                        var pngs = Directory.GetFiles(assetsDir, "*targetsize*.png")
                            .Concat(Directory.GetFiles(assetsDir, "*logo*.png"))
                            .ToArray();

                        if (pngs.Length > 0)
                        {
                            var best = pngs.OrderBy(m =>
                            {
                                if (m.Contains("targetsize-48", StringComparison.OrdinalIgnoreCase)) return 1;
                                if (m.Contains("targetsize-44", StringComparison.OrdinalIgnoreCase)) return 2;
                                if (m.Contains("targetsize-32", StringComparison.OrdinalIgnoreCase)) return 3;
                                if (m.Contains("targetsize-256", StringComparison.OrdinalIgnoreCase)) return 4;
                                if (m.Contains("scale-100", StringComparison.OrdinalIgnoreCase)) return 5;
                                return 10;
                            }).First();
                            var img = LoadBitmapFromFile(best);
                            if (img != null) return img;
                        }
                    }
                    break;
                }
                dir = System.IO.Path.GetDirectoryName(dir);
            }
        }
        catch { }
        return null;
    }

    private static string? GetWindowAumid(IntPtr hWnd)
    {
        try
        {
            Guid riid = NativeMethods.IID_IPropertyStore;
            int hr = NativeMethods.SHGetPropertyStoreForWindow(hWnd, ref riid, out NativeMethods.IPropertyStore store);
            if (hr == 0 && store != null)
            {
                var key = NativeMethods.PKEY_AppUserModel_ID;
                var pv = new NativeMethods.PROPVARIANT();
                try
                {
                    store.GetValue(ref key, ref pv);
                    if (pv.vt == 31 && pv.ptrVal != IntPtr.Zero)
                    {
                        return Marshal.PtrToStringUni(pv.ptrVal);
                    }
                }
                finally
                {
                    NativeMethods.PropVariantClear(ref pv);
                    Marshal.ReleaseComObject(store);
                }
            }
        }
        catch { }
        return null;
    }

    private static ImageSource? TryExtractIconFromAumid(string aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return null;

        try
        {
            string shellItemPath = @"shell:AppsFolder\" + aumid;
            Guid riid = NativeMethods.IID_IShellItemImageFactory;
            int hr = NativeMethods.SHCreateItemFromParsingName(shellItemPath, IntPtr.Zero, ref riid, out object objFactory);
            if (hr == 0 && objFactory is NativeMethods.IShellItemImageFactory factory)
            {
                var size = new NativeMethods.SIZE(48, 48);
                hr = factory.GetImage(size, NativeMethods.SIIGBF.SIIGBF_ICONONLY, out IntPtr hBitmap);
                if (hr == 0 && hBitmap != IntPtr.Zero)
                {
                    try
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze();
                        return bmp;
                    }
                    finally
                    {
                        NativeMethods.DeleteObject(hBitmap);
                    }
                }
            }
        }
        catch { }

        try
        {
            string packageFamilyName = aumid.Contains('!') ? aumid.Substring(0, aumid.IndexOf('!')) : aumid;
            string windowsApps = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
            if (Directory.Exists(windowsApps))
            {
                var matchedDirs = Directory.GetDirectories(windowsApps, packageFamilyName + "*");
                foreach (var dir in matchedDirs)
                {
                    var icon = TryExtractPackagedAppIcon(dir);
                    if (icon != null) return icon;
                }
            }
        }
        catch { }

        return null;
    }

    private static ImageSource? TryExtractChromiumWebAppIcon(string procName, string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle)) return null;

        try
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string[] browserUserDataRoots =
            {
                System.IO.Path.Combine(localAppData, "Microsoft", "Edge", "User Data"),
                System.IO.Path.Combine(localAppData, "Google", "Chrome", "User Data"),
                System.IO.Path.Combine(localAppData, "BraveSoftware", "Brave-Browser", "User Data")
            };

            foreach (var userRoot in browserUserDataRoots)
            {
                if (!Directory.Exists(userRoot)) continue;

                var subDirs = Directory.GetDirectories(userRoot);
                foreach (var sDir in subDirs)
                {
                    string webAppDir = System.IO.Path.Combine(sDir, "Web Applications");
                    if (!Directory.Exists(webAppDir)) continue;

                    var crxDirs = Directory.GetDirectories(webAppDir, "_crx_*");
                    foreach (var crxDir in crxDirs)
                    {
                        var files = Directory.GetFiles(crxDir, "*.*")
                            .Where(f => f.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ||
                                        f.EndsWith(".png", StringComparison.OrdinalIgnoreCase));

                        foreach (var file in files)
                        {
                            string nameNoExt = System.IO.Path.GetFileNameWithoutExtension(file);
                            if (windowTitle.Contains(nameNoExt, StringComparison.OrdinalIgnoreCase) ||
                                nameNoExt.Contains(windowTitle, StringComparison.OrdinalIgnoreCase))
                            {
                                var bmp = LoadBitmapFromFile(file);
                                if (bmp != null) return bmp;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static ImageSource? ExtractWindowIcon(IntPtr hWnd, Process proc, string? windowTitle = null)
    {
        try
        {
            IntPtr targetHwnd = hWnd;
            uint realPid = (uint)proc.Id;
            string procName = proc.ProcessName;
            bool isAppFrameHost = procName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase);

            // 1. Check Windows Property Store for AUMID (covers all modern UWP/MSIX packaged apps natively)
            string? aumid = GetWindowAumid(hWnd);

            // If it's a host window, enumerate child windows to get real PID or child window AUMID
            if (isAppFrameHost)
            {
                NativeMethods.EnumChildWindows(hWnd, (childHwnd, _) =>
                {
                    NativeMethods.GetWindowThreadProcessId(childHwnd, out uint childPid);
                    if (childPid != 0 && childPid != realPid)
                    {
                        realPid = childPid;
                        targetHwnd = childHwnd;
                    }
                    if (string.IsNullOrEmpty(aumid))
                    {
                        aumid = GetWindowAumid(childHwnd);
                    }
                    return string.IsNullOrEmpty(aumid);
                }, IntPtr.Zero);
            }

            // 2. If modern packaged app identity (AUMID) was found, extract official package icon
            if (!string.IsNullOrEmpty(aumid))
            {
                var aumidIcon = TryExtractIconFromAumid(aumid);
                if (aumidIcon != null) return aumidIcon;
            }

            // 3. Query WM_GETICON with timeout (only if not ApplicationFrameHost or if targetHwnd is a child)
            if (!isAppFrameHost || targetHwnd != hWnd)
            {
                try
                {
                    IntPtr hIcon = IntPtr.Zero;
                    NativeMethods.SendMessageTimeout(targetHwnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_BIG, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK, 150, out hIcon);
                    if (hIcon == IntPtr.Zero)
                        NativeMethods.SendMessageTimeout(targetHwnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL2, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK, 150, out hIcon);
                    if (hIcon == IntPtr.Zero)
                        NativeMethods.SendMessageTimeout(targetHwnd, NativeMethods.WM_GETICON, (IntPtr)NativeMethods.ICON_SMALL, IntPtr.Zero, NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK, 150, out hIcon);

                    if (hIcon != IntPtr.Zero)
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze();
                        return bmp;
                    }
                }
                catch { }
            }

            // 3. Query class icon (ONLY if NOT ApplicationFrameHost! ApplicationFrameHost's class icon is the generic window frame)
            if (!isAppFrameHost)
            {
                try
                {
                    IntPtr hIcon = NativeMethods.GetClassLongAuto(targetHwnd, NativeMethods.GCL_HICON);
                    if (hIcon == IntPtr.Zero)
                        hIcon = NativeMethods.GetClassLongAuto(targetHwnd, NativeMethods.GCL_HICONSM);

                    if (hIcon != IntPtr.Zero)
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze();
                        return bmp;
                    }
                }
                catch { }
            }

            // 4. Resolve process executable path
            string? exePath = GetProcessPath(realPid);
            if (string.IsNullOrEmpty(exePath) && realPid == proc.Id)
            {
                try { exePath = proc.MainModule?.FileName; } catch { }
            }

            // 5. Packaged application (UWP / MSIX / WindowsApps) logo assets
            if (!string.IsNullOrEmpty(exePath))
            {
                var packaged = TryExtractPackagedAppIcon(exePath);
                if (packaged != null) return packaged;
            }

            // 6. Chromium / Edge PWA web application icon
            if (!string.IsNullOrEmpty(windowTitle))
            {
                var pwaIcon = TryExtractChromiumWebAppIcon(procName, windowTitle);
                if (pwaIcon != null) return pwaIcon;
            }

            // 7. Win32 Associated Icon
            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath) && !isAppFrameHost)
            {
                try
                {
                    using var ico = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                    if (ico != null)
                    {
                        var bmp = Imaging.CreateBitmapSourceFromHIcon(ico.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze();
                        return bmp;
                    }
                }
                catch { }

                // 8. SHGetFileInfo
                try
                {
                    var shinfo = new NativeMethods.SHFILEINFO();
                    IntPtr hSh = NativeMethods.SHGetFileInfo(exePath, 0, ref shinfo, (uint)Marshal.SizeOf(shinfo), NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_LARGEICON);
                    if (hSh != IntPtr.Zero && shinfo.hIcon != IntPtr.Zero)
                    {
                        try
                        {
                            var bmp = Imaging.CreateBitmapSourceFromHIcon(shinfo.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                            bmp.Freeze();
                            return bmp;
                        }
                        finally
                        {
                            NativeMethods.DestroyIcon(shinfo.hIcon);
                        }
                    }
                }
                catch { }
            }

            // 9. Clean fallback icon
            return GetDefaultAppIcon();
        }
        catch
        {
            return GetDefaultAppIcon();
        }
    }

    private static string? GetProcessPath(uint pid)
    {
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            IntPtr hProcess = NativeMethods.OpenProcess(0x1000, false, pid);
            if (hProcess != IntPtr.Zero)
            {
                try
                {
                    if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref size))
                    {
                        return sb.ToString();
                    }
                }
                finally
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
        }
        catch { }
        return null;
    }

    
    private void ApplyTaskbarOption(IntPtr hWnd, bool ghosted)
    {
        bool shouldHideFromTaskbar = HideTaskbarCheckBox.IsChecked == true;
        bool shouldHide = ghosted && shouldHideFromTaskbar;

        if (_taskbarList == null) return;
        try
        {
            if (shouldHide)
            {
                _taskbarList.DeleteTab(hWnd);
            }
            else
            {
                _taskbarList.AddTab(hWnd);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ApplyTaskbarOption failed: {ex.Message}");
        }
    }

    
    private void OnHideTaskbarOptionChanged(object sender, RoutedEventArgs e)
    {
        bool hideFromTaskbar = HideTaskbarCheckBox.IsChecked == true;
        foreach (var win in _allWindows)
        {
            if (win.IsGhosted)
            {
                ApplyTaskbarOption(win.Handle, hideFromTaskbar);
            }
            else
            {
                ApplyTaskbarOption(win.Handle, false);
            }
        }
        var selfWin = _allWindows.FirstOrDefault(w => w.IsSelf);
        bool selfGhosted = selfWin?.IsGhosted ?? (SelfHideSwitch.IsChecked == true);
        ApplyTaskbarOption(_selfHwnd, selfGhosted);
    }

    
    public void SetGhost(WindowInfo win, bool ghost)
    {
        if (win.IsGhosted == ghost) return;

        if (win.IsSelf)
        {
            win.IsGhosted = ghost;
            win.StatusText = ghost ? "Oculta" : "Visible";
            SelfHideSwitch.IsChecked = ghost;
            SetSelfDisplayAffinity(ghost);
            ApplyTaskbarOption(_selfHwnd, ghost);
            UpdateStatusInfo();
            UpdateActionButtons();
            return;
        }

        uint targetAffinity = ghost
            ? NativeMethods.WDA_EXCLUDEFROMCAPTURE
            : NativeMethods.WDA_NONE;

        bool ok = NativeMethods.SetWindowDisplayAffinity(win.Handle, targetAffinity);
        if (!ok)
        {
            ok = Injector.SetAffinityRemote(win.ProcessId, win.Handle, targetAffinity);
        }

        if (ok)
        {
            win.IsGhosted = ghost;
            win.StatusText = ghost ? "Oculta" : "Visible";
            win.HasError = false;
            win.ErrorMessage = string.Empty;
            ApplyTaskbarOption(win.Handle, ghost);
        }
        else
        {
            win.HasError = true;
            win.ErrorMessage = "Acceso denegado. Se requieren permisos de Administrador.";

            if (!IsAdministrator())
            {
                var prompt = MessageBox.Show(
                    $"La ventana \"{win.Title}\" ({win.ProcessName}) pertenece a un proceso con privilegios elevados del sistema (como el Administrador de tareas).\n\nPara poder aislarla de la pantalla y grabaciones, CastDecoy debe ejecutarse con permisos de Administrador.\n\n¿Deseas reiniciar CastDecoy ahora como Administrador?",
                    "Permisos de Administrador requeridos",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (prompt == MessageBoxResult.Yes)
                {
                    RestartAsAdmin();
                }
            }
            else
            {
                MessageBox.Show(
                    $"No fue posible ocultar la ventana \"{win.Title}\". Es posible que esté protegida por el kernel o políticas de seguridad del sistema.",
                    "Aviso de Protección",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
    }

    
    
    private void OnWindowToggleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is WindowInfo win)
        {
            ToggleGhost(win);
            e.Handled = true;
        }
    }

    
    private void OnToggleAllProtectionClick(object sender, RoutedEventArgs e)
    {
        bool anyGhosted = _allWindows.Any(w => w.IsGhosted);
        SetAllGhost(!anyGhosted);
    }

    
    public void ToggleGhost(WindowInfo win)
    {
        SetGhost(win, !win.IsGhosted);
        UpdateStatusInfo();
        UpdateActionButtons();
        UpdateLiveDiagnosticStatus();
        BuildTrayContextMenu();
    }

    
    public void SetAllGhost(bool ghost)
    {
        uint targetAffinity = ghost
            ? NativeMethods.WDA_EXCLUDEFROMCAPTURE
            : NativeMethods.WDA_NONE;

        foreach (var win in _allWindows)
        {
            SetGhost(win, ghost);
        }

        UpdateStatusInfo();
        UpdateActionButtons();
        UpdateLiveDiagnosticStatus();
        BuildTrayContextMenu();
    }

    
    private void SyncAffinityAndStealth()
    {
        foreach (var win in _allWindows)
        {
            if (win.IsGhosted && !win.IsSelf)
            {
                NativeMethods.GetWindowDisplayAffinity(win.Handle, out uint currentAff);
                if (currentAff != NativeMethods.WDA_EXCLUDEFROMCAPTURE)
                {
                    bool ok = NativeMethods.SetWindowDisplayAffinity(win.Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                    if (!ok)
                    {
                        Injector.SetAffinityRemote(win.ProcessId, win.Handle, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                    }
                }
            }
        }

        var selfWin = _allWindows.FirstOrDefault(w => w.IsSelf);
        if (selfWin != null && selfWin.IsGhosted)
        {
            SetSelfDisplayAffinity(true);
        }
    }

    
    private void UpdateStatusInfo()
    {
        int total = _allWindows.Count;
        int isolated = _allWindows.Count(w => w.IsGhosted);
        if (isolated > 0)
        {
            StatusCountText.Text = $"{total} ventanas ({isolated} aislada{(isolated > 1 ? "s" : "")})";
        }
        else
        {
            StatusCountText.Text = $"{total} ventanas disponibles";
        }
    }

    
    private void UpdateActionButtons()
    {
        bool anyGhosted = _allWindows.Any(w => w.IsGhosted);
        if (ToggleAllProtectionButton != null)
        {
            ToggleAllProtectionButton.Content = anyGhosted ? "Restaurar todas" : "Ocultar todas";
            ToggleAllProtectionButton.Style = (Style)FindResource(anyGhosted ? "SecondaryPillBtn" : "PrimaryPillBtn");
        }
        if (StatusCountText != null)
        {
            int total = _allWindows.Count;
            int isolated = _allWindows.Count(w => w.IsGhosted);
            int visible = total - isolated;
            StatusCountText.Text = visible == 1 ? "1 ventana visible" : $"{visible} ventanas visibles";
        }
    }

    
    private void OnPrimaryActionClick(object sender, RoutedEventArgs e)
    {
        var selectedList = WindowListBox.SelectedItems.OfType<WindowInfo>().ToList();
        if (selectedList.Count == 0)
        {
            selectedList = _displayedWindows.Where(w => w.IsSelected).ToList();
        }
        if (selectedList.Count == 0 && WindowListBox.SelectedItem is WindowInfo single)
        {
            selectedList.Add(single);
        }

        if (selectedList.Count == 0) return;

        bool anyNotGhosted = selectedList.Any(w => !w.IsGhosted);
        foreach (var win in selectedList)
        {
            SetGhost(win, anyNotGhosted);
        }

        UpdateStatusInfo();
        UpdateActionButtons();
        BuildTrayContextMenu();
    }

    
    private void OnSelectAllClick(object sender, RoutedEventArgs e)
    {
        WindowListBox.SelectAll();
        foreach (var win in _displayedWindows)
        {
            win.IsSelected = true;
        }
        UpdateActionButtons();
    }

    
    private void OnDeselectAllClick(object sender, RoutedEventArgs e)
    {
        WindowListBox.UnselectAll();
        foreach (var win in _displayedWindows)
        {
            win.IsSelected = false;
        }
        UpdateActionButtons();
    }

    
    private void OnRefreshWindowsClick(object sender, RoutedEventArgs e) => RefreshWindows();

    
    private void OnRevealAllClick(object sender, RoutedEventArgs e) => SetAllGhost(false);

    
    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActionButtons();

    
    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is WindowInfo win)
        {
            WindowListBox.SelectedItem = win;
            FocusWindow(win.Handle);
            e.Handled = true;
        }
    }

    
    private void OnListBoxKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space)
        {
            OnPrimaryActionClick(sender, e);
            e.Handled = true;
        }
    }

    
    public static void FocusWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd)) return;

        try
        {
            if (NativeMethods.IsIconic(hWnd))
            {
                NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_RESTORE);
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
            }
            else
            {
                NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW_SHOW);
                NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
            }

            IntPtr fgWnd = NativeMethods.GetForegroundWindow();
            uint fgThread = NativeMethods.GetWindowThreadProcessId(fgWnd, out _);
            uint curThread = NativeMethods.GetCurrentThreadId();
            uint targetThread = NativeMethods.GetWindowThreadProcessId(hWnd, out _);

            bool attachedCur = false;
            bool attachedFg = false;

            try
            {
                if (curThread != targetThread && targetThread != 0)
                {
                    attachedCur = NativeMethods.AttachThreadInput(curThread, targetThread, true);
                }
                if (fgThread != 0 && fgThread != curThread && fgThread != targetThread)
                {
                    attachedFg = NativeMethods.AttachThreadInput(curThread, fgThread, true);
                }

                NativeMethods.keybd_event(0, 0, 0, 0);
                NativeMethods.BringWindowToTop(hWnd);
                NativeMethods.SetForegroundWindow(hWnd);
                NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
                    NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_SHOWWINDOW);

                NativeMethods.SwitchToThisWindow(hWnd, true);
            }
            catch { }
            finally
            {
                if (attachedCur)
                    NativeMethods.AttachThreadInput(curThread, targetThread, false);
                if (attachedFg)
                    NativeMethods.AttachThreadInput(curThread, fgThread, false);
            }
        }
        catch { }
    }

    
    public static void ForceForegroundWindow(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd)) return;
        if (NativeMethods.IsIconic(hWnd))
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_RESTORE);
        }
        else
        {
            NativeMethods.ShowWindow(hWnd, NativeMethods.SW_SHOW);
        }

        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg != hWnd)
        {
            uint fgThread = NativeMethods.GetWindowThreadProcessId(fg, out _);
            uint curThread = NativeMethods.GetCurrentThreadId();
            if (fgThread != curThread && fgThread != 0)
            {
                NativeMethods.AttachThreadInput(curThread, fgThread, true);
                NativeMethods.SetForegroundWindow(hWnd);
                NativeMethods.AttachThreadInput(curThread, fgThread, false);
            }
            else
            {
                NativeMethods.SetForegroundWindow(hWnd);
            }
        }
    }

    
private void OnCardEyeClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement elem && elem.DataContext is WindowInfo win)
        {
            ToggleGhost(win);
            e.Handled = true;
        }
    }

    
    private void SetSelfDisplayAffinity(bool enable)
    {
        if (_selfHwnd != IntPtr.Zero)
        {
            uint aff = enable ? NativeMethods.WDA_EXCLUDEFROMCAPTURE : NativeMethods.WDA_NONE;
            NativeMethods.SetWindowDisplayAffinity(_selfHwnd, aff);
        }
    }

    
    private void OnSelfHideToggleClick(object sender, RoutedEventArgs e)
    {
        bool enable = SelfHideSwitch.IsChecked == true;
        SetSelfDisplayAffinity(enable);

        var selfWin = _allWindows.FirstOrDefault(w => w.IsSelf);
        if (selfWin != null)
        {
            selfWin.IsGhosted = enable;
            selfWin.StatusText = enable ? "Oculta" : "Visible";
        }
        ApplyTaskbarOption(_selfHwnd, enable);
        UpdateStatusInfo();
        UpdateActionButtons();
    }

    
    private void InitMonitorInfo()
    {
        try
        {
            var p = WinForms.Screen.PrimaryScreen;
            string res = p != null ? $"{p.Bounds.Width} × {p.Bounds.Height}" : "2560 × 1600";
            MonitorInfoText.Text = res;
        }
        catch
        {
            MonitorInfoText.Text = "2560 × 1600";
        }
    }

    
    private void StartLivePreviewLoop()
    {
        _livePreviewCts?.Cancel();
        _livePreviewCts = new CancellationTokenSource();
        var token = _livePreviewCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested && !_isExiting)
            {
                try
                {
                    await Task.Delay(800, token);
                    if (token.IsCancellationRequested) break;

                    bool shouldCapture = false;
                    List<WindowInfo> toUpdate = new();

                    Dispatcher.Invoke(() =>
                    {
                        shouldCapture = _currentTab == 1 && IsVisible && WindowState != WindowState.Minimized;
                        if (shouldCapture)
                        {
                            toUpdate = _displayedWindows.Where(w => !w.IsMinimized).ToList();
                        }
                    });

                    if (shouldCapture && toUpdate.Count > 0)
                    {
                        foreach (var win in toUpdate)
                        {
                            if (token.IsCancellationRequested || _isExiting) break;
                            if (!NativeMethods.IsWindow(win.Handle) || NativeMethods.IsIconic(win.Handle)) continue;

                            var thumb = ThumbnailService.CaptureThumbnail(win.Handle);
                            if (thumb != null && !token.IsCancellationRequested)
                            {
                                _ = Dispatcher.InvokeAsync(() => win.Thumbnail = thumb, DispatcherPriority.Background);
                            }
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        }, token);
    }

    
        private string GetChromePath()
    {
        if (!string.IsNullOrEmpty(_chromePath) && File.Exists(_chromePath))
            return _chromePath;

        string[] paths = {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Google\Chrome\Application\chrome.exe"),
            Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Google\Chrome\Application\chrome.exe")
        };
        _chromePath = paths.FirstOrDefault(File.Exists) ?? paths[0];
        return _chromePath;
    }

    private string GetEdgePath()
    {
        if (!string.IsNullOrEmpty(_edgePath) && File.Exists(_edgePath))
            return _edgePath;

        string[] paths = {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe")
        };
        _edgePath = paths.FirstOrDefault(File.Exists) ?? paths[0];
        return _edgePath;
    }

    private string GetBravePath()
    {
        if (!string.IsNullOrEmpty(_bravePath) && File.Exists(_bravePath))
            return _bravePath;

        string[] paths = {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe"),
            Environment.ExpandEnvironmentVariables(@"%LocalAppData%\BraveSoftware\Brave-Browser\Application\brave.exe")
        };
        _bravePath = paths.FirstOrDefault(File.Exists) ?? paths[0];
        return _bravePath;
    }

    private string GetOperaPath()
    {
        if (!string.IsNullOrEmpty(_operaPath) && File.Exists(_operaPath))
            return _operaPath;

        string[] paths = {
            Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Programs\Opera\launcher.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Opera\launcher.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Opera\launcher.exe"),
            Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Programs\Opera GX\launcher.exe")
        };
        _operaPath = paths.FirstOrDefault(File.Exists) ?? paths[0];
        return _operaPath;
    }

    private string GetFirefoxPath()
    {
        if (!string.IsNullOrEmpty(_firefoxPath) && File.Exists(_firefoxPath))
            return _firefoxPath;

        string[] paths = {
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Mozilla Firefox\firefox.exe"),
            Environment.ExpandEnvironmentVariables(@"%ProgramFiles(x86)%\Mozilla Firefox\firefox.exe"),
            Environment.ExpandEnvironmentVariables(@"%LocalAppData%\Mozilla Firefox\firefox.exe")
        };
        _firefoxPath = paths.FirstOrDefault(File.Exists) ?? paths[0];
        return _firefoxPath;
    }

    private void DetectDefaultBrowser()
    {
        string path = GetChromePath();
        if (ChipBoxChrome != null)
        {
            SelectBrowserChipByKey("chrome", "Google Chrome", path);
        }
    }

    private void SetChipBoxState(Border? box, TextBlock? text, Border? divider, System.Windows.Shapes.Path? editIcon, bool isSelected)
    {
        if (box == null) return;

        var activeBg = (Brush)new BrushConverter().ConvertFromString("#ebf2fe")!;
        var activeBlue = (Brush)FindResource("AccentBlueBrush");
        var activeDivider = (Brush)new BrushConverter().ConvertFromString("#c2dafe")!;

        var inactiveBg = (Brush)FindResource("SurfaceSubtle");
        var inactiveBorder = (Brush)FindResource("CardBorderBrush");
        var inactiveDivider = (Brush)FindResource("DividerLineBrush");
        var textPrimary = (Brush)FindResource("TextPrimaryBrush");
        var textMuted = (Brush)FindResource("TextMutedBrush");

        if (isSelected)
        {
            box.Background = activeBg;
            box.BorderBrush = activeBlue;
            box.BorderThickness = new Thickness(1.5);
            if (text != null)
            {
                text.Foreground = activeBlue;
                text.FontWeight = FontWeights.SemiBold;
            }
            if (divider != null)
            {
                divider.Background = activeDivider;
            }
            if (editIcon != null)
            {
                editIcon.Stroke = activeBlue;
            }
        }
        else
        {
            box.Background = inactiveBg;
            box.BorderBrush = inactiveBorder;
            box.BorderThickness = new Thickness(1);
            if (text != null)
            {
                text.Foreground = textPrimary;
                text.FontWeight = FontWeights.Normal;
            }
            if (divider != null)
            {
                divider.Background = inactiveDivider;
            }
            if (editIcon != null)
            {
                editIcon.Stroke = textMuted;
            }
        }
    }

    private void ClearBrowserChips()
    {
        SetChipBoxState(ChipBoxChrome, ChipChromeText, ChipChromeDivider, ChipChromeEditIcon, false);
        SetChipBoxState(ChipBoxEdge, ChipEdgeText, ChipEdgeDivider, ChipEdgeEditIcon, false);
        SetChipBoxState(ChipBoxBrave, ChipBraveText, ChipBraveDivider, ChipBraveEditIcon, false);
        SetChipBoxState(ChipBoxOpera, ChipOperaText, ChipOperaDivider, ChipOperaEditIcon, false);
        SetChipBoxState(ChipBoxFirefox, ChipFirefoxText, ChipFirefoxDivider, ChipFirefoxEditIcon, false);

        if (ChipCustom != null)
        {
            ChipCustom.Style = (Style)FindResource("SecondaryPillBtn");
            ChipCustom.Tag = null;
        }
    }

    private void SelectBrowserChipByKey(string key, string name, string path)
    {
        ClearBrowserChips();
        _selectedBrowserKey = key;
        _selectedBrowserName = name;
        _selectedBrowserPath = path;

        switch (key.ToLowerInvariant())
        {
            case "chrome":
                SetChipBoxState(ChipBoxChrome, ChipChromeText, ChipChromeDivider, ChipChromeEditIcon, true);
                break;
            case "msedge":
                SetChipBoxState(ChipBoxEdge, ChipEdgeText, ChipEdgeDivider, ChipEdgeEditIcon, true);
                break;
            case "brave":
                SetChipBoxState(ChipBoxBrave, ChipBraveText, ChipBraveDivider, ChipBraveEditIcon, true);
                break;
            case "opera":
                SetChipBoxState(ChipBoxOpera, ChipOperaText, ChipOperaDivider, ChipOperaEditIcon, true);
                break;
            case "firefox":
                SetChipBoxState(ChipBoxFirefox, ChipFirefoxText, ChipFirefoxDivider, ChipFirefoxEditIcon, true);
                break;
            case "custom":
                if (ChipCustom != null && FindResource("BrowserChipActiveStyle") is Style activeStyle)
                {
                    ChipCustom.Style = activeStyle;
                    ChipCustom.Tag = "Selected";
                }
                break;
        }

        if (LaunchBrowserBtn != null)
        {
            LaunchBrowserBtn.Content = $"Iniciar {name}";
        }
        UpdateLiveDiagnosticStatus();
    }

    private void ToggleBrowserChipByKey(string key, string name, string path)
    {
        if (_selectedBrowserKey == key)
        {
            ClearBrowserChips();
            _selectedBrowserKey = null;
            _selectedBrowserName = null;
            _selectedBrowserPath = null;
            if (LaunchBrowserBtn != null)
            {
                LaunchBrowserBtn.Content = "Iniciar protegido";
            }
            UpdateLiveDiagnosticStatus();
        }
        else
        {
            SelectBrowserChipByKey(key, name, path);
        }
    }

    private void SelectBrowserChip(Button? chip, string key, string name, string path)
    {
        SelectBrowserChipByKey(key, name, path);
    }

    private void ToggleBrowserChip(Button? chip, string key, string name, string path)
    {
        ToggleBrowserChipByKey(key, name, path);
    }

    private void OnSelectBrowserChrome(object sender, RoutedEventArgs e)
    {
        ToggleBrowserChipByKey("chrome", "Google Chrome", GetChromePath());
    }

    private void OnSelectBrowserEdge(object sender, RoutedEventArgs e)
    {
        ToggleBrowserChipByKey("msedge", "Microsoft Edge", GetEdgePath());
    }

    private void OnSelectBrowserBrave(object sender, RoutedEventArgs e)
    {
        ToggleBrowserChipByKey("brave", "Brave", GetBravePath());
    }

    private void OnSelectBrowserOpera(object sender, RoutedEventArgs e)
    {
        ToggleBrowserChipByKey("opera", "Opera", GetOperaPath());
    }

    private void OnSelectBrowserFirefox(object sender, RoutedEventArgs e)
    {
        ToggleBrowserChipByKey("firefox", "Firefox", GetFirefoxPath());
    }

    private void OnSelectBrowserCustom(object sender, RoutedEventArgs e)
    {
        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Navegadores y Ejecutables (*.exe)|*.exe|Todos los archivos (*.*)|*.*",
            Title = "Seleccionar navegador o aplicación"
        };
        if (ofd.ShowDialog() == true)
        {
            string name = System.IO.Path.GetFileNameWithoutExtension(ofd.FileName);
            ToggleBrowserChipByKey("custom", name, ofd.FileName);
        }
    }

    private void PromptEditBrowserPath(string browserKey, string browserName, ref string? currentPath, Action onSelected)
    {
        string current = currentPath ?? string.Empty;
        string initialDir = !string.IsNullOrEmpty(current) && File.Exists(current)
            ? System.IO.Path.GetDirectoryName(current) ?? Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        var ofd = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Ejecutables (*.exe)|*.exe|Todos los archivos (*.*)|*.*",
            Title = $"Seleccionar ruta del ejecutable de {browserName}",
            FileName = !string.IsNullOrEmpty(current) ? System.IO.Path.GetFileName(current) : $"{browserName}.exe",
            InitialDirectory = initialDir
        };

        if (ofd.ShowDialog() == true)
        {
            currentPath = ofd.FileName;
            onSelected();
        }
    }

    private void OnEditBrowserPathChrome(object sender, RoutedEventArgs e)
    {
        PromptEditBrowserPath("chrome", "Google Chrome", ref _chromePath, () =>
        {
            if (ChipChromeEdit != null)
                ChipChromeEdit.ToolTip = $"Ruta: {_chromePath}";
            SelectBrowserChipByKey("chrome", "Google Chrome", _chromePath!);
        });
    }

    private void OnEditBrowserPathEdge(object sender, RoutedEventArgs e)
    {
        PromptEditBrowserPath("msedge", "Microsoft Edge", ref _edgePath, () =>
        {
            if (ChipEdgeEdit != null)
                ChipEdgeEdit.ToolTip = $"Ruta: {_edgePath}";
            SelectBrowserChipByKey("msedge", "Microsoft Edge", _edgePath!);
        });
    }

    private void OnEditBrowserPathBrave(object sender, RoutedEventArgs e)
    {
        PromptEditBrowserPath("brave", "Brave", ref _bravePath, () =>
        {
            if (ChipBraveEdit != null)
                ChipBraveEdit.ToolTip = $"Ruta: {_bravePath}";
            SelectBrowserChipByKey("brave", "Brave", _bravePath!);
        });
    }

    private void OnEditBrowserPathOpera(object sender, RoutedEventArgs e)
    {
        PromptEditBrowserPath("opera", "Opera", ref _operaPath, () =>
        {
            if (ChipOperaEdit != null)
                ChipOperaEdit.ToolTip = $"Ruta: {_operaPath}";
            SelectBrowserChipByKey("opera", "Opera", _operaPath!);
        });
    }

    private void OnEditBrowserPathFirefox(object sender, RoutedEventArgs e)
    {
        PromptEditBrowserPath("firefox", "Firefox", ref _firefoxPath, () =>
        {
            if (ChipFirefoxEdit != null)
                ChipFirefoxEdit.ToolTip = $"Ruta: {_firefoxPath}";
            SelectBrowserChipByKey("firefox", "Firefox", _firefoxPath!);
        });
    }

    
        private void OnToggleDiagnosticClick(object sender, RoutedEventArgs e)
    {
        if (DiagnosticRow.Visibility == Visibility.Visible)
        {
            DiagnosticRow.Visibility = Visibility.Collapsed;
        }
        else
        {
            DiagnosticRow.Visibility = Visibility.Visible;
            UpdateLiveDiagnosticStatus();
        }
    }

    private void OnCollapseDiagnosticClick(object sender, RoutedEventArgs e)
    {
        DiagnosticRow.Visibility = Visibility.Collapsed;
    }

    private static string GetProcessNameForBrowserKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key)) return "chrome";
        return key.ToLowerInvariant() switch
        {
            "msedge" or "edge" => "msedge",
            "brave" => "brave",
            "opera" => "opera",
            "firefox" => "firefox",
            _ => key.ToLowerInvariant()
        };
    }

    private void UpdateLiveDiagnosticStatus()
    {
        if (DiagnosticRow == null || DiagnosticRow.Visibility != Visibility.Visible)
            return;

        string targetKey = _selectedBrowserKey ?? _activeProtectedBrowserKey ?? "chrome";
        string expectedProcName = GetProcessNameForBrowserKey(targetKey);

        bool isBrowserActive = false;

        if (_protectedBrowserHwnd != IntPtr.Zero && NativeMethods.IsWindow(_protectedBrowserHwnd))
        {
            isBrowserActive = true;
        }

        if (!isBrowserActive && _protectedBrowserProc != null)
        {
            try
            {
                if (!_protectedBrowserProc.HasExited)
                {
                    isBrowserActive = true;
                }
            }
            catch { }
        }

        if (!isBrowserActive)
        {
            var ghostedWin = _allWindows.FirstOrDefault(w =>
                w.IsGhosted &&
                !string.IsNullOrEmpty(w.ProcessName) &&
                (w.ProcessName.Equals(expectedProcName, StringComparison.OrdinalIgnoreCase) ||
                 w.ProcessName.Contains(expectedProcName, StringComparison.OrdinalIgnoreCase)) &&
                NativeMethods.IsWindow(w.Handle));
            if (ghostedWin != null)
            {
                isBrowserActive = true;
                _protectedBrowserHwnd = ghostedWin.Handle;
            }
        }

        if (Environment.GetCommandLineArgs().Any(a => a.Equals("--test-diag-on", StringComparison.OrdinalIgnoreCase)))
        {
            isBrowserActive = true;
        }

        var blueBrush = (Brush)FindResource("AccentBlueBrush");
        var mutedBrush = (Brush)FindResource("TextMutedBrush");

        if (isBrowserActive)
        {
            DiagExtendedIndicator.Background = blueBrush;
            DiagExtendedSub.Text = "Screen.prototype neutralizado";

            DiagScreensIndicator.Background = blueBrush;
            DiagScreensSub.Text = "Arreglo screens bloqueado en 1";

            DiagOffsetsIndicator.Background = blueBrush;
            DiagOffsetsSub.Text = "availLeft / availTop: 0 (fijos)";

            DiagWebdriverIndicator.Background = blueBrush;
            DiagWebdriverSub.Text = "AutomationControlled oculto";
        }
        else
        {
            DiagExtendedIndicator.Background = mutedBrush;
            DiagExtendedSub.Text = "Inicia con 'Iniciar protegido'";

            DiagScreensIndicator.Background = mutedBrush;
            DiagScreensSub.Text = "Sin sesión activa";

            DiagOffsetsIndicator.Background = mutedBrush;
            DiagOffsetsSub.Text = "Sin monitor activo";

            DiagWebdriverIndicator.Background = mutedBrush;
            DiagWebdriverSub.Text = "Sin proceso protegido";
        }
    }

    private void OnLaunchProtectedBrowserClick(object sender, RoutedEventArgs e)
    {
        LaunchProtectedBrowser(null);
    }

    private void LaunchProtectedBrowser(string? targetUrl)
    {
        if (string.IsNullOrWhiteSpace(_selectedBrowserPath) || !File.Exists(_selectedBrowserPath))
        {
            MessageBox.Show("El ejecutable del navegador seleccionado no se encuentra en el equipo. Usa 'Examinar...' para seleccionarlo.",
                "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        string browserKey = _selectedBrowserKey ?? "chrome";
        _activeProtectedBrowserKey = browserKey;
        _protectedBrowserHwnd = IntPtr.Zero;

        ScreenSpoofAdapter.StartDiagnosticServer();

        string args = ScreenSpoofAdapter.BuildLaunchArguments(_selectedBrowserPath, targetUrl);
        bool hasExtension = ScreenSpoofAdapter.IsAvailable && ScreenSpoofAdapter.IsChromiumBrowser(_selectedBrowserPath);

        try
        {
            var proc = Process.Start(new ProcessStartInfo(_selectedBrowserPath, args) { UseShellExecute = true });
            _protectedBrowserProc = proc;
            DiagnosticRow.Visibility = Visibility.Visible;

            string expectedProcName = GetProcessNameForBrowserKey(browserKey);

            Task.Run(async () =>
            {
                IntPtr detectedHwnd = IntPtr.Zero;
                uint detectedPid = 0;

                for (int i = 0; i < 40; i++)
                {
                    await Task.Delay(150);

                    if (proc != null)
                    {
                        try
                        {
                            proc.Refresh();
                            if (proc.MainWindowHandle != IntPtr.Zero && NativeMethods.IsWindow(proc.MainWindowHandle))
                            {
                                detectedHwnd = proc.MainWindowHandle;
                                detectedPid = (uint)proc.Id;
                            }
                        }
                        catch { }
                    }

                    if (detectedHwnd == IntPtr.Zero)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            var win = _allWindows.FirstOrDefault(w =>
                                !string.IsNullOrEmpty(w.ProcessName) &&
                                (w.ProcessName.Equals(expectedProcName, StringComparison.OrdinalIgnoreCase) ||
                                 w.ProcessName.Contains(expectedProcName, StringComparison.OrdinalIgnoreCase)) &&
                                NativeMethods.IsWindow(w.Handle));
                            if (win != null)
                            {
                                detectedHwnd = win.Handle;
                                detectedPid = win.ProcessId;
                            }
                        });
                    }

                    if (detectedHwnd != IntPtr.Zero)
                    {
                        _protectedBrowserHwnd = detectedHwnd;
                        Dispatcher.Invoke(() =>
                        {
                            NativeMethods.SetWindowDisplayAffinity(detectedHwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                            if (detectedPid != 0)
                            {
                                Injector.SetAffinityRemote(detectedPid, detectedHwnd, NativeMethods.WDA_EXCLUDEFROMCAPTURE);
                            }
                            var winObj = _allWindows.FirstOrDefault(w => w.Handle == detectedHwnd);
                            if (winObj != null)
                            {
                                winObj.IsGhosted = true;
                            }
                            RefreshWindows();
                            UpdateLiveDiagnosticStatus();
                        });
                        break;
                    }
                }

                Dispatcher.Invoke(() =>
                {
                    UpdateLiveDiagnosticStatus();
                });
            });

            string msg = hasExtension
                ? $"Navegador {_selectedBrowserName} iniciado con aislamiento de pantalla única (ScreenSpoof cargado y flags de monitor único aplicados)."
                : $"Navegador {_selectedBrowserName} iniciado con aislamiento de monitor único.";

            if (string.IsNullOrEmpty(targetUrl))
            {
                MessageBox.Show(msg, "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error al iniciar el navegador: {ex.Message}", "CastDecoy", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
