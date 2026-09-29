using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;

namespace CastDecoySetup;

public partial class MainWindow : Window
{
    private readonly string _installDir;

    public MainWindow()
    {
        InitializeComponent();
        _installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "CastDecoy");
        DestinationPathText.Text = _installDir;
        CheckExistingInstallation();
    }

    private void CheckExistingInstallation()
    {
        string targetExe = Path.Combine(_installDir, "CastDecoy.exe");
        if (File.Exists(targetExe))
        {
            AlreadyInstalledBanner.Visibility = Visibility.Visible;
            UninstallBtn.Visibility = Visibility.Visible;
            InstallBtn.Content = "Reinstalar";
            StatusText.Text = "Listo para reinstalar o desinstalar";
        }
        else
        {
            AlreadyInstalledBanner.Visibility = Visibility.Collapsed;
            UninstallBtn.Visibility = Visibility.Collapsed;
            InstallBtn.Content = "Instalar ahora";
            StatusText.Text = "Listo para instalar";
        }
    }

    private async void OnUninstallClick(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(
            "¿Estás seguro de que deseas desinstalar CastDecoy por completo de este equipo?",
            "Desinstalar CastDecoy",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm != MessageBoxResult.Yes) return;

        InstallBtn.IsEnabled = false;
        UninstallBtn.IsEnabled = false;
        DesktopShortcutCheck.IsEnabled = false;
        StartMenuShortcutCheck.IsEnabled = false;
        LaunchAfterCheck.IsEnabled = false;

        try
        {
            StatusText.Text = "Cerrando procesos de CastDecoy...";
            InstallProgressBar.Value = 20;

            await Task.Run(() =>
            {
                foreach (var p in Process.GetProcessesByName("CastDecoy"))
                {
                    try { p.Kill(); p.WaitForExit(2000); } catch { }
                }
            });

            StatusText.Text = "Eliminando accesos directos y registros de Windows...";
            InstallProgressBar.Value = 55;

            await Task.Run(() =>
            {
                string desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "CastDecoy.lnk");
                if (File.Exists(desktopLnk))
                {
                    try { File.Delete(desktopLnk); } catch { }
                }

                string startMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\CastDecoy");
                if (Directory.Exists(startMenuFolder))
                {
                    try { Directory.Delete(startMenuFolder, true); } catch { }
                }

                try
                {
                    Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CastDecoy", false);
                }
                catch { }
            });

            StatusText.Text = "Eliminando archivos del programa...";
            InstallProgressBar.Value = 85;

            await Task.Run(() =>
            {
                if (Directory.Exists(_installDir))
                {
                    try
                    {
                        Directory.Delete(_installDir, true);
                    }
                    catch
                    {
                        foreach (var file in Directory.GetFiles(_installDir, "*.*", SearchOption.AllDirectories))
                        {
                            try { File.Delete(file); } catch { }
                        }
                    }
                }
            });

            InstallProgressBar.Value = 100;
            StatusText.Text = "¡Desinstalación completada con éxito!";

            MessageBox.Show(
                "CastDecoy ha sido desinstalado correctamente de tu equipo.",
                "Desinstalación Completa",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            CheckExistingInstallation();
            InstallProgressBar.Value = 0;
            InstallBtn.IsEnabled = true;
            DesktopShortcutCheck.IsEnabled = true;
            StartMenuShortcutCheck.IsEnabled = true;
            LaunchAfterCheck.IsEnabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocurrió un error al desinstalar:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            CheckExistingInstallation();
            InstallBtn.IsEnabled = true;
            UninstallBtn.IsEnabled = true;
            DesktopShortcutCheck.IsEnabled = true;
            StartMenuShortcutCheck.IsEnabled = true;
            LaunchAfterCheck.IsEnabled = true;
        }
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        InstallBtn.IsEnabled = false;
        UninstallBtn.IsEnabled = false;
        DesktopShortcutCheck.IsEnabled = false;
        StartMenuShortcutCheck.IsEnabled = false;
        LaunchAfterCheck.IsEnabled = false;

        try
        {
            StatusText.Text = "Cerrando instancias previas de CastDecoy...";
            InstallProgressBar.Value = 15;
            await Task.Run(() =>
            {
                foreach (var p in Process.GetProcessesByName("CastDecoy"))
                {
                    try { p.Kill(); p.WaitForExit(2000); } catch { }
                }
            });

            StatusText.Text = "Extrayendo archivos de la aplicación...";
            InstallProgressBar.Value = 40;

            await Task.Run(() =>
            {
                if (!Directory.Exists(_installDir))
                    Directory.CreateDirectory(_installDir);

                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("CastDecoyPackage.zip");
                if (stream != null)
                {
                    using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
                    archive.ExtractToDirectory(_installDir, overwriteFiles: true);
                }
                else
                {
                    // Fallback to local files if run in development
                    string devSource = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\bin\publish\win-x64"));
                    if (Directory.Exists(devSource))
                    {
                        foreach (string dirPath in Directory.GetDirectories(devSource, "*", SearchOption.AllDirectories))
                            Directory.CreateDirectory(dirPath.Replace(devSource, _installDir));
                        foreach (string newPath in Directory.GetFiles(devSource, "*.*", SearchOption.AllDirectories))
                            File.Copy(newPath, newPath.Replace(devSource, _installDir), true);
                    }
                }

                string uninstallerCmd = Path.Combine(_installDir, "Uninstall.cmd");
                string uninstallScript = @"@echo off
setlocal
title Desinstalador de CastDecoy
echo Cerrando CastDecoy...
taskkill /F /IM CastDecoy.exe 2>nul
echo Eliminando accesos directos...
del /F /Q ""%APPDATA%\Microsoft\Windows\Start Menu\Programs\CastDecoy\CastDecoy.lnk"" 2>nul
del /F /Q ""%USERPROFILE%\Desktop\CastDecoy.lnk"" 2>nul
rmdir /S /Q ""%APPDATA%\Microsoft\Windows\Start Menu\Programs\CastDecoy"" 2>nul
echo Eliminando entradas de registro...
reg delete ""HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\CastDecoy"" /f 2>nul
echo Desinstalacion completada exitosamente.
echo Puede eliminar esta carpeta si lo desea.
pause
";
                File.WriteAllText(uninstallerCmd, uninstallScript);
            });

            StatusText.Text = "Creando accesos directos y registro en Windows...";
            InstallProgressBar.Value = 75;

            bool makeDesktop = DesktopShortcutCheck.IsChecked == true;
            bool makeStartMenu = StartMenuShortcutCheck.IsChecked == true;

            await Task.Run(() =>
            {
                string targetExe = Path.Combine(_installDir, "CastDecoy.exe");
                Type? shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    dynamic? shell = Activator.CreateInstance(shellType);
                    if (shell != null)
                    {
                        if (makeStartMenu)
                        {
                            string startMenuFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs\CastDecoy");
                            if (!Directory.Exists(startMenuFolder))
                                Directory.CreateDirectory(startMenuFolder);

                            dynamic shortcut = shell.CreateShortcut(Path.Combine(startMenuFolder, "CastDecoy.lnk"));
                            shortcut.TargetPath = targetExe;
                            shortcut.WorkingDirectory = _installDir;
                            shortcut.Description = "CastDecoy - Suite de Aislamiento de Pantalla y Señuelos";
                            shortcut.IconLocation = targetExe + ",0";
                            shortcut.Save();
                        }

                        if (makeDesktop)
                        {
                            string desktopPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "CastDecoy.lnk");
                            dynamic shortcut = shell.CreateShortcut(desktopPath);
                            shortcut.TargetPath = targetExe;
                            shortcut.WorkingDirectory = _installDir;
                            shortcut.Description = "CastDecoy";
                            shortcut.IconLocation = targetExe + ",0";
                            shortcut.Save();
                        }
                    }
                }

                using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\CastDecoy");
                if (key != null)
                {
                    key.SetValue("DisplayName", "CastDecoy");
                    key.SetValue("DisplayVersion", "1.0.0");
                    key.SetValue("Publisher", "CastDecoy Project");
                    key.SetValue("DisplayIcon", Path.Combine(_installDir, "CastDecoy.exe"));
                    key.SetValue("InstallLocation", _installDir);
                    key.SetValue("UninstallString", $"\"{Path.Combine(_installDir, "Uninstall.cmd")}\"");
                    key.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    key.SetValue("EstimatedSize", 78000, RegistryValueKind.DWord);
                }
            });

            InstallProgressBar.Value = 100;
            StatusText.Text = "¡Instalación completada con éxito!";

            if (LaunchAfterCheck.IsChecked == true)
            {
                string targetExe = Path.Combine(_installDir, "CastDecoy.exe");
                if (File.Exists(targetExe))
                {
                    Process.Start(new ProcessStartInfo(targetExe) { WorkingDirectory = _installDir });
                }
            }

            await Task.Delay(800);
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ocurrió un error durante la instalación:\n{ex.Message}", "Error de Instalación", MessageBoxButton.OK, MessageBoxImage.Error);
            CheckExistingInstallation();
            InstallBtn.IsEnabled = true;
            DesktopShortcutCheck.IsEnabled = true;
            StartMenuShortcutCheck.IsEnabled = true;
            LaunchAfterCheck.IsEnabled = true;
            StatusText.Text = "Error al instalar. Intente nuevamente.";
        }
    }
}
