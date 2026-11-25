using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace Cato;

/// Terminal page: one shell that lives while Cato runs (port of the Windows page). It is a child process, so it has the
/// same user (and the same rights) as Cato: the title says "root" when Cato runs as root and "usuario" otherwise.
/// shortcut: plain stdin/stdout, so interactive programs (vim, sudo prompts, ssh passwords) don't work; Esc kills the session.
/// Inside a Flatpak it is the sandbox's shell, not the host's. Upgrade to a pty if real interactivity is needed.
public partial class NotchWindow
{
    readonly TerminalPage terminal;

    /// Gives this window the keyboard (it is a dock, which window managers never focus on their own).
    void TakeKeyboard()
    {
        Activate();
        if (TryGetPlatformHandle()?.Handle is { } h) X11Input.Focus(h);
    }

    sealed class TerminalPage
    {
        const string DoneMarker = "@@CATO@@"; // printed after each command: tells us it finished and where we are
        public readonly Grid Root = new() { IsVisible = false, RowDefinitions = new RowDefinitions("*,Auto") };
        readonly TextBox output = new()
        {
            IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Background = B("#17FFFFFF"), BorderThickness = new Thickness(0),
            Padding = new Thickness(8, 4), Margin = new Thickness(0, 0, 0, 4), FontSize = 11, FontFamily = new FontFamily("Cascadia Mono, DejaVu Sans Mono, monospace"),
        };
        readonly TextBox input = new()
        {
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 4), FontSize = 11,
            FontFamily = new FontFamily("Cascadia Mono, DejaVu Sans Mono, monospace"), Watermark = "Intro ejecuta · Esc cancela · ↑↓ historial",
        };
        readonly TextBlock prompt = new() { Foreground = B("#8CFFFFFF"), FontSize = 11, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), MaxWidth = 150, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly Action activate;
        readonly List<string> history = new();
        int historyAt;
        Process? shell;
        bool busy;
        string cwd = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public bool HasFocus => input.IsFocused;

        public TerminalPage(Action activate)
        {
            this.activate = activate;
            var bar = new Grid { Background = B("#1FFFFFFF") };
            bar.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
            bar.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            Grid.SetColumn(input, 1);
            bar.Children.Add(prompt);
            bar.Children.Add(input);
            Grid.SetRow(bar, 1);
            Root.Children.Add(output);
            Root.Children.Add(bar);
            input.PointerPressed += (_, _) => activate(); // the notch window is not active by default
            input.KeyDown += KeyDown;
            UpdatePrompt();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Kill();
        }

        void Start()
        {
            var sh = File.Exists("/bin/bash") ? "/bin/bash" : "/bin/sh";
            var psi = new ProcessStartInfo(sh) { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = cwd };
            if (sh.EndsWith("bash")) { psi.ArgumentList.Add("--noprofile"); psi.ArgumentList.Add("--norc"); }
            shell = Process.Start(psi)!;
            shell.EnableRaisingEvents = true;
            shell.OutputDataReceived += (_, e) => Line(e.Data);
            shell.Exited += (_, _) => Dispatcher.UIThread.Post(() => { if (shell?.HasExited == true) { shell = null; busy = false; Print("[sesión terminada]"); } });
            shell.BeginOutputReadLine();
            shell.StandardInput.WriteLine("exec 2>&1"); // errors go to the same stream as the output
        }

        void Kill()
        {
            try { shell?.Kill(entireProcessTree: true); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { Debug.WriteLine(ex); }
            shell = null;
            busy = false;
        }

        void Line(string? line)
        {
            if (line == null) return;
            Dispatcher.UIThread.Post(() =>
            {
                if (line.StartsWith(DoneMarker)) { cwd = line[DoneMarker.Length..]; busy = false; UpdatePrompt(); }
                else Print(line);
            });
        }

        void Print(string line)
        {
            var text = (output.Text ?? "") + (line.Length > 400 ? line[..400] + "…" : line) + "\n";
            output.Text = text.Length > 20000 ? text[^12000..] : text;
            output.CaretIndex = output.Text.Length; // keeps the last line in view
        }

        void UpdatePrompt() => prompt.Text = (Path.GetFileName(cwd.TrimEnd('/')) is { Length: > 0 } leaf ? leaf : cwd) + " ›";

        void KeyDown(object? s, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                    if (busy) { Kill(); Print("^C"); } else input.Text = "";
                    e.Handled = true;
                    break;
                case Key.Up or Key.Down when history.Count > 0:
                    historyAt = Math.Clamp(historyAt + (e.Key == Key.Up ? -1 : 1), 0, history.Count);
                    input.Text = historyAt == history.Count ? "" : history[historyAt];
                    input.CaretIndex = input.Text.Length;
                    e.Handled = true;
                    break;
                case Key.Enter:
                    Run((input.Text ?? "").Trim());
                    e.Handled = true;
                    break;
            }
        }

        void Run(string cmd)
        {
            if (cmd == "") return;
            if (busy) { Print("(en curso: Esc cancela)"); return; }
            history.Add(cmd);
            historyAt = history.Count;
            input.Text = "";
            if (cmd is "clear" or "cls") { output.Text = ""; return; }
            if (shell == null || shell.HasExited) Start();
            Print("› " + cmd);
            busy = true;
            try
            {
                shell!.StandardInput.WriteLine(cmd.ReplaceLineEndings(" "));
                shell.StandardInput.WriteLine($"printf '{DoneMarker}%s\\n' \"$PWD\"");
                shell.StandardInput.Flush();
            }
            catch (IOException ex) { Debug.WriteLine(ex); busy = false; Print("[la sesión se cerró; vuelve a intentarlo]"); Kill(); }
        }
    }
}
