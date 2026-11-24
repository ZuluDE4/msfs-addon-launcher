using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using MsfsAddonLauncher.Models;

namespace MsfsAddonLauncher.Services;

/// <summary>Findet, startet und beobachtet den Flight Simulator. Aendert nichts an dessen Dateien.</summary>
public static class SimService
{
    /// <summary>Paketname von MSFS 2024 im Microsoft Store (ohne Herausgeber-Kennung).</summary>
    public const string StorePaketPraefix = "Microsoft.Limitless_";

    // ---------- Installation erkennen ----------

    /// <summary>Liefert den PackageFamilyName der Store-Version, falls installiert.</summary>
    public static string? StoreFamilienName()
    {
        // Der Datenordner des Pakets heisst genau wie der PackageFamilyName.
        try
        {
            var pakete = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
            if (Directory.Exists(pakete))
            {
                var ordner = Directory.GetDirectories(pakete, StorePaketPraefix + "*").FirstOrDefault();
                if (ordner != null && StorePaketRegistriert())
                    return Path.GetFileName(ordner);
            }
        }
        catch { /* dann eben nicht erkannt */ }
        return null;
    }

    /// <summary>Prueft in der Paketliste des Benutzers, ob das Store-Paket wirklich installiert ist.</summary>
    private static bool StorePaketRegistriert()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages");
            if (key == null) return true; // Liste nicht lesbar: dem Datenordner glauben
            return key.GetSubKeyNames().Any(n => n.StartsWith(StorePaketPraefix, StringComparison.OrdinalIgnoreCase));
        }
        catch { return true; }
    }

    /// <summary>true, wenn Steam das Spiel mit dieser AppID installiert hat.</summary>
    public static bool SteamInstalliert(string appId)
    {
        try
        {
            foreach (var bibliothek in SteamBibliotheken())
            {
                if (File.Exists(Path.Combine(bibliothek, "steamapps", $"appmanifest_{appId}.acf")))
                    return true;
            }
        }
        catch { /* dann eben nicht erkannt */ }
        return false;
    }

    private static IEnumerable<string> SteamBibliotheken()
    {
        string? steam;
        using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            steam = key?.GetValue("SteamPath") as string;
        if (string.IsNullOrWhiteSpace(steam)) yield break;

        steam = steam.Replace('/', '\\');
        yield return steam;

        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;
        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            yield return m.Groups[1].Value.Replace(@"\\", @"\");
    }

    /// <summary>Welche Variante ist auf diesem PC installiert? null = keine gefunden.</summary>
    public static SimVariante? Erkennen(SimSettings sim, out string beschreibung)
    {
        var familie = StoreFamilienName();
        if (familie != null)
        {
            beschreibung = $"Microsoft-Store-Version gefunden ({familie})";
            return SimVariante.Store;
        }
        if (SteamInstalliert(sim.SteamAppId))
        {
            beschreibung = $"Steam-Version gefunden (AppID {sim.SteamAppId})";
            return SimVariante.Steam;
        }
        beschreibung = "Keine MSFS-2024-Installation gefunden";
        return null;
    }

    // ---------- Laufender Sim ----------

    public static bool Laeuft(SimSettings sim) => ProcessTools.Laeuft(sim.ProzessNamen, null);

    /// <summary>
    /// SimConnect ist bereit, sobald der Sim seine SimConnect-Pipe geoeffnet hat.
    /// Gesucht wird nach jeder Pipe mit "SimConnect" im Namen, damit der genaue Name egal ist.
    /// </summary>
    public static bool SimConnectBereit(out string gefunden)
    {
        gefunden = "";
        try
        {
            foreach (var pipe in Directory.EnumerateFiles(@"\\.\pipe\"))
            {
                if (pipe.Contains("SimConnect", StringComparison.OrdinalIgnoreCase))
                {
                    gefunden = pipe;
                    return true;
                }
            }
        }
        catch
        {
            // Pipes mit Sonderzeichen im Namen koennen die Auflistung abbrechen: dann "nicht bereit".
        }
        return false;
    }

    // ---------- Starten ----------

    public static bool Starten(SimSettings sim, out string meldung)
    {
        try
        {
            switch (sim.Variante)
            {
                case SimVariante.Store:
                    if (!sim.StoreAppId.Contains('!'))
                    {
                        meldung = "Die Store-App-ID ist unvollständig (erwartet: PackageFamilyName!App).";
                        return false;
                    }
                    // Store-Apps lassen sich nicht ueber einen Dateipfad starten, nur ueber ihre App-ID.
                    using (Process.Start(new ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = @"shell:AppsFolder\" + sim.StoreAppId,
                        UseShellExecute = false
                    })) { }
                    meldung = $"Store-Version gestartet ({sim.StoreAppId})";
                    return true;

                case SimVariante.Steam:
                    if (!sim.SteamAppId.All(char.IsDigit) || sim.SteamAppId.Length == 0)
                    {
                        meldung = "Die Steam-AppID muss eine Zahl sein.";
                        return false;
                    }
                    using (Process.Start(new ProcessStartInfo
                    {
                        FileName = "steam://rungameid/" + sim.SteamAppId,
                        UseShellExecute = true
                    })) { }
                    meldung = $"Steam-Version gestartet (AppID {sim.SteamAppId})";
                    return true;

                default:
                    var pfad = ProcessTools.PfadAufloesen(sim.EigenerPfad);
                    if (pfad.Length == 0 || (!ProcessTools.IstAdresse(pfad) && !File.Exists(pfad)))
                    {
                        meldung = $"Eigener Sim-Pfad nicht gefunden: {pfad}";
                        return false;
                    }
                    var info = new ProcessStartInfo
                    {
                        FileName = pfad,
                        Arguments = sim.EigeneArgumente,
                        UseShellExecute = true
                    };
                    if (!ProcessTools.IstAdresse(pfad))
                        info.WorkingDirectory = Path.GetDirectoryName(pfad) ?? "";
                    using (Process.Start(info)) { }
                    meldung = $"Sim gestartet ({pfad})";
                    return true;
            }
        }
        catch (Exception ex)
        {
            meldung = "Sim-Start fehlgeschlagen: " + ex.Message;
            return false;
        }
    }

    // ---------- Orte der MSFS-Dateien (nur zum Lesen) ----------

    /// <summary>Moegliche Ordner mit exe.xml und UserCfg.opt (Store zuerst, dann Steam).</summary>
    public static IEnumerable<string> DatenOrdner()
    {
        var lokal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var pakete = Path.Combine(lokal, "Packages");
        if (Directory.Exists(pakete))
        {
            string[] ordner;
            try { ordner = Directory.GetDirectories(pakete, StorePaketPraefix + "*"); }
            catch { ordner = Array.Empty<string>(); }
            foreach (var o in ordner)
            {
                var cache = Path.Combine(o, "LocalCache");
                if (Directory.Exists(cache)) yield return cache;
            }
        }

        var roaming = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft Flight Simulator 2024");
        if (Directory.Exists(roaming)) yield return roaming;
    }

    /// <summary>Community-Ordner laut UserCfg.opt (InstalledPackagesPath).</summary>
    public static IEnumerable<string> CommunityOrdner()
    {
        foreach (var ordner in DatenOrdner())
        {
            var cfg = Path.Combine(ordner, "UserCfg.opt");
            if (!File.Exists(cfg)) continue;
            string? pakete = null;
            try
            {
                foreach (var zeile in File.ReadLines(cfg))
                {
                    var m = Regex.Match(zeile, "^\\s*InstalledPackagesPath\\s+\"([^\"]+)\"");
                    if (m.Success) { pakete = m.Groups[1].Value; break; }
                }
            }
            catch { /* nicht lesbar: ueberspringen */ }

            if (pakete == null) continue;
            var community = Path.Combine(pakete, "Community");
            if (Directory.Exists(community)) yield return community;
        }
    }
}
