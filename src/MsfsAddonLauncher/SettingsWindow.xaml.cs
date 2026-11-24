using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using MsfsAddonLauncher.Models;
using MsfsAddonLauncher.Services;

namespace MsfsAddonLauncher;

public partial class SettingsWindow : Window
{
    private readonly AppConfig _cfg;

    /// <summary>Schreibt erst bei "Speichern" in die uebergebene Konfiguration.</summary>
    public SettingsWindow(AppConfig cfg)
    {
        InitializeComponent();
        _cfg = cfg;
        var sim = cfg.Sim;
        var v = cfg.Verhalten;

        VarianteBox.SelectedIndex = (int)sim.Variante;
        StoreIdBox.Text = sim.StoreAppId;
        SteamIdBox.Text = sim.SteamAppId;
        EigenPfadBox.Text = sim.EigenerPfad;
        EigenArgsBox.Text = sim.EigeneArgumente;
        SimStartenBox.IsChecked = sim.SimStarten;
        SimProzessBox.Text = string.Join("; ", sim.ProzessNamen);
        MaxSimBox.Text = sim.MaxWartenAufSimSek.ToString();
        SimConnectBox.IsChecked = sim.SimConnectPruefen;
        MaxSimConnectBox.Text = sim.MaxWartenAufSimConnectSek.ToString();
        FestBox.Text = sim.WartezeitNachSimStartSek.ToString();

        SchliessenBox.SelectedIndex = v.ProgrammeBeiSimEnde switch
        {
            SchliessenModus.Automatisch => 0,
            SchliessenModus.Nachfragen => 1,
            _ => 2
        };
        TimeoutBox.Text = v.SchliessenTimeoutSek.ToString();
        HartBox.IsChecked = v.HartBeenden;
        LauncherBeendenBox.IsChecked = v.LauncherNachSimEndeBeenden;
        TrayBox.IsChecked = v.TrayIcon;
        TrayMinBox.IsChecked = v.InTrayMinimieren;
        NachStartMinBox.IsChecked = v.NachStartMinimieren;
        IntervallBox.Text = v.StatusIntervallSek.ToString();

        SichtbarkeitAnpassen();
    }

    private void SichtbarkeitAnpassen()
    {
        // Nur die Felder zeigen, die zur gewaehlten Variante gehoeren.
        var variante = (SimVariante)Math.Max(0, VarianteBox.SelectedIndex);
        StoreZeile.Visibility = variante == SimVariante.Store ? Visibility.Visible : Visibility.Collapsed;
        SteamZeile.Visibility = variante == SimVariante.Steam ? Visibility.Visible : Visibility.Collapsed;
        EigenZeile.Visibility = variante == SimVariante.Benutzerdefiniert ? Visibility.Visible : Visibility.Collapsed;

        var simConnect = SimConnectBox.IsChecked == true;
        SimConnectZeile.Visibility = simConnect ? Visibility.Visible : Visibility.Collapsed;
        FestZeile.Visibility = simConnect ? Visibility.Collapsed : Visibility.Visible;

        TrayMinBox.IsEnabled = TrayBox.IsChecked == true;
    }

    private void VarianteBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IsInitialized && StoreZeile != null) SichtbarkeitAnpassen();
    }

    private void SimConnectBox_Click(object sender, RoutedEventArgs e) => SichtbarkeitAnpassen();
    private void TrayBox_Click(object sender, RoutedEventArgs e) => SichtbarkeitAnpassen();

    private void Erkennen_Click(object sender, RoutedEventArgs e)
    {
        var probe = new SimSettings { SteamAppId = SteamIdBox.Text.Trim() };
        var variante = SimService.Erkennen(probe, out var beschreibung);
        ErkennungText.Text = beschreibung + ".";
        ErkennungText.Visibility = Visibility.Visible;
        if (variante == null) return;

        VarianteBox.SelectedIndex = (int)variante.Value;
        var familie = SimService.StoreFamilienName();
        if (variante == SimVariante.Store && familie != null) StoreIdBox.Text = familie + "!App";
    }

    private void EigenDurchsuchen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Sim-Programm oder Verknüpfung auswählen",
            Filter = "Programme und Verknüpfungen|*.exe;*.lnk;*.bat;*.cmd;*.url|Alle Dateien|*.*",
            // Hier die Verknuepfung selbst uebernehmen: sie kann Startparameter enthalten.
            DereferenceLinks = false
        };
        if (dialog.ShowDialog(this) == true) EigenPfadBox.Text = dialog.FileName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (!Zahl(MaxSimBox, 30, 3600, "Höchstens warten auf Sim", out var maxSim)) return;
        if (!Zahl(MaxSimConnectBox, 0, 3600, "Höchstens warten auf SimConnect", out var maxSimConnect)) return;
        if (!Zahl(FestBox, 0, 3600, "Feste Wartezeit", out var fest)) return;
        if (!Zahl(TimeoutBox, 1, 120, "Zeit zum Selbst-Beenden", out var timeout)) return;
        if (!Zahl(IntervallBox, 1, 30, "Status prüfen alle", out var intervall)) return;

        var variante = (SimVariante)Math.Max(0, VarianteBox.SelectedIndex);
        var storeId = StoreIdBox.Text.Trim();
        var steamId = SteamIdBox.Text.Trim();
        var prozesse = ProcessTools.NamenAufteilen(SimProzessBox.Text).ToList();

        if (variante == SimVariante.Store && !storeId.Contains('!'))
        {
            Meldung("Die Store-App-ID ist unvollständig. Erwartet wird etwas wie\nMicrosoft.Limitless_8wekyb3d8bbwe!App");
            return;
        }
        if (variante == SimVariante.Steam && (steamId.Length == 0 || !steamId.All(char.IsDigit)))
        {
            Meldung("Die Steam-AppID muss eine Zahl sein.");
            return;
        }
        if (variante == SimVariante.Benutzerdefiniert && SimStartenBox.IsChecked == true &&
            EigenPfadBox.Text.Trim().Length == 0)
        {
            Meldung("Bitte den eigenen Sim-Pfad eintragen oder „Sim automatisch starten“ ausschalten.");
            return;
        }
        if (prozesse.Count == 0)
        {
            Meldung("Bitte mindestens einen Prozessnamen für den Sim eintragen (Standard: FlightSimulator2024).");
            return;
        }

        var sim = _cfg.Sim;
        sim.Variante = variante;
        sim.StoreAppId = storeId;
        sim.SteamAppId = steamId;
        sim.EigenerPfad = EigenPfadBox.Text.Trim().Trim('"');
        sim.EigeneArgumente = EigenArgsBox.Text.Trim();
        sim.SimStarten = SimStartenBox.IsChecked == true;
        sim.ProzessNamen = prozesse;
        sim.MaxWartenAufSimSek = maxSim;
        sim.SimConnectPruefen = SimConnectBox.IsChecked == true;
        sim.MaxWartenAufSimConnectSek = maxSimConnect;
        sim.WartezeitNachSimStartSek = fest;

        var v = _cfg.Verhalten;
        v.ProgrammeBeiSimEnde = SchliessenBox.SelectedIndex switch
        {
            0 => SchliessenModus.Automatisch,
            1 => SchliessenModus.Nachfragen,
            _ => SchliessenModus.Nie
        };
        v.SchliessenTimeoutSek = timeout;
        v.HartBeenden = HartBox.IsChecked == true;
        v.LauncherNachSimEndeBeenden = LauncherBeendenBox.IsChecked == true;
        v.TrayIcon = TrayBox.IsChecked == true;
        v.InTrayMinimieren = TrayMinBox.IsChecked == true;
        v.NachStartMinimieren = NachStartMinBox.IsChecked == true;
        v.StatusIntervallSek = intervall;

        DialogResult = true;
    }

    private bool Zahl(TextBox feld, int min, int max, string name, out int wert)
    {
        if (int.TryParse(feld.Text.Trim(), out wert) && wert >= min && wert <= max) return true;
        Meldung($"„{name}“ muss eine ganze Zahl zwischen {min} und {max} sein.");
        feld.Focus();
        return false;
    }

    private void Meldung(string text) =>
        MessageBox.Show(this, text, Title, MessageBoxButton.OK, MessageBoxImage.Information);
}
