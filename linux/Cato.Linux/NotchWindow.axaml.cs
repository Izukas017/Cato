using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Transformation;
using Avalonia.Threading;

namespace Cato;

enum Mode { Closed, Alert, Expanded, Game }

public partial class NotchWindow : Window
{
    internal static readonly CultureInfo Es = new("es-ES");
    internal static readonly string[] PageNames = { "Home", "Pomodoro", "Día y tiempo", "Agua", "Bandeja", "Portapapeles", "Tareas", "Terminal", "Claude", "Antigravity" };
    internal static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));
    static readonly IBrush GreenB = B("#30D158"), BlueB = B("#0A84FF"), PurpleB = B("#8B7CF6"), DimB = B("#8CFFFFFF"),
        FaintB = B("#59FFFFFF"), CardB = B("#17FFFFFF"), TrackB = B("#33FFFFFF");
    internal static Geometry Icon(string key) => (Geometry)Application.Current!.Resources[key]!;

    internal readonly Data data = Store.Load();
    Mode mode = Mode.Closed;
    Panel[] pages = Array.Empty<Panel>();
    int page;
    int lastMinute = -1;
    SettingsWindow? settings;
    readonly DispatcherTimer ui = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer alertTimer = new() { Interval = TimeSpan.FromSeconds(3.5) };
    readonly DispatcherTimer graceTimer = new();
    readonly string[] args;
    bool pinned; // dev: --show=… keeps it open for screenshots
    readonly TranslateTransform RootMove = new(); // slides the notch up into the screen edge when hiding

    public NotchWindow() : this(Array.Empty<string>()) { }

    public NotchWindow(string[] args)
    {
        this.args = args;
        InitializeComponent();
        Root.RenderTransform = RootMove;
        // a dock window, placed at the top centre before it is first shown
        if (TryGetPlatformHandle()?.Handle is { } h) X11Input.MakeDock(h);
        PlaceTopCenter();
        terminal = new TerminalPage(TakeKeyboard);
        InitHome();
        pages = new Panel[] { homeRoot, PagePomo, PageDay, PageWater, PageShelf, PageClip, PageTodo, terminal.Root, claudePage.Root, agPage.Root };
        Pages.Children.Add(terminal.Root);
        Pages.Children.Add(claudePage.Root);
        Pages.Children.Add(agPage.Root);
        claudePage.Footer(ClaudeLimits(false));
        agPage.Footer(AgLimits(false));
        // renamed pages ("Rezo" → "Día y tiempo", "Música" → "Home"): keep the user's order/visibility for them
        foreach (var list in new[] { data.PageOrder, data.HiddenPages })
            for (int i = 0; i < list.Count; i++) list[i] = list[i] switch { "Rezo" => PageNames[2], "Música" => PageNames[0], var n => n };
        InitTools();
        ApplySettings();
        page = VisiblePages()[0];
        foreach (var p in pages) p.IsVisible = p == pages[page];

        ui.Tick += (_, _) => UiTick();
        alertTimer.Tick += (_, _) => { alertTimer.Stop(); if (mode == Mode.Alert && !pinned) GoTo(Mode.Closed); };
        graceTimer.Tick += (_, _) => GraceTick();
        waveTimer.Tick += WaveTick;
        Notch.PointerEntered += (_, _) => { graceStart = null; if (mode is Mode.Closed && !notchHidden) GoTo(Mode.Expanded); };
        PointerExited += (_, _) => { if (mode == Mode.Expanded && !KeepOpen()) { graceStart = DateTime.Now; graceTimer.Interval = TimeSpan.FromMilliseconds(50); graceTimer.Start(); } };
        Notch.PointerEntered += (_, _) => graceStart = null;
        Notch.ContextRequested += (_, e) => { OpenSettings(); e.Handled = true; };
        Notch.PointerPressed += (_, e) => { if (mode == Mode.Alert && alertClick is { } click && e.GetCurrentPoint(Notch).Properties.IsLeftButtonPressed) { alertClick = null; alertTimer.Stop(); click(); GoTo(Mode.Closed); } };

        Opened += (_, _) => OnOpened();
        UiTick();
        ui.Start();
    }

    void OnOpened()
    {
        PlaceTopCenter();
        UpdateInput();
        Ipc.Listen(cmd => Dispatcher.UIThread.Post(() => OnCommand(cmd)));
        Hotkeys.Start(k => Dispatcher.UIThread.Post(() => OnCommand(k == 'n' ? "toggle" : "game")));
        StartMedia();
        AntigravityFeed.Start(OnAntigravity, OnAntigravityDone);
        DevShow();
        if (args.Contains("--settings")) OpenSettings();
    }

    void PlaceTopCenter()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null) return;
        var pos = new PixelPoint(screen.Bounds.X + (int)((screen.Bounds.Width - Width * screen.Scaling) / 2), screen.Bounds.Y);
        Position = pos;
        if (TryGetPlatformHandle()?.Handle is { } h) X11Input.Move(h, pos.X, pos.Y);
    }

    void OnCommand(string cmd)
    {
        var tab = cmd.IndexOf('\t');
        if (tab > 0) { OnAgentCommand(cmd[..tab], cmd[(tab + 1)..]); return; }
        switch (cmd)
        {
            case "toggle": ToggleHidden(); break;
            case "game": ToggleGame(); break;
            default: OpenSettings(); break;
        }
    }

    void DevShow()
    {
        var arg = args.FirstOrDefault(a => a.StartsWith("--show="))?[7..];
        if (arg == null) return;
        pinned = true;
        if (args.FirstOrDefault(a => a.StartsWith("--widgets="))?[10..] is { } w) { data.HomeWidgets = w.Split(',').ToList(); BuildHome(); } // not saved unless edited
        if (args.Contains("--editwidgets")) ToggleHomeEdit();
        if (args.FirstOrDefault(a => a.StartsWith("--limits="))?[9..].Split(',') is [var five, var week]) // e.g. --limits=23,91 (not saved)
        {
            var t = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            (data.ClaudeFive, data.ClaudeFiveReset, data.ClaudeWeek, data.ClaudeWeekReset) = (double.Parse(five, CultureInfo.InvariantCulture), t + 8000, double.Parse(week, CultureInfo.InvariantCulture), t + 300000);
        }
#if DEBUG
        // --snap=file.png: renders the notch to an image and quits (screenshots where the compositor won't show a dock window, e.g. WSLg)
        if (args.FirstOrDefault(a => a.StartsWith("--snap="))?[7..] is { } snap)
            DispatcherTimer.RunOnce(() =>
            {
                using var rtb = new Avalonia.Media.Imaging.RenderTargetBitmap(new PixelSize((int)Notch.Bounds.Width, (int)Notch.Bounds.Height));
                rtb.Render(Notch);
                rtb.Save(snap);
                Environment.Exit(0);
            }, TimeSpan.FromSeconds(4));
#endif
        if (int.TryParse(arg, out var p)) { page = p; foreach (var pg in pages) pg.IsVisible = pg == pages[p]; GoTo(Mode.Expanded); }
        else if (arg == "alert") ShowAlert("IcBolt", GreenB, "Cargando", "Cargador conectado", "70%");
        else if (arg == "game") ToggleGame();
    }

    internal void OpenSettings()
    {
        if (settings == null)
        {
            settings = new SettingsWindow(this);
            settings.Closed += (_, _) => settings = null;
        }
        settings.Show();
        settings.Activate();
    }

    void Settings_Click(object? s, RoutedEventArgs e) => OpenSettings();

    /// Re-reads settings after the panel changes them.
    internal void ApplySettings()
    {
        if (data.Anim == AnimStyle.Liquida) data.Anim = AnimStyle.Suave;
        if (data.PageAnim == PageAnim.Liquida) data.PageAnim = PageAnim.Deslizar;
        var accent = B(data.Accent);
        Ring.Stroke = accent;
        IBrush values = data.GameAccent ? accent : Brushes.White;
        GCpu.Foreground = GGpu.Foreground = GRam.Foreground = GVram.Foreground = values;
        Glow.Background = GlowBrush();
        ApplyTransitions();
        Glow.Opacity = GlowTarget(mode);
        NotchColor(mode);
        ApplyStyle();
        ConfigurePomo();
        InitWater();
        BuildHome(); // picks up the accent and the visible pages
        var v = VisiblePages();
        if (!v.Contains(page)) ShowPage(v[0], 0);
        UpdateDots();
        if (mode == Mode.Expanded) OnPageShown(page);
    }

    internal void SaveSettings() { Store.Save(data); ApplySettings(); }

    // ================= Modos y transiciones =================
    Control ViewOf(Mode m) => m switch
    {
        Mode.Closed => ClosedView,
        Mode.Alert => AlertView,
        Mode.Expanded => ExpandedView,
        _ => GameView,
    };

    static double gameWidth = 460;
    const double Ear = 16, IslandGap = 8;

    static (double W, double H, double R) SizeOf(Mode m) => m switch
    {
        Mode.Closed => (200, 38, 18),
        Mode.Alert => (380, 62, 24),
        Mode.Expanded => (expanded.W, expanded.H, 27), // depends on the page (FitExpanded)
        _ => (gameWidth, 26, 13),
    };

    CornerRadius Corners(Mode m)
    {
        var (_, h, r) = SizeOf(m);
        return data.Island ? new CornerRadius(h <= 70 ? h / 2 : r) : new CornerRadius(0, 0, r, r);
    }

    (Easing Ease, double Ms) ShapeEasing() => data.Anim switch
    {
        AnimStyle.Elastica => (new BackEaseOut(), 340),
        AnimStyle.Rebote => (new BounceEaseOut(), 520),
        AnimStyle.Rapida => (new QuadraticEaseOut(), 150),
        AnimStyle.Ninguna => (new LinearEasing(), 0),
        _ => (new CubicEaseOut(), 270),
    };

    TimeSpan Dur(double ms) => TimeSpan.FromMilliseconds(data.Anim == AnimStyle.Ninguna ? 0 : ms / Math.Clamp(data.AnimSpeed, 0.25, 3));

    void ApplyTransitions()
    {
        var (ease, ms) = ShapeEasing();
        Notch.Transitions = new Transitions
        {
            new DoubleTransition { Property = Layoutable.WidthProperty, Duration = Dur(ms), Easing = ease },
            new DoubleTransition { Property = Layoutable.HeightProperty, Duration = Dur(ms), Easing = ease },
        };
        foreach (var v in new Control[] { ClosedView, AlertView, ExpandedView, GameView, Glow, EarL, EarR })
            v.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = Dur(v == Glow ? 250 : 160) } };
        Root.Transitions = new Transitions { new ThicknessTransition { Property = Layoutable.MarginProperty, Duration = Dur(260), Easing = new CubicEaseOut() } };
        RootMove.Transitions = new Transitions { new DoubleTransition { Property = TranslateTransform.YProperty, Duration = Dur(340), Easing = new CubicEaseIn() } };
    }

    int goVersion;

    void GoTo(Mode m)
    {
        if (m == mode) return;
        var from = ViewOf(mode);
        var to = ViewOf(m);
        var v = ++goVersion;
        mode = m;

        var (w, h, _) = SizeOf(m);
        var (_, ms) = ShapeEasing();
        Notch.CornerRadius = Corners(m);
        Notch.Width = w;
        Notch.Height = h;
        Glow.Opacity = GlowTarget(m);
        NotchColor(m);
        ui.Interval = TimeSpan.FromMilliseconds(m == Mode.Expanded ? 250 : 1000);

        // old content fades out fast while the shape morphs; new content fades in once there's room
        from.Opacity = 0;
        DispatcherTimer.RunOnce(() => { if (ViewOf(mode) != from) from.IsVisible = false; }, Dur(170) + TimeSpan.FromMilliseconds(1));
        to.IsVisible = true;
        DispatcherTimer.RunOnce(() => { if (v == goVersion) to.Opacity = 1; }, Dur(ms * 0.45) + TimeSpan.FromMilliseconds(1));

        UpdateInput();
        DispatcherTimer.RunOnce(UpdateInput, Dur(ms) + TimeSpan.FromMilliseconds(30)); // exact area once it settles
        if (m == Mode.Expanded) OnPageShown(page);
    }

    // ================= Zona de gracia =================
    DateTime? graceStart;

    bool KeepOpen() => pinned || TaskInput.IsFocused || terminal.HasFocus || homeNote?.IsFocused == true || mode != Mode.Expanded;

    void GraceTick()
    {
        if (mode != Mode.Expanded || graceStart == null) { graceTimer.Stop(); return; }
        if (KeepOpen() || IsPointerOver) { graceStart = null; graceTimer.Stop(); return; }
        if ((DateTime.Now - graceStart.Value).TotalMilliseconds < data.GraceDelayMs) return;
        graceTimer.Stop();
        GoTo(Mode.Closed);
    }

    /// X11 input shape = the notch (plus the grace zone while open); clicks elsewhere go to the apps below.
    void UpdateInput()
    {
        if (TryGetPlatformHandle()?.Handle is not { } handle) return;
        var s = RenderScaling;
        var (w, h, _) = SizeOf(mode);
        w = Math.Max(w, Notch.Bounds.Width);
        h = Math.Max(h, Notch.Bounds.Height);
        double ears = data.Island ? 0 : Ear * 2, top = data.Island ? IslandGap : 0;
        double margin = mode == Mode.Expanded ? data.GraceZone : 0;
        double x = (Width - (w + ears)) / 2 - margin;
        X11Input.SetInputRect(handle, (int)(x * s), 0, (int)((w + ears + 2 * margin) * s), (int)((top + h + margin) * s));
    }

    // ================= Carrusel de páginas =================
    int[] VisiblePages()
    {
        var order = data.PageOrder.Where(PageNames.Contains).Concat(PageNames).Distinct();
        var v = order.Where(n => !data.HiddenPages.Contains(n)).Select(n => Array.IndexOf(PageNames, n)).ToArray();
        return v.Length == 0 ? new[] { 0 } : v;
    }

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

        old.Transitions = new Transitions { new DoubleTransition { Property = Visual.OpacityProperty, Duration = Dur(110) } };
        old.Opacity = 0;
        DispatcherTimer.RunOnce(() => { if (pages[page] != old) old.IsVisible = false; }, Dur(120) + TimeSpan.FromMilliseconds(1));

        // start state without transitions, then animate to rest on the next frame
        now.Transitions = null;
        now.Opacity = 0;
        now.RenderTransform = data.PageAnim switch
        {
            PageAnim.Deslizar when dir != 0 => TransformOperations.Parse($"translateX({44 * dir}px)"),
            PageAnim.Zoom => TransformOperations.Parse("scale(0.88)"),
            _ => TransformOperations.Identity,
        };
        now.IsVisible = true;
        Dispatcher.UIThread.Post(() =>
        {
            now.Transitions = new Transitions
            {
                new TransformOperationsTransition { Property = Visual.RenderTransformProperty, Duration = Dur(300), Easing = new CubicEaseOut() },
                new DoubleTransition { Property = Visual.OpacityProperty, Duration = Dur(220) },
            };
            now.RenderTransform = TransformOperations.Identity;
            now.Opacity = 1;
        }, DispatcherPriority.Background);
        UpdateDots();
        OnPageShown(i);
    }

    void UpdateDots()
    {
        Dots.Children.Clear();
        foreach (var i in VisiblePages())
            Dots.Children.Add(new Border { Width = i == page ? 14 : 5, Height = 5, CornerRadius = new CornerRadius(2.5), Background = i == page ? Brushes.White : FaintB });
    }

    /// Pages other than Home name themselves under the user's name in the top bar (Home shows the date there).
    void SetTitle(string main, string accent = "", IBrush? accentBrush = null)
    {
        if (page == 0) return;
        var inl = new InlineCollection { new Run(main) };
        if (accent != "") inl.Add(new Run(" " + accent) { Foreground = accentBrush ?? DimB, FontWeight = FontWeight.SemiBold });
        homeDate.Inlines = inl;
    }

    void OnPageShown(int i)
    {
        var now = DateTime.Now;
        FitExpanded();
        BuildHomeTabs(); // the bar is on every page: mark this one
        switch (i)
        {
            case 0: RefreshHome(); break;
            case 1: SetTitle("Pomodoro"); UpdatePomo(now); break;
            case 2: SetTitle("Tiempo", data.City); UpdateDay(now); break;
            case 3: SetTitle("Agua", "Recordatorio", BlueB); UpdateWater(); break;
            case 4: SetTitle("Bandeja"); UpdateShelf(); break;
            case 5: SetTitle("Portapapeles"); UpdateClip(); break;
            case 6: SetTitle("Tareas", "de hoy", GreenB); UpdateTodo(); break;
            case 7: SetTitle("Terminal", Environment.IsPrivilegedProcess ? "root" : "usuario", Environment.IsPrivilegedProcess ? OrangeB : DimB); break;
            case 8: UpdateActivity(); break;
            case 9: UpdateAntigravity(); break;
        }
    }

    bool PageVisible(int i) => mode == Mode.Expanded && page == i;

    // ================= Reloj y tareas periódicas =================
    void UiTick()
    {
        var now = DateTime.Now;
        Clock.Text = now.ToString("HH:mm");
        PomoTick(now);
        HomeTick(now);
        if (now.Second % 5 == 0) PowerTick();
        if (now.Minute != lastMinute)
        {
            lastMinute = now.Minute;
            WaterReminder(now);
            TodoReminders(now);
            if (PageVisible(2)) UpdateDay(now);
            if (PageVisible(5)) UpdateClip();
            if (PageVisible(8)) UpdateActivity();
            if (PageVisible(9)) UpdateAntigravity();
        }
    }

    // ================= Avisos compactos =================
    Action? alertClick;

    /// `onClick`: clicking the alert runs it and closes the notch. `image` replaces the icon (an app logo). `chat`: stays `MessageSecs`, like the Windows chat alerts.
    internal void ShowAlert(string icon, IBrush bg, string title, string sub, string value = "", Action? onClick = null, IImage? image = null, bool chat = false)
    {
        if (mode is Mode.Expanded or Mode.Game || notchHidden) return; // looking at it, gaming or hidden
        alertClick = onClick;
        AlertImage.Source = image;
        AlertImageBox.IsVisible = image != null;
        AlertIcon.IsVisible = AlertIconBg.IsVisible = image == null;
        if (image == null) AlertIcon.Data = Icon(icon);
        AlertIconBg.Fill = bg;
        AlertTitle.Text = title;
        AlertSub.Text = sub;
        AlertValue.Text = value;
        GoTo(Mode.Alert);
        alertTimer.Stop();
        alertTimer.Interval = TimeSpan.FromSeconds(chat ? data.MessageSecs : 3.5);
        alertTimer.Start();
    }

    bool? lastCharging;

    void PowerTick()
    {
        if (!SysInfo.TryBattery(out var charging, out var pct)) return; // desktop: no battery
        if (lastCharging != null && lastCharging != charging && data.ChargeAlerts)
            ShowAlert("IcBolt", charging ? GreenB : FaintB, charging ? "Cargando" : "Con batería",
                charging ? "Cargador conectado" : "Cargador desconectado", $"{pct}%");
        lastCharging = charging;
    }

    // ================= Anillo, onda, estilo =================
    internal static Geometry RingGeometry(double c, double r, double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        if (fraction >= 0.999) return new EllipseGeometry(new Rect(c - r, c - r, 2 * r, 2 * r));
        var a = fraction * 2 * Math.PI;
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c, c - r), false);
            ctx.ArcTo(new Point(c + r * Math.Sin(a), c - r * Math.Cos(a)), new Size(r, r), 0, fraction > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        return g;
    }

    double lastRing = -1;

    void SetRings(double fraction)
    {
        fraction = Math.Round(Math.Clamp(fraction, 0, 1), 3);
        if (fraction == lastRing) return;
        lastRing = fraction;
        Ring.Data = RingGeometry(9, 7.5, fraction);
    }

    // sound wave: bars bounce while music plays, flat white line when it's paused
    readonly DispatcherTimer waveTimer = new() { Interval = TimeSpan.FromMilliseconds(40) };
    readonly Stopwatch waveClock = Stopwatch.StartNew();
    bool wavePlaying;

    void SetWave(bool playing)
    {
        if (playing == wavePlaying) return;
        wavePlaying = playing;
        if (playing) { waveTimer.Start(); return; }
        waveTimer.Stop();
        foreach (var r in Wave.Children.OfType<Rectangle>()) r.Height = 4;
    }

    void WaveTick(object? s, EventArgs e)
    {
        if (notchHidden) return;
        var t = waveClock.Elapsed.TotalSeconds;
        double[] speed = { 9.0, 12.5, 7.3, 10.8 }, phase = { 0, 1.7, 3.1, 4.4 };
        int i = 0;
        foreach (var r in Wave.Children.OfType<Rectangle>()) { r.Height = 4 + 14 * (0.5 + 0.5 * Math.Sin(t * speed[i] + phase[i])); i++; }
    }

    internal static readonly Dictionary<string, string[]> GlowPalettes = new()
    {
        ["Aurora"] = new[] { "#1FC8C8", "#2D6BEA", "#7A3FE0" },
        ["Océano"] = new[] { "#00C6FF", "#0072FF", "#1A2A8F" },
        ["Atardecer"] = new[] { "#FF7E5F", "#FD3A69", "#8E2DE2" },
        ["Bosque"] = new[] { "#5BE37D", "#16A085", "#0B5E6B" },
        ["Acento"] = Array.Empty<string>(),
    };

    IBrush GlowBrush()
    {
        var cols = GlowPalettes.GetValueOrDefault(data.GlowPalette) is { Length: > 0 } p ? p : new[] { data.Accent, data.Accent, "#000000" };
        var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0.5, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0.5, RelativeUnit.Relative) };
        for (int i = 0; i < cols.Length; i++) b.GradientStops.Add(new GradientStop(Color.Parse(cols[i]), i / (double)(cols.Length - 1)));
        return b;
    }

    double GlowTarget(Mode m) => data.Glow && (m is not (Mode.Closed or Mode.Game) || data.GlowClosed) ? data.GlowIntensity : 0;

    void NotchColor(Mode m)
    {
        var a = (byte)(m == Mode.Game ? Math.Clamp(data.GameOpacity, 0.1, 1) * 255 : 255);
        var brush = new SolidColorBrush(Color.FromArgb(a, 0, 0, 0));
        Notch.Background = EarL.Fill = EarR.Fill = brush;
    }

    void ApplyStyle()
    {
        if (notchHidden) return;
        Notch.CornerRadius = Corners(mode);
        EarL.Opacity = EarR.Opacity = data.Island ? 0 : 1;
        Root.Margin = new Thickness(0, data.Island ? IslandGap : 0, 0, 0);
        UpdateInput();
    }

    // ================= --toggle: se hace bola y se esconde arriba =================
    bool notchHidden;
    int hideVersion;

    async void ToggleHidden()
    {
        var v = ++hideVersion;
        notchHidden = !notchHidden;
        graceTimer.Stop();
        alertTimer.Stop();
        if (notchHidden)
        {
            ViewOf(mode).Opacity = 0;
            EarL.Opacity = EarR.Opacity = Glow.Opacity = 0;
            Notch.CornerRadius = new CornerRadius(17);
            Notch.Width = Notch.Height = 34;
            await Task.Delay(Dur(320));
            if (v != hideVersion) return;
            RootMove.Y = -60;
            await Task.Delay(Dur(360));
            if (v != hideVersion) return;
            if (mode != Mode.Game) { ViewOf(mode).IsVisible = false; mode = Mode.Closed; ClosedView.IsVisible = true; }
            Hide();
        }
        else
        {
            Show();
            RootMove.Y = 0;
            await Task.Delay(Dur(380));
            if (v != hideVersion) return;
            var (w, h, _) = SizeOf(mode);
            Notch.CornerRadius = Corners(mode);
            Notch.Width = w;
            Notch.Height = h;
            EarL.Opacity = EarR.Opacity = data.Island ? 0 : 1;
            Glow.Opacity = GlowTarget(mode);
            await Task.Delay(Dur(140));
            if (v == hideVersion) ViewOf(mode).Opacity = 1;
            UpdateInput();
        }
    }

    // ================= --game: modo juego =================
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
            SysInfo.Cpu(); // prime the CPU delta
            while (!cts.IsCancellationRequested)
            {
                try { await Task.Delay(1000, cts.Token); } catch (TaskCanceledException) { return; }
                var cpu = SysInfo.Cpu();
                var mem = SysInfo.Memory();
                var gpu = SysInfo.Gpu();
                Dispatcher.UIThread.Post(() => ShowStats(cpu, mem.LoadPct, gpu.Gpu, gpu.VramUsedGb, gpu.VramTotalGb));
            }
        });
    }

    void ShowStats(double cpu, int ram, double? gpu, double? vramUsed, double? vramTotal)
    {
        if (mode != Mode.Game) return;
        GameClock.Text = DateTime.Now.ToString("HH:mm");
        GCpu.Text = $"{cpu:0}%";
        GRam.Text = $"{ram}%";
        GGpuBox.IsVisible = gpu != null;
        GGpu.Text = $"{gpu:0}%";
        GVramBox.IsVisible = vramUsed != null;
        GVram.Text = vramTotal is { } t ? $"{vramUsed:0.0}/{t:0} GB" : $"{vramUsed:0.0} GB";
        GameRow.Measure(Size.Infinity);
        var w = Math.Ceiling(GameRow.DesiredSize.Width) + 36;
        if (Math.Abs(w - gameWidth) < 2 || notchHidden) return;
        gameWidth = w;
        Notch.Width = w;
        UpdateInput();
    }
}
