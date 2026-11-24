using System.Text;
using System.Xml.Linq;
using MsfsAddonLauncher.Models;

namespace MsfsAddonLauncher.Services;

/// <summary>
/// Liest die exe.xml des Sims (nur lesend) und erzeugt eine Vorschau, wie Eintraege dort aussaehen.
/// Diese Klasse schreibt nie in einen MSFS-Ordner.
/// </summary>
public static class ExeXml
{
    public sealed record Eintrag(string Name, string Pfad, string Argumente, bool Deaktiviert, string Datei);

    /// <summary>Alle exe.xml-Dateien, die auf diesem PC gefunden werden.</summary>
    public static IEnumerable<string> Dateien()
    {
        foreach (var ordner in SimService.DatenOrdner())
        {
            var datei = Path.Combine(ordner, "exe.xml");
            if (File.Exists(datei)) yield return datei;
        }
    }

    /// <summary>Programme, die der Sim laut exe.xml selbst startet.</summary>
    public static List<Eintrag> Lesen()
    {
        var ergebnis = new List<Eintrag>();
        foreach (var datei in Dateien())
        {
            try
            {
                var doc = XDocument.Load(datei);
                foreach (var addon in doc.Descendants().Where(e => Ist(e, "Launch.Addon")))
                {
                    var pfad = Wert(addon, "Path");
                    if (pfad.Length == 0) continue;
                    ergebnis.Add(new Eintrag(
                        Wert(addon, "Name"),
                        pfad,
                        Wert(addon, "CommandLine"),
                        string.Equals(Wert(addon, "Disabled"), "true", StringComparison.OrdinalIgnoreCase),
                        datei));
                }
            }
            catch (Exception ex)
            {
                Log.Warnung($"exe.xml nicht lesbar ({datei}): {ex.Message}");
            }
        }
        return ergebnis;
    }

    private static bool Ist(XElement e, string name) =>
        string.Equals(e.Name.LocalName, name, StringComparison.OrdinalIgnoreCase);

    private static string Wert(XElement eltern, string name) =>
        eltern.Elements().FirstOrDefault(e => Ist(e, name))?.Value.Trim() ?? "";

    /// <summary>Reine Vorschau als Text. Admin-Start und Verzoegerung kennt die exe.xml nicht.</summary>
    public static string Vorschau(IEnumerable<AddonProgram> programme)
    {
        var liste = programme.ToList();
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        sb.AppendLine("<!--");
        sb.AppendLine("  NUR VORSCHAU. Der Launcher ändert die echte exe.xml nicht.");
        sb.AppendLine("  So sähen die Einträge aus, wenn der Sim diese Programme selbst starten sollte.");
        sb.AppendLine("  Die exe.xml kennt weder \"als Admin\" noch Verzögerung noch \"vor dem Sim\":");
        sb.AppendLine("  alles hier startet gleichzeitig, während der Sim lädt.");
        var orte = Dateien().ToList();
        if (orte.Count > 0)
        {
            sb.AppendLine("  Deine exe.xml liegt hier:");
            foreach (var ort in orte) sb.AppendLine("    " + ort);
        }
        sb.AppendLine("-->");
        sb.AppendLine("<SimBase.Document Type=\"Launch\" version=\"1,0\">");
        sb.AppendLine("  <Descr>Launch</Descr>");
        sb.AppendLine("  <Filename>exe.xml</Filename>");
        sb.AppendLine("  <Disabled>False</Disabled>");
        sb.AppendLine("  <Launch.ManualLoad>False</Launch.ManualLoad>");
        foreach (var p in liste)
        {
            var hinweise = new List<string>();
            if (p.AlsAdmin) hinweise.Add("im Launcher: als Admin");
            if (p.VerzoegerungSek > 0) hinweise.Add($"im Launcher: {p.VerzoegerungSek} s Verzögerung");
            if (p.Start == StartPhase.VorSim) hinweise.Add("im Launcher: vor dem Sim");
            if (hinweise.Count > 0) sb.AppendLine($"  <!-- {Kommentar(string.Join(", ", hinweise))} -->");

            sb.AppendLine("  <Launch.Addon>");
            sb.AppendLine($"    <Name>{Escape(p.Name)}</Name>");
            sb.AppendLine($"    <Disabled>{(p.Aktiv ? "False" : "True")}</Disabled>");
            sb.AppendLine("    <ManualLoad>False</ManualLoad>");
            sb.AppendLine($"    <Path>{Escape(ProcessTools.PfadAufloesen(p.Pfad))}</Path>");
            if (!string.IsNullOrWhiteSpace(p.Argumente))
                sb.AppendLine($"    <CommandLine>{Escape(p.Argumente)}</CommandLine>");
            sb.AppendLine("  </Launch.Addon>");
        }
        sb.AppendLine("</SimBase.Document>");
        return sb.ToString();
    }

    private static string Escape(string text) => new XText(text ?? "").ToString();

    private static string Kommentar(string text) => text.Replace("--", "- -");
}
