using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MsfsAddonLauncher.Models;
using MsfsAddonLauncher.Services;

namespace MsfsAddonLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Die exe.xml des Sims ist oft als Windows-1252 gespeichert; das kennt .NET erst nach dieser Zeile.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        if (!SingleInstance.Belegen())
        {
            // Der Launcher laeuft schon: Auftrag weiterreichen und beenden.
            var args = e.Args.Length > 0 ? e.Args : new[] { "--zeigen" };
            if (!SingleInstance.AnErsteInstanzSenden(args))
            {
                MessageBox.Show("Der MSFS Addon Launcher läuft bereits, reagiert aber nicht.",
                    "MSFS Addon Launcher", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            Shutdown();
            return;
        }

        Log.Start(ConfigStore.BasisOrdner);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Fehler("Unerwarteter Fehler: " + args.Exception);
            MessageBox.Show("Es ist ein unerwarteter Fehler aufgetreten:\n\n" + args.Exception.Message +
                            "\n\nDetails stehen im Log.", "MSFS Addon Launcher",
                MessageBoxButton.OK, MessageBoxImage.Error);
            args.Handled = true;
        };

        var cfg = ConfigStore.Laden(out var istNeu, out var ladeFehler);
        var fenster = new MainWindow(cfg, istNeu, ladeFehler);
        MainWindow = fenster;

        SingleInstance.ArgumenteEmpfangen += args =>
            Dispatcher.BeginInvoke(() => fenster.ArgumenteVerarbeiten(args, vonZweiterInstanz: true));

        var imTray = e.Args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase)) && cfg.Verhalten.TrayIcon;
        if (!imTray) fenster.Show();
        fenster.ArgumenteVerarbeiten(e.Args, vonZweiterInstanz: false);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SingleInstance.Freigeben();
        base.OnExit(e);
    }
}

/// <summary>Farbe des Statuspunkts in der Programmliste.</summary>
public sealed class StatusFarbe : IValueConverter
{
    private static readonly Brush Gruen = Fest(0x3F, 0xC9, 0x6B);
    private static readonly Brush Gelb = Fest(0xE8, 0xB3, 0x39);
    private static readonly Brush Rot = Fest(0xE8, 0x5D, 0x5D);
    private static readonly Brush Grau = Fest(0x7A, 0x7F, 0x87);
    private static readonly Brush Dunkel = Fest(0x4A, 0x4E, 0x55);

    private static Brush Fest(byte r, byte g, byte b)
    {
        var pinsel = new SolidColorBrush(Color.FromRgb(r, g, b));
        pinsel.Freeze();
        return pinsel;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ProgrammStatus s
            ? s switch
            {
                ProgrammStatus.Laeuft => Gruen,
                ProgrammStatus.Startet or ProgrammStatus.Wartet => Gelb,
                ProgrammStatus.Fehler or ProgrammStatus.PfadFehlt => Rot,
                ProgrammStatus.Deaktiviert => Dunkel,
                _ => Grau
            }
            : Grau;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
