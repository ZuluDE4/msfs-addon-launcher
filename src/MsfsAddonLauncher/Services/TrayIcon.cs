using WinForms = System.Windows.Forms;

namespace MsfsAddonLauncher.Services;

/// <summary>Symbol im Infobereich neben der Uhr, mit kleinem Menue.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly WinForms.NotifyIcon _icon;
    private readonly Func<IReadOnlyList<string>> _profile;

    public event Action? OeffnenGewuenscht;
    public event Action<string>? ProfilStartGewuenscht;
    public event Action? BeendenGewuenscht;

    public TrayIcon(Func<IReadOnlyList<string>> profile)
    {
        _profile = profile;

        System.Drawing.Icon? symbol = null;
        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null) symbol = System.Drawing.Icon.ExtractAssociatedIcon(exe);
        }
        catch { /* dann das Standardsymbol */ }

        var menue = new WinForms.ContextMenuStrip
        {
            // Dunkles Menue passend zum restlichen Programm.
            Renderer = new WinForms.ToolStripProfessionalRenderer(new DunkleFarben()),
            BackColor = DunkleFarben.Grund,
            ForeColor = System.Drawing.Color.White,
            ShowImageMargin = false
        };
        menue.Opening += (_, _) => MenueAufbauen(menue);

        _icon = new WinForms.NotifyIcon
        {
            Icon = symbol ?? System.Drawing.SystemIcons.Application,
            Text = "MSFS Addon Launcher",
            ContextMenuStrip = menue,
            Visible = true
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) OeffnenGewuenscht?.Invoke();
        };
        MenueAufbauen(menue);
    }

    private void MenueAufbauen(WinForms.ContextMenuStrip menue)
    {
        menue.Items.Clear();
        menue.Items.Add("Fenster öffnen", null, (_, _) => OeffnenGewuenscht?.Invoke());
        menue.Items.Add(new WinForms.ToolStripSeparator());
        foreach (var name in _profile())
        {
            var profil = name;
            menue.Items.Add($"Starten: {profil}", null, (_, _) => ProfilStartGewuenscht?.Invoke(profil));
        }
        menue.Items.Add(new WinForms.ToolStripSeparator());
        menue.Items.Add("Launcher beenden", null, (_, _) => BeendenGewuenscht?.Invoke());
        foreach (WinForms.ToolStripItem eintrag in menue.Items)
            eintrag.ForeColor = System.Drawing.Color.White;
    }

    /// <summary>Farben fuer das Rechtsklick-Menue.</summary>
    private sealed class DunkleFarben : WinForms.ProfessionalColorTable
    {
        public static readonly System.Drawing.Color Grund = System.Drawing.Color.FromArgb(43, 43, 43);
        private static readonly System.Drawing.Color Markiert = System.Drawing.Color.FromArgb(66, 66, 66);
        private static readonly System.Drawing.Color Rand = System.Drawing.Color.FromArgb(80, 80, 80);

        public override System.Drawing.Color ToolStripDropDownBackground => Grund;
        public override System.Drawing.Color ImageMarginGradientBegin => Grund;
        public override System.Drawing.Color ImageMarginGradientMiddle => Grund;
        public override System.Drawing.Color ImageMarginGradientEnd => Grund;
        public override System.Drawing.Color MenuBorder => Rand;
        public override System.Drawing.Color MenuItemBorder => Markiert;
        public override System.Drawing.Color MenuItemSelected => Markiert;
        public override System.Drawing.Color MenuItemSelectedGradientBegin => Markiert;
        public override System.Drawing.Color MenuItemSelectedGradientEnd => Markiert;
        public override System.Drawing.Color SeparatorDark => Rand;
        public override System.Drawing.Color SeparatorLight => Rand;
    }

    public void Hinweis(string titel, string text)
    {
        try { _icon.ShowBalloonTip(4000, titel, text, WinForms.ToolTipIcon.Info); }
        catch { /* Benachrichtigungen koennen abgeschaltet sein */ }
    }

    public void SetzeText(string text)
    {
        // Windows erlaubt hier hoechstens 127 Zeichen.
        _icon.Text = text.Length > 120 ? text[..120] : text;
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
    }
}
