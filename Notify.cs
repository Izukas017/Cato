using System.IO.Pipes;

namespace Cato;

/// Commands to the running Cato through a named pipe (only your own user can connect), one line each: `kind<TAB>text`.
/// `Cato.exe --notify "texto"` shows an alert; `Cato.exe --activity` (Claude Code hooks) feeds the Claude page.
static class Notify
{
    // pipe names are global to the machine: one per Windows user, or two logged-in users would talk to each other's Cato
    static readonly string PipeName = "Cato.notify." + System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;

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
            c.Connect(1000);
            using var w = new StreamWriter(c);
            w.WriteLine(kind + "\t" + text.ReplaceLineEndings(" "));
            return true;
        }
        catch (Exception ex) when (ex is TimeoutException or IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); return false; } // Cato is not running (or we may not talk to it)
    }

    /// Only your own user may write, but at any integrity level: an elevated Cato (admin startup) must still hear the
    /// non-elevated hooks of Claude Code. A pipe made by an elevated process otherwise refuses normal-rights clients.
    static PipeSecurity Security()
    {
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
        var sec = new PipeSecurity();
        // user: read+write; label: medium integrity may write. Only an elevated Cato needs the label, and only it may set one:
        // a normal one gets "privilege not held" and would never listen.
        sec.SetSecurityDescriptorSddlForm(Environment.IsPrivilegedProcess ? $"D:(A;;GRGW;;;{sid})S:(ML;;NW;;;ME)" : $"D:(A;;GRGW;;;{sid})");
        return sec;
    }

    public static void Listen(Action<string, string> onMessage) =>
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    // several hooks can fire at once (parallel tool calls): accept each client and read it on its own task
                    var s = NamedPipeServerStreamAcl.Create(PipeName, PipeDirection.In, NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, PipeOptions.None, 0, 0, Security());
                    s.WaitForConnection();
                    _ = Task.Run(() =>
                    {
                        using (s)
                        {
                            string? line;
                            try { line = new StreamReader(s).ReadLine(); } catch (IOException) { return; }
                            if (line == null) return;
                            var tab = line.IndexOf('\t');
                            var text = tab < 0 ? "" : line[(tab + 1)..];
                            onMessage(tab < 0 ? line : line[..tab], text.Length > 600 ? text[..600] : text);
                        }
                    });
                }
                catch (IOException ex) { Debug.WriteLine(ex); Thread.Sleep(500); } // client vanished: listen again
            }
        }) { IsBackground = true, Name = "cato-notify" }.Start();
}
