using System.Net.NetworkInformation;

namespace Cato;

record HomeSample(StatsSample Stats, double Down, double Up);

// Home: cabecera (quién, fecha, accesos a las demás páginas) y una rejilla de widgets que elige el usuario.
// Cada widget es una tarjeta hecha en código con su función de refresco, que corre al mostrarse y cada segundo.
public partial class MainWindow
{
    /// Everything Home can show: id (saved in data.HomeWidgets), name, icon, columns it takes (of 3) and the page its title opens (-1: none).
    internal static readonly (string Id, string Name, string Glyph, int Span, int Page)[] HomeCatalog =
    {
        ("sistema", "Sistema", "", 1, -1),
        ("tiempo", "Tiempo", "", 2, 2),
        ("musica", "Reproduciendo", "", 2, -1),
        ("volumen", "Volumen", "", 1, -1),
        ("pomodoro", "Pomodoro", "", 1, 1),
        ("agua", "Agua", "", 1, 3),
        ("tareas", "Tareas", "", 1, 6),
        ("semana", "Esta semana", "", 1, 6),
        ("calendario", "Calendario", "", 1, -1),
        ("relojes", "Relojes del mundo", "", 1, -1),
        ("progreso", "Progreso", "", 1, -1),
        ("discos", "Discos", "", 1, -1),
        ("equipo", "Equipo", "", 1, -1),
        ("claude", "Claude", "", 1, 8),
        ("antigravity", "Antigravity", "", 1, 9),
        ("portapapeles", "Portapapeles", "", 2, 5),
        ("nota", "Nota rápida", "", 1, -1),
    };

    static readonly string[] PageGlyphs = { "", "", "", "💧", "", "", "", "", "", "" };
    static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    const double HomeW = 700, CardH = 124, HomeGap = 8;

    readonly Grid homeRoot = new() { Visibility = Visibility.Collapsed };
    readonly Grid homeGrid = new() { Margin = new Thickness(-4) };
    readonly WrapPanel homeAdd = new() { Margin = new Thickness(0, 12, 0, 0) };
    readonly StackPanel homeBody = new();
    readonly StackPanel homeTabs = new() { Orientation = Orientation.Horizontal };
    readonly ScrollViewer homeScroll = new() { VerticalScrollBarVisibility = ScrollBarVisibility.Hidden };
    readonly List<Action> homeRefresh = new();
    FrameworkElement homeHead = null!;
    TextBlock homeDate = null!;
    Button homePin = null!, homeEdit = null!;
    TextBox? homeNote;
    bool homeEditing;
    int homeSecond = -1;
    static (double W, double H) expanded = (580, 150);

    void InitHome()
    {
        homeHead = HomeHeader();
        homeHead.Margin = new Thickness(0, 0, 0, 8);
        ExpandedView.Children.Add(homeHead); // row 0: the top bar of every page
        for (int i = 0; i < 3; i++) homeGrid.ColumnDefinitions.Add(new ColumnDefinition());
        homeBody.Children.Add(homeGrid);
        homeBody.Children.Add(homeAdd);
        homeScroll.Content = homeBody;
        homeRoot.Children.Add(homeScroll);
        Pages.Children.Add(homeRoot);
    }

    FrameworkElement HomeHeader()
    {
        var g = new Grid();
        // equal sides: the tabs sit in the exact centre on every page, and long text on the left trims instead of overlapping
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        var name = Environment.UserName;
        var avatar = new Grid { Width = 34, Height = 34, Margin = new Thickness(0, 0, 10, 0) };
        avatar.Children.Add(new Ellipse { Fill = Hex("#1E3A6E"), Stroke = Hex("#405B8DEF"), StrokeThickness = 1 });
        avatar.Children.Add(new TextBlock
        {
            Text = name[..1].ToUpperInvariant(), Foreground = Hex("#8FB8FF"), FontSize = 15, FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        if (AccountPicture() is { } pic) avatar.Children.Add(new Ellipse { Fill = new ImageBrush(pic) { Stretch = Stretch.UniformToFill } });
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(Txt(name, 14, bold: true));
        who.Children.Add(homeDate = Txt("", 11, DimB));
        var left = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(avatar);
        left.Children.Add(who);
        var tabs = new Border { Background = Hex("#14FFFFFF"), CornerRadius = new CornerRadius(13), Padding = new Thickness(3), Child = homeTabs, Margin = new Thickness(10, 0, 10, 0), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(tabs, 1);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(homeEdit = IconButton("", 12, ToggleHomeEdit, "Editar widgets"));
        right.Children.Add(IconButton("", 12, () => Settings_Click(this, new RoutedEventArgs()), "Configuración"));
        right.Children.Add(homePin = IconButton("", 12, TogglePin, "Fijar abierto"));
        Grid.SetColumn(right, 2);
        g.Children.Add(left);
        g.Children.Add(tabs);
        g.Children.Add(right);
        return g;
    }

    /// The Windows account picture (Settings → Cuentas → Tu información), or null to keep the initial.
    static BitmapImage? AccountPicture()
    {
        try
        {
            var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User!.Value;
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AccountPicture\Users\" + sid);
            if ((key?.GetValue("Image448") ?? key?.GetValue("Image96")) is not string path || !File.Exists(path)) return null;
            var img = new BitmapImage();
            img.BeginInit();
            img.UriSource = new Uri(path);
            img.CacheOption = BitmapCacheOption.OnLoad; // read now: the file is not kept open
            img.DecodePixelWidth = 96;
            img.EndInit();
            img.Freeze();
            return img;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or NotSupportedException or FileFormatException) { Debug.WriteLine(ex); return null; }
    }

    void BuildHomeTabs()
    {
        homeTabs.Children.Clear();
        foreach (var i in VisiblePages())
        {
            object content = i switch
            {
                8 => new Image { Source = ClaudeIcon, Width = 15, Height = 15 },
                9 => new Image { Source = AntigravityIcon, Width = 15, Height = 15 },
                _ => PageGlyphs[i],
            };
            var b = new Button
            {
                Style = Res("IconBtn"), Content = content, FontSize = 13, Width = 36, Height = 30, Padding = new Thickness(0), Margin = new Thickness(1, 0, 1, 0),
                ToolTip = PageNames[i], Background = i == page ? Hex("#2EFFFFFF") : Brushes.Transparent, Foreground = i == page ? Hex(data.Accent) : Brushes.White,
            };
            if (i == 3) b.FontFamily = new FontFamily("Segoe UI Emoji");
            var target = i;
            b.Click += (_, _) => ShowPage(target, 1);
            homeTabs.Children.Add(b);
        }
    }

    void TogglePin()
    {
        pinned = !pinned;
        homePin.Content = pinned ? "" : "";
        homePin.Background = pinned ? Hex("#33FFFFFF") : Brushes.Transparent;
        homePin.ToolTip = pinned ? "Soltar" : "Fijar abierto";
        if (!pinned && !Notch.IsMouseOver) { outsideSince = null; graceTimer.Start(); }
    }

    void ToggleHomeEdit()
    {
        homeEditing = !homeEditing;
        homeEdit.Background = homeEditing ? Hex("#33FFFFFF") : Brushes.Transparent;
        homeEdit.Content = homeEditing ? "" : "";
        homeEdit.ToolTip = homeEditing ? "Listo" : "Editar widgets";
        BuildHome();
    }

    void SaveHome() { Store.Save(data); BuildHome(); }

    void MoveWidget(string id, int d)
    {
        var i = data.HomeWidgets.IndexOf(id);
        var j = i + d;
        if (i < 0 || j < 0 || j >= data.HomeWidgets.Count) return;
        (data.HomeWidgets[i], data.HomeWidgets[j]) = (data.HomeWidgets[j], data.HomeWidgets[i]);
        SaveHome();
    }

    /// Lays the chosen widgets out on a 3-column grid, a row at a time. A widget that doesn't fit the row
    /// starts the next one, and the previous widget stretches to close the gap.
    void BuildHome()
    {
        homeRefresh.Clear();
        homeGrid.Children.Clear();
        homeGrid.RowDefinitions.Clear();
        homeNote = null;
        var ids = data.HomeWidgets.Where(id => HomeCatalog.Any(w => w.Id == id)).Distinct().ToList();
        int row = 0, col = 0;
        Border? last = null;
        foreach (var def in ids.Select(id => HomeCatalog.First(w => w.Id == id)))
        {
            if (col + def.Span > 3) { Grid.SetColumnSpan(last!, Grid.GetColumnSpan(last!) + 3 - col); row++; col = 0; }
            if (col == 0) homeGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CardH + HomeGap) });
            last = Card(def);
            Grid.SetRow(last, row);
            Grid.SetColumn(last, col);
            Grid.SetColumnSpan(last, def.Span);
            homeGrid.Children.Add(last);
            col += def.Span;
            if (col == 3) { row++; col = 0; }
        }
        if (ids.Count == 0 && !homeEditing)
        {
            homeGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(CardH + HomeGap) });
            var hint = new TextBlock { Foreground = DimB, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            hint.Inlines.Add(new Run("Pulsa "));
            hint.Inlines.Add(new Run("") { FontFamily = IconFont });
            hint.Inlines.Add(new Run(" para añadir widgets"));
            Grid.SetColumnSpan(hint, 3);
            homeGrid.Children.Add(hint);
        }

        homeAdd.Children.Clear();
        homeAdd.Visibility = homeEditing ? Visibility.Visible : Visibility.Collapsed;
        if (homeEditing)
        {
            var missing = HomeCatalog.Where(w => !ids.Contains(w.Id)).ToList();
            if (missing.Count == 0) homeAdd.Children.Add(Txt("Ya tienes todos los widgets", 11, DimB));
            foreach (var def in missing)
            {
                var label = new TextBlock();
                label.Inlines.Add(new Run("  ") { FontFamily = IconFont, FontSize = 10 });
                label.Inlines.Add(new Run(def.Name));
                var b = new Button { Content = label, Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(0, 0, 6, 6), FontSize = 11 };
                b.Click += (_, _) => { data.HomeWidgets.Add(def.Id); SaveHome(); };
                homeAdd.Children.Add(b);
            }
        }
        RefreshHome();
        FitExpanded();
    }

    Border Card((string Id, string Name, string Glyph, int Span, int Page) def)
    {
        var title = new TextBlock { Text = def.Name, Foreground = DimB, FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var (body, refresh) = MakeWidget(def.Id, title);
        homeRefresh.Add(refresh);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 5), Height = 16 };
        if (homeEditing)
        {
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -6, -8, -6) };
            tools.Children.Add(IconButton("", 9, () => MoveWidget(def.Id, -1), "Mover antes"));
            tools.Children.Add(IconButton("", 9, () => MoveWidget(def.Id, 1), "Mover después"));
            tools.Children.Add(IconButton("", 9, () => { data.HomeWidgets.Remove(def.Id); SaveHome(); }, "Quitar"));
            DockPanel.SetDock(tools, Dock.Right);
            head.Children.Add(tools);
        }
        else if (def.Page >= 0 && VisiblePages().Contains(def.Page))
        {
            title.Text += "  ›";
            title.Cursor = Cursors.Hand;
            title.MouseLeftButtonUp += (_, e) => { ShowPage(def.Page, 1); e.Handled = true; };
        }
        head.Children.Add(title);
        DockPanel.SetDock(head, Dock.Top);
        var dock = new DockPanel();
        dock.Children.Add(head);
        dock.Children.Add(body);
        return new Border
        {
            Background = Hex("#14FFFFFF"), BorderBrush = homeEditing ? Hex("#40FFFFFF") : Hex("#1AFFFFFF"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(4), Child = dock, ClipToBounds = true,
        };
    }

    /// The expanded notch takes the size of the page: Home grows to fit its widgets (three rows at most, then it scrolls).
    void FitExpanded()
    {
        var home = page == 0;
        Dots.Visibility = home ? Visibility.Collapsed : Visibility.Visible;
        homeEdit.Visibility = home ? Visibility.Visible : Visibility.Hidden; // keeps its place: the tabs don't shift between pages
        var inner = HomeW - 36; // ExpandedView margins
        homeHead.Measure(new Size(inner, double.PositiveInfinity));
        double content = page is 8 or 9 ? 150 : 108; // Claude and Antigravity: room for the limits strip
        if (home)
        {
            homeBody.InvalidateMeasure(); // a direct Measure() returns the cached size until the next layout pass
            homeBody.Measure(new Size(inner, double.PositiveInfinity));
            content = Math.Min(homeBody.DesiredSize.Height, 3 * (CardH + HomeGap) - HomeGap + (homeEditing ? 40 : 0));
        }
        var size = (W: HomeW, H: Math.Ceiling(9 + homeHead.DesiredSize.Height + content + (home ? 12 : 15)));
        if (size == expanded) return;
        expanded = size;
        if (mode != Mode.Expanded || notchHidden || morphing) return;
        var (ease, ms) = ShapeEasing();
        Notch.CornerRadius = Glow.CornerRadius = Corners(Mode.Expanded);
        Resize(size.W, size.H, Math.Max(ms, 200), ease);
    }

    void RefreshHome()
    {
        var d = DateTime.Now.ToString("dddd, d 'de' MMMM", Es);
        homeDate.Text = char.ToUpper(d[0]) + d[1..];
        foreach (var r in homeRefresh) r();
    }

    /// From the UI timer: refreshes the widgets once a second while Home is open.
    void HomeTick(DateTime now)
    {
        homeLive = PageVisible(0) && data.HomeWidgets.Contains("sistema");
        if (homeLive) StartHomeSampler();
        if (!PageVisible(0) || now.Second == homeSecond) return;
        homeSecond = now.Second;
        RefreshHome();
    }

    // ---- system sampling for the "Sistema" widget, on a background thread and only while it's on screen ----
    volatile bool homeLive;
    volatile HomeSample? homeSample;
    bool homeSampling;

    void StartHomeSampler()
    {
        if (homeSampling) return;
        homeSampling = true;
        _ = Task.Run(async () =>
        {
            using var stats = new SystemStats(fps: false);
            long rx = -1, tx = 0;
            var clock = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    if (!homeLive) rx = -1;
                    else
                    {
                        var s = stats.Sample(false);
                        var (r, t) = NetBytes();
                        var secs = Math.Max(0.2, clock.Elapsed.TotalSeconds);
                        homeSample = new HomeSample(s, rx < 0 ? 0 : Math.Max(0, r - rx) / secs, rx < 0 ? 0 : Math.Max(0, t - tx) / secs);
                        (rx, tx) = (r, t);
                    }
                    clock.Restart();
                }
                catch (Exception ex) { Debug.WriteLine(ex); }
                await Task.Delay(1000);
            }
        });
    }

    static (long Rx, long Tx) NetBytes()
    {
        long r = 0, t = 0;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            var st = n.GetIPStatistics();
            r += st.BytesReceived;
            t += st.BytesSent;
        }
        return (r, t);
    }

    static string Speed(double bps) => bps < 1048576 ? $"{bps / 1024:0} KB/s" : $"{bps / 1048576:0.0} MB/s";

    // ================= Piezas comunes =================
    static Style Res(string key) => (Style)Application.Current.FindResource(key);

    static TextBlock Txt(string s, double size, Brush? fg = null, bool bold = false) => new()
    {
        Text = s, FontSize = size, Foreground = fg ?? Brushes.White, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    static Button IconButton(string glyph, double size, Action click, string tip)
    {
        var b = new Button { Style = Res("IconBtn"), Content = glyph, FontSize = size, ToolTip = tip, Padding = new Thickness(6), Margin = new Thickness(1, 0, 1, 0) };
        b.Click += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    static ProgressBar Bar(Brush fg, double top = 3) => new() { Style = Res("Thin"), Foreground = fg, Margin = new Thickness(0, top, 0, 0) };

    /// Key on the left, value on the right.
    static Grid KeyValue(string key, TextBlock value, double top = 0)
    {
        var g = new Grid { Margin = new Thickness(0, top, 0, 0) };
        g.Children.Add(Txt(key, 11.5, Hex("#CCFFFFFF")));
        value.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(value);
        return g;
    }

    /// A round button (play/pause in the player).
    static Grid RoundButton(TextBlock glyph, double size, Brush bg, Action click)
    {
        var g = new Grid { Width = size, Height = size, Cursor = Cursors.Hand, Background = Brushes.Transparent };
        g.Children.Add(new Ellipse { Fill = bg });
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        g.Children.Add(glyph);
        g.MouseLeftButtonUp += (_, e) => { click(); e.Handled = true; };
        return g;
    }

    static TextBlock Icon(string glyph, double size, Brush? fg = null) => new() { Text = glyph, FontFamily = IconFont, FontSize = size, Foreground = fg ?? Brushes.White };

    static string Cap(string s) => s == "" ? s : char.ToUpper(s[0]) + s[1..];

    // ================= Widgets =================
    (FrameworkElement Body, Action Refresh) MakeWidget(string id, TextBlock title) => id switch
    {
        "sistema" => SysWidget(),
        "tiempo" => WeatherWidget(),
        "musica" => MusicWidget(),
        "volumen" => VolumeWidget(),
        "pomodoro" => PomoWidget(),
        "agua" => WaterWidget(),
        "tareas" => TasksWidget(),
        "semana" => WeekWidget(),
        "calendario" => CalendarWidget(title),
        "relojes" => ClocksWidget(),
        "progreso" => ProgressWidget(),
        "discos" => DisksWidget(),
        "equipo" => PcWidget(),
        "claude" => AgentWidget(claudePage, ClaudeIcon, ClaudeLimits(true)),
        "antigravity" => AgentWidget(agPage, AntigravityIcon, AgLimits(true)),
        "portapapeles" => ClipWidget(),
        _ => NoteWidget(),
    };

    (FrameworkElement, Action) SysWidget()
    {
        TextBlock cpu = Txt("…", 11.5, bold: true), ram = Txt("…", 11.5, bold: true), gpu = Txt("…", 11.5, bold: true), net = Txt("…", 11.5, bold: true);
        ProgressBar cpuBar = Bar(BlueB), ramBar = Bar(BlueB);
        var p = new StackPanel();
        p.Children.Add(KeyValue("CPU", cpu));
        p.Children.Add(cpuBar);
        p.Children.Add(KeyValue("RAM", ram, 5));
        p.Children.Add(ramBar);
        p.Children.Add(KeyValue("GPU", gpu, 5));
        p.Children.Add(KeyValue("Red", net, 1));
        var totalGb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1073741824.0;
        return (p, () =>
        {
            if (homeSample is not { } h) return;
            var s = h.Stats;
            cpu.Text = $"{s.Cpu:0}%";
            cpuBar.Value = s.Cpu / 100;
            ram.Text = $"{s.RamPct / 100 * totalGb:0.0}/{totalGb:0.0} GB";
            ramBar.Value = s.RamPct / 100;
            gpu.Text = s.Gpu is { } g ? $"{g:0}%" : "—";
            net.Text = $"↓ {Speed(h.Down)}  ↑ {Speed(h.Up)}";
        });
    }

    (FrameworkElement, Action) WeatherWidget()
    {
        var glyph = new TextBlock { FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 32, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        TextBlock temp = Txt("--°", 28, bold: true), desc = Txt("", 12, DimB), meta = Txt("", 10.5, FaintB);
        meta.Margin = new Thickness(0, 6, 0, 0);
        var tv = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        tv.Children.Add(temp);
        tv.Children.Add(desc);
        var top = new DockPanel();
        top.Children.Add(glyph);
        top.Children.Add(tv);
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(top);
        left.Children.Add(meta);
        var days = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(days, 1);
        g.Children.Add(left);
        g.Children.Add(days);
        var shown = DateTime.MinValue;
        return (g, () =>
        {
            EnsureWeather(DateTime.Now);
            if (weather == null)
            {
                temp.Text = "--°";
                desc.Text = weatherLoading ? "Cargando…" : "Sin datos";
                meta.Text = data.City;
                return;
            }
            var (gl, text) = weather.Describe();
            glyph.Text = gl;
            glyph.Foreground = weather.Code <= 2 && weather.IsDay ? Hex("#FFD66B") : Brushes.White;
            temp.Text = $"{weather.Temp:0}°";
            desc.Text = text;
            meta.Text = $"{data.City} · Sensación {weather.Feels:0}° · {weather.Wind:0} km/h";
            if (shown == weatherAt) return; // the forecast only changes when the weather reloads
            shown = weatherAt;
            days.Children.Clear();
            foreach (var (day, code, max, min) in forecast.Take(4))
            {
                var col = new StackPanel { Width = 48 };
                var name = day.Date == DateTime.Today ? "Hoy" : Cap(day.ToString("ddd", Es).TrimEnd('.'));
                col.Children.Add(new TextBlock { Text = name, Foreground = DimB, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
                col.Children.Add(new TextBlock
                {
                    Text = Weather.Describe(code, true).Glyph, FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 20, Margin = new Thickness(0, 2, 0, 2),
                    Foreground = code <= 2 ? Hex("#FFD66B") : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center,
                });
                col.Children.Add(new TextBlock { Text = $"{max:0}°", Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
                col.Children.Add(new TextBlock { Text = $"{min:0}°", Foreground = DimB, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center });
                days.Children.Add(col);
            }
        });
    }

    (FrameworkElement, Action) MusicWidget()
    {
        var art = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(12), Background = Hex("#26FFFFFF"), VerticalAlignment = VerticalAlignment.Center };
        var artGlyph = Icon("", 26);
        artGlyph.HorizontalAlignment = HorizontalAlignment.Center;
        artGlyph.VerticalAlignment = VerticalAlignment.Center;
        art.Child = artGlyph;
        TextBlock title = Txt("", 15, bold: true), artist = Txt("", 12, DimB), elapsed = Txt("0:00", 10.5, DimB), total = Txt("0:00", 10.5, DimB);
        var bar = Bar(BlueB, 8);
        var times = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        total.HorizontalAlignment = HorizontalAlignment.Right;
        times.Children.Add(elapsed);
        times.Children.Add(total);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 14, 0) };
        foreach (UIElement e in new UIElement[] { title, artist, bar, times }) text.Children.Add(e);
        var playGlyph = Icon("", 16, Brushes.Black);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        controls.Children.Add(IconButton("", 12, () => Lyrics_Click(this, new RoutedEventArgs()), "Letra"));
        controls.Children.Add(IconButton("", 15, () => Prev_Click(this, new RoutedEventArgs()), "Anterior"));
        var play = RoundButton(playGlyph, 46, Hex(data.Accent), () => Play_Click(this, new RoutedEventArgs()));
        play.Margin = new Thickness(6, 0, 6, 0);
        controls.Children.Add(play);
        controls.Children.Add(IconButton("", 15, () => Next_Click(this, new RoutedEventArgs()), "Siguiente"));
        var g = new Grid();
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(text, 1);
        Grid.SetColumn(controls, 2);
        g.Children.Add(art);
        g.Children.Add(text);
        g.Children.Add(controls);
        return (g, () =>
        {
            title.Text = mediaTitle;
            artist.Text = mediaArtist;
            SetArt(art, artGlyph, mediaArt);
            playGlyph.Text = IsPlaying() ? "" : "";
            var (pos, len) = Timeline();
            bar.Value = len > TimeSpan.Zero ? pos / len : 0;
            elapsed.Text = Fmt(pos);
            total.Text = Fmt(len);
        });
    }

    (FrameworkElement, Action) VolumeWidget()
    {
        var pct = Txt("—", 28, bold: true);
        var mute = IconButton("", 14, () =>
        {
            try { if (audioDev != null) audioDev.AudioEndpointVolume.Mute = !audioDev.AudioEndpointVolume.Mute; }
            catch (COMException ex) { Debug.WriteLine(ex); } // device just went away
        }, "Silenciar");
        mute.VerticalAlignment = VerticalAlignment.Center;
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 4, 0, 0) };
        top.Children.Add(mute);
        top.Children.Add(pct);
        var slider = new Slider { Minimum = 0, Maximum = 100, IsSnapToTickEnabled = false, IsMoveToPointEnabled = true, Margin = new Thickness(0, 10, 0, 0) };
        var updating = false;
        slider.ValueChanged += (_, e) =>
        {
            if (updating || audioDev == null) return;
            try { audioDev.AudioEndpointVolume.MasterVolumeLevelScalar = (float)(e.NewValue / 100); }
            catch (COMException ex) { Debug.WriteLine(ex); }
            pct.Text = $"{e.NewValue:0}%";
        };
        var p = new StackPanel();
        p.Children.Add(top);
        p.Children.Add(slider);
        return (p, () =>
        {
            try
            {
                if (audioDev == null) return;
                var v = audioDev.AudioEndpointVolume;
                var level = v.MasterVolumeLevelScalar * 100;
                pct.Text = $"{level:0}%";
                mute.Content = v.Mute ? "" : "";
                if (slider.IsMouseCaptureWithin) return; // the user is dragging it
                updating = true;
                slider.Value = level;
                updating = false;
            }
            catch (COMException ex) { Debug.WriteLine(ex); }
        });
    }

    (FrameworkElement, Action) PomoWidget()
    {
        TextBlock phase = Txt("", 11, DimB), time = Txt("", 26, bold: true);
        var bar = Bar(PurpleB, 2);
        var start = new Button { Padding = new Thickness(12, 3, 12, 3), Background = Hex("#E6FFFFFF"), Foreground = Brushes.Black, FontWeight = FontWeights.SemiBold, FontSize = 11 };
        start.Click += (_, _) => { pomo.Toggle(DateTime.Now); RefreshHome(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        row.Children.Add(start);
        row.Children.Add(IconButton("", 11, () => { pomo.Reset(); ConfigurePomo(); RefreshHome(); }, "Reiniciar"));
        var p = new StackPanel();
        foreach (UIElement e in new UIElement[] { phase, time, bar, row }) p.Children.Add(e);
        return (p, () =>
        {
            var now = DateTime.Now;
            time.Text = Mmss(pomo.Remaining(now));
            phase.Text = pomo.Current.Focus ? $"Concentración · {pomo.FocusNumber}/{pomo.Sessions}" : pomo.Current.Long ? "Descanso largo" : "Descanso";
            bar.Value = 1 - pomo.Remaining(now) / pomo.Current.Len;
            start.Content = pomo.Running ? "Pausa" : pomo.Elapsed(now) > TimeSpan.Zero ? "Seguir" : "Iniciar";
        });
    }

    (FrameworkElement, Action) WaterWidget()
    {
        var amount = new TextBlock { Foreground = Brushes.White };
        var status = Txt("", 11, DimB);
        var bar = Bar(BlueB, 6);
        var drink = new Button { Background = BlueB, Padding = new Thickness(10, 3, 10, 3), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 9, 0, 0) };
        drink.Click += (_, _) => { Drink_Click(this, new RoutedEventArgs()); RefreshHome(); };
        var p = new StackPanel();
        foreach (UIElement e in new UIElement[] { amount, status, bar, drink }) p.Children.Add(e);
        return (p, () =>
        {
            InitWater();
            amount.Inlines.Clear();
            amount.Inlines.Add(new Run((WaterTotal / 1000.0).ToString("0.0#", Es)) { FontSize = 24, FontWeight = FontWeights.Bold });
            amount.Inlines.Add(new Run($" / {(data.WaterGoal / 1000.0).ToString("0.0#", Es)} L") { FontSize = 12, Foreground = DimB });
            var pctDone = Math.Min(100, WaterTotal * 100 / Math.Max(1, data.WaterGoal));
            status.Text = pctDone >= 100 ? "Meta cumplida" : $"{pctDone}% · faltan {data.WaterGoal - WaterTotal} ml";
            bar.Value = Math.Min(1, WaterTotal / (double)data.WaterGoal);
            drink.Content = $"💧 +{data.WaterSip} ml";
        });
    }

    (FrameworkElement, Action) TasksWidget()
    {
        var count = Txt("", 11, DimB);
        var list = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
        var p = new StackPanel();
        p.Children.Add(count);
        p.Children.Add(list);
        string? shown = null;
        return (p, () =>
        {
            var todo = data.Todos;
            count.Text = todo.Count == 0 ? "Sin tareas" : $"{todo.Count(t => t.Done)} de {todo.Count} hechas";
            var items = todo.OrderBy(t => t.Done).Take(3).ToList();
            var key = string.Join("|", items.Select(t => t.Text + t.Done + t.At));
            if (key == shown) return; // rebuilt only on change, so a click never lands on a row that's being replaced
            shown = key;
            list.Children.Clear();
            if (todo.Count == 0) list.Children.Add(Txt("Añádelas en la página Tareas", 11, FaintB));
            foreach (var t in items)
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                var check = new Grid { Width = 15, Height = 15, Margin = new Thickness(0, 0, 8, 0) };
                check.Children.Add(new Ellipse { Fill = t.Done ? GreenB : Brushes.Transparent, Stroke = DimB, StrokeThickness = t.Done ? 0 : 1.2 });
                if (t.Done) check.Children.Add(new TextBlock { Text = "", FontFamily = IconFont, FontSize = 8, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                row.Children.Add(check);
                if (t.At != null) { var at = Txt(t.At, 10, FaintB); DockPanel.SetDock(at, Dock.Right); row.Children.Add(at); }
                var text = Txt(t.Text, 12, t.Done ? DimB : Brushes.White);
                if (t.Done) text.TextDecorations = TextDecorations.Strikethrough;
                row.Children.Add(text);
                var item = t;
                row.MouseLeftButtonUp += (_, _) => { item.Done = !item.Done; item.Day = Today; Store.Save(data); RefreshHome(); };
                list.Children.Add(row);
            }
        });
    }

    (FrameworkElement, Action) WeekWidget()
    {
        var week = new Grid { Margin = new Thickness(0, 2, 0, 0) };
        TextBlock pending = Txt("", 12, bold: true), next = Txt("", 10.5, DimB);
        pending.Margin = new Thickness(0, 6, 0, 0);
        var p = new StackPanel();
        foreach (UIElement e in new UIElement[] { week, pending, next }) p.Children.Add(e);
        var drawn = DateTime.MinValue;
        return (p, () =>
        {
            var today = DateTime.Today;
            if (drawn != today)
            {
                drawn = today;
                week.Children.Clear();
                week.ColumnDefinitions.Clear();
                week.RowDefinitions.Clear();
                week.RowDefinitions.Add(new RowDefinition());
                week.RowDefinitions.Add(new RowDefinition());
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                string[] letters = { "L", "M", "X", "J", "V", "S", "D" };
                for (int i = 0; i < 7; i++)
                {
                    week.ColumnDefinitions.Add(new ColumnDefinition());
                    var day = monday.AddDays(i);
                    var l = new TextBlock { Text = letters[i], FontSize = 9, Foreground = DimB, HorizontalAlignment = HorizontalAlignment.Center };
                    Grid.SetColumn(l, i);
                    week.Children.Add(l);
                    var cell = new Grid { Width = 20, Height = 20, Margin = new Thickness(0, 2, 0, 0) };
                    if (day == today) cell.Children.Add(new Ellipse { Fill = BlueB });
                    cell.Children.Add(new TextBlock
                    {
                        Text = day.Day.ToString(), FontSize = 10.5, Foreground = Brushes.White, FontWeight = day == today ? FontWeights.Bold : FontWeights.Normal,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    });
                    Grid.SetColumn(cell, i);
                    Grid.SetRow(cell, 1);
                    week.Children.Add(cell);
                }
            }
            var left = data.Todos.Where(t => !t.Done).ToList();
            pending.Text = left.Count == 1 ? "1 tarea pendiente" : $"{left.Count} tareas pendientes";
            var nowHm = DateTime.Now.ToString("HH:mm");
            var n = left.Where(t => t.At != null && string.CompareOrdinal(t.At, nowHm) >= 0).OrderBy(t => t.At).FirstOrDefault();
            next.Text = n != null ? $"Siguiente: {n.Text}, {n.At}" : "Sin recordatorios pendientes";
        });
    }

    (FrameworkElement, Action) CalendarWidget(TextBlock title)
    {
        var g = new Grid();
        for (int i = 0; i < 7; i++) g.ColumnDefinitions.Add(new ColumnDefinition());
        var drawn = DateTime.MinValue;
        return (g, () =>
        {
            var today = DateTime.Today;
            if (drawn == today) return;
            drawn = today;
            title.Text = Cap(today.ToString("MMMM yyyy", Es));
            g.Children.Clear();
            g.RowDefinitions.Clear();
            var first = new DateTime(today.Year, today.Month, 1);
            var offset = ((int)first.DayOfWeek + 6) % 7;
            var weeks = (offset + DateTime.DaysInMonth(today.Year, today.Month) + 6) / 7;
            for (int r = 0; r <= weeks; r++) g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(weeks > 5 ? 11 : 13) });
            string[] letters = { "L", "M", "X", "J", "V", "S", "D" };
            for (int i = 0; i < 7; i++)
            {
                var l = new TextBlock { Text = letters[i], FontSize = 8.5, Foreground = FaintB, HorizontalAlignment = HorizontalAlignment.Center };
                Grid.SetColumn(l, i);
                g.Children.Add(l);
            }
            for (int d = 1; d <= DateTime.DaysInMonth(today.Year, today.Month); d++)
            {
                var k = offset + d - 1;
                var isToday = d == today.Day;
                var cell = new Border
                {
                    Background = isToday ? BlueB : Brushes.Transparent, CornerRadius = new CornerRadius(5), Padding = new Thickness(3, 0, 3, 0), HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock { Text = d.ToString(), FontSize = 9, Foreground = k % 7 >= 5 && !isToday ? DimB : Brushes.White, FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal },
                };
                Grid.SetColumn(cell, k % 7);
                Grid.SetRow(cell, k / 7 + 1);
                g.Children.Add(cell);
            }
        });
    }

    static readonly (string City, string Zone)[] WorldClocks = { ("Nueva York", "Eastern Standard Time"), ("Londres", "GMT Standard Time"), ("Tokio", "Tokyo Standard Time") };

    (FrameworkElement, Action) ClocksWidget()
    {
        var p = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        var rows = new List<(TimeZoneInfo Zone, TextBlock Time, TextBlock Diff)>();
        foreach (var (city, id) in WorldClocks)
        {
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (TimeZoneNotFoundException) { continue; }
            TextBlock time = Txt("", 15, bold: true), diff = Txt("", 10, FaintB);
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            name.Children.Add(Txt(city, 11.5));
            diff.Margin = new Thickness(6, 1, 0, 0);
            name.Children.Add(diff);
            var row = new Grid { Height = 25 };
            time.HorizontalAlignment = HorizontalAlignment.Right;
            time.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            row.Children.Add(time);
            p.Children.Add(row);
            rows.Add((zone, time, diff));
        }
        return (p, () =>
        {
            var utc = DateTime.UtcNow;
            foreach (var (zone, time, diff) in rows)
            {
                var there = TimeZoneInfo.ConvertTimeFromUtc(utc, zone);
                time.Text = there.ToString("HH:mm");
                var h = (zone.GetUtcOffset(utc) - TimeZoneInfo.Local.GetUtcOffset(utc)).TotalHours;
                var day = there.Date == DateTime.Today ? "" : there.Date > DateTime.Today ? ", mañana" : ", ayer";
                diff.Text = (h == 0 ? "igual" : $"{(h > 0 ? "+" : "−")}{Math.Abs(h):0.#} h") + day;
            }
        });
    }

    (FrameworkElement, Action) ProgressWidget()
    {
        var p = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        var rows = new List<(Func<DateTime, double> Of, ProgressBar Bar, TextBlock Pct)>();
        foreach (var (name, of) in new (string, Func<DateTime, double>)[]
        {
            ("Día", n => n.TimeOfDay.TotalHours / 24),
            ("Semana", n => ((((int)n.DayOfWeek + 6) % 7) + n.TimeOfDay.TotalHours / 24) / 7),
            ("Mes", n => (n.Day - 1 + n.TimeOfDay.TotalHours / 24) / DateTime.DaysInMonth(n.Year, n.Month)),
            ("Año", n => (n.DayOfYear - 1 + n.TimeOfDay.TotalHours / 24) / (DateTime.IsLeapYear(n.Year) ? 366 : 365)),
        })
        {
            var g = new Grid { Height = 20 };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
            var label = Txt(name, 11.5, Hex("#CCFFFFFF"));
            label.VerticalAlignment = VerticalAlignment.Center;
            var bar = Bar(PurpleB, 0);
            bar.VerticalAlignment = VerticalAlignment.Center;
            var pct = Txt("", 11, bold: true);
            pct.HorizontalAlignment = HorizontalAlignment.Right;
            pct.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(bar, 1);
            Grid.SetColumn(pct, 2);
            g.Children.Add(label);
            g.Children.Add(bar);
            g.Children.Add(pct);
            p.Children.Add(g);
            rows.Add((of, bar, pct));
        }
        return (p, () =>
        {
            var now = DateTime.Now;
            foreach (var (of, bar, pct) in rows)
            {
                var v = Math.Clamp(of(now), 0, 1);
                bar.Value = v;
                pct.Text = $"{v * 100:0}%";
            }
        });
    }

    (FrameworkElement, Action) DisksWidget()
    {
        var p = new StackPanel();
        var read = DateTime.MinValue;
        return (p, () =>
        {
            if ((DateTime.Now - read).TotalSeconds < 30) return; // free space barely moves
            read = DateTime.Now;
            p.Children.Clear();
            try
            {
                foreach (var d in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed && d.IsReady).Take(3))
                {
                    double total = d.TotalSize / 1073741824.0, used = total - d.TotalFreeSpace / 1073741824.0;
                    p.Children.Add(KeyValue(d.Name.TrimEnd('\\'), Txt(total >= 1000 ? $"{used / 1024:0.0}/{total / 1024:0.0} TB" : $"{used:0}/{total:0} GB", 11, bold: true), p.Children.Count == 0 ? 0 : 5));
                    var bar = Bar(used / total > 0.9 ? OrangeB : BlueB);
                    bar.Value = used / total;
                    p.Children.Add(bar);
                }
            }
            catch (IOException ex) { Debug.WriteLine(ex); }
        });
    }

    (FrameworkElement, Action) PcWidget()
    {
        var ring = new System.Windows.Shapes.Path { Stroke = GreenB, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
        var glyph = Icon("", 15);
        glyph.HorizontalAlignment = HorizontalAlignment.Center;
        glyph.VerticalAlignment = VerticalAlignment.Center;
        var dial = new Grid { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center };
        dial.Children.Add(new Ellipse { Stroke = TrackB, StrokeThickness = 4 });
        dial.Children.Add(ring);
        dial.Children.Add(glyph);
        TextBlock value = Txt("", 20, bold: true), state = Txt("", 11, GreenB), uptime = Txt("", 10.5, DimB);
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        foreach (UIElement e in new UIElement[] { value, state, uptime }) text.Children.Add(e);
        var g = new DockPanel();
        g.Children.Add(dial);
        g.Children.Add(text);
        return (g, () =>
        {
            if (Native.TryPower(out var charging, out var pct))
            {
                value.Text = $"{pct}%";
                state.Text = charging ? "Cargando" : "Con batería";
                glyph.Text = charging ? "" : "";
                ring.Data = RingGeometry(23, 21, pct / 100.0);
            }
            else
            {
                var load = Native.MemoryLoad();
                value.Text = $"{load}%";
                state.Text = "Memoria en uso";
                glyph.Text = "";
                ring.Data = RingGeometry(23, 21, load / 100.0);
            }
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            uptime.Text = up.TotalDays >= 1 ? $"Encendido {(int)up.TotalDays} d {up.Hours} h" : $"Encendido {up.Hours} h {up.Minutes} min";
        });
    }

    (FrameworkElement, Action) AgentWidget(ActivityPage feed, ImageSource icon, (FrameworkElement Body, Action Refresh) limits)
    {
        var state = new TextBlock { FontSize = 12, Foreground = Brushes.White, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel();
        head.Children.Add(new Image { Source = icon, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), Clip = new RectangleGeometry(new Rect(0, 0, 18, 18), 5, 5) });
        head.Children.Add(state);
        var note = new TextBlock { Foreground = Hex("#CCFFFFFF"), FontSize = 11, TextWrapping = TextWrapping.Wrap, LineHeight = 15, MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) };
        // limits pinned to the bottom of the card, the feed above takes what is left
        var p = new DockPanel { LastChildFill = true };
        limits.Body.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(limits.Body, Dock.Bottom);
        DockPanel.SetDock(head, Dock.Top);
        p.Children.Add(limits.Body);
        p.Children.Add(head);
        p.Children.Add(note);
        return (p, () =>
        {
            limits.Refresh();
            var (count, working, last) = feed.Overview();
            state.Inlines.Clear();
            if (last == null) { state.Inlines.Add(new Run("Sin actividad") { Foreground = DimB }); note.Text = ""; return; }
            var (label, dot) = ActivityPage.Look(last.EffectiveState);
            state.Inlines.Add(new Run("● ") { Foreground = count > 1 ? (working > 0 ? GreenB : FaintB) : dot });
            state.Inlines.Add(new Run(count > 1 ? $"{count} sesiones · {working} trabajando" : $"{label} · {last.Name}"));
            note.Text = last.Summary;
        });
    }

    (FrameworkElement, Action) ClipWidget()
    {
        var row = new Grid();
        for (int i = 0; i < 3; i++) row.ColumnDefinitions.Add(new ColumnDefinition());
        string? shown = null;
        return (row, () =>
        {
            var key = string.Join("|", clips.Take(3).Select(c => c.Text + Ago(c.At)));
            if (key == shown) return;
            shown = key;
            row.Children.Clear();
            if (clips.Count == 0)
            {
                var empty = Txt("Copia texto y aparecerá aquí · clic para volver a copiarlo", 11, FaintB);
                Grid.SetColumnSpan(empty, 3);
                row.Children.Add(empty);
            }
            foreach (var ((text, at), i) in clips.Take(3).Select((c, i) => (c, i)))
            {
                var g = new DockPanel();
                var age = Txt(Ago(at), 9.5, FaintB);
                DockPanel.SetDock(age, Dock.Bottom);
                g.Children.Add(age);
                g.Children.Add(new TextBlock { Text = Shorten(text), Foreground = Brushes.White, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis });
                var card = new Border
                {
                    Background = Hex("#17FFFFFF"), CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 5), Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
                    BorderThickness = new Thickness(1), BorderBrush = i == 0 ? PurpleB : Brushes.Transparent, Cursor = Cursors.Hand, Child = g, ToolTip = "Copiar",
                };
                card.MouseLeftButtonUp += (_, _) => { try { Clipboard.SetText(text); } catch (COMException) { } };
                Grid.SetColumn(card, i);
                row.Children.Add(card);
            }
        });
    }

    (FrameworkElement, Action) NoteWidget()
    {
        var box = new TextBox
        {
            Text = data.Note, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            Background = Brushes.Transparent, Foreground = Brushes.White, CaretBrush = Brushes.White, BorderThickness = new Thickness(0), FontSize = 12, Padding = new Thickness(-2, 0, 0, 0),
        };
        var hint = Txt("Escribe algo para no olvidarlo…", 12, FaintB);
        hint.IsHitTestVisible = false;
        hint.Visibility = box.Text == "" ? Visibility.Visible : Visibility.Collapsed;
        box.TextChanged += (_, _) => { data.Note = box.Text; hint.Visibility = box.Text == "" ? Visibility.Visible : Visibility.Collapsed; };
        box.PreviewMouseDown += (_, _) => { Activate(); box.Focus(); }; // the notch window isn't active until asked
        box.KeyDown += (_, e) => { if (e.Key == Key.Escape) Keyboard.ClearFocus(); };
        box.LostKeyboardFocus += (_, _) =>
        {
            Store.Save(data);
            if (!Notch.IsMouseOver) { outsideSince = null; graceTimer.Start(); } // typing kept it open; now let it close
        };
        homeNote = box;
        var g = new Grid();
        g.Children.Add(hint);
        g.Children.Add(box);
        return (g, () => { });
    }
}
