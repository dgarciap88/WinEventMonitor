using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Serilog;

namespace WinEventMonitor.Tray;

public partial class MainWindow : Window
{
    private string _apiKey = "";
    private int    _port   = 51847;

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

            // Interceptar peticiones /api/* para inyectar el header X-Api-Key
            WebView.CoreWebView2.AddWebResourceRequestedFilter(
                $"http://127.0.0.1:{_port}/api/*",
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

    // ── Inyección automática de la API Key ───────────────────────────────────

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs args)
    {
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
        // Navega a /index.html directamente (evita el redirect de UseDefaultFiles
        // que pasa por ApiKeyMiddleware y devuelve 401)
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
