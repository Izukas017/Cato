using System.Windows.Interop;
using Microsoft.Win32;
using Path = System.IO.Path; // WPF's global using also brings in Shapes.Path

namespace Cato;

static class Native
{
    /// Lets the app we launch bring its window to the front (Windows blocks it otherwise).
    [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int processId);

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr hwnd);

    /// Brings an existing window to the front, restoring it if minimized. Launching the app again does not do this for Electron apps.
    public static void Focus(IntPtr hwnd)
    {
        if (IsIconic(hwnd)) ShowWindow(hwnd, 9); // SW_RESTORE
        SetForegroundWindow(hwnd);
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool AddClipboardFormatListener(IntPtr hwnd);

    // ---- Low-level keyboard hook: sees every key, swallows only our shortcuts ----
    delegate IntPtr HookProc(int code, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, HookProc proc, IntPtr hMod, uint threadId);

    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    static extern IntPtr GetModuleHandle(string? name);

    static HookProc? hookProc; // kept alive: the OS calls it for every key press
    static IntPtr hook;

    /// onChord(vk) runs for Ctrl + LEFT Alt + key and returns true to swallow it. AltGr (right Alt,
    /// which Windows reports as Ctrl+Alt) never matches, so AltGr+N still types ñ.
    public static bool StartKeyHook(Func<int, bool> onChord)
    {
        hookProc = (code, wParam, lParam) =>
        {
            const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
            if (code >= 0 && (wParam == WM_KEYDOWN || wParam == WM_SYSKEYDOWN))
            {
                var vk = Marshal.ReadInt32(lParam); // KBDLLHOOKSTRUCT.vkCode
                bool ctrl = Down(0xA2) || Down(0xA3), leftAlt = Down(0xA4), rightAlt = Down(0xA5);
                if (ctrl && leftAlt && !rightAlt && onChord(vk)) return 1;
            }
            return CallNextHookEx(hook, code, wParam, lParam);
        };
        hook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, hookProc, GetModuleHandle(null), 0);
        return hook != IntPtr.Zero;
    }

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hwnd, out int pid);

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vk);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    static extern IntPtr SetWindowLongPtr(IntPtr hwnd, int index, IntPtr value);

    /// Tool windows are left out of Alt+Tab (and the taskbar).
    public static void HideFromAltTab(IntPtr hwnd)
    {
        const int GWL_EXSTYLE = -20;
        const long WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000;
        var ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr((ex | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW));
    }

    public static bool IsToolWindow(IntPtr hwnd) => (GetWindowLongPtr(hwnd, -20).ToInt64() & 0x80) != 0;

    public static int ForegroundPid() { GetWindowThreadProcessId(GetForegroundWindow(), out var pid); return pid; }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT { public int X, Y; }

    [DllImport("user32.dll")]
    static extern bool GetCursorPos(out POINT p);

    /// Cursor in physical screen pixels (same space as Visual.PointToScreen).
    public static Point CursorPos() => GetCursorPos(out var p) ? new Point(p.X, p.Y) : new Point(double.NaN, double.NaN);

    [StructLayout(LayoutKind.Sequential)]
    struct SYSTEM_POWER_STATUS { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }

    [DllImport("kernel32.dll")]
    static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS s);

    /// False on machines without a battery (desktops).
    public static bool TryPower(out bool charging, out int pct)
    {
        charging = false; pct = 0;
        if (!GetSystemPowerStatus(out var s) || s.BatteryFlag == 128 || s.BatteryLifePercent == 255) return false;
        charging = s.ACLineStatus == 1;
        pct = s.BatteryLifePercent;
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong a, b, c, d, e, f, g; }

    [DllImport("kernel32.dll")]
    static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);

    public static int MemoryLoad()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? (int)m.dwMemoryLoad : 0;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr SHGetFileInfo(string path, uint attr, ref SHFILEINFO info, uint size, uint flags);

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr h);

    public static ImageSource? ShellIcon(string path)
    {
        var info = new SHFILEINFO();
        const uint SHGFI_ICON = 0x100; // large icon by default
        if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON) == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
        try
        {
            var src = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            src.Freeze();
            return src;
        }
        finally { DestroyIcon(info.hIcon); }
    }

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static bool StartupEnabled
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(RunKey); return k?.GetValue("Cato") != null; }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) k.SetValue("Cato", $"\"{Environment.ProcessPath}\"");
            else k.DeleteValue("Cato", false);
        }
    }

    // ---- Start with Windows as administrator: a scheduled task at logon with the highest privileges ----
    // The Run key cannot do this (it would ask for UAC at every logon, or be skipped). Creating the task needs admin once.
    const string AdminTask = "Cato";

    const string AdminTaskXml = """
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.4" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo><Description>Inicia Cato con Windows con permisos de administrador</Description></RegistrationInfo>
          <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>
          <Principals><Principal id="Author"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <AllowHardTerminate>true</AllowHardTerminate>
            <StartWhenAvailable>true</StartWhenAvailable>
            <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
            <Enabled>true</Enabled>
          </Settings>
          <Actions Context="Author"><Exec><Command>"{exe}"</Command><WorkingDirectory>{dir}</WorkingDirectory></Exec></Actions>
        </Task>
        """;

    static int RunSchtasks(string args, bool elevated)
    {
        var psi = new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = elevated, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden };
        if (elevated) psi.Verb = "runas";
        using var p = Process.Start(psi)!;
        p.WaitForExit();
        return p.ExitCode;
    }

    public static bool AdminStartupTask
    {
        get
        {
            try { return RunSchtasks($"/Query /TN \"{AdminTask}\"", false) == 0; }
            catch (System.ComponentModel.Win32Exception) { return false; }
        }
    }

    /// Creates or deletes the task. Windows asks for permission (UAC); returns false if it was refused or failed.
    public static bool SetAdminStartupTask(bool on)
    {
        string? xml = null;
        try
        {
            if (!on) return RunSchtasks($"/Delete /TN \"{AdminTask}\" /F", true) == 0;
            var exe = Environment.ProcessPath!;
            string Esc(string s) => System.Security.SecurityElement.Escape(s);
            xml = Path.Combine(Path.GetTempPath(), "cato-task.xml");
            File.WriteAllText(xml, AdminTaskXml.Replace("{user}", Esc(System.Security.Principal.WindowsIdentity.GetCurrent().Name))
                .Replace("{exe}", Esc(exe)).Replace("{dir}", Esc(Path.GetDirectoryName(exe)!)), System.Text.Encoding.Unicode); // schtasks wants UTF-16
            return RunSchtasks($"/Create /TN \"{AdminTask}\" /XML \"{xml}\" /F", true) == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException) { Debug.WriteLine(ex); return false; } // UAC refused
        finally { if (xml != null) try { File.Delete(xml); } catch (IOException) { } }
    }
}
