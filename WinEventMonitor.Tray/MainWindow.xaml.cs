using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace WinEventMonitor.Tray;

public partial class MainWindow : Window
{
    private string _apiKey    = "";
    private int    _port      = 51847;
    private string _wwwroot   = "";   // ruta local a wwwroot del Tray

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    // ── Al cargar la ventana, inicializar WebView2 ───────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _port   = ReadPort();
        _apiKey = ReadApiKey();

        try
        {
            Log.Information("Inicializando WebView2 en puerto {Port}", _port);
            Log.Information("API Key presente: {HasKey}", !string.IsNullOrEmpty(_apiKey));

            // WebView2 necesita una carpeta de datos escribible por su proceso hijo (Medium IL).
            // - ProgramData: creada por el admin, el proceso hijo de Edge no puede escribir → falla
            // - Program Files: claramente solo lectura → falla
            // - LocalApplicationData (C:\Users\...\AppData\Local): el proceso hijo
            //   corre como el usuario actual y SÍ puede escribir aquí aunque el padre sea admin.
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinEventMonitor", "WebView2");
            Directory.CreateDirectory(userDataFolder);
            Log.Debug("WebView2 user data folder: {Folder}", userDataFolder);

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await WebView.EnsureCoreWebView2Async(env);

            _wwwroot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
            Log.Debug(_wwwroot != "" && Directory.Exists(_wwwroot)
                ? "wwwroot local encontrado: {Path}" : "wwwroot no encontrado, usando servicio", _wwwroot);

            // Interceptar TODOS los requests al servicio.
            // - Ficheros estáticos: servirlos desde el wwwroot local del Tray (siempre actualizados).
            // - Llamadas /api/*: inyectar el header X-Api-Key.
            // Al navegar a http://127.0.0.1:{_port}/ el origen es el servicio → sin CORS.
            WebView.CoreWebView2.AddWebResourceRequestedFilter(
                $"http://127.0.0.1:{_port}/*",
                CoreWebView2WebResourceContext.All);

            WebView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            WebView.CoreWebView2.NavigationCompleted  += OnNavigationCompleted;

            Navigate();
        }
        catch (Exception ex)
        {            Log.Error(ex, "Error al inicializar WebView2");            ShowError($"Error al inicializar WebView2:\n{ex.Message}\n\n" +
                      "Asegúrate de tener instalado el runtime de Microsoft Edge WebView2.");
        }
    }

    // ── Interceptar ficheros estáticos + inyección de API Key ─────────────────

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
        var uri = new Uri(args.Request.Uri);
        var path = uri.AbsolutePath;

        if (!path.StartsWith("/api") && Directory.Exists(_wwwroot))
        {
            // Fichero estático: servir desde wwwroot local del Tray
            var filePath = path == "/" || path == "/index.html"
                ? Path.Combine(_wwwroot, "index.html")
                : Path.Combine(_wwwroot, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

            if (File.Exists(filePath))
            {
                var ext = Path.GetExtension(filePath).ToLower();
                var mime = ext switch {
                    ".html" => "text/html",
                    ".js"   => "application/javascript",
                    ".css"  => "text/css",
                    ".svg"  => "image/svg+xml",
                    ".png"  => "image/png",
                    _       => "application/octet-stream"
                };
                var stream = File.OpenRead(filePath);
                args.Response = WebView.CoreWebView2.Environment.CreateWebResourceResponse(
                    stream, 200, "OK", $"Content-Type: {mime}");
                return;
            }
        }

        // Petición a /api/*: inyectar API Key
        if (!string.IsNullOrEmpty(_apiKey))
            args.Request.Headers.SetHeader("X-Api-Key", _apiKey);
    }

    // ── Detectar si el servicio no responde ──────────────────────────────────

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (args.IsSuccess)
        {
            Log.Debug("Navegacion correcta a http://localhost:{Port}", _port);
            WebView.Visibility    = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
        else if (args.WebErrorStatus == CoreWebView2WebErrorStatus.CannotConnect ||
                 args.WebErrorStatus == CoreWebView2WebErrorStatus.ServerUnreachable ||
                 args.WebErrorStatus == CoreWebView2WebErrorStatus.Disconnected ||
                 args.WebErrorStatus == CoreWebView2WebErrorStatus.Unknown)
        {
            Log.Warning("Servicio no disponible en puerto {Port} — WebErrorStatus: {Status}", _port, args.WebErrorStatus);
            ShowError(
                $"No se puede conectar con el servicio en http://localhost:{_port}\n\n" +
                "Comprueba que el servicio WinEventMonitor est\u00e1 en ejecuci\u00f3n:\n" +
                "Panel de Control \u2192 Servicios \u2192 Windows Event Monitor");
        }
        else
        {
            Log.Error("Error de navegacion: {Status}", args.WebErrorStatus);
            ShowError($"Error de navegación: {args.WebErrorStatus}");
        }
    }

    // ── Botón Reintentar ─────────────────────────────────────────────────────

    private void RetryButton_Click(object sender, RoutedEventArgs e) => Navigate();

    private void Navigate()
    {
        WebView.Visibility    = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        WebView.CoreWebView2?.Navigate($"http://127.0.0.1:{_port}/index.html");
    }

    private void ShowError(string message)
    {
        ErrorDetail.Text      = message;
        ErrorPanel.Visibility = Visibility.Visible;
        WebView.Visibility    = Visibility.Collapsed;
    }

    // ── Minimizar a bandeja al cerrar (no terminar) ──────────────────────────

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        e.Cancel = true; // No cerrar; ocultar
        Hide();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Lee el puerto de appsettings.json en el mismo directorio que el exe.
    /// En producción, ese fichero es el del servicio (mismo directorio de instalación).
    /// </summary>
    private static int ReadPort()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
            if (!File.Exists(path)) return 51847;

            var json = JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
            if (json.TryGetProperty("EventMonitor", out var em) &&
                em.TryGetProperty("Port", out var portEl))
                return portEl.GetInt32();
        }
        catch { /* fallback */ }
        return 51847;
    }

    /// <summary>
    /// Lee la API Key generada por el servicio de ProgramData.
    /// El servicio la crea al arrancar; el Tray la inyecta en cada petición.
    /// </summary>
    private static string ReadApiKey()
    {
        try
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "WinEventMonitor", "api.key");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : "";
        }
        catch { return ""; }
    }
}
