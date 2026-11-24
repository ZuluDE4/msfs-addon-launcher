using System.Windows;
using Microsoft.Win32;
using MsfsAddonLauncher.Models;
using MsfsAddonLauncher.Services;

namespace MsfsAddonLauncher;

public partial class ProgramEditWindow : Window
{
    /// <summary>Das bearbeitete Programm (eine Kopie; der Aufrufer uebernimmt sie bei "Speichern").</summary>
    public AddonProgram Programm { get; }

    public ProgramEditWindow(AddonProgram programm, bool neu)
    {
        InitializeComponent();
        Programm = programm;
        Title = neu ? "Programm hinzufügen" : "Programm bearbeiten";

        NameBox.Text = programm.Name;
        PfadBox.Text = programm.Pfad;
        ArgumenteBox.Text = programm.Argumente;
        ArbeitBox.Text = programm.Arbeitsverzeichnis;
        StartBox.SelectedIndex = programm.Start == StartPhase.VorSim ? 0 : 1;
        VerzoegerungBox.Text = programm.VerzoegerungSek.ToString();
        AktivBox.IsChecked = programm.Aktiv;
        AdminBox.IsChecked = programm.AlsAdmin;
        MinimiertBox.IsChecked = programm.Minimiert;
        SchliessenBox.IsChecked = programm.BeiSimEndeSchliessen;
        ProzessBox.Text = programm.ProzessName;
        FilterBox.Text = programm.ProzessPfadFilter;
        NotizBox.Text = programm.Notiz;

        Loaded += (_, _) => NameBox.Focus();
    }

    private void Durchsuchen_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Programm auswählen",
            Filter = "Programme und Verknüpfungen|*.exe;*.lnk;*.bat;*.cmd|Alle Dateien|*.*",
            // Bei einer Verknuepfung wird ihr Ziel uebernommen: nur so laesst sich der Prozess spaeter erkennen.
            DereferenceLinks = true
        };
        var aktuell = ProcessTools.PfadAufloesen(PfadBox.Text);
        try
        {
            var ordner = Path.GetDirectoryName(aktuell);
            if (!string.IsNullOrEmpty(ordner) && Directory.Exists(ordner)) dialog.InitialDirectory = ordner;
        }
        catch { /* ungueltiger Pfad im Feld: Dialog startet am Standardort */ }

        if (dialog.ShowDialog(this) != true) return;

        PfadBox.Text = dialog.FileName;
        var dateiName = Path.GetFileNameWithoutExtension(dialog.FileName);
        if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = dateiName;

        // Bei node.exe & Co. gleich den Ordner als Filter vorschlagen.
        if (string.IsNullOrWhiteSpace(FilterBox.Text) &&
            ProcessTools.AllerweltsNamen.Contains(dateiName, StringComparer.OrdinalIgnoreCase))
            FilterBox.Text = Path.GetDirectoryName(dialog.FileName) ?? "";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim();
        var pfad = PfadBox.Text.Trim().Trim('"');

        if (name.Length == 0)
        {
            Meldung("Bitte einen Namen eintragen.");
            NameBox.Focus();
            return;
        }
        if (pfad.Length == 0)
        {
            Meldung("Bitte einen Pfad eintragen oder über „Durchsuchen“ wählen.");
            PfadBox.Focus();
            return;
        }
        if (!int.TryParse(VerzoegerungBox.Text.Trim(), out var verzoegerung) || verzoegerung < 0 || verzoegerung > 3600)
        {
            Meldung("Die Verzögerung muss eine ganze Zahl zwischen 0 und 3600 sein.");
            VerzoegerungBox.Focus();
            return;
        }

        var aufgeloest = ProcessTools.PfadAufloesen(pfad);
        if (!ProcessTools.IstAdresse(aufgeloest) && !File.Exists(aufgeloest))
        {
            var antwort = MessageBox.Show(this,
                $"Die Datei wurde nicht gefunden:\n{aufgeloest}\n\nTrotzdem speichern?",
                Title, MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (antwort != MessageBoxResult.Yes) return;
        }

        Programm.Name = name;
        Programm.Pfad = pfad;
        Programm.Argumente = ArgumenteBox.Text.Trim();
        Programm.Arbeitsverzeichnis = ArbeitBox.Text.Trim().Trim('"');
        Programm.Start = StartBox.SelectedIndex == 0 ? StartPhase.VorSim : StartPhase.NachSim;
        Programm.VerzoegerungSek = verzoegerung;
        Programm.Aktiv = AktivBox.IsChecked == true;
        Programm.AlsAdmin = AdminBox.IsChecked == true;
        Programm.Minimiert = MinimiertBox.IsChecked == true;
        Programm.BeiSimEndeSchliessen = SchliessenBox.IsChecked == true;
        Programm.ProzessName = string.Join("; ", ProcessTools.NamenAufteilen(ProzessBox.Text));
        Programm.ProzessPfadFilter = FilterBox.Text.Trim().Trim('"');
        Programm.Notiz = NotizBox.Text.Trim();

        DialogResult = true;
    }

    private void Meldung(string text) =>
        MessageBox.Show(this, text, Title, MessageBoxButton.OK, MessageBoxImage.Information);
}
