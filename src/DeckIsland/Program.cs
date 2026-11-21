namespace DeckIsland;

internal static class Program
{
    // The app was called BrimDeck: its data folder (settings, themes, logs) is copied to the new name once, and the old one is left
    // alone in case a BrimDeck is still installed.
    private static void MigrateBrimDeckData()
    {
        try
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string previous = System.IO.Path.Combine(local, "BrimDeck"), current = System.IO.Path.Combine(local, "DeckIsland");
            if (!System.IO.Directory.Exists(previous) || System.IO.Directory.Exists(current)) return;
            foreach (string directory in System.IO.Directory.EnumerateDirectories(previous, "*", System.IO.SearchOption.AllDirectories))
                System.IO.Directory.CreateDirectory(System.IO.Path.Combine(current, System.IO.Path.GetRelativePath(previous, directory)));
            System.IO.Directory.CreateDirectory(current);
            foreach (string file in System.IO.Directory.EnumerateFiles(previous, "*", System.IO.SearchOption.AllDirectories))
                System.IO.File.Copy(file, System.IO.Path.Combine(current, System.IO.Path.GetRelativePath(previous, file)));
        }
        catch (Exception error) when (error is System.IO.IOException or UnauthorizedAccessException) { }
    }

    [STAThread]
    public static void Main(string[] args)
    {
        // A newer portable version started to replace this folder's files; it does that and exits without opening the app.
        if (Core.PortableUpdate.TryRun(args, out var exitCode)) { Environment.ExitCode = exitCode; return; }
        // The elevated instance a player's install starts writes its libcef.dll and exits without opening the app.
        if (args is [Native.KugouInstall.ElevatedSwitch, var mode, var library]) { Environment.ExitCode = Native.KugouInstall.RunElevated(mode, library); return; }
        if (args is [Native.NeteaseInstall.ElevatedSwitch, var neteaseMode, var neteaseLibrary]) { Environment.ExitCode = Native.NeteaseInstall.RunElevated(neteaseMode, neteaseLibrary); return; }
        // The elevated instance FramePermission starts adds the user to Performance Log Users and exits.
        if (args is [Native.FramePermission.ElevatedSwitch, var user]) { Environment.ExitCode = Native.FramePermission.RunElevated(user); return; }
        // The elevated instance the enhanced sensors page starts sets up or removes the sensor service and exits.
        if (args is [Native.SensorSetup.ElevatedSwitch, .. var sensors]) { Environment.ExitCode = Native.SensorSetup.RunElevated(sensors); return; }
        MigrateBrimDeckData();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
