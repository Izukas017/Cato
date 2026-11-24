namespace Cato;

public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        // `Cato.exe --notify "texto"` only talks to the running Cato; it never opens a second notch
        if (Notify.TextFrom(e.Args) is { } text) { Notify.Send(text); Shutdown(); return; }
        base.OnStartup(e);
    }
}
