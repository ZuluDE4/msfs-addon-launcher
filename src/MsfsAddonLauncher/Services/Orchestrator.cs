using MsfsAddonLauncher.Models;

namespace MsfsAddonLauncher.Services;

public enum LaufZustand
{
    Bereit,
    StartetVorSim,
    StartetSim,
    WartetAufSim,
    WartetAufSimConnect,
    StartetNachSim,
    Ueberwacht,
    Schliesst
}

/// <summary>
/// Der Startablauf: Programme vor dem Sim, Sim, Programme nach dem Sim, Ueberwachung, Aufraeumen.
/// Alle Methoden werden vom Oberflaechen-Thread aufgerufen und kehren dorthin zurueck;
/// nur die eigentlichen Prozessabfragen laufen im Hintergrund.
/// </summary>
public sealed class Orchestrator
{
    private CancellationTokenSource? _abbruch;
    private Task? _lauf;
    private List<AddonProgram> _laufProgramme = new();

    public LaufZustand Zustand { get; private set; } = LaufZustand.Bereit;
    public string ZustandText { get; private set; } = "Bereit";
    /// <summary>true vom Klick auf "Alles starten" bis zum Ende des Ablaufs (inklusive Ueberwachung).</summary>
    public bool Aktiv { get; private set; }

    public event Action? ZustandGeaendert;

    /// <summary>Wird bei "Nachfragen" aufgerufen; true = Programme schliessen.</summary>
    public Func<string, bool>? Nachfrage { get; set; }

    /// <summary>Der Sim wurde beendet und das Aufraeumen ist abgeschlossen.</summary>
    public event Action? SimEndeAbgeschlossen;

    private void Setze(LaufZustand zustand, string text)
    {
        Zustand = zustand;
        ZustandText = text;
        ZustandGeaendert?.Invoke();
    }

    // ---------- Gesamter Ablauf ----------

    public void Starten(AppConfig cfg, Profil profil)
    {
        if (Aktiv) return;
        Aktiv = true;
        _abbruch?.Dispose();
        _abbruch = new CancellationTokenSource();
        Setze(LaufZustand.StartetVorSim, "Ablauf startet …");
        _lauf = AusfuehrenAsync(cfg, profil, _abbruch.Token);
    }

    public void Abbrechen()
    {
        if (Aktiv) _abbruch?.Cancel();
    }

    private async Task AusfuehrenAsync(AppConfig cfg, Profil profil, CancellationToken ct)
    {
        // Momentaufnahme der Auswahl: spaetere Aenderungen in der Oberflaeche stoeren den Lauf nicht.
        var liste = cfg.Programme.Where(p => p.Aktiv && profil.Programme.Contains(p.Id)).ToList();
        var vorSim = liste.Where(p => p.Start == StartPhase.VorSim).ToList();
        var nachSim = liste.Where(p => p.Start == StartPhase.NachSim).ToList();
        _laufProgramme = liste;
        var sim = cfg.Sim;

        try
        {
            Log.Info($"===== Start: Profil \"{profil.Name}\" mit {liste.Count} Programm(en) =====");
            foreach (var p in liste) p.NichtErkanntGemeldet = false;
            foreach (var p in nachSim) p.SetStatus(ProgrammStatus.Wartet, "Wartet auf Sim");

            var simLiefSchon = await Task.Run(() => SimService.Laeuft(sim), ct);

            // 1. Programme, die keinen laufenden Sim brauchen.
            if (vorSim.Count > 0)
            {
                Setze(LaufZustand.StartetVorSim, "Starte Programme vor dem Sim …");
                foreach (var p in vorSim) await StarteMitVerzoegerungAsync(p, ct);
            }

            // 2. Der Simulator.
            if (simLiefSchon)
            {
                Log.Info("Der Sim läuft bereits und wird nicht noch einmal gestartet.");
            }
            else
            {
                if (sim.SimStarten)
                {
                    Setze(LaufZustand.StartetSim, "Starte den Flight Simulator …");
                    // Zwischen der ersten Pruefung und jetzt koennen Minuten liegen (Verzoegerungen).
                    if (await Task.Run(() => SimService.Laeuft(sim), ct))
                    {
                        Log.Info("Der Sim wurde inzwischen von Hand gestartet.");
                    }
                    else
                    {
                        var (ok, meldung) = await Task.Run(() =>
                        {
                            var erfolg = SimService.Starten(sim, out var m);
                            return (erfolg, m);
                        }, ct);
                        if (ok)
                        {
                            Log.Info(meldung);
                        }
                        else
                        {
                            Log.Fehler(meldung);
                            Log.Info("Starte den Sim von Hand; der Launcher wartet weiter auf ihn.");
                        }
                    }
                }
                else
                {
                    Log.Info("Sim-Start ist ausgeschaltet. Warte, bis du den Sim selbst startest.");
                }

                Setze(LaufZustand.WartetAufSim, "Warte auf den Sim-Prozess …");
                if (!await WarteAufSimAsync(sim, ct))
                {
                    Log.Fehler($"Der Sim-Prozess ({string.Join(", ", sim.ProzessNamen)}) ist nach " +
                               $"{sim.MaxWartenAufSimSek} s nicht erschienen. Programme \"nach Sim\" werden nicht gestartet.");
                    foreach (var p in nachSim.Where(p => p.Status == ProgrammStatus.Wartet))
                        p.SetStatus(ProgrammStatus.Aus, "Übersprungen");
                    return;
                }
                Log.Info("Sim-Prozess erkannt.");
            }

            // 3. Programme, die eine Sim-Verbindung brauchen.
            if (nachSim.Count > 0)
            {
                await WarteAufSimConnectAsync(sim, simLiefSchon, ct);

                Setze(LaufZustand.StartetNachSim, "Starte Programme nach dem Sim …");
                foreach (var p in nachSim)
                {
                    if (!await Task.Run(() => SimService.Laeuft(sim), ct))
                    {
                        Log.Warnung("Der Sim läuft nicht mehr. Die restlichen Programme werden nicht gestartet.");
                        foreach (var rest in nachSim.Where(r => r.Status == ProgrammStatus.Wartet))
                            rest.SetStatus(ProgrammStatus.Aus, "Übersprungen");
                        break;
                    }
                    await StarteMitVerzoegerungAsync(p, ct);
                }
            }

            Log.Info("Startablauf fertig. Der Sim wird jetzt überwacht.");

            // 4. Warten, bis der Sim beendet wird.
            Setze(LaufZustand.Ueberwacht, "Sim läuft – wird überwacht");
            await WarteAufSimEndeAsync(sim, ct);
            Log.Info("Der Sim wurde beendet.");

            // 5. Aufraeumen.
            Setze(LaufZustand.Schliesst, "Sim beendet – räume auf …");
            await NachSimEndeAsync(cfg, ct);
            SimEndeAbgeschlossen?.Invoke();
        }
        catch (OperationCanceledException)
        {
            Log.Warnung("Ablauf abgebrochen. Bereits gestartete Programme laufen weiter.");
            foreach (var p in liste.Where(p => p.Status == ProgrammStatus.Wartet))
                p.SetStatus(ProgrammStatus.Aus);
        }
        catch (Exception ex)
        {
            Log.Fehler("Unerwarteter Fehler im Ablauf: " + ex.Message);
        }
        finally
        {
            Aktiv = false;
            Setze(LaufZustand.Bereit, "Bereit");
        }
    }

    private static async Task<bool> WarteAufSimAsync(SimSettings sim, CancellationToken ct)
    {
        var ende = DateTime.UtcNow.AddSeconds(sim.MaxWartenAufSimSek);
        while (DateTime.UtcNow < ende)
        {
            if (await Task.Run(() => SimService.Laeuft(sim), ct)) return true;
            await Task.Delay(2000, ct);
        }
        return false;
    }

    private async Task WarteAufSimConnectAsync(SimSettings sim, bool simLiefSchon, CancellationToken ct)
    {
        if (!sim.SimConnectPruefen)
        {
            if (simLiefSchon || sim.WartezeitNachSimStartSek <= 0) return;
            Setze(LaufZustand.WartetAufSimConnect, $"Warte {sim.WartezeitNachSimStartSek} s, bis der Sim geladen hat …");
            Log.Info($"SimConnect-Prüfung ist aus: feste Wartezeit {sim.WartezeitNachSimStartSek} s.");
            await Task.Delay(TimeSpan.FromSeconds(sim.WartezeitNachSimStartSek), ct);
            return;
        }

        Setze(LaufZustand.WartetAufSimConnect, "Warte auf SimConnect …");
        var ende = DateTime.UtcNow.AddSeconds(sim.MaxWartenAufSimConnectSek);
        while (true)
        {
            var (bereit, pipe) = await Task.Run(() =>
            {
                var ok = SimService.SimConnectBereit(out var name);
                return (ok, name);
            }, ct);

            if (bereit)
            {
                Log.Info($"SimConnect ist bereit ({pipe}).");
                return;
            }
            if (DateTime.UtcNow >= ende)
            {
                Log.Warnung($"SimConnect wurde nach {sim.MaxWartenAufSimConnectSek} s nicht erkannt. " +
                            "Es geht trotzdem weiter. Falls Programme zu früh starten: Verzögerung erhöhen " +
                            "oder die SimConnect-Prüfung in den Einstellungen ausschalten.");
                return;
            }
            if (!await Task.Run(() => SimService.Laeuft(sim), ct)) return; // wird vom Aufrufer behandelt
            await Task.Delay(2000, ct);
        }
    }

    private static async Task WarteAufSimEndeAsync(SimSettings sim, CancellationToken ct)
    {
        // Erst nach zwei Fehlversuchen in Folge gilt der Sim als beendet. Das faengt
        // einzelne fehlgeschlagene Abfragen ab, ohne das Aufraeumen spuerbar zu verzoegern.
        var fehlversuche = 0;
        while (fehlversuche < 2)
        {
            await Task.Delay(3000, ct);
            fehlversuche = await Task.Run(() => SimService.Laeuft(sim), ct) ? 0 : fehlversuche + 1;
        }
    }

    private async Task NachSimEndeAsync(AppConfig cfg, CancellationToken ct)
    {
        var kandidaten = _laufProgramme.Where(p => p.BeiSimEndeSchliessen).ToList();
        if (kandidaten.Count == 0) return;

        switch (cfg.Verhalten.ProgrammeBeiSimEnde)
        {
            case SchliessenModus.Nie:
                Log.Info("Programme bleiben offen (Einstellung: nie schließen).");
                return;

            case SchliessenModus.Nachfragen:
                var namen = string.Join(", ", kandidaten.Select(p => p.Name));
                if (Nachfrage?.Invoke($"Der Sim wurde beendet.\n\nDiese Programme jetzt schließen?\n{namen}") != true)
                {
                    Log.Info("Programme bleiben offen (Nachfrage verneint).");
                    return;
                }
                break;
        }

        await SchliessenAsync(kandidaten, cfg.Verhalten, ct);
    }

    // ---------- Statusanzeige ----------

    /// <summary>So lange darf ein Programm nach dem Start brauchen, bis sein Prozess auftaucht (Updater!).</summary>
    private static readonly TimeSpan ErkennungsFrist = TimeSpan.FromSeconds(90);

    /// <summary>Prueft im Hintergrund, welche Programme laufen, und traegt das Ergebnis in die Liste ein.</summary>
    public static async Task StatusAktualisierenAsync(IReadOnlyList<AddonProgram> programme)
    {
        var liste = programme.ToList();
        var daten = await Task.Run(() => liste.Select(p =>
        {
            var namen = ProcessTools.NamenVon(p);
            var laeuft = namen.Count > 0 && ProcessTools.Laeuft(namen, ProcessTools.FilterVon(p));
            var pfad = ProcessTools.PfadAufloesen(p.Pfad);
            var fehlt = pfad.Length == 0 || (!ProcessTools.IstAdresse(pfad) && !File.Exists(pfad));
            return (laeuft, fehlt, erkennbar: namen.Count > 0);
        }).ToList());

        for (var i = 0; i < liste.Count; i++)
            StatusAnwenden(liste[i], daten[i].laeuft, daten[i].fehlt, daten[i].erkennbar);
    }

    private static void StatusAnwenden(AddonProgram p, bool laeuft, bool fehlt, bool erkennbar)
    {
        if (laeuft)
        {
            if (p.Status != ProgrammStatus.Laeuft) p.SetStatus(ProgrammStatus.Laeuft);
            return;
        }

        switch (p.Status)
        {
            case ProgrammStatus.Wartet:
            case ProgrammStatus.Fehler:
                return; // bleibt stehen, bis der Ablauf oder ein neuer Start es aendert

            case ProgrammStatus.Startet:
                var alter = DateTime.Now - (p.GestartetUm ?? DateTime.MinValue);
                if (!erkennbar)
                {
                    // Verknuepfung oder Adresse ohne Prozessnamen: mehr als "gestartet" laesst sich nicht sagen.
                    if (alter > TimeSpan.FromSeconds(5)) p.SetStatus(ProgrammStatus.Aus, "Gestartet (ohne Erkennung)");
                }
                else if (alter > ErkennungsFrist)
                {
                    p.SetStatus(ProgrammStatus.Fehler, "Nicht erkannt");
                    if (!p.NichtErkanntGemeldet)
                    {
                        p.NichtErkanntGemeldet = true;
                        Log.Warnung($"{p.Name}: gestartet, aber der Prozess \"{string.Join("; ", ProcessTools.NamenVon(p))}\" " +
                                    "ist nicht aufgetaucht. Prozessname in den Programm-Einstellungen prüfen " +
                                    "(im Task-Manager unter \"Details\" nachsehen).");
                    }
                }
                return;

            case ProgrammStatus.Laeuft:
                p.SetStatus(ProgrammStatus.Aus, "Beendet");
                return;

            default:
                var soll = fehlt ? ProgrammStatus.PfadFehlt : p.Aktiv ? ProgrammStatus.Aus : ProgrammStatus.Deaktiviert;
                if (p.Status != soll) p.SetStatus(soll);
                return;
        }
    }

    // ---------- Einzelne Programme ----------

    private async Task StarteMitVerzoegerungAsync(AddonProgram p, CancellationToken ct)
    {
        if (p.VerzoegerungSek > 0)
        {
            p.SetStatus(ProgrammStatus.Wartet, $"Wartet {p.VerzoegerungSek} s");
            await Task.Delay(TimeSpan.FromSeconds(p.VerzoegerungSek), ct);
        }
        await StarteAsync(p, ct);
    }

    /// <summary>Startet ein Programm, sofern es nicht schon laeuft. Fehler landen im Log, nie als Ausnahme.</summary>
    public async Task StarteAsync(AddonProgram p, CancellationToken ct = default)
    {
        try
        {
            if (await Task.Run(() => ProcessTools.Laeuft(p), ct))
            {
                p.SetStatus(ProgrammStatus.Laeuft);
                Log.Info($"{p.Name}: läuft bereits, wird nicht doppelt gestartet.");
                return;
            }

            // Im Hintergrund starten: bei "als Admin" wartet Windows auf die Antwort in der
            // Sicherheitsabfrage, und so lange soll das Fenster nicht einfrieren.
            var (ergebnis, meldung) = await Task.Run(() =>
            {
                var e = ProcessTools.Starten(p, out var m);
                return (e, m);
            }, ct);
            switch (ergebnis)
            {
                case StartErgebnis.Gestartet:
                    p.GestartetUm = DateTime.Now;
                    p.NichtErkanntGemeldet = false;
                    p.SetStatus(ProgrammStatus.Startet);
                    Log.Info($"{p.Name}: gestartet{(p.AlsAdmin ? " (als Admin)" : "")}.");
                    break;
                case StartErgebnis.PfadFehlt:
                    p.SetStatus(ProgrammStatus.PfadFehlt);
                    Log.Fehler($"{p.Name}: {meldung}");
                    break;
                default:
                    p.SetStatus(ProgrammStatus.Fehler, "Start fehlgeschlagen");
                    Log.Fehler($"{p.Name}: Start fehlgeschlagen. {meldung}");
                    break;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            p.SetStatus(ProgrammStatus.Fehler, "Start fehlgeschlagen");
            Log.Fehler($"{p.Name}: Start fehlgeschlagen. {ex.Message}");
        }
    }

    /// <summary>Schliesst die Programme gleichzeitig, damit die Wartezeit nur einmal anfaellt.</summary>
    public async Task SchliessenAsync(IReadOnlyCollection<AddonProgram> programme, VerhaltenSettings verhalten, CancellationToken ct = default)
    {
        if (programme.Count == 0) return;
        var vorher = (Zustand, ZustandText);
        Setze(LaufZustand.Schliesst, "Schließe Programme …");
        try
        {
            var aufgaben = programme.Select(async p =>
            {
                var namen = ProcessTools.NamenVon(p);
                if (namen.Count == 0)
                {
                    Log.Warnung($"{p.Name}: kein Prozessname bekannt, kann nicht geschlossen werden.");
                    return;
                }
                var geschuetzt = namen.FirstOrDefault(n => ProcessTools.NieSchliessen.Contains(n, StringComparer.OrdinalIgnoreCase));
                if (geschuetzt != null)
                {
                    // Sonst wuerden z. B. alle Browserfenster zugehen, nur weil ein Zusatzprogramm im Browser laeuft.
                    Log.Warnung($"{p.Name}: wird nicht geschlossen, weil \"{geschuetzt}\" auch für andere Dinge läuft (Browser, Windows).");
                    return;
                }
                try
                {
                    var ergebnis = await ProcessTools.SchliessenAsync(
                        namen, ProcessTools.FilterVon(p), verhalten.SchliessenTimeoutSek, verhalten.HartBeenden, ct);
                    p.GestartetUm = null;
                    Log.Info($"{p.Name}: {ergebnis}.");
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log.Fehler($"{p.Name}: Schließen fehlgeschlagen. {ex.Message}");
                }
            }).ToList();
            await Task.WhenAll(aufgaben);
        }
        finally
        {
            // Wurde von Hand waehrend eines Laufs geschlossen, geht es danach im alten Zustand weiter.
            if (Aktiv && vorher.Zustand != LaufZustand.Schliesst) Setze(vorher.Zustand, vorher.ZustandText);
            else Setze(LaufZustand.Bereit, "Bereit");
        }
    }
}
