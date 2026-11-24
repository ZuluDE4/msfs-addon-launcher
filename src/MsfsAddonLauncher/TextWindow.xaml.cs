using System.Text;
using System.Windows;
using Microsoft.Win32;
using MsfsAddonLauncher.Services;

namespace MsfsAddonLauncher;

/// <summary>Zeigt einen Text zum Lesen, Kopieren oder Speichern an einem selbst gewaehlten Ort.</summary>
public partial class TextWindow : Window
{
    private readonly string _dateiVorschlag;

    public TextWindow(string titel, string text, string dateiVorschlag)
    {
        InitializeComponent();
        Title = titel;
        TextBox1.Text = text;
        _dateiVorschlag = dateiVorschlag;
    }

    private void Kopieren_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(TextBox1.Text); }
        catch (Exception ex) { Log.Warnung("Kopieren in die Zwischenablage fehlgeschlagen: " + ex.Message); }
    }

    private void Speichern_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Vorschau speichern",
            FileName = _dateiVorschlag,
            Filter = "XML-Datei|*.xml|Textdatei|*.txt|Alle Dateien|*.*",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            File.WriteAllText(dialog.FileName, TextBox1.Text, new UTF8Encoding(false));
            Log.Info("Vorschau gespeichert: " + dialog.FileName);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Speichern fehlgeschlagen:\n" + ex.Message, Title,
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
