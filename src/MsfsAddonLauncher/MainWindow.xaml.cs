using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MsfsAddonLauncher.Models;
using MsfsAddonLauncher.Services;

namespace MsfsAddonLauncher;

public partial class MainWindow : Window
{
    private const int MaxLogZeilen = 3000;

    private readonly AppConfig _cfg;
    private readonly bool _ersteinrichtung;
    private readonly Orchestrator _ablauf = new();
    private readonly ObservableCollection<AddonProgram> _programme;
    private readonly ObservableCollection<Profil> _profile;
    private readonly DispatcherTimer _timer = new();

    private TrayIcon? _tray;
    private bool _statusLaeuft;
    private bool _profilWechselIntern;
    private bool _beendenBestaetigt;
    private int _logZeilen;

    private static readonly Brush SimAn = new SolidColorBrush(Color.FromRgb(0x3F, 0xC9, 0x6B));
    private static readonly Brush SimAus = new SolidColorBrush(Color.FromRgb(0x7A, 0x7F, 0x87));

    public MainWindow(AppConfig cfg, bool ersteinrichtung, string? ladeFehler)
    {
        InitializeComponent();
        _cfg = cfg;
        _ersteinrichtung = ersteinrichtung;
        _programme = new ObservableCollection<AddonProgram>(cfg.Programme);
        _profile = new ObservableCollection<Profil>(cfg.Profile);

        Liste.ItemsSource = _programme;
        ProfilBox.ItemsSource = _profile;
        PfadText.Text = ConfigStore.ConfigPfad;

        Log.ZeileHinzugefuegt += zeile => Dispatcher.BeginInvoke(() => LogAnhaengen(zeile));

        var version = typeof(MainWindow).Assembly.GetName().Version;
        // Die Version steht im Fenstertitel, damit Tester sagen koennen, welchen Stand sie haben.
        Title = $"MSFS Addon Launcher {version?.ToString(3)}";
        Log.Info($"MSFS Addon Launcher {version?.ToString(3)} gestartet. Konfiguration: {ConfigStore.ConfigPfad}");
        if (ladeFehler != null) Log.Fehler(ladeFehler);
        if (ProcessTools.LauncherIstAdmin())
            Log.Warnung("Der Launcher läuft mit Adminrechten. Dadurch starten ALLE Programme als Admin. " +
                        "Besser normal starten und Adminrechte nur pro Programm einschalten.");

        _ablauf.ZustandGeaendert += ZustandAnzeigen;
        _ablauf.Nachfrage = frage =>
        {
            Zeigen();
            return MessageBox.Show(this, frage, "Sim beendet", MessageBoxButton.YesNo, MessageBoxImage.Question)
                   == MessageBoxResult.Yes;
        };
        _ablauf.SimEndeAbgeschlossen += SimEndeAbgeschlossen;

        // Profil auswaehlen, ohne gleich wieder zu speichern.
        _profilWechselIntern = true;
        ProfilBox.SelectedItem = _profile.FirstOrDefault(p =>
            string.Equals(p.Name, cfg.AktivesProfil, StringComparison.OrdinalIgnoreCase)) ?? _profile[0];
        _profilWechselIntern = false;
        ProfilHakenSetzen();

        TrayAnpassen();

        _timer.Interval = TimeSpan.FromSeconds(cfg.Verhalten.StatusIntervallSek);
        _timer.Tick += async (_, _) => await StatusPruefenAsync();
        _timer.Start();

        Loaded += async (_, _) =>
        {
            await StatusPruefenAsync();
            if (_ersteinrichtung) await ErsteinrichtungAsync();
        };
    }

    private Profil AktuellesProfil => ProfilBox.SelectedItem as Profil ?? _profile[0];
    private AddonProgram? Auswahl => Liste.SelectedItem as AddonProgram;

    // ---------- Speichern ----------

    private void Speichern()
    {
        _cfg.Programme = _programme.ToList();
        _cfg.Profile = _profile.ToList();
        _cfg.AktivesProfil = AktuellesProfil.Name;
        try
        {
            ConfigStore.Speichern(_cfg);
        }
        catch (Exception ex)
        {
            Log.Fehler("Die config.json konnte nicht gespeichert werden: " + ex.Message);
        }
    }

    // ---------- Protokoll ----------

    private void LogAnhaengen(string zeile)
    {
        if (_logZeilen >= MaxLogZeilen)
        {
            // Nur die Anzeige kuerzen; die Logdatei bleibt vollstaendig.
            var text = LogBox.Text;
            var schnitt = text.Length / 2;
            var umbruch = text.IndexOf('\n', schnitt);
            LogBox.Text = umbruch >= 0 ? text[(umbruch + 1)..] : "";
            _logZeilen = MaxLogZeilen / 2;
        }
        LogBox.AppendText(zeile + Environment.NewLine);
        _logZeilen++;
        LogBox.ScrollToEnd();
    }

    private void LogLeeren_Click(object sender, RoutedEventArgs e)
    {
        LogBox.Clear();
        _logZeilen = 0;
    }

    // ---------- Status ----------

    private async Task StatusPruefenAsync()
    {
        if (_statusLaeuft) return; // vorige Pruefung laeuft noch
        _statusLaeuft = true;
        try
        {
            await Orchestrator.StatusAktualisierenAsync(_programme);
            var sim = _cfg.Sim;
            var simLaeuft = await Task.Run(() => SimService.Laeuft(sim));
            SimPunkt.Fill = simLaeuft ? SimAn : SimAus;
            SimText.Text = simLaeuft ? "Sim läuft" : "Sim läuft nicht";
        }
        catch (Exception ex)
        {
            Log.Fehler("Statusprüfung fehlgeschlagen: " + ex.Message);
        }
        finally
        {
            _statusLaeuft = false;
        }
    }

    private void ZustandAnzeigen()
    {
        ZustandText.Text = _ablauf.ZustandText;
        StartKnopf.IsEnabled = _ablauf.Zustand == LaufZustand.Bereit;
        AbbrechenKnopf.IsEnabled = _ablauf.Aktiv;
        _tray?.SetzeText("MSFS Addon Launcher – " + _ablauf.ZustandText);
    }

    private void SimEndeAbgeschlossen()
    {
        if (_cfg.Verhalten.LauncherNachSimEndeBeenden)
        {
            Log.Info("Launcher beendet sich (Einstellung: nach Sim-Ende beenden).");
            _beendenBestaetigt = true;
            // Erst nach dem Ende des Ablaufs schliessen, sonst fragt das Fenster nach.
            Dispatcher.BeginInvoke(Close, DispatcherPriority.ApplicationIdle);
        }
    }

    // ---------- Ersteinrichtung ----------

    private async Task ErsteinrichtungAsync()
    {
        var variante = SimService.Erkennen(_cfg.Sim, out var beschreibung);
        if (variante != null)
        {
            _cfg.Sim.Variante = variante.Value;
            var familie = SimService.StoreFamilienName();
            if (variante == SimVariante.Store && familie != null) _cfg.Sim.StoreAppId = familie + "!App";
            Log.Info("Ersteinrichtung: " + beschreibung + ".");
        }
        else
        {
            Log.Warnung("Ersteinrichtung: " + beschreibung + ". Bitte in den Einstellungen die Sim-Variante prüfen.");
        }
        Speichern();
        await AutoErkennungAsync(erststart: true, simInfo: beschreibung);
    }

    private async Task AutoErkennungAsync(bool erststart, string? simInfo = null)
    {
        _cfg.Programme = _programme.ToList();
        AutoDetect.Ergebnis ergebnis;
        Cursor = Cursors.Wait;
        try
        {
            var cfg = _cfg;
            ergebnis = await ShortcutTools.AufStaThread(() => AutoDetect.Suchen(cfg));
        }
        catch (Exception ex)
        {
            Log.Fehler("Auto-Erkennung fehlgeschlagen: " + ex.Message);
            return;
        }
        finally
        {
            Cursor = null;
        }

        Log.Info($"Auto-Erkennung: {ergebnis.Vorschlaege.Count(v => v.Bekannt)} Flugsim-Programm(e) gefunden, " +
                 $"{ergebnis.VomSimGestartet} davon startet der Sim schon selbst, {ergebnis.BereitsVorhanden} stehen bereits in der Liste.");

        if (!IsVisible) Zeigen();
        var dialog = new DetectWindow(ergebnis, erststart, simInfo) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var profil = AktuellesProfil;
        var nurAtc = _profile.FirstOrDefault(p => p.Name.Equals("Nur ATC", StringComparison.OrdinalIgnoreCase));
        // "vor Sim" zuerst, damit die Liste die Startreihenfolge zeigt.
        foreach (var v in dialog.Ausgewaehlt.OrderBy(v => v.Start))
        {
            var p = v.AlsProgramm();
            p.ImProfil = true;
            _programme.Add(p);
            if (!profil.Programme.Contains(p.Id)) profil.Programme.Add(p.Id);
            // Bei der Ersteinrichtung landen ATC-Programme zusaetzlich im Profil "Nur ATC".
            if (erststart && v.Kategorie == "ATC" && nurAtc != null && nurAtc != profil)
                nurAtc.Programme.Add(p.Id);
            Log.Info($"Übernommen: {p.Name} ({p.Pfad})");
        }
        Speichern();
        await StatusPruefenAsync();
    }

    // ---------- Profile ----------

    private void ProfilHakenSetzen()
    {
        var ids = AktuellesProfil.Programme;
        foreach (var p in _programme) p.ImProfil = ids.Contains(p.Id);
    }

    private void ProfilBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_profilWechselIntern || ProfilBox.SelectedItem == null) return;
        ProfilHakenSetzen();
        Speichern();
    }

    private void ImProfil_Click(object sender, RoutedEventArgs e)
    {
        // Reihenfolge der Ids folgt der Liste, damit die Datei lesbar bleibt.
        AktuellesProfil.Programme = _programme.Where(p => p.ImProfil).Select(p => p.Id).ToList();
        Speichern();
    }

    private void ProfilNeu_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new InputDialog("Neues Profil", "Name des neuen Profils:", "") { Owner = this };
        if (dialog.ShowDialog() != true) return;
        var name = dialog.Wert;
        if (!ProfilNameFrei(name, null)) return;

        var neu = new Profil { Name = name, Programme = AktuellesProfil.Programme.ToList() };
        _profile.Add(neu);
        ProfilBox.SelectedItem = neu;
        Log.Info($"Profil \"{name}\" angelegt (Auswahl vom vorigen Profil übernommen).");
    }

    private void ProfilUmbenennen_Click(object sender, RoutedEventArgs e)
    {
        var profil = AktuellesProfil;
        var dialog = new InputDialog("Profil umbenennen", "Neuer Name:", profil.Name) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        if (!ProfilNameFrei(dialog.Wert, profil)) return;

        profil.Name = dialog.Wert;
        // Die Auswahlbox zeigt den neuen Namen erst, wenn der Eintrag neu eingesetzt wird.
        _profilWechselIntern = true;
        var index = _profile.IndexOf(profil);
        _profile.RemoveAt(index);
        _profile.Insert(index, profil);
        ProfilBox.SelectedItem = profil;
        _profilWechselIntern = false;
        Speichern();
    }

    private void ProfilLoeschen_Click(object sender, RoutedEventArgs e)
    {
        if (_profile.Count <= 1)
        {
            MessageBox.Show(this, "Das letzte Profil kann nicht gelöscht werden.", "Profil löschen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var profil = AktuellesProfil;
        if (MessageBox.Show(this, $"Profil \"{profil.Name}\" löschen?\n\nDie Programme selbst bleiben erhalten.",
                "Profil löschen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _profilWechselIntern = true;
        _profile.Remove(profil);
        ProfilBox.SelectedItem = _profile[0];
        _profilWechselIntern = false;
        ProfilHakenSetzen();
        Speichern();
    }

    private bool ProfilNameFrei(string name, Profil? ausser)
    {
        if (name.Contains('"'))
        {
            // Der Name landet in der Desktop-Verknuepfung zwischen Anfuehrungszeichen.
            MessageBox.Show(this, "Der Profilname darf keine Anführungszeichen enthalten.", "Profil",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        if (_profile.Any(p => p != ausser && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
        {
            MessageBox.Show(this, $"Ein Profil namens \"{name}\" gibt es schon.", "Profil",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return false;
        }
        return true;
    }

    // ---------- Programmliste ----------

    private void Liste_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var p = Auswahl;
        var index = p == null ? -1 : _programme.IndexOf(p);
        BearbeitenKnopf.IsEnabled = EntfernenKnopf.IsEnabled = p != null;
        EinzelStartKnopf.IsEnabled = EinzelStoppKnopf.IsEnabled = p != null;
        HochKnopf.IsEnabled = index > 0;
        RunterKnopf.IsEnabled = index >= 0 && index < _programme.Count - 1;
    }

    private void Liste_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        // Doppelklick auf das Haekchen soll nicht den Bearbeiten-Dialog oeffnen.
        if (e.OriginalSource is DependencyObject quelle && FindeEltern<CheckBox>(quelle) != null) return;
        if (Auswahl != null) Bearbeiten_Click(sender, e);
    }

    private static T? FindeEltern<T>(DependencyObject? element) where T : DependencyObject
    {
        while (element != null)
        {
            if (element is T treffer) return treffer;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return null;
    }

    private void Hinzufuegen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ProgramEditWindow(new AddonProgram(), neu: true) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        var p = dialog.Programm;
        p.ImProfil = true;
        _programme.Add(p);
        AktuellesProfil.Programme.Add(p.Id);
        Liste.SelectedItem = p;
        Speichern();
        Log.Info($"Programm hinzugefügt: {p.Name}");
    }

    private void Bearbeiten_Click(object sender, RoutedEventArgs e)
    {
        var p = Auswahl;
        if (p == null) return;
        var dialog = new ProgramEditWindow(p.Kopie(), neu: false) { Owner = this };
        if (dialog.ShowDialog() != true) return;

        p.UebernehmeVon(dialog.Programm);
        p.NichtErkanntGemeldet = false;
        Speichern();
    }

    private void Entfernen_Click(object sender, RoutedEventArgs e)
    {
        var p = Auswahl;
        if (p == null) return;
        if (MessageBox.Show(this, $"\"{p.Name}\" aus dem Launcher entfernen?\n\nDas Programm selbst wird nicht deinstalliert.",
                "Programm entfernen", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

        _programme.Remove(p);
        foreach (var profil in _profile) profil.Programme.Remove(p.Id);
        Speichern();
    }

    private void Hoch_Click(object sender, RoutedEventArgs e) => Verschieben(-1);
    private void Runter_Click(object sender, RoutedEventArgs e) => Verschieben(1);

    private void Verschieben(int richtung)
    {
        var p = Auswahl;
        if (p == null) return;
        var alt = _programme.IndexOf(p);
        var neu = alt + richtung;
        if (neu < 0 || neu >= _programme.Count) return;
        _programme.Move(alt, neu);
        Liste.SelectedItem = p;
        Liste_SelectionChanged(Liste, null!);
        Speichern();
    }

    private async void EinzelStart_Click(object sender, RoutedEventArgs e)
    {
        var p = Auswahl;
        if (p != null) await _ablauf.StarteAsync(p);
    }

    private async void EinzelStopp_Click(object sender, RoutedEventArgs e)
    {
        var p = Auswahl;
        if (p != null) await _ablauf.SchliessenAsync(new[] { p }, _cfg.Verhalten);
    }

    // ---------- Starten / Abbrechen ----------

    private void AllesStarten_Click(object sender, RoutedEventArgs e) => ProfilStarten(AktuellesProfil);

    private void ProfilStarten(Profil profil)
    {
        if (_ablauf.Aktiv)
        {
            Log.Warnung("Es läuft bereits ein Ablauf. Erst abbrechen oder das Sim-Ende abwarten.");
            return;
        }
        if (ProfilBox.SelectedItem != profil) ProfilBox.SelectedItem = profil;
        Speichern();
        _ablauf.Starten(_cfg, profil);
        if (_cfg.Verhalten.NachStartMinimieren && IsVisible) WindowState = WindowState.Minimized;
    }

    private void Abbrechen_Click(object sender, RoutedEventArgs e) => _ablauf.Abbrechen();

    private async void AlleSchliessen_Click(object sender, RoutedEventArgs e)
    {
        var liste = _programme.Where(p => p.ImProfil && p.BeiSimEndeSchliessen).ToList();
        if (liste.Count == 0)
        {
            MessageBox.Show(this, "In diesem Profil ist kein Programm zum Schließen markiert.", "Programme schließen",
                MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var namen = string.Join("\n", liste.Select(p => "• " + p.Name));
        if (MessageBox.Show(this, "Diese Programme jetzt schließen?\n\n" + namen, "Programme schließen",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await _ablauf.SchliessenAsync(liste, _cfg.Verhalten);
    }

    // ---------- Weitere Knoepfe ----------

    private async void AutoErkennung_Click(object sender, RoutedEventArgs e) => await AutoErkennungAsync(erststart: false);

    private void Einstellungen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(_cfg) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        Speichern();
        _timer.Interval = TimeSpan.FromSeconds(_cfg.Verhalten.StatusIntervallSek);
        TrayAnpassen();
        Log.Info("Einstellungen gespeichert.");
    }

    private void Mehr_Click(object sender, RoutedEventArgs e)
    {
        if (MehrKnopf.ContextMenu == null) return;
        MehrKnopf.ContextMenu.PlacementTarget = MehrKnopf;
        MehrKnopf.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        MehrKnopf.ContextMenu.IsOpen = true;
    }

    private void Verknuepfung_Click(object sender, RoutedEventArgs e)
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        var profil = AktuellesProfil;
        var dateiName = string.Concat($"MSFS starten ({profil.Name})".Split(Path.GetInvalidFileNameChars())) + ".lnk";
        var ziel = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), dateiName);
        try
        {
            ShortcutTools.Erstellen(ziel, exe, $"--start \"{profil.Name}\"",
                $"Startet das Profil \"{profil.Name}\" und den Flight Simulator");
            Log.Info($"Desktop-Verknüpfung erstellt: {ziel}");
            MessageBox.Show(this, $"Auf dem Desktop liegt jetzt \"{Path.GetFileNameWithoutExtension(dateiName)}\".\n\n" +
                                  "Ein Doppelklick startet dieses Profil sofort.", "Verknüpfung erstellt",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Log.Fehler("Verknüpfung konnte nicht erstellt werden: " + ex.Message);
        }
    }

    private void ExeXml_Click(object sender, RoutedEventArgs e)
    {
        var text = ExeXml.Vorschau(_programme.Where(p => p.ImProfil));
        new TextWindow("exe.xml-Vorschau (wird nirgends automatisch eingetragen)", text, "exe-vorschau.xml") { Owner = this }
            .ShowDialog();
    }

    private void ConfigOrdner_Click(object sender, RoutedEventArgs e) => OrdnerOeffnen(ConfigStore.BasisOrdner);
    private void LogOrdner_Click(object sender, RoutedEventArgs e) => OrdnerOeffnen(Log.Ordner);

    private static void OrdnerOeffnen(string ordner)
    {
        try
        {
            if (Directory.Exists(ordner))
                using (Process.Start(new ProcessStartInfo { FileName = ordner, UseShellExecute = true })) { }
        }
        catch (Exception ex)
        {
            Log.Fehler("Ordner konnte nicht geöffnet werden: " + ex.Message);
        }
    }

    // ---------- Aufruf mit Argumenten ----------

    /// <summary>Versteht: --start ["Profilname"], --tray, --zeigen.</summary>
    public void ArgumenteVerarbeiten(string[] args, bool vonZweiterInstanz)
    {
        var start = false;
        string? profilName = null;
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals("--start", StringComparison.OrdinalIgnoreCase)) continue;
            start = true;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--")) profilName = args[i + 1];
        }

        if (vonZweiterInstanz && !args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase))) Zeigen();
        if (!start) return;

        // Bei der Ersteinrichtung gibt es noch nichts zu starten.
        if (_ersteinrichtung && _programme.Count == 0 && !vonZweiterInstanz) return;

        var profil = profilName == null
            ? AktuellesProfil
            : _profile.FirstOrDefault(p => p.Name.Equals(profilName, StringComparison.OrdinalIgnoreCase));
        if (profil == null)
        {
            Log.Fehler($"Das Profil \"{profilName}\" aus dem Aufruf gibt es nicht. Vorhanden: " +
                       string.Join(", ", _profile.Select(p => p.Name)));
            Zeigen();
            return;
        }
        ProfilStarten(profil);
    }

    // ---------- Fenster und Tray ----------

    public void Zeigen()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void TrayAnpassen()
    {
        if (_cfg.Verhalten.TrayIcon && _tray == null)
        {
            try
            {
                _tray = new TrayIcon(() => _profile.Select(p => p.Name).ToList());
                _tray.OeffnenGewuenscht += Zeigen;
                _tray.ProfilStartGewuenscht += name =>
                {
                    var profil = _profile.FirstOrDefault(p => p.Name == name);
                    if (profil != null) ProfilStarten(profil);
                };
                _tray.BeendenGewuenscht += Close;
            }
            catch (Exception ex)
            {
                Log.Warnung("Tray-Icon konnte nicht angelegt werden: " + ex.Message);
            }
        }
        else if (!_cfg.Verhalten.TrayIcon && _tray != null)
        {
            _tray.Dispose();
            _tray = null;
            if (!IsVisible) Zeigen();
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized && _tray != null && _cfg.Verhalten.InTrayMinimieren)
            Hide();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_ablauf.Aktiv && !_beendenBestaetigt)
        {
            Zeigen();
            var antwort = MessageBox.Show(this,
                "Es läuft noch ein Ablauf (der Sim wird überwacht).\n\n" +
                "Wenn du den Launcher jetzt beendest, werden die Programme beim Sim-Ende nicht mehr geschlossen.\n\n" +
                "Trotzdem beenden?", "Launcher beenden", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (antwort != MessageBoxResult.Yes)
            {
                e.Cancel = true;
                return;
            }
        }
        base.OnClosing(e);
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        _ablauf.Abbrechen();
        _tray?.Dispose();
        _tray = null;
        base.OnClosed(e);
        Application.Current.Shutdown();
    }
}
