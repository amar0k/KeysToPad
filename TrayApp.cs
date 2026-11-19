using System.Diagnostics;
using System.Drawing.Drawing2D;

namespace KeysToPad;

/// <summary>The icon beside the clock: shows whether controller mode is on, and holds the menu.</summary>
internal sealed class TrayApp : ApplicationContext
{
    readonly string settingsPath;
    readonly NotifyIcon tray;
    readonly ToolStripMenuItem modeItem;
    readonly Icon onIcon, offIcon;
    readonly VirtualPad pad;
    readonly Engine engine;
    readonly FileSystemWatcher watcher;
    readonly System.Windows.Forms.Timer reloadTimer;
    Profile profile;

    public TrayApp(string settingsPath, Profile profile, VirtualPad pad)
    {
        this.settingsPath = settingsPath;
        this.profile = profile;
        this.pad = pad;
        onIcon = DrawIcon(Color.FromArgb(16, 124, 16));
        offIcon = DrawIcon(Color.FromArgb(110, 110, 110));

        modeItem = new ToolStripMenuItem("Controller mode") { CheckOnClick = false };
        modeItem.Click += (_, _) => engine!.Toggle();
        var menu = new ContextMenuStrip();
        menu.Items.Add(modeItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Edit settings", null, (_, _) => Open("notepad.exe", $"\"{settingsPath}\""));
        menu.Items.Add("Reload settings", null, (_, _) => Reload(announce: true));
        menu.Items.Add("Open folder", null, (_, _) => Open("explorer.exe", $"\"{Path.GetDirectoryName(settingsPath)}\""));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        tray = new NotifyIcon { Icon = offIcon, ContextMenuStrip = menu, Visible = true };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) engine!.Toggle(); };

        engine = new Engine(pad, profile);
        engine.ActiveChanged += OnActiveChanged;
        engine.Start();
        Show(engine.Active);

        // Saving the settings file applies it. Editors often write twice, so wait a moment first.
        reloadTimer = new System.Windows.Forms.Timer { Interval = 300 };
        reloadTimer.Tick += (_, _) => { reloadTimer.Stop(); Reload(announce: true); };
        watcher = new FileSystemWatcher(Path.GetDirectoryName(settingsPath)!, Path.GetFileName(settingsPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            SynchronizingObject = null,
        };
        var ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        FileSystemEventHandler changed = (_, _) => ui.Post(_ => { reloadTimer.Stop(); reloadTimer.Start(); }, null);
        watcher.Changed += changed;
        watcher.Created += changed;
        watcher.Renamed += (_, _) => ui.Post(_ => { reloadTimer.Stop(); reloadTimer.Start(); }, null);
        watcher.EnableRaisingEvents = true;

        tray.ShowBalloonTip(4000, "KeysToPad is running", $"Press {profile.Toggle.Text} to switch controller mode on and off.", ToolTipIcon.Info);
    }

    void OnActiveChanged(bool on)
    {
        Show(on);
        if (profile.Beep) ThreadPool.QueueUserWorkItem(_ => Native.Beep(on ? 880u : 440u, 70));
    }

    void Show(bool on)
    {
        tray.Icon = on ? onIcon : offIcon;
        tray.Text = on ? $"KeysToPad: controller mode ON ({profile.Toggle.Text} to turn off)" : $"KeysToPad: off ({profile.Toggle.Text} to turn on)";
        modeItem.Checked = on;
        modeItem.Text = $"Controller mode ({profile.Toggle.Text})";
    }

    void Reload(bool announce)
    {
        try
        {
            var next = Profile.From(Settings.Parse(ReadSettings(settingsPath)));
            profile = next;
            engine.SetProfile(next);
            Show(engine.Active);
            if (announce) tray.ShowBalloonTip(2000, "KeysToPad", "Settings applied.", ToolTipIcon.Info);
        }
        catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException)
        {
            tray.ShowBalloonTip(8000, "KeysToPad: settings not applied", error.Message + " The previous settings are still in use.", ToolTipIcon.Warning);
        }
    }

    /// <summary>Reads the settings file, waiting briefly if an editor still has it open for writing.</summary>
    public static string ReadSettings(string path)
    {
        for (int attempt = 0; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < 4) { Thread.Sleep(40); }
        }
    }

    static void Open(string program, string arguments)
    {
        try { Process.Start(new ProcessStartInfo(program, arguments) { UseShellExecute = true }); } catch { }
    }

    static Icon DrawIcon(Color body)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var path = new GraphicsPath();
            var box = new Rectangle(1, 7, 30, 19);
            const int r = 9;
            path.AddArc(box.Left, box.Top, r * 2, r * 2, 180, 90);
            path.AddArc(box.Right - r * 2, box.Top, r * 2, r * 2, 270, 90);
            path.AddArc(box.Right - r * 2, box.Bottom - r * 2, r * 2, r * 2, 0, 90);
            path.AddArc(box.Left, box.Bottom - r * 2, r * 2, r * 2, 90, 90);
            path.CloseFigure();
            using var fill = new SolidBrush(body);
            g.FillPath(fill, path);
            g.FillRectangle(Brushes.White, 7, 15, 8, 3);    // d-pad
            g.FillRectangle(Brushes.White, 9.5f, 12.5f, 3, 8);
            g.FillEllipse(Brushes.White, 19, 16, 4, 4);     // buttons
            g.FillEllipse(Brushes.White, 23.5f, 12, 4, 4);
        }
        IntPtr handle = bitmap.GetHicon();
        try { return (Icon)Icon.FromHandle(handle).Clone(); }
        finally { Native.DestroyIcon(handle); }
    }

    protected override void ExitThreadCore()
    {
        watcher.Dispose();
        reloadTimer.Dispose();
        engine.Dispose();
        pad.Dispose();
        tray.Visible = false;
        tray.Dispose();
        base.ExitThreadCore();
    }
}
