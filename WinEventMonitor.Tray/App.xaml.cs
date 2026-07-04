using System.Drawing;
using System.IO;
using System.ServiceProcess;
using System.Windows;
using System.Windows.Forms;
using Serilog;
using Application = System.Windows.Application;

namespace WinEventMonitor.Tray;

public partial class App : Application
{
    private NotifyIcon? _tray;
    private MainWindow? _window;
    private static Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Instancia única: si ya hay una copia abierta, activa su ventana y sale
        _mutex = new Mutex(true, "Global\\WinEventMonitor-Tray", out bool isNew);
        if (!isNew)
        {
            Shutdown();
            return;
        }

        // Log en AppData\Local (escribible por usuario sin admin)
        var logDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WinEventMonitor", "logs");
        Directory.CreateDirectory(logDir);
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(logDir, "tray-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
            Log.Fatal(ex.ExceptionObject as Exception, "Excepcion no manejada");
        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error(ex.Exception, "Excepcion no manejada en Dispatcher");
            ex.Handled = true;
        };

        Log.Information("Tray iniciado");

        _tray = new NotifyIcon
        {
            Icon   = CreateIcon(),
            Visible = true,
            Text   = "Windows Event Monitor"
        };

        // Menú contextual
        var menu = new ContextMenuStrip();

        var openItem = (ToolStripMenuItem)menu.Items.Add("Abrir interfaz");
        openItem.Font  = new Font(menu.Font, System.Drawing.FontStyle.Bold); // acción por defecto
        openItem.Click += (_, _) => ShowWindow();

        menu.Items.Add("Estado del servicio").Click += (_, _) => ShowServiceStatus();
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Salir").Click += (_, _) => ExitApp();

        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick      += (_, _) => ShowWindow();

        // Al arrancar, abre la ventana directamente
        ShowWindow();
    }

    private void ShowWindow()
    {
        if (_window == null || !_window.IsLoaded)
        {
            _window = new MainWindow();
            _window.Closed += (_, _) => _window = null;
        }
        _window.Show();
        _window.WindowState = WindowState.Normal;
        _window.Activate();
    }

    private void ShowServiceStatus()
    {
        string status;
        try
        {
            using var sc = new ServiceController("WinEventMonitor");
            status = sc.Status switch
            {
                ServiceControllerStatus.Running      => "✅  En ejecución",
                ServiceControllerStatus.Stopped      => "⛔  Detenido",
                ServiceControllerStatus.StartPending => "⏳  Iniciando…",
                ServiceControllerStatus.StopPending  => "⏳  Deteniendo…",
                _                                    => sc.Status.ToString()
            };
        }
        catch
        {
            status = "⚠️  Servicio no instalado\n(ejecuta en modo consola como administrador)";
        }

        System.Windows.MessageBox.Show(
            $"WinEventMonitor\n\n{status}",
            "Estado del servicio",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void ExitApp()
    {
        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        Log.Information("Tray cerrado");
        Log.CloseAndFlush();
        _mutex?.ReleaseMutex();
        Shutdown();
    }

    /// <summary>Genera un icono de bandeja de 32×32 px en tiempo de ejecución (sin fichero .ico).</summary>
    private static Icon CreateIcon()
    {
        var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Transparent);
            // Círculo azul oscuro
            g.FillEllipse(
                new SolidBrush(Color.FromArgb(30, 64, 175)),
                1, 1, 30, 30);
            // Letra "W" en blanco
            using var font = new Font("Arial", 13f, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("W", font, System.Drawing.Brushes.White, 4f, 8f);
        }
        return Icon.FromHandle(bmp.GetHicon());
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
