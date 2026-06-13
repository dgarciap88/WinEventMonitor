using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

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
            // Carpeta de datos de usuario de WebView2 en AppData (sin escritura en Program Files)
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WinEventMonitor", "WebView2");

            var env = await CoreWebView2Environment.CreateAsync(
                browserExecutableFolder: null,
                userDataFolder: userDataFolder);

            await WebView.EnsureCoreWebView2Async(env);

            // Interceptar peticiones /api/* para inyectar el header X-Api-Key
            // (el usuario nunca ve ni introduce la clave)
            WebView.CoreWebView2.AddWebResourceRequestedFilter(
                $"http://localhost:{_port}/api/*",
                CoreWebView2WebResourceContext.All);

            WebView.CoreWebView2.WebResourceRequested += OnWebResourceRequested;
            WebView.CoreWebView2.NavigationCompleted  += OnNavigationCompleted;

            Navigate();
        }
        catch (Exception ex)
        {
            ShowError($"Error al inicializar WebView2:\n{ex.Message}\n\n" +
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
            WebView.Visibility    = Visibility.Visible;
            ErrorPanel.Visibility = Visibility.Collapsed;
        }
        else if (args.WebErrorStatus == CoreWebView2WebErrorStatus.CannotConnect ||
                 args.WebErrorStatus == CoreWebView2WebErrorStatus.ServerUnreachable ||
                 args.WebErrorStatus == CoreWebView2WebErrorStatus.Disconnected)
        {
            ShowError(
                $"No se puede conectar con el servicio en http://localhost:{_port}\n\n" +
                "Comprueba que el servicio WinEventMonitor está en ejecución:\n" +
                "Panel de Control → Servicios → Windows Event Monitor");
        }
        else
        {
            ShowError($"Error de navegación: {args.WebErrorStatus}");
        }
    }

    // ── Botón Reintentar ─────────────────────────────────────────────────────

    private void RetryButton_Click(object sender, RoutedEventArgs e) => Navigate();

    private void Navigate()
    {
        WebView.Visibility    = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        WebView.CoreWebView2?.Navigate($"http://localhost:{_port}");
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
