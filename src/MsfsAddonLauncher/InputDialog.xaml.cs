using System.Windows;

namespace MsfsAddonLauncher;

/// <summary>Kleiner Dialog fuer eine einzelne Texteingabe (z. B. Profilname).</summary>
public partial class InputDialog : Window
{
    public string Wert { get; private set; } = "";

    public InputDialog(string titel, string frage, string vorgabe)
    {
        InitializeComponent();
        Title = titel;
        FrageText.Text = frage;
        WertBox.Text = vorgabe;
        Loaded += (_, _) =>
        {
            WertBox.Focus();
            WertBox.SelectAll();
        };
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var wert = WertBox.Text.Trim();
        if (wert.Length == 0)
        {
            WertBox.Focus();
            return;
        }
        Wert = wert;
        DialogResult = true;
    }
}
