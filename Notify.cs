using System.IO.Pipes;

namespace Cato;

/// `Cato.exe --notify "texto"` reaches the running Cato through a named pipe (only your own user can connect)
/// and shows an alert in the notch. Claude Code's Stop hook uses it to say "he terminado".
static class Notify
{
    const string PipeName = "Cato.notify";

    /// The text after --notify, or null when the flag is absent.
    public static string? TextFrom(string[] args)
    {
        var i = Array.IndexOf(args, "--notify");
        return i < 0 ? null : i + 1 < args.Length ? args[i + 1] : "";
    }

    public static bool Send(string text)
    {
        try
        {
            using var c = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            c.Connect(300);
            using var w = new StreamWriter(c);
            w.WriteLine(text.ReplaceLineEndings(" "));
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException) { return false; } // Cato is not running
    }

    public static void Listen(Action<string> onText) =>
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var s = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                    s.WaitForConnection();
                    var line = new StreamReader(s).ReadLine();
                    if (line != null) onText(line.Length > 80 ? line[..80] : line);
                }
                catch (IOException ex) { Debug.WriteLine(ex); Thread.Sleep(500); } // client vanished: listen again
            }
        }) { IsBackground = true, Name = "cato-notify" }.Start();
}
