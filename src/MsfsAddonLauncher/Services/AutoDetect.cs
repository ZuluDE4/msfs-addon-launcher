using System.Text.RegularExpressions;
using Microsoft.Win32;
using MsfsAddonLauncher.Models;

namespace MsfsAddonLauncher.Services;

/// <summary>Ein gefundenes Programm. Uebernommen wird es nur, wenn der Benutzer es anhakt und bestaetigt.</summary>
public sealed class Vorschlag : ObservableObject
{
    private bool _auswahl;
    public bool Auswahl { get => _auswahl; set => SetField(ref _auswahl, value); }

    public string Name { get; set; } = "";
    public string Pfad { get; set; } = "";
    public string Argumente { get; set; } = "";
    public string Arbeitsverzeichnis { get; set; } = "";
    public string Quelle { get; set; } = "";
    public string Hinweis { get; set; } = "";
    public string Kategorie { get; set; } = "";
    /// <summary>true = als Flugsim-Zusatzprogramm erkannt (steht im Katalog).</summary>
    public bool Bekannt { get; set; }
    public StartPhase Start { get; set; } = StartPhase.NachSim;
    public int VerzoegerungSek { get; set; }
    public string ProzessName { get; set; } = "";
    public string ProzessPfadFilter { get; set; } = "";
    public bool BeiSimEndeSchliessen { get; set; }

    public string StartText => Start == StartPhase.VorSim ? "vor Sim" : "nach Sim";
    public string PfadText => Argumente.Length > 0 ? $"{Pfad}  {Argumente}" : Pfad;
    public string ArtText => Bekannt ? (Kategorie.Length > 0 ? Kategorie : "Flugsim") : "Sonstiges";

    public AddonProgram AlsProgramm() => new()
    {
        Name = Name,
        Pfad = Pfad,
        Argumente = Argumente,
        Arbeitsverzeichnis = Arbeitsverzeichnis,
        Start = Start,
        VerzoegerungSek = VerzoegerungSek,
        ProzessName = ProzessName,
        ProzessPfadFilter = ProzessPfadFilter,
        BeiSimEndeSchliessen = BeiSimEndeSchliessen
    };
}

/// <summary>
/// Sucht Zusatzprogramme auf diesem PC. Pfade stammen immer vom PC selbst (Verknuepfungen,
/// Registry, Community-Ordner); der Katalog liefert nur Erkennungsmuster und Startvorschlaege.
/// </summary>
public static class AutoDetect
{
    private sealed record KatalogEintrag(
        string[] Muster,
        StartPhase Phase,
        int Verzoegerung,
        string Kategorie,
        string ProzessName = "",
        bool StandardAn = true,
        bool Schliessen = true,
        string Hinweis = "");

    // Phase und Verzoegerung sind Startvorschlaege und in der Oberflaeche aenderbar.
    private static readonly KatalogEintrag[] Katalog =
    {
        new(new[] { "rex atmos", "rex weather force", "rex accuseason", "rex axis" }, StartPhase.NachSim, 10, "Wetter"),
        // Bei SayIntentions startet ein Updater das eigentliche Programm (auf dem Entwicklungs-PC geprueft).
        new(new[] { "sayintentions" }, StartPhase.NachSim, 15, "ATC", ProzessName: "SayIntentionsAI"),
        new(new[] { "beyondatc", "beyond atc" }, StartPhase.NachSim, 15, "ATC"),
        new(new[] { "pilot2atc" }, StartPhase.NachSim, 15, "ATC"),
        new(new[] { "fshud" }, StartPhase.NachSim, 15, "ATC"),
        new(new[] { "atc chatter" }, StartPhase.NachSim, 15, "ATC"),
        new(new[] { "vpilot" }, StartPhase.NachSim, 15, "ATC"),
        new(new[] { "navigraph simlink" }, StartPhase.VorSim, 0, "Navigation", Schliessen: false),
        new(new[] { "navigraph charts" }, StartPhase.VorSim, 0, "Navigation", StandardAn: false),
        new(new[] { "little navmap" }, StartPhase.VorSim, 0, "Navigation", StandardAn: false),
        new(new[] { "volanta" }, StartPhase.VorSim, 0, "Flugverfolgung"),
        new(new[] { "simtoolkitpro" }, StartPhase.VorSim, 0, "Flugverfolgung"),
        new(new[] { "flyconomy miniclient" }, StartPhase.NachSim, 20, "Airline"),
        new(new[] { "smartcars" }, StartPhase.NachSim, 20, "Airline"),
        new(new[] { "raas pro", "raaspro" }, StartPhase.NachSim, 20, "Cockpit"),
        new(new[] { "fsuipc7" }, StartPhase.NachSim, 10, "Schnittstelle"),
        new(new[] { "spad.next" }, StartPhase.VorSim, 0, "Hardware", Schliessen: false),
        new(new[] { "axis and ohs", "axisandohs" }, StartPhase.NachSim, 10, "Hardware"),
        new(new[] { "mobiflight" }, StartPhase.VorSim, 0, "Hardware", Schliessen: false),
        new(new[] { "rowsfire", "rf-connect" }, StartPhase.VorSim, 0, "Hardware", StandardAn: false, Schliessen: false),
        new(new[] { "stream deck" }, StartPhase.VorSim, 0, "Hardware", StandardAn: false, Schliessen: false),
        new(new[] { "fsltl" }, StartPhase.NachSim, 30, "Verkehr"),
        new(new[] { "traffic audio" }, StartPhase.NachSim, 20, "Verkehr"),
        new(new[] { "apronlife", "apron life" }, StartPhase.NachSim, 20, "Verkehr"),
        new(new[] { "couatl", "gsx" }, StartPhase.NachSim, 20, "Bodenabfertigung"),
        new(new[] { "self-loading cargo", "selfloadingcargo" }, StartPhase.NachSim, 20, "Kabine"),
        new(new[] { "pacx" }, StartPhase.NachSim, 20, "Kabine"),
        new(new[] { "fsrealistic" }, StartPhase.NachSim, 20, "Effekte"),
        new(new[] { "touchdownfx" }, StartPhase.NachSim, 20, "Effekte"),
        new(new[] { "simhaptic" }, StartPhase.NachSim, 20, "Effekte"),
        new(new[] { "chaseplane" }, StartPhase.NachSim, 20, "Kamera"),
        new(new[] { "flight recorder", "flightrecorder" }, StartPhase.NachSim, 20, "Aufzeichnung"),
        new(new[] { "landing rate" }, StartPhase.NachSim, 20, "Aufzeichnung"),
        new(new[] { "fskneeboard" }, StartPhase.NachSim, 20, "Cockpit"),
        new(new[] { "map enhancement" }, StartPhase.VorSim, 0, "Szenerie",
            Hinweis: "Braucht evtl. Adminrechte"),
        new(new[] { "opentrack" }, StartPhase.VorSim, 0, "Headtracking", Schliessen: false),
        new(new[] { "trackir" }, StartPhase.VorSim, 0, "Headtracking", Schliessen: false),
        new(new[] { "voiceattack" }, StartPhase.VorSim, 0, "Sprachsteuerung", StandardAn: false, Schliessen: false)
    };

    private static readonly string[] Unerwuenscht =
    {
        "unins", "deinstall", "documentation", "handbuch", "manual", "release notes", "website",
        "report a problem", "hilfe zu", "was ist neu", "readme"
    };

    public sealed record Ergebnis(List<Vorschlag> Vorschlaege, int BereitsVorhanden, int VomSimGestartet);

    /// <summary>Durchsucht den PC. Muss auf einem STA-Thread laufen (siehe ShortcutTools.AufStaThread).</summary>
    public static Ergebnis Suchen(AppConfig cfg)
    {
        var simEintraege = ExeXml.Lesen().Where(e => !e.Deaktiviert).ToList();
        var autostart = AutostartPfade();
        var vorhanden = new HashSet<string>(
            cfg.Programme.Select(p => Schluessel(ProcessTools.PfadAufloesen(p.Pfad), p.Argumente)),
            StringComparer.OrdinalIgnoreCase);

        var funde = new List<Vorschlag>();
        var schluessel = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pfade = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        void Hinzu(string name, string ziel, string args, string arbeit, string quelle)
        {
            ziel = Norm(ziel);
            if (!ziel.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(ziel)) return;
            if (ziel.StartsWith(windows, StringComparison.OrdinalIgnoreCase)) return;
            if (Unerwuenscht.Any(u => name.Contains(u, StringComparison.OrdinalIgnoreCase) ||
                                      Path.GetFileName(ziel).Contains(u, StringComparison.OrdinalIgnoreCase))) return;
            if (!schluessel.Add(Schluessel(ziel, args))) return;
            pfade.Add(ziel);
            // "Programm.exe - Verknüpfung" wird zu "Programm".
            name = Regex.Replace(name, @"\s*-\s*(Verknüpfung|Shortcut)\s*$", "", RegexOptions.IgnoreCase);
            name = Regex.Replace(name, @"\.exe$", "", RegexOptions.IgnoreCase);
            funde.Add(new Vorschlag { Name = name.Trim(), Pfad = ziel, Argumente = args.Trim(), Arbeitsverzeichnis = arbeit, Quelle = quelle });
        }

        // 1. Verknuepfungen im Startmenue und auf dem Desktop.
        var startmenue = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms)
        };
        var desktop = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };
        foreach (var v in ShortcutTools.Lesen(startmenue)) Hinzu(v.Name, v.Ziel, v.Argumente, v.Arbeitsverzeichnis, "Startmenü");
        foreach (var v in ShortcutTools.Lesen(desktop)) Hinzu(v.Name, v.Ziel, v.Argumente, v.Arbeitsverzeichnis, "Desktop");

        // 2. Installierte Programme (Registry), aber nur bekannte Flugsim-Programme ohne Verknuepfung.
        foreach (var (name, icon, ort) in InstallierteProgramme())
        {
            if (KatalogTreffer(name, "") == null) continue;
            var exe = ExeAusIcon(icon) ?? EinzigeExe(ort);
            if (exe == null || pfade.Contains(Norm(exe))) continue;
            Hinzu(name, exe, "", "", "Installierte Programme");
        }

        // 3. Werkzeuge, die im Community-Ordner liegen und von Hand gestartet werden.
        foreach (var community in SimService.CommunityOrdner())
        {
            var fsltl = Path.Combine(community, "fsltl-traffic-injector", "fsltl-trafficinjector.exe");
            if (File.Exists(fsltl) && !pfade.Contains(Norm(fsltl)))
                Hinzu("FSLTL Traffic Injector", fsltl, "", "", "Community-Ordner");
        }

        // 4. Einordnen und markieren.
        var bereitsVorhanden = 0;
        var vomSim = 0;
        var ergebnis = new List<Vorschlag>();
        foreach (var v in funde)
        {
            if (vorhanden.Contains(Schluessel(v.Pfad, v.Argumente)))
            {
                bereitsVorhanden++;
                continue;
            }

            var eintrag = KatalogTreffer(v.Name, v.Pfad);
            var exeName = Path.GetFileNameWithoutExtension(v.Pfad);
            if (ProcessTools.AllerweltsNamen.Contains(exeName, StringComparer.OrdinalIgnoreCase))
                v.ProzessPfadFilter = Path.GetDirectoryName(v.Pfad) ?? "";

            if (eintrag != null)
            {
                v.Bekannt = true;
                v.Kategorie = eintrag.Kategorie;
                v.Start = eintrag.Phase;
                v.VerzoegerungSek = eintrag.Verzoegerung;
                v.ProzessName = eintrag.ProzessName;
                v.BeiSimEndeSchliessen = eintrag.Schliessen;
                v.Hinweis = eintrag.Hinweis;
                v.Auswahl = eintrag.StandardAn;
            }

            if (StartetDerSim(v.Pfad, simEintraege))
            {
                vomSim++;
                v.Auswahl = false;
                v.Hinweis = "Startet der Sim schon selbst";
            }
            else if (autostart.Contains(v.Pfad))
            {
                v.Auswahl = false;
                v.Hinweis = "Startet schon mit Windows";
            }

            ergebnis.Add(v);
        }

        // Gibt es mehrere Verknuepfungen auf dieselbe Datei (z. B. eine fuer MSFS 2020 und eine fuer 2024),
        // wird nur eine vorausgewaehlt: bevorzugt die mit "2024" im Namen oder in den Argumenten.
        foreach (var gruppe in ergebnis.Where(v => v.Auswahl).GroupBy(v => v.Pfad, StringComparer.OrdinalIgnoreCase))
        {
            if (gruppe.Count() < 2) continue;
            var favorit = gruppe.FirstOrDefault(v => (v.Name + " " + v.Argumente).Contains("2024")) ?? gruppe.First();
            foreach (var v in gruppe) v.Auswahl = ReferenceEquals(v, favorit);
        }

        ergebnis = ergebnis
            .OrderByDescending(v => v.Bekannt)
            .ThenBy(v => v.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new Ergebnis(ergebnis, bereitsVorhanden, vomSim);
    }

    // ---------- Hilfen ----------

    /// <summary>Installer, Updater und Verwaltungsprogramme sind keine Zusatzprogramme fuer den Flug.</summary>
    private static readonly string[] KeinFlugProgramm = { "installer", "update", "manager", "setup" };

    private static KatalogEintrag? KatalogTreffer(string name, string pfad)
    {
        if (KeinFlugProgramm.Any(w => name.Contains(w, StringComparison.OrdinalIgnoreCase))) return null;
        foreach (var eintrag in Katalog)
        {
            foreach (var muster in eintrag.Muster)
            {
                if (name.Contains(muster, StringComparison.OrdinalIgnoreCase) ||
                    pfad.Contains(muster, StringComparison.OrdinalIgnoreCase))
                    return eintrag;
            }
        }
        return null;
    }

    /// <summary>true, wenn die exe.xml genau diese Datei oder eine Datei aus demselben Programmordner startet.</summary>
    private static bool StartetDerSim(string pfad, List<ExeXml.Eintrag> simEintraege)
    {
        var ordner = (Path.GetDirectoryName(pfad) ?? "").TrimEnd('\\') + "\\";
        foreach (var e in simEintraege)
        {
            var ziel = Norm(e.Pfad);
            if (string.Equals(ziel, pfad, StringComparison.OrdinalIgnoreCase)) return true;
            // Nur echte Programmordner zaehlen, keine Sammelordner wie Laufwerkswurzel oder Desktop.
            if (ordner.Count(c => c == '\\') >= 3 && ziel.StartsWith(ordner, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static string Norm(string pfad)
    {
        pfad = ProcessTools.PfadAufloesen(pfad);
        try { return pfad.Length > 0 ? Path.GetFullPath(pfad) : pfad; }
        catch { return pfad; }
    }

    private static string Schluessel(string pfad, string? args) => Norm(pfad) + "|" + (args ?? "").Trim();

    private static string? ExeAusIcon(string? icon)
    {
        if (string.IsNullOrWhiteSpace(icon)) return null;
        var pfad = Regex.Replace(icon.Trim(), @",\s*-?\d+$", "").Trim().Trim('"');
        if (!pfad.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return null;
        if (Unerwuenscht.Any(u => Path.GetFileName(pfad).Contains(u, StringComparison.OrdinalIgnoreCase))) return null;
        return File.Exists(pfad) ? pfad : null;
    }

    /// <summary>Liefert die Programmdatei eines Installationsordners, wenn sie eindeutig ist.</summary>
    private static string? EinzigeExe(string? ordner)
    {
        if (string.IsNullOrWhiteSpace(ordner)) return null;
        ordner = ordner.Trim().Trim('"');
        try
        {
            if (!Directory.Exists(ordner)) return null;
            var exes = Directory.GetFiles(ordner, "*.exe")
                .Where(f =>
                {
                    var n = Path.GetFileName(f);
                    return !Unerwuenscht.Any(u => n.Contains(u, StringComparison.OrdinalIgnoreCase)) &&
                           !n.Contains("update", StringComparison.OrdinalIgnoreCase) &&
                           !n.Contains("crash", StringComparison.OrdinalIgnoreCase) &&
                           !n.Contains("setup", StringComparison.OrdinalIgnoreCase);
                })
                .ToList();
            return exes.Count == 1 ? exes[0] : null;
        }
        catch { return null; }
    }

    private static IEnumerable<(string Name, string? Icon, string? Ort)> InstallierteProgramme()
    {
        const string zweig = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
        var quellen = new (RegistryHive Hive, RegistryView Sicht)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default)
        };

        var liste = new List<(string, string?, string?)>();
        foreach (var (hive, sicht) in quellen)
        {
            try
            {
                using var basis = RegistryKey.OpenBaseKey(hive, sicht);
                using var key = basis.OpenSubKey(zweig);
                if (key == null) continue;
                foreach (var unter in key.GetSubKeyNames())
                {
                    try
                    {
                        using var k = key.OpenSubKey(unter);
                        if (k?.GetValue("DisplayName") is not string name || name.Length == 0) continue;
                        liste.Add((name, k.GetValue("DisplayIcon") as string, k.GetValue("InstallLocation") as string));
                    }
                    catch { /* einzelner Eintrag nicht lesbar */ }
                }
            }
            catch { /* Zweig nicht lesbar */ }
        }
        return liste;
    }

    /// <summary>Programmdateien, die Windows beim Anmelden ohnehin startet.</summary>
    private static HashSet<string> AutostartPfade()
    {
        var pfade = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        const string zweig = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        var quellen = new (RegistryHive Hive, RegistryView Sicht)[]
        {
            (RegistryHive.CurrentUser, RegistryView.Default),
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32)
        };

        foreach (var (hive, sicht) in quellen)
        {
            try
            {
                using var basis = RegistryKey.OpenBaseKey(hive, sicht);
                using var key = basis.OpenSubKey(zweig);
                if (key == null) continue;
                foreach (var wertName in key.GetValueNames())
                {
                    if (key.GetValue(wertName) is not string befehl) continue;
                    var m = Regex.Match(befehl.Trim(), "^\"([^\"]+)\"|^(.+?\\.exe)", RegexOptions.IgnoreCase);
                    if (!m.Success) continue;
                    pfade.Add(Norm(m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value));
                }
            }
            catch { /* Zweig nicht lesbar */ }
        }

        var autostartOrdner = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
        };
        foreach (var v in ShortcutTools.Lesen(autostartOrdner)) pfade.Add(Norm(v.Ziel));

        return pfade;
    }
}
