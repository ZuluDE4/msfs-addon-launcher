namespace MsfsAddonLauncher.Services;

/// <summary>Kleine Rechenhilfen fuer Listen, ohne Bezug zur Oberflaeche.</summary>
public static class ListTools
{
    /// <summary>
    /// Rechnet eine Einfuegestelle in den neuen Index eines verschobenen Eintrags um.
    /// </summary>
    /// <param name="alt">Bisheriger Index des Eintrags.</param>
    /// <param name="einfuegeStelle">
    /// Luecke, in die der Eintrag soll: 0 = vor den ersten Eintrag, anzahl = hinter den letzten.
    /// </param>
    /// <param name="anzahl">Anzahl der Eintraege in der Liste.</param>
    /// <returns>Der neue Index; gleich <paramref name="alt"/>, wenn sich nichts aendert.</returns>
    public static int ZielIndex(int alt, int einfuegeStelle, int anzahl)
    {
        if (anzahl <= 0 || alt < 0 || alt >= anzahl) return alt;
        einfuegeStelle = Math.Clamp(einfuegeStelle, 0, anzahl);
        // Liegt die Luecke hinter dem Eintrag, rueckt sie um eins vor, sobald er herausgenommen ist.
        var neu = einfuegeStelle > alt ? einfuegeStelle - 1 : einfuegeStelle;
        return Math.Clamp(neu, 0, anzahl - 1);
    }
}
