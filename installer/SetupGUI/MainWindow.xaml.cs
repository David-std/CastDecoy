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
    }

    private async void OnInstallClick(object sender, RoutedEventArgs e)
    {
        InstallBtn.IsEnabled = false;
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

                // Generar Uninstall.cmd en la carpeta instalada
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

                // Registro en Windows "Aplicaciones instaladas"
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
            InstallBtn.IsEnabled = true;
            StatusText.Text = "Error al instalar. Intente nuevamente.";
        }
    }
}
