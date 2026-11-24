using System.Management;
using System.Windows.Interop;
using NAudio.CoreAudioApi;

namespace Cato;

enum Mode { Closed, Alert, Expanded, Lyrics, Game }

public partial class MainWindow : Window
{
    internal static readonly CultureInfo Es = new("es-ES");
    internal static readonly string[] PageNames = { "Música", "Pomodoro", "Día y tiempo", "Agua", "Bandeja", "Portapapeles", "Tareas", "Widgets" };
    static readonly Brush GreenB = Hex("#30D158"), BlueB = Hex("#0A84FF"), PurpleB = Hex("#8B7CF6"),
        DimB = Hex("#8CFFFFFF"), FaintB = Hex("#59FFFFFF"), CardB = Hex("#17FFFFFF"), TrackB = Hex("#33FFFFFF");
    const int WM_CLIPBOARDUPDATE = 0x031D;

    internal readonly Data data = Store.Load();
    Mode mode = Mode.Closed;
    readonly DispatcherTimer ui = new() { Interval = TimeSpan.FromMilliseconds(1000) };
    readonly DispatcherTimer alertTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    readonly DispatcherTimer overlayTimer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    Grid[] pages = Array.Empty<Grid>();
    int page;
    bool lyricsMode;
    int lastMinute = -1;
    SettingsWindow? settings;

    internal static SolidColorBrush Hex(string s)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(s));
        b.Freeze();
        return b;
    }

    public MainWindow()
    {
        SelfCheck.Run();
        InitializeComponent();
        Notch.Background = EarLFill.Fill = EarRFill.Fill = notchBrush;
        pages = new[] { PageMusic, PagePomo, PageDay, PageWater, PageShelf, PageClip, PageTodo, PageWidgets };
        // the "Rezo" page became "Día y tiempo"; keep the user's order/visibility for it
        for (int i = 0; i < data.PageOrder.Count; i++) if (data.PageOrder[i] == "Rezo") data.PageOrder[i] = PageNames[2];
        for (int i = 0; i < data.HiddenPages.Count; i++) if (data.HiddenPages[i] == "Rezo") data.HiddenPages[i] = PageNames[2];
        InitWaves();
        InitShelf();
        InitTodo();
        ApplySettings();
        page = VisiblePages()[0];
        foreach (var p in pages) p.Visibility = p == pages[page] ? Visibility.Visible : Visibility.Collapsed;
        ui.Tick += (_, _) => UiTick();
        alertTimer.Tick += (_, _) => { alertTimer.Stop(); if (mode == Mode.Alert && !pinned) GoTo(Mode.Closed); };
        overlayTimer.Tick += (_, _) => HideOverlay();
        graceTimer.Tick += (_, _) => GraceTick();
        Loaded += OnLoaded;
        UiTick();
        ui.Start();
    }

    async void OnLoaded(object? s, RoutedEventArgs e)
    {
        Left = (SystemParameters.PrimaryScreenWidth - Width) / 2;
        Top = 0;
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd).AddHook(WndProc);
        Native.HideFromAltTab(hwnd);
        Native.AddClipboardFormatListener(hwnd);
        if (!Native.StartKeyHook(OnShortcut))
            ShowAlert("", FaintB, "Atajos no disponibles", "Windows no dejó escuchar el teclado");
        Notify.Listen(text => Dispatcher.Invoke(() => ShowAlert("", GreenB, "Claude terminó", text)));
        StartAudio();
        StartBrightnessWatch();
        await StartMedia();
        DevShow();
    }

    bool pinned;

    /// Dev flag for screenshots: --show=0..7 (page), lyrics, alert or settings. Pins the notch open.
    void DevShow()
    {
        if (Environment.GetCommandLineArgs().Contains("--island")) { data.Island = true; ApplyStyle(); } // not saved
        var arg = Environment.GetCommandLineArgs().FirstOrDefault(a => a.StartsWith("--show="))?[7..];
        if (arg == null) return;
        pinned = true;
        if (int.TryParse(arg, out var p)) { page = p; foreach (var pg in pages) pg.Visibility = pg == pages[p] ? Visibility.Visible : Visibility.Collapsed; GoTo(Mode.Expanded); }
        else if (arg == "lyrics") { lyricsMode = true; GoTo(Mode.Lyrics); }
        else if (arg == "alert") ShowAlert("", GreenB, "Cargando", "Cargador conectado", "70%");
        else if (arg == "settings") Settings_Click(this, new RoutedEventArgs());
        else if (arg == "game") ToggleGame();
        if (Environment.GetCommandLineArgs().Contains("--openclose")) // dev: open/close in a loop to film the morph
        {
            var oc = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
            oc.Tick += (_, _) => GoTo(mode == Mode.Expanded ? Mode.Closed : Mode.Expanded);
            oc.Start();
        }
        if (Environment.GetCommandLineArgs().Contains("--cycle")) // dev: flip pages to film transitions
        {
            var cycle = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            cycle.Tick += (_, _) => Step(1);
            cycle.Start();
        }
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) OnClipboard();
        return IntPtr.Zero;
    }

    /// Ctrl + left Alt + N/G. Returns true to swallow the key; anything else (AltGr+N = ñ…) passes through.
    bool OnShortcut(int vk)
    {
        if (vk == 0x4E) { Dispatcher.BeginInvoke(ToggleHidden); return true; } // N
        if (vk == 0x47) { Dispatcher.BeginInvoke(ToggleGame); return true; }   // G
        return false;
    }

    // ================= Ctrl+Alt+N: se hace bola y se mete en el marco =================
    bool notchHidden, morphing;
    int hideVersion;
    const double Ball = 34;

    FrameworkElement[] Views => new FrameworkElement[] { ClosedView, AlertView, ExpandedView, LyricsView, GameView };

    async void ToggleHidden()
    {
        var v = ++hideVersion; // a second press mid-animation takes over from wherever this one is
        notchHidden = !notchHidden;
        morphing = true;
        graceTimer.Stop();
        alertTimer.Stop();
        if (notchHidden)
        {
            // 1. contents and ears fade while the notch rounds off into a ball
            foreach (var el in Views) el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Dur(120)));
            foreach (var el in new UIElement[] { EarL, EarR, Glow }) el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, Dur(160)));
            Notch.CornerRadius = Glow.CornerRadius = new CornerRadius(Ball / 2);
            Resize(Ball, Ball, 300, new CubicEase { EasingMode = EasingMode.EaseOut });
            if (!await Step(v, 320)) return;
            // 2. a little dip, then it shoots up into the frame
            RootMove.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-(Ball + 20 + IslandGap), Dur(340)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseIn, Amplitude = 0.6 } });
            if (!await Step(v, 360)) return;
            SettleMode(mode == Mode.Game ? Mode.Game : Mode.Closed); // comes back closed (or as the game bar)
            Hide(); // still gets hotkeys and clipboard events while hidden
        }
        else
        {
            Show();
            // 1. the ball drops out of the frame with a small bounce
            RootMove.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(0, Dur(380)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 } });
            if (!await Step(v, 400)) return;
            // 2. and unrolls back into the notch
            var (w, h, r) = SizeOf(mode);
            var (ease, ms) = ShapeEasing();
            Notch.CornerRadius = Glow.CornerRadius = Corners(mode);
            Resize(w, h, Math.Max(ms, 260), ease);
            foreach (var el in new UIElement[] { EarL, EarR }) el.BeginAnimation(OpacityProperty, new DoubleAnimation(EarOpacity, Dur(200)));
            Glow.BeginAnimation(OpacityProperty, new DoubleAnimation(GlowTarget(mode), Dur(260)));
            ViewOf(mode).BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Dur(200)) { BeginTime = Dur(140) });
            if (!await Step(v, Math.Max(ms, 260) + 60)) return;
        }
        morphing = false;
    }

    async Task<bool> Step(int version, double ms)
    {
        await Task.Delay(Dur(ms));
        return version == hideVersion;
    }

    void Resize(double w, double h, double ms, IEasingFunction? ease)
    {
        Notch.BeginAnimation(WidthProperty, new DoubleAnimation(w, Dur(ms)) { EasingFunction = ease });
        Notch.BeginAnimation(HeightProperty, new DoubleAnimation(h, Dur(ms)) { EasingFunction = ease });
    }

    /// Switches mode without the usual transition (the notch is out of sight).
    void SettleMode(Mode m)
    {
        foreach (var el in Views) el.Visibility = Visibility.Collapsed;
        mode = m;
        var view = ViewOf(m);
        view.BeginAnimation(OpacityProperty, null);
        view.Opacity = 0;
        view.Visibility = Visibility.Visible;
    }

    // ================= Ctrl+Alt+G: modo juego =================
    CancellationTokenSource? gameCts;

    void ToggleGame()
    {
        if (mode == Mode.Game)
        {
            gameCts?.Cancel();
            GoTo(Mode.Closed);
            return;
        }
        graceTimer.Stop();
        GoTo(Mode.Game);
        var cts = gameCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            using var stats = new SystemStats();
            while (!cts.IsCancellationRequested)
            {
                var s = stats.Sample();
                _ = Dispatcher.BeginInvoke(() => ShowStats(s));
                try { await Task.Delay(1000, cts.Token); } catch (TaskCanceledException) { }
            }
        });
    }

    void ShowStats(StatsSample s)
    {
        if (mode != Mode.Game) return;
        GameClock.Text = DateTime.Now.ToString("HH:mm");
        // Only show what there's data for; the bar resizes to fit.
        Metric(GFpsBox, GFps, s.Fps?.ToString());
        Metric(GCpuBox, GCpu, $"{s.Cpu:0}%");
        Metric(GGpuBox, GGpu, s.Gpu is { } g ? $"{g:0}%" : null);
        Metric(GRamBox, GRam, $"{s.RamPct:0}%");
        Metric(GVramBox, GVram, s.VramUsedGb is { } u ? (s.VramTotalGb is { } t ? $"{u:0.0}/{t:0} GB" : $"{u:0.0} GB") : null);
        FitGameBar();
    }

    static void Metric(FrameworkElement box, TextBlock value, string? text)
    {
        box.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
        value.Text = text ?? "";
    }

    void FitGameBar()
    {
        GameRow.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var w = Math.Ceiling(GameRow.DesiredSize.Width) + 36;
        if (Math.Abs(w - gameWidth) < 2) return;
        gameWidth = w;
        if (mode == Mode.Game && !notchHidden && !morphing)
            Notch.BeginAnimation(WidthProperty, new DoubleAnimation(w, Dur(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    internal void RestartAsAdmin()
    {
        try
        {
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = true, Verb = "runas" });
            Application.Current.Shutdown();
        }
        catch (System.ComponentModel.Win32Exception ex) { Debug.WriteLine(ex); } // UAC cancelled: keep running
    }

    /// Re-reads settings after the panel changes them.
    internal void ApplySettings()
    {
        // removed styles fall back to the defaults
        if (data.Anim == AnimStyle.Liquida) data.Anim = AnimStyle.Suave;
        if (data.PageAnim == PageAnim.Liquida) data.PageAnim = PageAnim.Deslizar;
        var accent = Hex(data.Accent);
        Ring.Stroke = HeaderRing.Stroke = accent;
        Brush values = data.GameAccent ? accent : Brushes.White; // game bar values: accent colour or plain white
        GFps.Foreground = GCpu.Foreground = GGpu.Foreground = GRam.Foreground = GVram.Foreground = values;
        Glow.Background = GlowBrush();
        Glow.BeginAnimation(OpacityProperty, null);
        Glow.Opacity = GlowTarget(mode);
        ApplyStyle();
        notchBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        notchBrush.Color = NotchColor(mode);
        ConfigurePomo();
        InitWater();
        var v = VisiblePages();
        if (!v.Contains(page)) ShowPage(v[0], 0);
        UpdateDots();
        if (mode == Mode.Expanded) OnPageShown(page);
    }

    internal void SaveSettings() { Store.Save(data); ApplySettings(); }

    void Settings_Click(object s, RoutedEventArgs e)
    {
        if (settings == null)
        {
            settings = new SettingsWindow(this);
            settings.Closed += (_, _) => settings = null;
        }
        settings.Show();
        settings.Activate();
    }

    void Exit_Click(object s, RoutedEventArgs e) => Application.Current.Shutdown();

    // ================= Modos y transiciones =================
    FrameworkElement ViewOf(Mode m) => m switch
    {
        Mode.Closed => ClosedView,
        Mode.Alert => AlertView,
        Mode.Expanded => ExpandedView,
        Mode.Game => GameView,
        _ => LyricsView,
    };

    const double IslandGap = 8; // space between the island and the top of the screen
    double EarOpacity => data.Island ? 0 : 1;

    /// Fused: flat top that melts into the frame. Island: rounded all round (a pill when it's short).
    CornerRadius Corners(Mode m)
    {
        var (_, h, r) = SizeOf(m);
        return data.Island ? new CornerRadius(h <= 70 ? h / 2 : r) : new CornerRadius(0, 0, r, r);
    }

    void ApplyStyle()
    {
        if (notchHidden || morphing) return; // the hide/show animation sets these itself
        Notch.CornerRadius = Glow.CornerRadius = Corners(mode);
        foreach (var el in new UIElement[] { EarL, EarR }) el.BeginAnimation(OpacityProperty, new DoubleAnimation(EarOpacity, Dur(220)));
        Root.BeginAnimation(MarginProperty, new ThicknessAnimation(new Thickness(0, data.Island ? IslandGap : 0, 0, 0), Dur(260))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    static double gameWidth = 520; // follows the game bar's content
    // one shared, animatable brush so the body and the ears fade together
    readonly SolidColorBrush notchBrush = new(Colors.Black);

    Color NotchColor(Mode m) => Color.FromArgb((byte)(m == Mode.Game ? Math.Clamp(data.GameOpacity, 0.1, 1) * 255 : 255), 0, 0, 0);

    static (double W, double H, double R) SizeOf(Mode m) => m switch
    {
        Mode.Closed => (200, 38, 18),
        Mode.Alert => (380, 62, 24),
        Mode.Expanded => (580, 150, 27),
        Mode.Game => (gameWidth, 26, 13),
        _ => (680, 140, 27),
    };

    (IEasingFunction? Ease, double Ms) ShapeEasing() => data.Anim switch
    {
        AnimStyle.Elastica => (new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 }, 340),
        // BounceEase never overshoots past the target, so the size can't go negative when shrinking.
        AnimStyle.Rebote => (new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 2, Bounciness = 3 }, 520),
        AnimStyle.Rapida => (new QuadraticEase { EasingMode = EasingMode.EaseOut }, 150),
        AnimStyle.Ninguna => (null, 0),
        _ => (new CubicEase { EasingMode = EasingMode.EaseOut }, 270),
    };

    TimeSpan Dur(double ms) =>
        TimeSpan.FromMilliseconds(data.Anim == AnimStyle.Ninguna ? 0 : ms / Math.Clamp(data.AnimSpeed, 0.25, 3));

    void GoTo(Mode m)
    {
        if (m == mode) return;
        var from = ViewOf(mode);
        var to = ViewOf(m);
        mode = m;

        var (w, h, r) = SizeOf(m);
        var (ease, ms) = ShapeEasing();
        Notch.CornerRadius = Glow.CornerRadius = Corners(m);
        // smooth progress bars only matter while the panel is open; otherwise once a second is plenty
        ui.Interval = TimeSpan.FromMilliseconds(m is Mode.Expanded or Mode.Lyrics ? 250 : 1000);
        Notch.BeginAnimation(WidthProperty, new DoubleAnimation(w, Dur(ms)) { EasingFunction = ease });
        Notch.BeginAnimation(HeightProperty, new DoubleAnimation(h, Dur(ms)) { EasingFunction = ease });
        Glow.BeginAnimation(OpacityProperty, new DoubleAnimation(GlowTarget(m), Dur(Math.Max(ms, 200))));
        notchBrush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(NotchColor(m), Dur(Math.Max(ms, 200))));

        // Old content fades out fast while the shape morphs; new content fades in once there's room.
        var fadeOut = new DoubleAnimation(0, Dur(90));
        fadeOut.Completed += (_, _) => { if (ViewOf(mode) != from) from.Visibility = Visibility.Collapsed; };
        from.BeginAnimation(OpacityProperty, fadeOut);
        to.Visibility = Visibility.Visible;
        to.BeginAnimation(OpacityProperty, null);
        to.Opacity = 0;
        to.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Dur(200)) { BeginTime = Dur(ms * 0.45) });

        if (m == Mode.Expanded) OnPageShown(page);
        if (m is Mode.Expanded or Mode.Lyrics) { lyricIndex = -2; UpdateMediaProgress(); }
    }

    internal async void PreviewAnimation()
    {
        GoTo(Mode.Expanded);
        await Task.Delay(1400);
        if (!Notch.IsMouseOver) GoTo(Mode.Closed);
    }

    void Notch_MouseEnter(object s, MouseEventArgs e)
    {
        graceTimer.Stop();
        if (mode == Mode.Game || notchHidden || morphing) return; // game mode / hidden: never pops open under the cursor
        GoTo(lyricsMode && data.Lyrics ? Mode.Lyrics : Mode.Expanded);
    }

    // ================= Zona de gracia =================
    readonly DispatcherTimer graceTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    DateTime? outsideSince;

    bool KeepOpen() => pinned || Notch.ContextMenu?.IsOpen == true || TaskInput.IsKeyboardFocusWithin;

    void Notch_MouseLeave(object s, MouseEventArgs e)
    {
        if (KeepOpen()) return;
        outsideSince = null;
        GraceTick();
        if (mode != Mode.Closed) graceTimer.Start();
    }

    /// Closes only once the cursor has been outside the notch + margin for the configured delay.
    void GraceTick()
    {
        if (mode is Mode.Closed or Mode.Game) { graceTimer.Stop(); return; }
        if (KeepOpen() || CursorInGraceZone()) { outsideSince = null; return; }
        outsideSince ??= DateTime.Now;
        if ((DateTime.Now - outsideSince.Value).TotalMilliseconds < data.GraceDelayMs) return;
        graceTimer.Stop();
        GoTo(Mode.Closed);
    }

    bool CursorInGraceZone()
    {
        var c = Native.CursorPos();
        var tl = Notch.PointToScreen(new Point(0, 0));
        var br = Notch.PointToScreen(new Point(Notch.ActualWidth, Notch.ActualHeight));
        var m = data.GraceZone * VisualTreeHelper.GetDpi(this).DpiScaleX;
        return c.X >= tl.X - m && c.X <= br.X + m && c.Y >= tl.Y - m && c.Y <= br.Y + m;
    }

    // ================= Fundido de color =================
    internal static readonly Dictionary<string, string[]> GlowPalettes = new()
    {
        ["Aurora"] = new[] { "#1FC8C8", "#2D6BEA", "#7A3FE0" },
        ["Océano"] = new[] { "#00C6FF", "#0072FF", "#1A2A8F" },
        ["Atardecer"] = new[] { "#FF7E5F", "#FD3A69", "#8E2DE2" },
        ["Bosque"] = new[] { "#5BE37D", "#16A085", "#0B5E6B" },
        ["Acento"] = Array.Empty<string>(),
    };

    Brush GlowBrush()
    {
        var cols = GlowPalettes.GetValueOrDefault(data.GlowPalette) is { Length: > 0 } p ? p : new[] { data.Accent, data.Accent, "#000000" };
        var b = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) };
        for (int i = 0; i < cols.Length; i++) b.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString(cols[i]), i / (double)(cols.Length - 1)));
        b.Freeze();
        return b;
    }

    double GlowTarget(Mode m) => data.Glow && (m is not (Mode.Closed or Mode.Game) || data.GlowClosed) ? data.GlowIntensity : 0;

    // ================= Carrusel de páginas =================
    /// Page indices in the user's order, hidden ones removed.
    int[] VisiblePages()
    {
        var order = data.PageOrder.Where(PageNames.Contains).Concat(PageNames).Distinct();
        var v = order.Where(n => !data.HiddenPages.Contains(n)).Select(n => Array.IndexOf(PageNames, n)).ToArray();
        return v.Length == 0 ? new[] { 0 } : v;
    }

    void PrevPage_Click(object s, RoutedEventArgs e) => Step(-1);
    void NextPage_Click(object s, RoutedEventArgs e) => Step(1);

    void Step(int d)
    {
        var v = VisiblePages();
        var k = Array.IndexOf(v, page);
        ShowPage(v[k < 0 ? 0 : (k + d + v.Length) % v.Length], d);
    }

    void ShowPage(int i, int dir)
    {
        if (i == page) { OnPageShown(i); return; }
        var old = pages[page];
        var now = pages[i];
        page = i;

        var fo = new DoubleAnimation(0, Dur(110));
        fo.Completed += (_, _) => { if (pages[page] != old) old.Visibility = Visibility.Collapsed; };
        old.BeginAnimation(OpacityProperty, fo);

        now.Visibility = Visibility.Visible;
        now.BeginAnimation(OpacityProperty, null);
        now.Opacity = 0;
        now.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Dur(220)) { BeginTime = Dur(70) });

        var scale = new ScaleTransform(1, 1);
        var move = new TranslateTransform();
        now.RenderTransformOrigin = new Point(0.5, 0.5);
        now.RenderTransform = new TransformGroup { Children = { scale, move } };
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        switch (data.PageAnim)
        {
            case PageAnim.Deslizar when dir != 0:
                move.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(44 * dir, 0, Dur(300)) { EasingFunction = ease });
                break;
            case PageAnim.Zoom:
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.88, 1, Dur(300)) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.88, 1, Dur(300)) { EasingFunction = ease });
                break;
        }
        UpdateDots();
        OnPageShown(i);
    }

    void UpdateDots()
    {
        Dots.Children.Clear();
        foreach (var i in VisiblePages())
            Dots.Children.Add(new Border
            {
                Width = i == page ? 14 : 5, Height = 5, CornerRadius = new CornerRadius(2.5),
                Margin = new Thickness(2, 0, 2, 0), Background = i == page ? Brushes.White : FaintB,
            });
    }

    void SetTitle(string main, string accent = "", Brush? accentBrush = null)
    {
        PageTitle.Inlines.Clear();
        PageTitle.Inlines.Add(new Run(main));
        if (accent != "") PageTitle.Inlines.Add(new Run(" " + accent) { Foreground = accentBrush ?? DimB, FontWeight = FontWeights.SemiBold });
    }

    void OnPageShown(int i)
    {
        var now = DateTime.Now;
        switch (i)
        {
            case 0: SetTitle(""); UpdateMediaProgress(); break;
            case 1: SetTitle("Pomodoro"); UpdatePomo(now); break;
            case 2: SetTitle("Tiempo", data.City); UpdateDay(now); break;
            case 3: SetTitle("Agua", "Recordatorio", BlueB); UpdateWater(); break;
            case 4: SetTitle("Bandeja"); UpdateShelf(); break;
            case 5: SetTitle("Portapapeles"); UpdateClip(); break;
            case 6: SetTitle("Tareas", "de hoy", GreenB); UpdateTodo(); break;
            case 7: SetTitle("Widgets"); UpdateWidgets(); break;
        }
    }

    bool PageVisible(int i) => mode == Mode.Expanded && page == i;

    // ================= Reloj y tareas periódicas =================
    void UiTick()
    {
        var now = DateTime.Now;
        Clock.Text = HeaderTime.Text = now.ToString("HH:mm");
        HeaderDate.Text = now.ToString("ddd d MMM yyyy", Es);
        PomoTick(now);
        if (mode is Mode.Expanded or Mode.Lyrics) UpdateMediaProgress();
        PowerTick();
        if (now.Minute != lastMinute)
        {
            lastMinute = now.Minute;
            WaterReminder(now);
            TodoReminders(now);
            if (PageVisible(2)) UpdateDay(now);
            if (PageVisible(5)) UpdateClip();
            if (PageVisible(7)) UpdateWidgets();
        }
    }

    // ================= Alertas compactas =================
    internal void ShowAlert(string glyph, Brush bg, string title, string sub, string value = "")
    {
        if (mode is Mode.Expanded or Mode.Lyrics or Mode.Game || notchHidden || morphing) return; // looking at it, gaming or hidden
        AlertIcon.Text = glyph;
        AlertIconBg.Fill = bg;
        AlertTitle.Text = title;
        AlertSub.Text = sub;
        AlertValue.Text = value;
        GoTo(Mode.Alert);
        alertTimer.Stop();
        alertTimer.Start();
    }

    // ================= Anillo y onda =================
    internal static Geometry RingGeometry(double c, double r, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction >= 0.999) return new EllipseGeometry(new Point(c, c), r, r);
        var a = fraction * 2 * Math.PI;
        var fig = new PathFigure { StartPoint = new Point(c, c - r) };
        fig.Segments.Add(new ArcSegment(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise, true));
        return new PathGeometry(new[] { fig });
    }

    double lastRing = -1;

    void SetRings(double fraction)
    {
        fraction = Math.Round(Math.Clamp(fraction, 0, 1), 3);
        if (fraction == lastRing) return; // new geometry = a full redraw of the window
        lastRing = fraction;
        Ring.Data = RingGeometry(9, 7.5, fraction);
        HeaderRing.Data = RingGeometry(6, 5, fraction);
    }

    static readonly double[] WaveRest = { 0.22, 0.22, 0.22, 0.22 }; // paused: flat line of dots
    bool? wavePlaying;

    IEnumerable<(Rectangle Bar, int I)> Bars() =>
        Wave.Children.OfType<Rectangle>().Select((b, i) => (b, i))
            .Concat(HeaderWave.Children.OfType<Rectangle>().Select((b, i) => (b, i)));

    void InitWaves()
    {
        foreach (var (b, i) in Bars())
        {
            b.RenderTransformOrigin = new Point(0.5, 0.5);
            b.RenderTransform = new ScaleTransform(1, WaveRest[i]);
        }
        SetWave(false);
    }

    void SetWave(bool playing)
    {
        if (wavePlaying == playing) return;
        wavePlaying = playing;
        foreach (var (bar, i) in Bars())
        {
            var st = (ScaleTransform)bar.RenderTransform;
            if (playing)
            {
                var anim = new DoubleAnimation(0.25, 1, TimeSpan.FromMilliseconds(280 + 90 * i)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                System.Windows.Media.Animation.Timeline.SetDesiredFrameRate(anim, 30); // keeps CPU low while music plays
                st.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            }
            else
            {
                st.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                st.ScaleY = WaveRest[i];
            }
        }
    }

    // ================= Volumen, brillo, salida de audio, carga =================
    MMDeviceEnumerator? audioEnum;
    MMDevice? audioDev;
    MMDeviceNotificationClient? audioNotify; // fields keep the COM callbacks alive
    ManagementEventWatcher? brightnessWatcher;

    void StartAudio()
    {
        try
        {
            audioEnum = new MMDeviceEnumerator();
            audioNotify = audioEnum.CreateNotificationClient(false);
            audioNotify.DefaultDeviceChanged += (_, e) =>
            {
                if (e.Flow == DataFlow.Render && e.Role == Role.Multimedia) Dispatcher.BeginInvoke(OnDefaultDevice);
            };
            HookVolume();
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    void HookVolume()
    {
        if (audioDev != null) audioDev.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
        audioDev = audioEnum!.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        audioDev.AudioEndpointVolume.OnVolumeNotification += OnVolume;
    }

    void OnVolume(AudioVolumeNotificationData d) =>
        Dispatcher.BeginInvoke(() => { if (data.VolumeOverlay) ShowOverlay(d.Muted ? "" : "", d.MasterVolume); });

    void OnDefaultDevice()
    {
        try { HookVolume(); }
        catch (Exception ex) { Debug.WriteLine(ex); return; }
        if (data.AudioAlerts) ShowAlert("", BlueB, audioDev!.FriendlyName, "Salida de audio conectada");
    }

    void StartBrightnessWatch()
    {
        try
        {
            // Only fires for displays Windows controls (laptop panels); external monitors never raise it.
            brightnessWatcher = new ManagementEventWatcher(new ManagementScope(@"root\wmi"), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
            brightnessWatcher.EventArrived += (_, e) =>
            {
                var b = Convert.ToDouble(e.NewEvent["Brightness"]) / 100;
                Dispatcher.BeginInvoke(() => { if (data.VolumeOverlay) ShowOverlay("", b); });
            };
            brightnessWatcher.Start();
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }

    void ShowOverlay(string icon, double level)
    {
        OverlayIcon.Text = icon;
        OverlayBar.Value = level;
        OverlayPct.Text = $"{level * 100:0}";
        Clock.Visibility = Visibility.Collapsed;
        OverlayPanel.Visibility = Visibility.Visible;
        overlayTimer.Stop();
        overlayTimer.Start();
    }

    void HideOverlay()
    {
        overlayTimer.Stop();
        OverlayPanel.Visibility = Visibility.Collapsed;
        Clock.Visibility = Visibility.Visible;
    }

    bool? lastCharging;

    void PowerTick()
    {
        if (!Native.TryPower(out var charging, out var pct)) return; // desktop: no battery
        if (lastCharging != null && lastCharging != charging && data.ChargeAlerts)
            ShowAlert(charging ? "" : "", charging ? GreenB : FaintB,
                charging ? "Cargando" : "Con batería", charging ? "Cargador conectado" : "Cargador desconectado", $"{pct}%");
        lastCharging = charging;
    }
}
