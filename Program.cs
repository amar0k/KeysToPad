using System.Diagnostics;

namespace KeysToPad;

internal static class Program
{
    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    [STAThread]
    static int Main(string[] args)
    {
        // Checks that open no window (used while building; safe to run any time):
        //   --selftest [file]  the mapping maths and the settings reader
        //   --hooktest [file]  the keyboard and mouse watch, with one made-up key press and mouse nudge
        //   --padtest  [file]  the virtual controller, read back the way a game reads it (needs the driver)
        if (args.Length > 0 && args[0].StartsWith("--", StringComparison.Ordinal))
        {
            string? file = args.Length > 1 ? args[1] : null;
            return args[0] switch
            {
                "--selftest" => SelfTest.Run(file),
                "--hooktest" => SelfTest.RunHooks(file),
                "--padtest" => SelfTest.RunPad(file),
                _ => 2,
            };
        }

        ApplicationConfiguration.Initialize();
        using var once = new Mutex(true, "KeysToPad.OneCopy", out bool first);
        if (!first)
        {
            MessageBox.Show("KeysToPad is already running. Look for its controller icon beside the clock.", "KeysToPad", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Profile profile;
        try
        {
            if (!File.Exists(SettingsPath)) File.WriteAllText(SettingsPath, Settings.DefaultFile);
            profile = Profile.From(Settings.Parse(TrayApp.ReadSettings(SettingsPath)));
        }
        catch (Exception error) when (error is FormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show($"{error.Message}\n\nThe built-in settings are used for now. Fix the file ({SettingsPath}) and choose \"Reload settings\" from the icon beside the clock.",
                "KeysToPad: problem with settings.json", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            profile = Profile.From(Settings.Parse(Settings.DefaultFile));
        }

        VirtualPad pad;
        try
        {
            pad = new VirtualPad();
        }
        catch (Exception error)
        {
            bool missing = error.GetType().Name.Contains("NotFound", StringComparison.Ordinal);
            var answer = MessageBox.Show(
                (missing ? "The ViGEmBus driver is not installed, so KeysToPad cannot create the virtual Xbox controller.\n\n"
                         : $"KeysToPad could not create the virtual Xbox controller ({error.GetType().Name}: {error.Message}).\n\n") +
                "Install the free ViGEmBus driver once (the ViGEmBus setup program from its official page), then start KeysToPad again.\n\n" +
                "Open the driver's download page now?",
                "KeysToPad: driver needed", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (answer == DialogResult.Yes)
            {
                try { Process.Start(new ProcessStartInfo(VirtualPad.DriverPage) { UseShellExecute = true }); } catch { }
            }
            return 1;
        }

        Application.Run(new TrayApp(SettingsPath, profile, pad));
        return 0;
    }
}
