using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using MsfsAddonLauncher.Models;

namespace MsfsAddonLauncher.Services;

/// <summary>Liest und schreibt die config.json neben der .exe.</summary>
public static class ConfigStore
{
    public const string DateiName = "config.json";

    private static readonly JsonSerializerOptions Optionen = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        // Umlaute und Backslashes lesbar lassen statt ä zu schreiben.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Lazy<string> _basisOrdner = new(ErmittleBasisOrdner);

    /// <summary>Ordner der .exe; falls dort nicht geschrieben werden darf, %APPDATA%\MSFS Addon Launcher.</summary>
    public static string BasisOrdner => _basisOrdner.Value;

    public static string ConfigPfad => Path.Combine(BasisOrdner, DateiName);

    private static string ErmittleBasisOrdner()
    {
        var exeOrdner = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        try
        {
            var probe = Path.Combine(exeOrdner, ".schreibtest-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return exeOrdner;
        }
        catch
        {
            var ausweich = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MSFS Addon Launcher");
            Directory.CreateDirectory(ausweich);
            return ausweich;
        }
    }

    /// <param name="istNeu">true, wenn noch keine Konfiguration vorhanden war (Ersteinrichtung).</param>
    /// <param name="fehler">Meldung, falls die vorhandene Datei nicht lesbar war.</param>
    public static AppConfig Laden(out bool istNeu, out string? fehler)
    {
        istNeu = false;
        fehler = null;
        var pfad = ConfigPfad;

        if (!File.Exists(pfad))
        {
            istNeu = true;
            return Normalisieren(new AppConfig());
        }

        try
        {
            var json = File.ReadAllText(pfad);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, Optionen) ?? new AppConfig();
            return Normalisieren(cfg);
        }
        catch (Exception ex)
        {
            // Die kaputte Datei nie ueberschreiben, ohne sie vorher zu sichern.
            var sicherung = Path.Combine(BasisOrdner, $"config.fehlerhaft-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            try { File.Copy(pfad, sicherung, true); } catch { /* Sicherung ist ein Zusatz, kein Muss */ }
            fehler = $"Die config.json konnte nicht gelesen werden ({ex.Message}). " +
                     $"Sie wurde als \"{Path.GetFileName(sicherung)}\" gesichert; es wird mit Standardwerten gestartet.";
            return Normalisieren(new AppConfig());
        }
    }

    public static void Speichern(AppConfig cfg)
    {
        Normalisieren(cfg);
        var pfad = ConfigPfad;
        var temp = pfad + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(cfg, Optionen));
        // Erst fertig schreiben, dann austauschen: so bleibt bei einem Absturz die alte Datei heil.
        File.Move(temp, pfad, true);
    }

    public static string AlsJson(AppConfig cfg) => JsonSerializer.Serialize(cfg, Optionen);

    /// <summary>Repariert fehlende oder widerspruechliche Angaben, damit der Rest sich darauf verlassen kann.</summary>
    public static AppConfig Normalisieren(AppConfig cfg)
    {
        cfg.Sim ??= new SimSettings();
        cfg.Verhalten ??= new VerhaltenSettings();
        cfg.Profile ??= new List<Profil>();
        cfg.Programme ??= new List<AddonProgram>();

        cfg.Sim.ProzessNamen = (cfg.Sim.ProzessNamen ?? new List<string>())
            .SelectMany(ProcessTools.NamenAufteilen)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (cfg.Sim.ProzessNamen.Count == 0) cfg.Sim.ProzessNamen.Add("FlightSimulator2024");
        cfg.Sim.StoreAppId = (cfg.Sim.StoreAppId ?? "").Trim();
        cfg.Sim.SteamAppId = (cfg.Sim.SteamAppId ?? "").Trim();
        cfg.Sim.EigenerPfad ??= "";
        cfg.Sim.EigeneArgumente ??= "";
        cfg.Sim.MaxWartenAufSimSek = Math.Clamp(cfg.Sim.MaxWartenAufSimSek, 30, 3600);
        cfg.Sim.MaxWartenAufSimConnectSek = Math.Clamp(cfg.Sim.MaxWartenAufSimConnectSek, 0, 3600);
        cfg.Sim.WartezeitNachSimStartSek = Math.Clamp(cfg.Sim.WartezeitNachSimStartSek, 0, 3600);
        cfg.Verhalten.SchliessenTimeoutSek = Math.Clamp(cfg.Verhalten.SchliessenTimeoutSek, 1, 120);
        cfg.Verhalten.StatusIntervallSek = Math.Clamp(cfg.Verhalten.StatusIntervallSek, 1, 30);

        // Jede Id genau einmal.
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var p in cfg.Programme)
        {
            p.Name ??= "";
            p.Pfad ??= "";
            p.Argumente ??= "";
            p.Arbeitsverzeichnis ??= "";
            p.ProzessName ??= "";
            p.ProzessPfadFilter ??= "";
            p.Notiz ??= "";
            p.VerzoegerungSek = Math.Clamp(p.VerzoegerungSek, 0, 3600);
            if (string.IsNullOrWhiteSpace(p.Id) || !ids.Add(p.Id))
            {
                p.Id = AddonProgram.NeueId();
                ids.Add(p.Id);
            }
        }

        if (cfg.Profile.Count == 0)
        {
            cfg.Profile.Add(new Profil { Name = "Airliner komplett", Programme = cfg.Programme.Select(p => p.Id).ToList() });
            cfg.Profile.Add(new Profil { Name = "Nur ATC" });
            cfg.Profile.Add(new Profil { Name = "Schnellstart" });
        }

        foreach (var profil in cfg.Profile)
        {
            profil.Name = string.IsNullOrWhiteSpace(profil.Name) ? "Profil" : profil.Name.Trim();
            profil.Programme = (profil.Programme ?? new List<string>())
                .Where(ids.Contains)
                .Distinct()
                .ToList();
        }

        if (cfg.Profile.All(p => !string.Equals(p.Name, cfg.AktivesProfil, StringComparison.OrdinalIgnoreCase)))
            cfg.AktivesProfil = cfg.Profile[0].Name;

        return cfg;
    }
}
