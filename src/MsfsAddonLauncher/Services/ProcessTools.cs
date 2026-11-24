using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using MsfsAddonLauncher.Models;

namespace MsfsAddonLauncher.Services;

public enum StartErgebnis { Gestartet, PfadFehlt, AdminAbgelehnt, Fehler }

/// <summary>Alles rund um Prozesse: erkennen, starten, schliessen.</summary>
public static class ProcessTools
{
    /// <summary>Programme, deren Name allein nichts aussagt; hier wird zusaetzlich der Pfad geprueft.</summary>
    public static readonly string[] AllerweltsNamen =
        { "node", "python", "pythonw", "java", "javaw", "electron", "cmd", "powershell", "pwsh", "wscript", "cscript", "dotnet" };

    /// <summary>
    /// Prozesse, die der Launcher nie schliesst: Ein Zusatzprogramm, das als Web-App im Browser laeuft,
    /// teilt sich den Prozessnamen mit allen anderen Browserfenstern.
    /// </summary>
    public static readonly string[] NieSchliessen =
        { "msedge", "chrome", "firefox", "brave", "opera", "vivaldi", "explorer", "steam" };

    // ---------- Namen ----------

    /// <summary>"A.exe; B" wird zu ["A", "B"].</summary>
    public static IEnumerable<string> NamenAufteilen(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;
        foreach (var teil in text.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var name = teil.Trim().Trim('"');
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            if (name.Length > 0) yield return name;
        }
    }

    public static string PfadAufloesen(string? pfad) =>
        Environment.ExpandEnvironmentVariables((pfad ?? "").Trim().Trim('"'));

    public static bool IstAdresse(string pfad) => pfad.Contains("://", StringComparison.Ordinal);

    /// <summary>Prozessnamen, an denen ein Programm erkannt wird.</summary>
    public static List<string> NamenVon(AddonProgram p)
    {
        var namen = NamenAufteilen(p.ProzessName).ToList();
        if (namen.Count == 0)
        {
            var pfad = PfadAufloesen(p.Pfad);
            if (!IstAdresse(pfad) && pfad.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                namen.Add(Path.GetFileNameWithoutExtension(pfad));
        }
        return namen;
    }

    /// <summary>
    /// Pfadfilter eines Programms. Ist keiner eingetragen und der Prozess heisst z. B. "node",
    /// wird der Ordner der Startdatei genommen, damit nicht ein fremdes node.exe gezaehlt wird.
    /// </summary>
    public static string FilterVon(AddonProgram p)
    {
        if (!string.IsNullOrWhiteSpace(p.ProzessPfadFilter)) return PfadAufloesen(p.ProzessPfadFilter);
        if (!string.IsNullOrWhiteSpace(p.ProzessName)) return "";
        var pfad = PfadAufloesen(p.Pfad);
        if (IstAdresse(pfad) || !pfad.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "";
        var name = Path.GetFileNameWithoutExtension(pfad);
        return AllerweltsNamen.Contains(name, StringComparer.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(pfad) ?? ""
            : "";
    }

    // ---------- Erkennen ----------

    /// <summary>
    /// Liefert die passenden Prozesse. Der Aufrufer muss sie freigeben (Dispose).
    /// Mit Pfadfilter zaehlen nur Prozesse, deren Pfad lesbar ist und den Filter enthaelt.
    /// </summary>
    public static List<Process> Finden(IEnumerable<string> namen, string? pfadFilter)
    {
        var treffer = new List<Process>();
        var eigene = Environment.ProcessId;
        var filter = (pfadFilter ?? "").Trim();

        foreach (var name in namen.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            Process[] kandidaten;
            try { kandidaten = Process.GetProcessesByName(name); }
            catch { continue; }

            foreach (var proc in kandidaten)
            {
                var passt = proc.Id != eigene;
                if (passt && filter.Length > 0)
                {
                    var pfad = PfadVon(proc.Id);
                    passt = pfad != null && pfad.Contains(filter, StringComparison.OrdinalIgnoreCase);
                }
                if (passt) treffer.Add(proc);
                else proc.Dispose();
            }
        }
        return treffer;
    }

    public static bool Laeuft(IEnumerable<string> namen, string? pfadFilter)
    {
        var liste = Finden(namen, pfadFilter);
        foreach (var p in liste) p.Dispose();
        return liste.Count > 0;
    }

    public static bool Laeuft(AddonProgram p)
    {
        var namen = NamenVon(p);
        return namen.Count > 0 && Laeuft(namen, FilterVon(p));
    }

    /// <summary>Voller Pfad eines Prozesses. Funktioniert auch bei Prozessen mit Adminrechten.</summary>
    public static string? PfadVon(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var puffer = new StringBuilder(1024);
            var laenge = puffer.Capacity;
            return QueryFullProcessImageName(handle, 0, puffer, ref laenge) ? puffer.ToString(0, laenge) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>true, wenn der Launcher selbst mit Adminrechten laeuft (soll er nicht).</summary>
    public static bool LauncherIstAdmin()
    {
        try
        {
            using var identitaet = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identitaet).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    // ---------- Starten ----------

    public static StartErgebnis Starten(AddonProgram p, out string meldung)
    {
        var pfad = PfadAufloesen(p.Pfad);
        if (pfad.Length == 0)
        {
            meldung = "Kein Pfad eingetragen.";
            return StartErgebnis.PfadFehlt;
        }
        if (!IstAdresse(pfad) && !File.Exists(pfad))
        {
            meldung = $"Datei nicht gefunden: {pfad}";
            return StartErgebnis.PfadFehlt;
        }

        try
        {
            // UseShellExecute = true: so funktionieren auch Verknuepfungen (.lnk), Skripte und
            // Adressen (steam://...), und "runas" loest die Windows-Adminabfrage nur fuer dieses Programm aus.
            var info = new ProcessStartInfo
            {
                FileName = pfad,
                Arguments = Environment.ExpandEnvironmentVariables(p.Argumente ?? ""),
                UseShellExecute = true
            };
            if (!IstAdresse(pfad))
            {
                var ordner = PfadAufloesen(p.Arbeitsverzeichnis);
                if (ordner.Length == 0 || !Directory.Exists(ordner))
                    ordner = Path.GetDirectoryName(pfad) ?? "";
                if (ordner.Length > 0) info.WorkingDirectory = ordner;
            }
            if (p.AlsAdmin) info.Verb = "runas";
            if (p.Minimiert) info.WindowStyle = ProcessWindowStyle.Minimized;

            using var gestartet = Process.Start(info);
            meldung = "";
            return StartErgebnis.Gestartet;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            meldung = "Die Windows-Adminabfrage wurde abgelehnt.";
            return StartErgebnis.AdminAbgelehnt;
        }
        catch (Exception ex)
        {
            meldung = ex.Message;
            return StartErgebnis.Fehler;
        }
    }

    // ---------- Schliessen ----------

    /// <summary>
    /// Bittet die Prozesse, sich zu beenden, wartet bis zum Timeout und beendet den Rest
    /// auf Wunsch hart. Liefert eine Kurzbeschreibung fuers Log.
    /// </summary>
    public static async Task<string> SchliessenAsync(
        IReadOnlyCollection<string> namen, string? pfadFilter, int timeoutSek, bool hartBeenden, CancellationToken ct)
    {
        var prozesse = await Task.Run(() => Finden(namen, pfadFilter), ct);
        if (prozesse.Count == 0) return "lief nicht";

        try
        {
            // 1. Hoeflich: wie ein Klick aufs X.
            foreach (var proc in prozesse)
            {
                try
                {
                    if (!proc.CloseMainWindow())
                        FensterSchliessen(proc.Id);
                }
                catch { /* Prozess evtl. schon weg oder mit Adminrechten; siehe unten */ }
            }

            // 2. Warten, ob alle von selbst gehen.
            var ende = DateTime.UtcNow.AddSeconds(timeoutSek);
            while (DateTime.UtcNow < ende && prozesse.Any(p => !IstBeendet(p)))
                await Task.Delay(300, ct);

            var uebrig = prozesse.Where(p => !IstBeendet(p)).ToList();
            if (uebrig.Count == 0) return "sauber geschlossen";
            if (!hartBeenden) return $"reagiert nicht ({uebrig.Count} Prozess(e) laufen weiter)";

            // 3. Hart beenden, inklusive Kindprozessen.
            var fehler = 0;
            foreach (var proc in uebrig)
            {
                try { proc.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { /* inzwischen von selbst beendet */ }
                catch { fehler++; }
            }
            return fehler == 0
                ? "nach Wartezeit hart beendet"
                : $"konnte nicht beendet werden ({fehler} Prozess(e); laufen vermutlich mit Adminrechten)";
        }
        finally
        {
            foreach (var proc in prozesse) proc.Dispose();
        }
    }

    private static bool IstBeendet(Process proc)
    {
        try { return proc.HasExited; }
        catch
        {
            // Kein Zugriff (Prozess mit Adminrechten): ueber die Prozessliste nachsehen.
            try
            {
                using var p = Process.GetProcessById(proc.Id);
                return false;
            }
            catch (ArgumentException) { return true; }
            catch { return false; }
        }
    }

    /// <summary>Schickt allen sichtbaren Hauptfenstern eines Prozesses ein "Schliessen".</summary>
    private static void FensterSchliessen(int pid)
    {
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var besitzer);
            if (besitzer == pid && IsWindowVisible(hwnd) && GetWindow(hwnd, GwOwner) == IntPtr.Zero)
                PostMessage(hwnd, WmClose, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }

    // ---------- Windows-API ----------

    private const int ProcessQueryLimitedInformation = 0x1000;
    private const uint WmClose = 0x0010;
    private const uint GwOwner = 4;

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(int zugriff, bool vererben, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(IntPtr handle, int flags, StringBuilder name, ref int groesse);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
