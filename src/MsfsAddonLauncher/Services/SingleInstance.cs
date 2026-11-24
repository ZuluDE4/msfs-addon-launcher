using System.IO.Pipes;
using System.Text;

namespace MsfsAddonLauncher.Services;

/// <summary>
/// Sorgt dafuer, dass der Launcher nur einmal laeuft. Ein zweiter Aufruf (z. B. ueber die
/// Desktop-Verknuepfung mit "--start") reicht seine Argumente an das laufende Fenster weiter.
/// </summary>
public static class SingleInstance
{
    private static readonly string Kennung =
        "MsfsAddonLauncher-" + Environment.UserName.Replace('\\', '_').Replace('/', '_');

    private static Mutex? _mutex;
    private static CancellationTokenSource? _stopp;

    /// <summary>Argumente eines zweiten Aufrufs. Kommt aus einem Hintergrund-Thread.</summary>
    public static event Action<string[]>? ArgumenteEmpfangen;

    /// <summary>true = dies ist die erste Instanz.</summary>
    public static bool Belegen()
    {
        _mutex = new Mutex(true, Kennung, out var neu);
        if (!neu)
        {
            _mutex.Dispose();
            _mutex = null;
            return false;
        }
        _stopp = new CancellationTokenSource();
        _ = LauschenAsync(_stopp.Token);
        return true;
    }

    public static void Freigeben()
    {
        _stopp?.Cancel();
        try { _mutex?.ReleaseMutex(); } catch { /* gehoert einem anderen Thread: egal beim Beenden */ }
        _mutex?.Dispose();
        _mutex = null;
    }

    /// <summary>Schickt die Argumente an die laufende Instanz. false = nicht erreicht.</summary>
    public static bool AnErsteInstanzSenden(string[] args)
    {
        try
        {
            using var pipe = new NamedPipeClientStream(".", Kennung, PipeDirection.Out);
            pipe.Connect(3000);
            var daten = Encoding.UTF8.GetBytes(string.Join("\n", args));
            pipe.Write(daten, 0, daten.Length);
            pipe.Flush();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task LauschenAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(
                    Kennung, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                using var leser = new StreamReader(pipe, Encoding.UTF8);
                var text = await leser.ReadToEndAsync(ct);
                var args = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                ArgumenteEmpfangen?.Invoke(args);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
                // Kurz warten, damit ein Dauerfehler keine Endlosschleife mit voller Last wird.
                try { await Task.Delay(1000, ct); } catch { return; }
            }
        }
    }
}
