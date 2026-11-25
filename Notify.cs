using System.IO.Pipes;

namespace Cato;

/// Commands to the running Cato through a named pipe (only your own user can connect), one line each: `kind<TAB>text`.
/// `Cato.exe --notify "texto"` shows an alert; `Cato.exe --activity` (Claude Code hooks) feeds the Claude page.
static class Notify
{
    const string PipeName = "Cato.notify";

    /// The text after --notify, or null when the flag is absent.
    public static string? TextFrom(string[] args)
    {
        var i = Array.IndexOf(args, "--notify");
        return i < 0 ? null : i + 1 < args.Length ? args[i + 1] : "";
    }

    public static bool Send(string kind, string text)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            c.Connect(300);
            using var w = new StreamWriter(c);
            w.WriteLine(kind + "\t" + text.ReplaceLineEndings(" "));
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException) { return false; } // Cato is not running
    }

    public static void Listen(Action<string, string> onMessage) =>
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    s.WaitForConnection();
                    var line = new StreamReader(s).ReadLine();
                    if (line == null) continue;
                    var tab = line.IndexOf('\t');
                    var text = tab < 0 ? "" : line[(tab + 1)..];
                    onMessage(tab < 0 ? line : line[..tab], text.Length > 200 ? text[..200] : text);
                }
                catch (IOException ex) { Debug.WriteLine(ex); Thread.Sleep(500); } // client vanished: listen again
            }
        }) { IsBackground = true, Name = "cato-notify" }.Start();
}
