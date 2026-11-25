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
            if (Activity.FromHookJson(stdin.ReadToEnd()) is { } a) Notify.Send("activity", string.Join('\t', a.Name, a.Session, a.Project, a.Text));
            Shutdown();
            return;
        }
        single = new Mutex(true, @"Local\Cato.SingleInstance", out var first);
        if (!first) { Shutdown(); return; } // one notch per session
        base.OnStartup(e);
    }
}
