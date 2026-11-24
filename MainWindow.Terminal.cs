using System.Text;

namespace Cato;

/// Terminal page: one PowerShell session that lives while Cato runs. It is a child process, so it runs
/// elevated exactly when Cato does (FpsCounter.IsAdmin) and as a normal user otherwise.
/// shortcut: plain stdin/stdout, so interactive programs (vim, Read-Host, password prompts) don't work;
/// Esc kills the session. Upgrade to a ConPTY if real interactivity is needed.
public partial class MainWindow
{
    const string DoneMarker = "@@CATO@@"; // printed after each command: tells us it finished and where we are
    static readonly Brush OrangeB = Hex("#FF9F0A");

    Process? shell;
    bool shellBusy;
    string shellCwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    readonly List<string> shellHistory = new();
    int shellHistoryAt;

    void StartShell()
    {
        var psi = new ProcessStartInfo("powershell.exe", "-NoLogo -NoProfile -NonInteractive -Command -")
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            UseShellExecute = false, CreateNoWindow = true,
            StandardInputEncoding = new UTF8Encoding(false), StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = shellCwd,
        };
        shell = Process.Start(psi)!;
        shell.EnableRaisingEvents = true;
        shell.OutputDataReceived += (_, e) => ShellLine(e.Data);
        shell.ErrorDataReceived += (_, e) => ShellLine(e.Data);
        shell.Exited += (_, _) => Dispatcher.BeginInvoke(() => { if (shell?.HasExited == true) { shell = null; shellBusy = false; TermPrint("[sesión terminada]"); } });
        shell.BeginOutputReadLine();
        shell.BeginErrorReadLine();
        shell.StandardInput.WriteLine("[Console]::OutputEncoding = [Text.Encoding]::UTF8; $ProgressPreference = 'SilentlyContinue'");
        Application.Current.Exit += (_, _) => KillShell();
    }

    void KillShell()
    {
        try { shell?.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Debug.WriteLine(ex); }
        shell = null;
        shellBusy = false;
    }

    void ShellLine(string? line)
    {
        if (line == null) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (line.StartsWith(DoneMarker))
            {
                shellCwd = line[DoneMarker.Length..];
                shellBusy = false;
                UpdateTermPrompt();
            }
            else TermPrint(line);
        });
    }

    void TermPrint(string line)
    {
        TermOut.AppendText((line.Length > 400 ? line[..400] + "…" : line) + "\n");
        if (TermOut.LineCount > 300) TermOut.Text = TermOut.Text[TermOut.GetCharacterIndexFromLineIndex(100)..];
        TermOut.ScrollToEnd();
    }

    void UpdateTermPrompt() => TermPrompt.Text = (System.IO.Path.GetFileName(shellCwd.TrimEnd('\\')) is { Length: > 0 } leaf ? leaf : shellCwd) + " ›";

    void TermIn_Focus(object s, MouseButtonEventArgs e) => Activate(); // the notch window is not active by default

    void TermIn_KeyDown(object s, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                if (shellBusy) { KillShell(); TermPrint("^C"); } else { TermIn.Clear(); Keyboard.ClearFocus(); }
                e.Handled = true;
                return;
            case Key.Up or Key.Down when shellHistory.Count > 0:
                shellHistoryAt = Math.Clamp(shellHistoryAt + (e.Key == Key.Up ? -1 : 1), 0, shellHistory.Count);
                TermIn.Text = shellHistoryAt == shellHistory.Count ? "" : shellHistory[shellHistoryAt];
                TermIn.CaretIndex = TermIn.Text.Length;
                e.Handled = true;
                return;
            case Key.Enter:
                RunCommand(TermIn.Text.Trim());
                e.Handled = true;
                return;
        }
    }

    void RunCommand(string cmd)
    {
        if (cmd == "") return;
        if (shellBusy) { TermPrint("(en curso: Esc cancela)"); return; }
        shellHistory.Add(cmd);
        shellHistoryAt = shellHistory.Count;
        TermIn.Clear();
        if (cmd is "cls" or "clear") { TermOut.Clear(); return; }
        if (shell == null || shell.HasExited) StartShell();
        TermPrint("› " + cmd);
        shellBusy = true;
        try
        {
            shell!.StandardInput.WriteLine(cmd.ReplaceLineEndings(" "));
            shell.StandardInput.WriteLine($"Write-Output ('{DoneMarker}' + (Get-Location).ProviderPath)");
            shell.StandardInput.Flush();
        }
        catch (IOException ex) { Debug.WriteLine(ex); shellBusy = false; TermPrint("[la sesión se cerró; vuelve a intentarlo]"); KillShell(); }
    }
}
