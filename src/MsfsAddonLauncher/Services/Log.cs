using System.Text;

namespace MsfsAddonLauncher.Services;

/// <summary>Protokoll: jede Zeile geht ins Fenster (ueber das Ereignis) und in eine Tagesdatei.</summary>
public static class Log
{
    private static readonly object Sperre = new();
    private static string? _ordner;

    /// <summary>Wird fuer jede neue Zeile ausgeloest, eventuell aus einem Hintergrund-Thread.</summary>
    public static event Action<string>? ZeileHinzugefuegt;

    public static string Ordner => _ordner ?? "";

    public static void Start(string basisOrdner)
    {
        _ordner = Path.Combine(basisOrdner, "logs");
        try
        {
            Directory.CreateDirectory(_ordner);
            // Alte Protokolle nach 30 Tagen entfernen, damit der Ordner nicht zuwaechst.
            foreach (var datei in Directory.GetFiles(_ordner, "launcher-*.log"))
            {
                if (File.GetLastWriteTime(datei) < DateTime.Now.AddDays(-30))
                    File.Delete(datei);
            }
        }
        catch
        {
            // Ohne Logdatei laeuft der Launcher trotzdem; das Fenster-Log bleibt.
        }
    }

    // Gleich lange Kennungen, damit die Meldungen im Log untereinander stehen.
    public static void Info(string text) => Schreiben("INFO  ", text);
    public static void Warnung(string text) => Schreiben("WARN  ", text);
    public static void Fehler(string text) => Schreiben("FEHLER", text);

    private static void Schreiben(string stufe, string text)
    {
        var zeile = $"{DateTime.Now:HH:mm:ss}  {stufe}  {text}";
        lock (Sperre)
        {
            if (_ordner != null)
            {
                try
                {
                    var datei = Path.Combine(_ordner, $"launcher-{DateTime.Now:yyyy-MM-dd}.log");
                    File.AppendAllText(datei, zeile + Environment.NewLine, Encoding.UTF8);
                }
                catch
                {
                    // Ein Schreibfehler im Log darf den Startablauf nie stoppen.
                }
            }
        }
        ZeileHinzugefuegt?.Invoke(zeile);
    }
}
