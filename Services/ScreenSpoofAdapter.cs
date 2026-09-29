using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace CastDecoy.Services;

/// <summary>
/// Adaptador para la extensión y políticas de aislamiento de pantalla ScreenSpoof.
/// Proporciona los argumentos de línea de comandos necesarios para simular
/// un entorno de monitor único para pruebas de aislamiento y desarrollo web,
/// y alojar el servidor local de diagnóstico.
/// </summary>
public static class ScreenSpoofAdapter
{
    private static string? _cachedExtensionPath;
    private static HttpListener? _diagnosticServer;
    private const int DiagnosticPort = 28923;

    /// <summary>
    /// URL local para la prueba de diagnóstico de ScreenSpoof.
    /// </summary>
    public static string DiagnosticUrl => $"http://127.0.0.1:{DiagnosticPort}/test_screenspoof.html";

    /// <summary>
    /// Obtiene la ruta al directorio con el paquete de extensión ScreenSpoof.
    /// </summary>
    public static string? GetExtensionDirectory()
    {
        if (_cachedExtensionPath != null && Directory.Exists(_cachedExtensionPath))
            return _cachedExtensionPath;

        string appDir = AppDomain.CurrentDomain.BaseDirectory;
        string[] candidatePaths =
        [
            Path.Combine(appDir, "ScreenSpoof"),
            Path.Combine(Directory.GetCurrentDirectory(), "ScreenSpoof"),
            Path.GetFullPath(Path.Combine(appDir, @"..\..\..\ScreenSpoof")),
            Path.Combine(appDir, "Assets", "ScreenSpoof")
        ];

        foreach (var path in candidatePaths)
        {
            if (Directory.Exists(path) &&
                File.Exists(Path.Combine(path, "manifest.json")) &&
                File.Exists(Path.Combine(path, "inject.js")))
            {
                _cachedExtensionPath = path;
                return path;
            }
        }

        return null;
    }

    /// <summary>
    /// Indica si el paquete de extensión de ScreenSpoof está disponible localmente.
    /// </summary>
    public static bool IsAvailable => GetExtensionDirectory() != null;

    /// <summary>
    /// Determina si el navegador especificado es compatible con extensiones Chromium y flags de pantalla.
    /// </summary>
    public static bool IsChromiumBrowser(string browserPath)
    {
        if (string.IsNullOrWhiteSpace(browserPath)) return false;
        string exeName = Path.GetFileName(browserPath).ToLowerInvariant();
        return exeName.Contains("chrome") ||
               exeName.Contains("msedge") ||
               exeName.Contains("brave") ||
               exeName.Contains("opera") ||
               exeName.Contains("vivaldi");
    }

    /// <summary>
    /// Construye los argumentos de inicio para el navegador con aislamiento de pantalla única y configuración de pruebas.
    /// </summary>
    public static string BuildLaunchArguments(string browserPath, string? targetUrl = null)
    {
        string profileDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CastDecoy", "BrowserProfile");
        try
        {
            if (!Directory.Exists(profileDir))
                Directory.CreateDirectory(profileDir);
        }
        catch { }

        var flags = new List<string>();

        if (IsChromiumBrowser(browserPath))
        {
            // Flags para cargar extensiones locales desempaquetadas para pruebas
            flags.Add("--enable-automation");
            flags.Add("--disable-infobars");
            flags.Add("--disable-blink-features=AutomationControlled");

            // Deshabilitar APIs de detección de pantalla múltiple y limitar a 1 monitor
            flags.Add("--disable-features=WindowPlacement,MultiScreen,MultiScreenWindowPlacement");
            flags.Add("--screen-count=1");
            flags.Add($"--user-data-dir=\"{profileDir}\"");
            flags.Add("--no-first-run");
            flags.Add("--no-default-browser-check");
            flags.Add("--allow-file-access-from-files");

            string? extDir = GetExtensionDirectory();
            if (!string.IsNullOrEmpty(extDir))
            {
                flags.Add($"--load-extension=\"{extDir}\"");
                flags.Add($"--disable-extensions-except=\"{extDir}\"");
            }
        }
        else
        {
            // Navegadores no-Chromium (ej. Firefox)
            flags.Add("--disable-features=WindowPlacement,MultiScreen,MultiScreenWindowPlacement");
            flags.Add("--screen-count=1");
        }

        if (!string.IsNullOrWhiteSpace(targetUrl))
        {
            flags.Add($"\"{targetUrl}\"");
        }

        return string.Join(" ", flags);
    }

    /// <summary>
    /// Inicia el servidor HTTP local para servir el archivo de diagnóstico ScreenSpoof
    /// permitiendo que la extensión inyecte sus scripts sobre HTTP sin problemas de permisos de archivo.
    /// </summary>
    public static void StartDiagnosticServer()
    {
        if (_diagnosticServer != null && _diagnosticServer.IsListening)
            return;

        try
        {
            _diagnosticServer = new HttpListener();
            _diagnosticServer.Prefixes.Add($"http://127.0.0.1:{DiagnosticPort}/");
            _diagnosticServer.Start();

            Task.Run(async () =>
            {
                while (_diagnosticServer != null && _diagnosticServer.IsListening)
                {
                    try
                    {
                        var context = await _diagnosticServer.GetContextAsync();
                        ProcessDiagnosticRequest(context);
                    }
                    catch (HttpListenerException)
                    {
                        break;
                    }
                    catch { }
                }
            });
        }
        catch { }
    }

    private static void ProcessDiagnosticRequest(HttpListenerContext context)
    {
        try
        {
            string urlPath = context.Request.Url?.AbsolutePath.TrimStart('/') ?? "";
            if (string.IsNullOrEmpty(urlPath)) urlPath = "test_screenspoof.html";

            string? extDir = GetExtensionDirectory();
            string filePath = extDir != null ? Path.Combine(extDir, urlPath) : "";

            if (!File.Exists(filePath))
            {
                string assetsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
                string assetFile = Path.Combine(assetsDir, urlPath.Replace("Assets/", ""));
                if (File.Exists(assetFile))
                {
                    filePath = assetFile;
                }
                else
                {
                    context.Response.StatusCode = 404;
                    context.Response.Close();
                    return;
                }
            }

            byte[] bytes = File.ReadAllBytes(filePath);
            string contentType = urlPath.EndsWith(".html", StringComparison.OrdinalIgnoreCase)
                ? "text/html; charset=utf-8"
                : urlPath.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
                    ? "application/javascript; charset=utf-8"
                    : urlPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                        ? "application/json; charset=utf-8"
                        : urlPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                            ? "image/png"
                            : urlPath.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)
                                ? "font/ttf"
                                : urlPath.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
                                    ? "image/x-icon"
                                    : "text/plain; charset=utf-8";

            context.Response.ContentType = contentType;
            context.Response.ContentLength64 = bytes.Length;
            context.Response.Headers.Add("Access-Control-Allow-Origin", "*");
            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
            context.Response.OutputStream.Close();
        }
        catch
        {
            try { context.Response.Close(); } catch { }
        }
    }
}
