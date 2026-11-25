using Avalonia;

namespace Cato;

static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        SelfCheck.Run();
        // Claude Code hook: the JSON arrives on stdin and goes to the running Cato; with none running there is nobody to tell.
        if (args.Contains("--activity"))
        {
            using var stdin = new StreamReader(Console.OpenStandardInput());
            if (Activity.FromHookJson(stdin.ReadToEnd()) is { } a)
                Ipc.TrySend("activity\t" + string.Join('\t', a.Name, a.Session, a.Project, a.Action.Replace('\t', ' '), a.Note));
            return 0;
        }
        // A second launch (menu, or a desktop shortcut bound to --toggle/--game) talks to the running Cato and exits.
        var command = Ipc.CommandFrom(args);
        if (Ipc.TrySend(command)) return 0;
        if (command.StartsWith("notify\t")) return 0; // not running: do not open a notch just to say it
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new X11PlatformOptions { EnableIme = false })
            .WithInterFont()
            .LogToTrace();
}
