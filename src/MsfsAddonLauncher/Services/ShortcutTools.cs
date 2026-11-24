using System.Runtime.InteropServices;

namespace MsfsAddonLauncher.Services;

/// <summary>Liest und erstellt Windows-Verknuepfungen (.lnk) ueber die eingebaute Windows-Shell.</summary>
public static class ShortcutTools
{
    public sealed record Verknuepfung(string Name, string Ziel, string Argumente, string Arbeitsverzeichnis);

    /// <summary>Liest alle Verknuepfungen der angegebenen Ordner (mit Unterordnern).</summary>
    public static List<Verknuepfung> Lesen(IEnumerable<string> ordner)
    {
        var ergebnis = new List<Verknuepfung>();
        object? shell = null;
        try
        {
            var typ = Type.GetTypeFromProgID("WScript.Shell");
            if (typ == null) return ergebnis;
            shell = Activator.CreateInstance(typ);
            if (shell == null) return ergebnis;
            dynamic sh = shell;

            foreach (var basis in ordner.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                IEnumerable<string> dateien;
                try
                {
                    dateien = Directory.EnumerateFiles(basis, "*.lnk", new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        IgnoreInaccessible = true,
                        MaxRecursionDepth = 4
                    }).ToList();
                }
                catch { continue; }

                foreach (var datei in dateien)
                {
                    object? eintrag = null;
                    try
                    {
                        eintrag = sh.CreateShortcut(datei);
                        dynamic e = eintrag!;
                        string ziel = e.TargetPath ?? "";
                        string args = e.Arguments ?? "";
                        string arbeit = e.WorkingDirectory ?? "";
                        if (ziel.Length > 0)
                            ergebnis.Add(new Verknuepfung(Path.GetFileNameWithoutExtension(datei), ziel, args, arbeit));
                    }
                    catch { /* defekte Verknuepfung: ueberspringen */ }
                    finally
                    {
                        if (eintrag != null) Marshal.FinalReleaseComObject(eintrag);
                    }
                }
            }
        }
        catch { /* Windows-Shell nicht verfuegbar: dann ohne Verknuepfungen */ }
        finally
        {
            if (shell != null) Marshal.FinalReleaseComObject(shell);
        }
        return ergebnis;
    }

    /// <summary>Legt eine Verknuepfung an oder ueberschreibt sie.</summary>
    public static void Erstellen(string lnkPfad, string ziel, string argumente, string beschreibung)
    {
        var typ = Type.GetTypeFromProgID("WScript.Shell")
                  ?? throw new InvalidOperationException("Die Windows-Shell (WScript.Shell) ist nicht verfügbar.");
        object shell = Activator.CreateInstance(typ)
                       ?? throw new InvalidOperationException("Die Windows-Shell konnte nicht gestartet werden.");
        object? eintrag = null;
        try
        {
            dynamic sh = shell;
            eintrag = sh.CreateShortcut(lnkPfad);
            dynamic e = eintrag!;
            e.TargetPath = ziel;
            e.Arguments = argumente;
            e.WorkingDirectory = Path.GetDirectoryName(ziel) ?? "";
            e.Description = beschreibung;
            e.IconLocation = ziel + ",0";
            e.Save();
        }
        finally
        {
            if (eintrag != null) Marshal.FinalReleaseComObject(eintrag);
            Marshal.FinalReleaseComObject(shell);
        }
    }

    /// <summary>Fuehrt eine Funktion auf einem eigenen STA-Thread aus (die Windows-Shell verlangt das).</summary>
    public static Task<T> AufStaThread<T>(Func<T> funktion)
    {
        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { tcs.SetResult(funktion()); }
            catch (Exception ex) { tcs.SetException(ex); }
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return tcs.Task;
    }
}
