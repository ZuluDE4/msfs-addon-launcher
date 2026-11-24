using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace MsfsAddonLauncher.Models;

/// <summary>Basisklasse, damit die Oberflaeche Aenderungen (z. B. am Status) mitbekommt.</summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>Wann ein Programm gestartet wird.</summary>
public enum StartPhase
{
    /// <summary>Vor dem Simulator (braucht keine Sim-Verbindung).</summary>
    VorSim,
    /// <summary>Erst wenn der Simulator laeuft (braucht SimConnect).</summary>
    NachSim
}

public enum SimVariante { Store, Steam, Benutzerdefiniert }

/// <summary>Was mit den Zusatzprogrammen passiert, wenn der Sim beendet wird.</summary>
public enum SchliessenModus { Nie, Nachfragen, Automatisch }

public enum ProgrammStatus { Aus, Wartet, Startet, Laeuft, Fehler, PfadFehlt, Deaktiviert }

/// <summary>Ein Zusatzprogramm, wie es in der config.json steht.</summary>
public sealed class AddonProgram : ObservableObject
{
    public string Id { get; set; } = NeueId();
    public string Name { get; set; } = "";
    /// <summary>Was gestartet wird. Umgebungsvariablen wie %LOCALAPPDATA% sind erlaubt.</summary>
    public string Pfad { get; set; } = "";
    public string Argumente { get; set; } = "";
    /// <summary>Leer = Ordner der gestarteten Datei.</summary>
    public string Arbeitsverzeichnis { get; set; } = "";
    /// <summary>Aus = wird in keinem Profil gestartet.</summary>
    public bool Aktiv { get; set; } = true;
    public StartPhase Start { get; set; } = StartPhase.NachSim;
    /// <summary>Wartezeit in Sekunden direkt vor dem Start dieses Programms.</summary>
    public int VerzoegerungSek { get; set; }
    public bool AlsAdmin { get; set; }
    public bool Minimiert { get; set; }
    public bool BeiSimEndeSchliessen { get; set; } = true;
    /// <summary>
    /// Prozessname(n) zur Statuserkennung, ohne ".exe", mehrere mit ";" getrennt.
    /// Leer = Dateiname aus "Pfad". Wichtig, wenn ein Updater vorgeschaltet ist.
    /// </summary>
    public string ProzessName { get; set; } = "";
    /// <summary>
    /// Optional: Der Prozess zaehlt nur, wenn sein Dateipfad diesen Text enthaelt.
    /// Noetig bei Allerweltsnamen wie node.exe.
    /// </summary>
    public string ProzessPfadFilter { get; set; } = "";
    public string Notiz { get; set; } = "";

    // ---- Laufzeitwerte, stehen nicht in der Datei ----

    private bool _imProfil;
    private ProgrammStatus _status = ProgrammStatus.Aus;
    private string _statusText = "Aus";

    [JsonIgnore]
    public bool ImProfil { get => _imProfil; set => SetField(ref _imProfil, value); }

    [JsonIgnore]
    public ProgrammStatus Status { get => _status; private set => SetField(ref _status, value); }

    [JsonIgnore]
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }

    /// <summary>Zeitpunkt des letzten Startversuchs durch den Launcher.</summary>
    [JsonIgnore]
    public DateTime? GestartetUm { get; set; }

    /// <summary>Damit die Warnung "Prozess nicht erkannt" nur einmal im Log landet.</summary>
    [JsonIgnore]
    public bool NichtErkanntGemeldet { get; set; }

    [JsonIgnore]
    public string StartText => Start == StartPhase.VorSim ? "vor Sim" : "nach Sim";

    [JsonIgnore]
    public string VerzoegerungText => VerzoegerungSek > 0 ? $"{VerzoegerungSek} s" : "–";

    [JsonIgnore]
    public string AdminText => AlsAdmin ? "ja" : "";

    [JsonIgnore]
    public string SchliessenText => BeiSimEndeSchliessen ? "ja" : "";

    public void SetStatus(ProgrammStatus status, string? text = null)
    {
        Status = status;
        StatusText = text ?? status switch
        {
            ProgrammStatus.Aus => "Aus",
            ProgrammStatus.Wartet => "Wartet",
            ProgrammStatus.Startet => "Startet …",
            ProgrammStatus.Laeuft => "Läuft",
            ProgrammStatus.Fehler => "Fehler",
            ProgrammStatus.PfadFehlt => "Datei fehlt",
            ProgrammStatus.Deaktiviert => "Deaktiviert",
            _ => status.ToString()
        };
    }

    /// <summary>Meldet der Tabelle, dass sich die angezeigten Stammdaten geaendert haben.</summary>
    public void AnzeigeAktualisieren()
    {
        Raise(nameof(Name));
        Raise(nameof(Pfad));
        Raise(nameof(StartText));
        Raise(nameof(VerzoegerungText));
        Raise(nameof(AdminText));
        Raise(nameof(SchliessenText));
    }

    public AddonProgram Kopie() => new()
    {
        Id = Id,
        Name = Name,
        Pfad = Pfad,
        Argumente = Argumente,
        Arbeitsverzeichnis = Arbeitsverzeichnis,
        Aktiv = Aktiv,
        Start = Start,
        VerzoegerungSek = VerzoegerungSek,
        AlsAdmin = AlsAdmin,
        Minimiert = Minimiert,
        BeiSimEndeSchliessen = BeiSimEndeSchliessen,
        ProzessName = ProzessName,
        ProzessPfadFilter = ProzessPfadFilter,
        Notiz = Notiz
    };

    public void UebernehmeVon(AddonProgram q)
    {
        Name = q.Name;
        Pfad = q.Pfad;
        Argumente = q.Argumente;
        Arbeitsverzeichnis = q.Arbeitsverzeichnis;
        Aktiv = q.Aktiv;
        Start = q.Start;
        VerzoegerungSek = q.VerzoegerungSek;
        AlsAdmin = q.AlsAdmin;
        Minimiert = q.Minimiert;
        BeiSimEndeSchliessen = q.BeiSimEndeSchliessen;
        ProzessName = q.ProzessName;
        ProzessPfadFilter = q.ProzessPfadFilter;
        Notiz = q.Notiz;
        AnzeigeAktualisieren();
    }

    public static string NeueId() => Guid.NewGuid().ToString("N")[..8];
}

/// <summary>Ein Profil ist eine benannte Auswahl von Programmen.</summary>
public sealed class Profil
{
    public string Name { get; set; } = "";
    /// <summary>Ids der Programme, die in diesem Profil gestartet werden.</summary>
    public List<string> Programme { get; set; } = new();

    public override string ToString() => Name;
}

public sealed class SimSettings
{
    public SimVariante Variante { get; set; } = SimVariante.Store;
    /// <summary>Aus = der Launcher startet den Sim nicht selbst, wartet aber auf ihn.</summary>
    public bool SimStarten { get; set; } = true;
    /// <summary>PackageFamilyName + "!App". Pruefen mit: Get-StartApps | Where Name -like "*Flight Simulator 2024*"</summary>
    public string StoreAppId { get; set; } = "Microsoft.Limitless_8wekyb3d8bbwe!App";
    public string SteamAppId { get; set; } = "2537590";
    public string EigenerPfad { get; set; } = "";
    public string EigeneArgumente { get; set; } = "";
    /// <summary>Prozessnamen des laufenden Sims, ohne ".exe".</summary>
    public List<string> ProzessNamen { get; set; } = new() { "FlightSimulator2024" };
    /// <summary>So lange wird nach dem Start auf den Sim-Prozess gewartet.</summary>
    public int MaxWartenAufSimSek { get; set; } = 600;
    /// <summary>An = "nach Sim"-Programme starten erst, wenn die SimConnect-Schnittstelle des Sims da ist.</summary>
    public bool SimConnectPruefen { get; set; } = true;
    /// <summary>Taucht SimConnect in dieser Zeit nicht auf, geht es trotzdem weiter (mit Warnung im Log).</summary>
    public int MaxWartenAufSimConnectSek { get; set; } = 240;
    /// <summary>Feste Wartezeit nach Erscheinen des Sim-Prozesses, wenn SimConnectPruefen aus ist.</summary>
    public int WartezeitNachSimStartSek { get; set; } = 90;
}

public sealed class VerhaltenSettings
{
    public SchliessenModus ProgrammeBeiSimEnde { get; set; } = SchliessenModus.Automatisch;
    /// <summary>So lange bekommt ein Programm Zeit, sich selbst sauber zu beenden.</summary>
    public int SchliessenTimeoutSek { get; set; } = 10;
    /// <summary>An = was nach dem Timeout noch laeuft, wird hart beendet.</summary>
    public bool HartBeenden { get; set; } = true;
    public bool TrayIcon { get; set; } = true;
    /// <summary>An = Minimieren versteckt das Fenster im Infobereich (nur mit TrayIcon).</summary>
    public bool InTrayMinimieren { get; set; } = true;
    /// <summary>An = Fenster nach "Alles starten" minimieren.</summary>
    public bool NachStartMinimieren { get; set; }
    /// <summary>An = Launcher beendet sich, nachdem der Sim beendet und aufgeraeumt ist.</summary>
    public bool LauncherNachSimEndeBeenden { get; set; }
    /// <summary>Wie oft der Status der Programme geprueft wird.</summary>
    public int StatusIntervallSek { get; set; } = 2;
}

/// <summary>Inhalt der config.json.</summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;
    public SimSettings Sim { get; set; } = new();
    public VerhaltenSettings Verhalten { get; set; } = new();
    public string AktivesProfil { get; set; } = "";
    public List<Profil> Profile { get; set; } = new();
    public List<AddonProgram> Programme { get; set; } = new();
}
