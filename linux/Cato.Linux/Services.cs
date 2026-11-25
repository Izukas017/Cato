using System.Net.Sockets;
using System.Text;
using Tmds.DBus;

namespace Cato;

/// Single instance + commands: `cato --toggle`, `cato --game`, `cato --settings` reach the running Cato
/// through a Unix socket. Desktops can bind those commands to keyboard shortcuts.
static class Ipc
{
    static string SocketPath =>
        Path.Combine(Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR") is { Length: > 0 } d ? d : Path.GetTempPath(), "cato.sock");

    /// `toggle`, `game`, `settings`, or `notify<TAB>text` for `cato --notify "text"`.
    public static string CommandFrom(string[] args)
    {
        var i = Array.IndexOf(args, "--notify");
        if (i >= 0) return "notify\t" + (i + 1 < args.Length ? args[i + 1] : "");
        return args.FirstOrDefault(a => a is "--toggle" or "--game" or "--settings")?.TrimStart('-') ?? "settings";
    }

    public static bool TrySend(string command)
    {
        if (!File.Exists(SocketPath)) return false;
        try
        {
            using var s = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            s.Connect(new UnixDomainSocketEndPoint(SocketPath));
            s.Send(Encoding.UTF8.GetBytes(command.ReplaceLineEndings(" ") + "\n"));
            return true;
        }
        catch (SocketException) { return false; } // stale socket left by a crash: we become the instance
    }

    public static void Listen(Action<string> onCommand)
    {
        try { File.Delete(SocketPath); } catch (IOException) { }
        var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint(SocketPath));
        server.Listen(4);
        new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var c = server.Accept();
                    // read until the client closes: activity events carry a few hundred characters
                    var all = new MemoryStream();
                    var buf = new byte[2048];
                    int n;
                    while (all.Length < 8192 && (n = c.Receive(buf)) > 0) all.Write(buf, 0, n);
                    var cmd = Encoding.UTF8.GetString(all.ToArray()).TrimEnd('\r', '\n'); // not Trim(): an empty last field must keep its tab
                    if (cmd != "") onCommand(cmd);
                }
                catch (SocketException) { return; }
            }
        }) { IsBackground = true, Name = "cato-ipc" }.Start();
    }
}

/// X11 input shape: only the notch takes clicks; the rest of the (transparent) window lets them through.
static class X11Input
{
    [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
    [DllImport("libX11.so.6")] static extern int XFlush(IntPtr display);
    [DllImport("libX11.so.6")] static extern int XSetInputFocus(IntPtr display, IntPtr window, int revertTo, IntPtr time);

    /// A dock window never gets keyboard focus from the window manager, so take it ourselves (needed to type in the terminal and task boxes).
    public static void Focus(IntPtr window)
    {
        if (broken || window == IntPtr.Zero) return;
        try
        {
            if (display == IntPtr.Zero) display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) { broken = true; return; }
            XSetInputFocus(display, window, 1, IntPtr.Zero); // RevertToPointerRoot, CurrentTime
            XFlush(display);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { Debug.WriteLine(ex); broken = true; }
    }
    [DllImport("libXext.so.6")]
    static extern void XShapeCombineRectangles(IntPtr display, IntPtr window, int kind, int x, int y, XRectangle[] rects, int n, int op, int ordering);

    [StructLayout(LayoutKind.Sequential)]
    struct XRectangle { public short X, Y; public ushort Width, Height; }

    [DllImport("libX11.so.6")] static extern IntPtr XInternAtom(IntPtr display, string name, bool onlyIfExists);
    [DllImport("libX11.so.6")]
    static extern int XChangeProperty(IntPtr display, IntPtr window, IntPtr property, IntPtr type, int format, int mode, IntPtr[] data, int n);
    [DllImport("libX11.so.6")] static extern int XMoveWindow(IntPtr display, IntPtr window, int x, int y);

    static IntPtr display;
    static bool broken;

    /// Marks the window as a dock (like a desktop panel): it stays where it asks, above other windows,
    /// out of the taskbar and Alt+Tab. Must run before the window is mapped.
    public static void MakeDock(IntPtr window)
    {
        if (broken || window == IntPtr.Zero) return;
        try
        {
            if (display == IntPtr.Zero) display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) { broken = true; return; }
            const int XA_ATOM = 4, PropModeReplace = 0;
            var type = XInternAtom(display, "_NET_WM_WINDOW_TYPE", false);
            var dock = XInternAtom(display, "_NET_WM_WINDOW_TYPE_DOCK", false);
            XChangeProperty(display, window, type, (IntPtr)XA_ATOM, 32, PropModeReplace, new[] { dock }, 1);
            XFlush(display);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { Debug.WriteLine(ex); broken = true; }
    }

    /// Moves the window directly through X11, for window managers that ignore the toolkit's request.
    public static void Move(IntPtr window, int x, int y)
    {
        if (broken || display == IntPtr.Zero || window == IntPtr.Zero) return;
        XMoveWindow(display, window, x, y);
        XFlush(display);
    }

    public static void SetInputRect(IntPtr window, int x, int y, int w, int h)
    {
        if (broken || window == IntPtr.Zero) return;
        try
        {
            if (display == IntPtr.Zero) display = XOpenDisplay(IntPtr.Zero);
            if (display == IntPtr.Zero) { broken = true; return; }
            const int ShapeInput = 2, ShapeSet = 0, Unsorted = 0;
            XShapeCombineRectangles(display, window, ShapeInput, 0, 0,
                new[] { new XRectangle { X = (short)x, Y = (short)y, Width = (ushort)Math.Max(1, w), Height = (ushort)Math.Max(1, h) } }, 1, ShapeSet, Unsorted);
            XFlush(display);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            Debug.WriteLine(ex);
            broken = true; // not X11: the whole window takes clicks
        }
    }
}

/// Battery, memory, CPU and AMD GPU readings from /proc and /sys.
static class SysInfo
{
    public static bool TryBattery(out bool charging, out int pct)
    {
        charging = false; pct = 0;
        try
        {
            var bat = Directory.GetDirectories("/sys/class/power_supply", "BAT*").FirstOrDefault();
            if (bat == null) return false;
            pct = int.Parse(File.ReadAllText(Path.Combine(bat, "capacity")).Trim());
            var status = File.ReadAllText(Path.Combine(bat, "status")).Trim();
            charging = status is "Charging" or "Full";
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { return false; }
    }

    public static (int LoadPct, double UsedGb, double TotalGb) Memory()
    {
        try
        {
            long total = 0, avail = 0;
            foreach (var line in File.ReadLines("/proc/meminfo"))
            {
                if (line.StartsWith("MemTotal:")) total = Kb(line);
                else if (line.StartsWith("MemAvailable:")) avail = Kb(line);
            }
            if (total == 0) return (0, 0, 0);
            return ((int)Math.Round(100.0 * (total - avail) / total), (total - avail) / 1048576.0, total / 1048576.0);
        }
        catch (IOException) { return (0, 0, 0); }
        static long Kb(string l) => long.Parse(l.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]);
    }

    static long lastIdle, lastTotal;

    /// Whole-CPU load since the previous call, from /proc/stat.
    public static double Cpu()
    {
        try
        {
            var f = File.ReadLines("/proc/stat").First().Split(' ', StringSplitOptions.RemoveEmptyEntries).Skip(1).Select(long.Parse).ToArray();
            long idle = f[3] + f[4], total = f.Sum();
            long di = idle - lastIdle, dt = total - lastTotal;
            (lastIdle, lastTotal) = (idle, total);
            return dt <= 0 ? 0 : Math.Clamp(100.0 * (dt - di) / dt, 0, 100);
        }
        catch (Exception ex) when (ex is IOException or FormatException) { return 0; }
    }

    /// AMD (amdgpu) exposes load and VRAM in sysfs; other GPUs return null and the game bar hides them.
    public static (double? Gpu, double? VramUsedGb, double? VramTotalGb) Gpu()
    {
        try
        {
            foreach (var card in Directory.GetDirectories("/sys/class/drm", "card*").Where(d => !Path.GetFileName(d).Contains('-')))
            {
                var dev = Path.Combine(card, "device");
                var busy = Path.Combine(dev, "gpu_busy_percent");
                if (!File.Exists(busy)) continue;
                double? used = null, total = null;
                var u = Path.Combine(dev, "mem_info_vram_used"); var t = Path.Combine(dev, "mem_info_vram_total");
                if (File.Exists(u)) used = long.Parse(File.ReadAllText(u).Trim()) / 1073741824.0;
                if (File.Exists(t)) total = long.Parse(File.ReadAllText(t).Trim()) / 1073741824.0;
                return (double.Parse(File.ReadAllText(busy).Trim(), CultureInfo.InvariantCulture), used, total);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { Debug.WriteLine(ex); }
        return (null, null, null);
    }
}

/// "Iniciar con la sesión": an XDG autostart entry.
static class Autostart
{
    static string FilePath => Path.Combine(
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } c && !InFlatpak ? c
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
        "autostart", "io.github.Izukas017.Cato.desktop");

    // inside Flatpak XDG_CONFIG_HOME points at the sandbox; the session reads ~/.config/autostart
    internal static bool InFlatpak => Environment.GetEnvironmentVariable("FLATPAK_ID") != null;

    public static bool Enabled
    {
        get => File.Exists(FilePath);
        set
        {
            try
            {
                if (!value) { File.Delete(FilePath); return; }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var exec = InFlatpak ? "flatpak run io.github.Izukas017.Cato" : $"\"{Environment.ProcessPath}\"";
                File.WriteAllText(FilePath, $"[Desktop Entry]\nType=Application\nName=Cato\nExec={exec}\nIcon=io.github.Izukas017.Cato\nX-GNOME-Autostart-enabled=true\n");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
        }
    }
}

// ---------- MPRIS: music from any player (Spotify, browsers, VLC…) over D-Bus ----------
[DBusInterface("org.mpris.MediaPlayer2.Player")]
public interface IMprisPlayer : IDBusObject
{
    Task PlayPauseAsync();
    Task NextAsync();
    Task PreviousAsync();
    Task<object> GetAsync(string prop);
}

public record MediaInfo(string Title, string Artist, string? ArtUrl, TimeSpan Length, TimeSpan Position, bool Playing);

/// Polls the most relevant MPRIS player (a playing one first).
sealed class Mpris
{
    IMprisPlayer? player;
    string? playerName;

    public async Task<MediaInfo?> ReadAsync()
    {
        try
        {
            var names = (await Connection.Session.ListServicesAsync()).Where(n => n.StartsWith("org.mpris.MediaPlayer2.")).ToArray();
            if (names.Length == 0) { player = null; return null; }
            var found = new List<(string Name, MediaInfo Info)>();
            foreach (var name in names)
                if (await Read(Connection.Session.CreateProxy<IMprisPlayer>(name, "/org/mpris/MediaPlayer2")) is { } info) found.Add((name, info));
            if (found.Count == 0) { player = null; return null; }
            // a playing player wins; otherwise stay on the one we had; otherwise any
            var pick = found.FirstOrDefault(x => x.Info.Playing);
            if (pick.Name == null) pick = found.FirstOrDefault(x => x.Name == playerName);
            if (pick.Name == null) pick = found[0];
            playerName = pick.Name;
            player = Connection.Session.CreateProxy<IMprisPlayer>(pick.Name, "/org/mpris/MediaPlayer2");
            return pick.Info;
        }
        catch (Exception ex) { Debug.WriteLine(ex); player = null; return null; } // no session bus / player went away
    }

    static async Task<MediaInfo?> Read(IMprisPlayer p)
    {
        try
        {
            var meta = (IDictionary<string, object>)await p.GetAsync("Metadata");
            var status = (string)await p.GetAsync("PlaybackStatus");
            long pos = 0;
            try { pos = Convert.ToInt64(await p.GetAsync("Position")); } catch (DBusException) { }
            string title = meta.TryGetValue("xesam:title", out var t) ? t?.ToString() ?? "" : "";
            string artist = meta.TryGetValue("xesam:artist", out var a) ? a switch { string[] arr => string.Join(", ", arr), _ => a?.ToString() ?? "" } : "";
            string? art = meta.TryGetValue("mpris:artUrl", out var u) ? u?.ToString() : null;
            long len = meta.TryGetValue("mpris:length", out var l) ? Convert.ToInt64(l) : 0;
            if (title == "" && artist == "") return null;
            return new MediaInfo(title, artist, art, TimeSpan.FromTicks(len * 10), TimeSpan.FromTicks(pos * 10), status == "Playing");
        }
        catch (Exception ex) when (ex is DBusException or InvalidCastException) { Debug.WriteLine(ex); return null; }
    }

    public Task PlayPause() => Call(p => p.PlayPauseAsync());
    public Task Next() => Call(p => p.NextAsync());
    public Task Previous() => Call(p => p.PreviousAsync());

    async Task Call(Func<IMprisPlayer, Task> f)
    {
        if (player == null) return;
        try { await f(player); } catch (DBusException ex) { Debug.WriteLine(ex); }
    }
}
