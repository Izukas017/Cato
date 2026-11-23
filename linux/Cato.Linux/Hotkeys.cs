using Tmds.DBus;

namespace Cato;

/// Global Ctrl+Alt+N / Ctrl+Alt+G. X11 sessions: a key grab on the root window. Wayland sessions:
/// the xdg-desktop-portal GlobalShortcuts API (GNOME 48+, KDE Plasma 6), which works inside Flatpak.
/// AltGr is not Alt on Linux (it is ISO_Level3_Shift / Mod5), so AltGr+N still types ñ.
static class Hotkeys
{
    static DateTime last;
    static char lastKey;

    public static void Start(Action<char> onKey)
    {
        void Fire(char c)
        {
            if (c == lastKey && (DateTime.Now - last).TotalMilliseconds < 300) return; // one press, one action
            (lastKey, last) = (c, DateTime.Now);
            onKey(c);
        }
        if (Environment.GetEnvironmentVariable("XDG_SESSION_TYPE") == "wayland") _ = Portal.Start(Fire);
        else X11.Start(Fire);
    }

    static class X11
    {
        [DllImport("libX11.so.6")] static extern IntPtr XOpenDisplay(IntPtr name);
        [DllImport("libX11.so.6")] static extern IntPtr XDefaultRootWindow(IntPtr display);
        [DllImport("libX11.so.6")] static extern IntPtr XStringToKeysym(string s);
        [DllImport("libX11.so.6")] static extern byte XKeysymToKeycode(IntPtr display, IntPtr keysym);
        [DllImport("libX11.so.6")] static extern int XGrabKey(IntPtr display, int keycode, uint modifiers, IntPtr window, bool ownerEvents, int pointerMode, int keyboardMode);
        [DllImport("libX11.so.6")] static extern int XSync(IntPtr display, bool discard);
        [DllImport("libX11.so.6")] static extern int XNextEvent(IntPtr display, byte[] ev);

        public static void Start(Action<char> onKey)
        {
            try
            {
                // own connection: it is only ever used by the listener thread below
                var d = XOpenDisplay(IntPtr.Zero);
                if (d == IntPtr.Zero) return;
                var root = XDefaultRootWindow(d);
                const uint Lock = 2, Control = 4, Mod1 = 8, Mod2 = 16; // Mod1 = Alt, Mod2 = NumLock
                var keys = new Dictionary<int, char>();
                foreach (var c in "ng")
                {
                    int kc = XKeysymToKeycode(d, XStringToKeysym(c.ToString()));
                    if (kc == 0) continue;
                    keys[kc] = c;
                    foreach (var extra in new[] { 0u, Lock, Mod2, Lock | Mod2 }) // grabs ignore Caps/NumLock only if we ask for each
                        XGrabKey(d, kc, Control | Mod1 | extra, root, false, 1, 1);
                }
                XSync(d, false);
                new Thread(() =>
                {
                    var ev = new byte[192];
                    while (true)
                    {
                        XNextEvent(d, ev);
                        const int KeyPress = 2, KeycodeOffset = 84; // XKeyEvent.keycode on 64-bit
                        if (BitConverter.ToInt32(ev, 0) == KeyPress && keys.TryGetValue((int)BitConverter.ToUInt32(ev, KeycodeOffset), out var c)) onKey(c);
                    }
                }) { IsBackground = true, Name = "cato-hotkeys" }.Start();
            }
            catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException) { Debug.WriteLine(ex); }
        }
    }

    // ---------- xdg-desktop-portal GlobalShortcuts ----------
    [DBusInterface("org.freedesktop.portal.GlobalShortcuts")]
    public interface IGlobalShortcuts : IDBusObject
    {
        Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
        Task<ObjectPath> BindShortcutsAsync(ObjectPath session, (string, IDictionary<string, object>)[] shortcuts, string parentWindow, IDictionary<string, object> options);
        Task<IDisposable> WatchActivatedAsync(Action<(ObjectPath Session, string ShortcutId, ulong Timestamp, IDictionary<string, object> Options)> handler, Action<Exception>? onError = null);
    }

    [DBusInterface("org.freedesktop.portal.Request")]
    public interface IPortalRequest : IDBusObject
    {
        Task<IDisposable> WatchResponseAsync(Action<(uint Response, IDictionary<string, object> Results)> handler, Action<Exception>? onError = null);
    }

    static class Portal
    {
        static Connection? conn; // kept open: the session and its shortcuts live as long as this connection

        public static async Task Start(Action<char> onKey)
        {
            try
            {
                conn = new Connection(Address.Session);
                var info = await conn.ConnectAsync();
                var sender = info.LocalName.TrimStart(':').Replace('.', '_');
                var portal = conn.CreateProxy<IGlobalShortcuts>("org.freedesktop.portal.Desktop", "/org/freedesktop/portal/desktop");

                // portal calls answer through a Request object whose path we can predict from our token
                async Task<IDictionary<string, object>> Call(Func<string, Task> call)
                {
                    var token = "cato" + Random.Shared.Next(1_000_000);
                    var req = conn.CreateProxy<IPortalRequest>("org.freedesktop.portal.Desktop", $"/org/freedesktop/portal/desktop/request/{sender}/{token}");
                    var done = new TaskCompletionSource<IDictionary<string, object>>();
                    using var _ = await req.WatchResponseAsync(r => done.TrySetResult(r.Results));
                    await call(token);
                    return await done.Task.WaitAsync(TimeSpan.FromMinutes(2)); // the user may have to confirm in a dialog
                }

                var created = await Call(t => portal.CreateSessionAsync(new Dictionary<string, object> { ["handle_token"] = t, ["session_handle_token"] = "cato" }));
                var session = new ObjectPath(created["session_handle"].ToString()!);
                await portal.WatchActivatedAsync(a => { if (a.ShortcutId == "toggle") onKey('n'); else if (a.ShortcutId == "game") onKey('g'); });
                (string, IDictionary<string, object>) Shortcut(string id, string what, string trigger) =>
                    (id, new Dictionary<string, object> { ["description"] = what, ["preferred_trigger"] = trigger });
                await Call(t => portal.BindShortcutsAsync(session, new[]
                {
                    Shortcut("toggle", "Ocultar o mostrar el notch", "CTRL+ALT+n"),
                    Shortcut("game", "Modo juego", "CTRL+ALT+g"),
                }, "", new Dictionary<string, object> { ["handle_token"] = t }));
            }
            catch (Exception ex) { Debug.WriteLine(ex); } // no portal or no GlobalShortcuts: use the --toggle/--game commands
        }
    }
}
