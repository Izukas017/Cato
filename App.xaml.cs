using System.Text;

namespace Cato;

public partial class App : System.Windows.Application
{
    static Mutex? single; // held for the whole run: it is what marks this Cato as "the" Cato

    protected override void OnStartup(StartupEventArgs e)
    {
        // These two only talk to the running Cato; they never open a second notch.
        if (Notify.TextFrom(e.Args) is { } text) { Notify.Send("notify", text); Shutdown(); return; }
        if (e.Args.Contains("--activity"))
        {
            using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            if (Activity.FromHookJson(stdin.ReadToEnd()) is { } a) Notify.Send("activity", string.Join('\t', a.Name, a.Session, a.Project, a.Action.Replace('\t', ' '), a.Note));
            Shutdown();
            return;
        }
        if (e.Args.Contains("--statusline")) // Claude Code status line: forward the plan limits, print them as the line
        {
            using var stdin = new StreamReader(Console.OpenStandardInput(), Encoding.UTF8);
            var line = "Cato";
            if (Activity.Limits(stdin.ReadToEnd()) is { } l)
            {
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                Notify.Send("limits", string.Join('\t', l.Five.ToString(inv), l.FiveReset, l.Week.ToString(inv), l.WeekReset));
                var parts = new List<string>();
                if (l.Five >= 0) parts.Add($"5 h {l.Five:0} %");
                if (l.Week >= 0) parts.Add($"7 d {l.Week:0} %");
                if (parts.Count > 0) line = string.Join(" · ", parts);
            }
            using (var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false))) stdout.Write(line);
            Shutdown();
            return;
        }
        var first = false;
        try { single = new Mutex(true, @"Local\Cato.SingleInstance", out first); }
        catch (UnauthorizedAccessException) { } // the running copy is elevated and ours is not: it is still "already running"
#if DEBUG
        if (e.Args.Any(a => a.StartsWith("--show="))) first = true; // dev screenshots run beside the installed Cato
#endif
        if (!first) { Shutdown(); return; } // one notch per session
        base.OnStartup(e);
    }
}
