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
        // Claude Code status line: forward the plan limits to the running Cato and print them as the line.
        if (args.Contains("--statusline"))
        {
            using var stdin = new StreamReader(Console.OpenStandardInput());
            var line = "Cato";
            if (Activity.Limits(stdin.ReadToEnd()) is { } l)
            {
                var inv = CultureInfo.InvariantCulture;
                Ipc.TrySend("limits\t" + string.Join('\t', l.Five.ToString(inv), l.FiveReset, l.Week.ToString(inv), l.WeekReset));
                var parts = new List<string>();
                if (l.Five >= 0) parts.Add($"5 h {l.Five:0} %");
                if (l.Week >= 0) parts.Add($"7 d {l.Week:0} %");
                if (parts.Count > 0) line = string.Join(" · ", parts);
            }
            Console.Write(line);
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
