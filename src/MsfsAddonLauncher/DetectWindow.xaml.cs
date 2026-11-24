using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using MsfsAddonLauncher.Services;

namespace MsfsAddonLauncher;

public partial class DetectWindow : Window
{
    private readonly List<Vorschlag> _alle;
    private readonly ICollectionView _sicht;

    /// <summary>Was der Benutzer angehakt und bestaetigt hat.</summary>
    public List<Vorschlag> Ausgewaehlt { get; private set; } = new();

    public DetectWindow(AutoDetect.Ergebnis ergebnis, bool erststart, string? simInfo)
    {
        InitializeComponent();
        _alle = ergebnis.Vorschlaege;

        var bekannt = _alle.Count(v => v.Bekannt);
        var sonstige = _alle.Count - bekannt;

        if (erststart)
        {
            Title = "Willkommen – Ersteinrichtung";
            TitelText.Text = "Willkommen beim MSFS Addon Launcher";
            AbbrechenKnopf.Content = "Später";
        }

        ErklaerText.Text =
            (bekannt == 0
                ? "Es wurden keine bekannten Flugsim-Zusatzprogramme gefunden. "
                : $"Auf diesem PC wurden {bekannt} Flugsim-Zusatzprogramm(e) gefunden. ") +
            "Hake an, was der Launcher für dich starten soll. Übernommen wird nur, was angehakt ist; " +
            "alles lässt sich später ändern. Programme, die der Sim oder Windows schon von selbst startet, " +
            "sind nicht angehakt, damit nichts doppelt läuft." +
            (ergebnis.BereitsVorhanden > 0 ? $" {ergebnis.BereitsVorhanden} Programm(e) stehen schon in deiner Liste und werden hier nicht gezeigt." : "");

        if (!string.IsNullOrWhiteSpace(simInfo))
        {
            SimText.Text = "Flight Simulator: " + simInfo + ".";
            SimText.Visibility = Visibility.Visible;
        }

        AlleBox.Content = $"Auch sonstige Programme anzeigen ({sonstige})";
        AlleBox.IsEnabled = sonstige > 0;
        // Gibt es keine bekannten Treffer, gleich alles zeigen, damit die Liste nicht leer ist.
        AlleBox.IsChecked = bekannt == 0 && sonstige > 0;

        _sicht = CollectionViewSource.GetDefaultView(_alle);
        _sicht.Filter = o => o is Vorschlag v && (v.Bekannt || v.Auswahl || AlleBox.IsChecked == true);
        Liste.ItemsSource = _sicht;
        KnopfAnpassen();
    }

    private void AlleBox_Click(object sender, RoutedEventArgs e) => _sicht.Refresh();

    private void Haken_Click(object sender, RoutedEventArgs e) => KnopfAnpassen();

    private void KnopfAnpassen()
    {
        var anzahl = _alle.Count(v => v.Auswahl);
        OkKnopf.Content = anzahl == 0 ? "Nichts übernehmen" : $"{anzahl} Programm(e) übernehmen";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Ausgewaehlt = _alle.Where(v => v.Auswahl).ToList();
        DialogResult = true;
    }
}
