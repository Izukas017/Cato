namespace Cato;

public partial class App : System.Windows.Application
{
    static Mutex? single; // held for the whole run: it is what marks this Cato as "the" Cato

    protected override void OnStartup(StartupEventArgs e)
    {
        // `Cato.exe --notify "texto"` only talks to the running Cato; it never opens a second notch
        if (Notify.TextFrom(e.Args) is { } text) { Notify.Send(text); Shutdown(); return; }
        single = new Mutex(true, @"Local\Cato.SingleInstance", out var first);
        if (!first) { Shutdown(); return; } // one notch per session
        base.OnStartup(e);
    }
}
