using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using IOPath = System.IO.Path;

namespace Cato;

// Home (port of MainWindow.Home.cs): the top bar of every page (who, date, tabs to the other pages) and a grid of widgets the user picks.
// Each widget is a card built in code with its refresh function, which runs when shown and once a second.
public partial class NotchWindow
{
    /// Everything Home can show: id (saved in data.HomeWidgets), name, columns it takes (of 3) and the page its title opens (-1: none).
    internal static readonly (string Id, string Name, int Span, int Page)[] HomeCatalog =
    {
        ("sistema", "Sistema", 1, -1),
        ("tiempo", "Tiempo", 2, 2),
        ("musica", "Reproduciendo", 2, -1),
        ("volumen", "Volumen", 1, -1),
        ("pomodoro", "Pomodoro", 1, 1),
        ("agua", "Agua", 1, 3),
        ("tareas", "Tareas", 1, 6),
        ("semana", "Esta semana", 1, 6),
        ("calendario", "Calendario", 1, -1),
        ("relojes", "Relojes del mundo", 1, -1),
        ("progreso", "Progreso", 1, -1),
        ("discos", "Discos", 1, -1),
        ("equipo", "Equipo", 1, -1),
        ("claude", "Claude", 1, 8),
        ("antigravity", "Antigravity", 1, 9),
        ("portapapeles", "Portapapeles", 2, 5),
        ("nota", "Nota rápida", 1, -1),
    };

    static readonly string[] PageIcons = { "IcHome", "IcTimer", "IcCloud", "IcDrop", "IcTray", "IcClipboard", "IcCheck", "IcConsole" }; // 8, 9: app logos
    const double HomeW = 700, CardH = 124, HomeGap = 8;
    static readonly IBrush TrackB2 = B("#14FFFFFF");

    readonly Panel homeRoot = new() { IsVisible = false };
    readonly Grid homeGrid = new() { Margin = new Thickness(-4), ColumnDefinitions = new ColumnDefinitions("*,*,*") };
    readonly WrapPanel homeAdd = new() { Margin = new Thickness(0, 12, 0, 0) };
    readonly StackPanel homeBody = new();
    readonly StackPanel homeTabs = new() { Orientation = Orientation.Horizontal };
    readonly List<Action> homeRefresh = new();
    Control homeHead = null!;
    TextBlock homeDate = null!;
    Button homePin = null!, homeEdit = null!;
    TextBox? homeNote;
    bool homeEditing;
    int homeSecond = -1;
    static (double W, double H) expanded = (HomeW, 150);

    void InitHome()
    {
        homeHead = HomeHeader();
        ExpandedView.Children.Add(homeHead); // row 0: the top bar of every page
        homeBody.Children.Add(homeGrid);
        homeBody.Children.Add(homeAdd);
        homeRoot.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Hidden, Content = homeBody });
        Pages.Children.Add(homeRoot);
    }

    Control HomeHeader()
    {
        // equal sides: the tabs sit in the exact centre on every page, and long text on the left trims instead of overlapping
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*"), Margin = new Thickness(0, 0, 0, 8) };
        var name = Environment.UserName;
        var avatar = new Panel { Width = 34, Height = 34, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
        avatar.Children.Add(new Ellipse { Fill = B("#1E3A6E"), Stroke = B("#405B8DEF"), StrokeThickness = 1 });
        avatar.Children.Add(new TextBlock
        {
            Text = name[..1].ToUpperInvariant(), Foreground = B("#8FB8FF"), FontSize = 15, FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        });
        if (AccountPicture() is { } pic) avatar.Children.Add(new Border { CornerRadius = new CornerRadius(17), ClipToBounds = true, Child = new Image { Source = pic, Stretch = Stretch.UniformToFill } });
        var who = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        who.Children.Add(Txt(name, 14, bold: true));
        who.Children.Add(homeDate = Txt("", 11, DimB));
        var left = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(avatar);
        left.Children.Add(who);
        var tabs = new Border { Background = TrackB2, CornerRadius = new CornerRadius(13), Padding = new Thickness(3), Child = homeTabs, Margin = new Thickness(10, 0), HorizontalAlignment = HorizontalAlignment.Center };
        Grid.SetColumn(tabs, 1);
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        right.Children.Add(homeEdit = IconBtn("IcPencil", 13, ToggleHomeEdit, "Editar widgets"));
        right.Children.Add(IconBtn("IcCog", 13, OpenSettings, "Configuración"));
        right.Children.Add(homePin = IconBtn("IcPin", 13, TogglePin, "Fijar abierto"));
        Grid.SetColumn(right, 2);
        g.Children.Add(left);
        g.Children.Add(tabs);
        g.Children.Add(right);
        return g;
    }

    /// The account picture: AccountsService (what the desktop's user settings show), else ~/.face. Null keeps the initial.
    static Bitmap? AccountPicture()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var p in new[] { IOPath.Combine("/var/lib/AccountsService/icons", Environment.UserName), IOPath.Combine(home, ".face"), IOPath.Combine(home, ".face.icon") })
        {
            try
            {
                if (!File.Exists(p)) continue;
                using var f = File.OpenRead(p);
                return Bitmap.DecodeToWidth(f, 96);
            }
            catch (Exception ex) { Debug.WriteLine(ex); } // unreadable or not an image: try the next
        }
        return null;
    }

    void BuildHomeTabs()
    {
        homeTabs.Children.Clear();
        foreach (var i in VisiblePages())
        {
            Control content = i switch
            {
                8 => new Image { Source = ClaudeIcon, Width = 15, Height = 15 },
                9 => new Image { Source = AntigravityIcon, Width = 15, Height = 15 },
                _ => new PathIcon { Data = Icon(PageIcons[i]), Width = 14, Height = 14, Foreground = i == page ? B(data.Accent) : Brushes.White },
            };
            var b = new Button
            {
                Classes = { "icon" }, Content = content, Width = 33, Height = 30, Padding = new Thickness(0), Margin = new Thickness(1, 0), CornerRadius = new CornerRadius(10),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                Background = i == page ? B("#2EFFFFFF") : Brushes.Transparent,
            };
            ToolTip.SetTip(b, PageNames[i]);
            var target = i;
            b.Click += (_, _) => ShowPage(target, 1);
            homeTabs.Children.Add(b);
        }
    }

    void TogglePin()
    {
        pinned = !pinned;
        homePin.Background = pinned ? B("#33FFFFFF") : Brushes.Transparent;
        ToolTip.SetTip(homePin, pinned ? "Soltar" : "Fijar abierto");
        if (!pinned && !IsPointerOver && mode == Mode.Expanded) { graceStart = DateTime.Now; graceTimer.Interval = TimeSpan.FromMilliseconds(50); graceTimer.Start(); }
    }

    void ToggleHomeEdit()
    {
        homeEditing = !homeEditing;
        homeEdit.Background = homeEditing ? B("#33FFFFFF") : Brushes.Transparent;
        ((PathIcon)homeEdit.Content!).Data = Icon(homeEditing ? "IcCheck" : "IcPencil");
        ToolTip.SetTip(homeEdit, homeEditing ? "Listo" : "Editar widgets");
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
            if (col == 0) homeGrid.RowDefinitions.Add(new RowDefinition(CardH + HomeGap, GridUnitType.Pixel));
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
            homeGrid.RowDefinitions.Add(new RowDefinition(CardH + HomeGap, GridUnitType.Pixel));
            var hint = new TextBlock { Text = "Pulsa el lápiz para añadir widgets", Foreground = DimB, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumnSpan(hint, 3);
            homeGrid.Children.Add(hint);
        }

        homeAdd.Children.Clear();
        homeAdd.IsVisible = homeEditing;
        if (homeEditing)
        {
            var missing = HomeCatalog.Where(w => !ids.Contains(w.Id)).ToList();
            if (missing.Count == 0) homeAdd.Children.Add(Txt("Ya tienes todos los widgets", 11, DimB));
            foreach (var def in missing)
            {
                var b = new Button
                {
                    Classes = { "pill" }, FontSize = 11, Padding = new Thickness(10, 4), Margin = new Thickness(0, 0, 6, 6),
                    Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Children = { new PathIcon { Data = Icon("IcPlus"), Width = 9, Height = 9 }, new TextBlock { Text = def.Name } } },
                };
                b.Click += (_, _) => { data.HomeWidgets.Add(def.Id); SaveHome(); };
                homeAdd.Children.Add(b);
            }
        }
        RefreshHome();
        FitExpanded();
    }

    Border Card((string Id, string Name, int Span, int Page) def)
    {
        var title = new TextBlock { Text = def.Name, Foreground = DimB, FontSize = 11, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var (body, refresh) = MakeWidget(def.Id, title);
        homeRefresh.Add(refresh);
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 5), Height = 16 };
        if (homeEditing)
        {
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, -6, -8, -6) };
            tools.Children.Add(IconBtn("IcLeft", 10, () => MoveWidget(def.Id, -1), "Mover antes"));
            tools.Children.Add(IconBtn("IcRight", 10, () => MoveWidget(def.Id, 1), "Mover después"));
            tools.Children.Add(IconBtn("IcClose", 10, () => { data.HomeWidgets.Remove(def.Id); SaveHome(); }, "Quitar"));
            DockPanel.SetDock(tools, Dock.Right);
            head.Children.Add(tools);
        }
        else if (def.Page >= 0 && VisiblePages().Contains(def.Page))
        {
            title.Text += "  ›";
            title.Cursor = new Cursor(StandardCursorType.Hand);
            title.Tapped += (_, e) => { ShowPage(def.Page, 1); e.Handled = true; };
        }
        head.Children.Add(title);
        DockPanel.SetDock(head, Dock.Top);
        var dock = new DockPanel();
        dock.Children.Add(head);
        dock.Children.Add(body);
        return new Border
        {
            Background = TrackB2, BorderBrush = homeEditing ? B("#40FFFFFF") : B("#1AFFFFFF"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 8), Margin = new Thickness(4), Child = dock, ClipToBounds = true,
        };
    }

    /// The expanded notch takes the size of the page: Home grows to fit its widgets (three rows at most, then it scrolls).
    void FitExpanded()
    {
        var home = page == 0;
        Dots.IsVisible = !home;
        homeEdit.Opacity = home ? 1 : 0; // keeps its place: the tabs don't shift between pages
        homeEdit.IsHitTestVisible = home;
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
        if (mode != Mode.Expanded || notchHidden) return;
        Notch.Width = size.W;
        Notch.Height = size.H;
        UpdateInput();
        DispatcherTimer.RunOnce(UpdateInput, Dur(ShapeEasing().Ms) + TimeSpan.FromMilliseconds(30)); // exact area once it settles
    }

    void RefreshHome()
    {
        if (page == 0)
        {
            var d = DateTime.Now.ToString("dddd, d 'de' MMMM", Es);
            homeDate.Text = Cap(d);
        }
        foreach (var r in homeRefresh) r();
    }

    /// From the UI timer: refreshes the widgets once a second while Home is open.
    void HomeTick(DateTime now)
    {
        homeLive = PageVisible(0) && (data.HomeWidgets.Contains("sistema") || data.HomeWidgets.Contains("volumen"));
        if (homeLive) StartHomeSampler();
        if (!PageVisible(0) || now.Second == homeSecond) return;
        homeSecond = now.Second;
        RefreshHome();
    }

    // ---- system and volume sampling for the widgets, on a background thread and only while Home is on screen ----
    record HomeSample(double Cpu, int RamPct, double UsedGb, double TotalGb, double? Gpu, double Down, double Up, (double Level, bool Muted)? Volume);
    volatile bool homeLive;
    volatile HomeSample? homeSample;
    bool homeSampling;

    void StartHomeSampler()
    {
        if (homeSampling) return;
        homeSampling = true;
        _ = Task.Run(async () =>
        {
            long rx = -1, tx = 0;
            var clock = Stopwatch.StartNew();
            while (true)
            {
                try
                {
                    if (!homeLive) rx = -1;
                    else
                    {
                        var cpu = SysInfo.Cpu();
                        var (load, used, total) = SysInfo.Memory();
                        var (r, t) = NetBytes();
                        var secs = Math.Max(0.2, clock.Elapsed.TotalSeconds);
                        homeSample = new HomeSample(cpu, load, used, total, SysInfo.Gpu().Gpu,
                            rx < 0 ? 0 : Math.Max(0, r - rx) / secs, rx < 0 ? 0 : Math.Max(0, t - tx) / secs, Volume.Read());
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
    static TextBlock Txt(string s, double size, IBrush? fg = null, bool bold = false) => new()
    {
        Text = s, FontSize = size, Foreground = fg ?? Brushes.White, FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    static Button IconBtn(string icon, double size, Action click, string tip)
    {
        var b = new Button { Classes = { "icon" }, Padding = new Thickness(6), Margin = new Thickness(1, 0), Content = new PathIcon { Data = Icon(icon), Width = size, Height = size } };
        ToolTip.SetTip(b, tip);
        b.Click += (_, e) => { click(); e.Handled = true; };
        return b;
    }

    static ProgressBar Bar(IBrush fg, double top = 3) => new() { Classes = { "thin" }, Foreground = fg, Margin = new Thickness(0, top, 0, 0) };

    /// Key on the left, value on the right.
    static Grid KeyValue(string key, TextBlock value, double top = 0)
    {
        var g = new Grid { Margin = new Thickness(0, top, 0, 0) };
        g.Children.Add(Txt(key, 11.5, B("#CCFFFFFF")));
        value.HorizontalAlignment = HorizontalAlignment.Right;
        g.Children.Add(value);
        return g;
    }

    static string Cap(string s) => s == "" ? s : char.ToUpper(s[0]) + s[1..];

    // ================= Widgets =================
    (Control Body, Action Refresh) MakeWidget(string id, TextBlock title) => id switch
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

    (Control, Action) SysWidget()
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
        return (p, () =>
        {
            if (homeSample is not { } h) return;
            cpu.Text = $"{h.Cpu:0}%";
            cpuBar.Value = h.Cpu / 100;
            ram.Text = $"{h.UsedGb:0.0}/{h.TotalGb:0.0} GB";
            ramBar.Value = h.RamPct / 100.0;
            gpu.Text = h.Gpu is { } g ? $"{g:0}%" : "—"; // only AMD reports it
            net.Text = $"↓ {Speed(h.Down)}  ↑ {Speed(h.Up)}";
        });
    }

    (Control, Action) WeatherWidget()
    {
        var glyph = new TextBlock { FontSize = 32, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
        TextBlock temp = Txt("--°", 28, bold: true), desc = Txt("", 12, DimB), meta = Txt("", 10.5, FaintB);
        meta.Margin = new Thickness(0, 6, 0, 0);
        var tv = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { temp, desc } };
        var top = new DockPanel { Children = { glyph, tv } };
        var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Children = { top, meta } };
        var days = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
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
            glyph.Foreground = weather.Code <= 2 && weather.IsDay ? B("#FFD66B") : Brushes.White;
            temp.Text = $"{weather.Temp:0}°";
            desc.Text = text;
            meta.Text = $"{data.City} · Sensación {weather.Feels:0}° · {weather.Wind:0} km/h";
            if (shown == weatherAt) return; // the forecast only changes when the weather reloads
            shown = weatherAt;
            days.Children.Clear();
            foreach (var (day, code, max, min) in forecast.Take(4))
            {
                var name = day.Date == DateTime.Today ? "Hoy" : Cap(day.ToString("ddd", Es).TrimEnd('.'));
                days.Children.Add(new StackPanel
                {
                    Width = 48,
                    Children =
                    {
                        new TextBlock { Text = name, Foreground = DimB, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = Weather.Describe(code, true).Glyph, FontSize = 20, Margin = new Thickness(0, 2), Foreground = code <= 2 ? B("#FFD66B") : Brushes.White, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = $"{max:0}°", FontSize = 12, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                        new TextBlock { Text = $"{min:0}°", Foreground = DimB, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center },
                    },
                });
            }
        });
    }

    (Control, Action) MusicWidget()
    {
        var artImg = new Image { Stretch = Stretch.UniformToFill };
        var artGlyph = new PathIcon { Data = Icon("IcMusic"), Width = 26, Height = 26 };
        var art = new Border { Width = 76, Height = 76, CornerRadius = new CornerRadius(12), Background = B("#26FFFFFF"), ClipToBounds = true, VerticalAlignment = VerticalAlignment.Center, Child = new Panel { Children = { artGlyph, artImg } } };
        TextBlock title = Txt("", 15, bold: true), artist = Txt("", 12, DimB), lyric = Txt("", 11.5, GreenB), elapsed = Txt("0:00", 10.5, DimB), total = Txt("0:00", 10.5, DimB);
        var bar = Bar(BlueB, 8);
        total.HorizontalAlignment = HorizontalAlignment.Right;
        var times = new Grid { Margin = new Thickness(0, 4, 0, 0), Children = { elapsed, total } };
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0), Children = { title, artist, lyric, bar, times } };
        var playIcon = new PathIcon { Data = Icon("IcPlay"), Width = 18, Height = 18, Foreground = Brushes.Black };
        var playBg = new Ellipse { Fill = B(data.Accent) };
        var play = new Panel { Width = 46, Height = 46, Margin = new Thickness(6, 0), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent, Children = { playBg, playIcon } };
        play.Tapped += (_, e) => { Play_Click(null, new RoutedEventArgs()); e.Handled = true; };
        var controls = new StackPanel
        {
            Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center,
            Children = { IconBtn("IcPrev", 16, () => Prev_Click(null, new RoutedEventArgs()), "Anterior"), play, IconBtn("IcNext", 16, () => Next_Click(null, new RoutedEventArgs()), "Siguiente") },
        };
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto") };
        Grid.SetColumn(text, 1);
        Grid.SetColumn(controls, 2);
        g.Children.Add(art);
        g.Children.Add(text);
        g.Children.Add(controls);
        return (g, () =>
        {
            title.Text = media?.Title is { Length: > 0 } t ? t : "Nada sonando";
            artist.Text = media?.Artist ?? "";
            lyric.Text = CurrentLyric();
            lyric.IsVisible = lyric.Text != "";
            artImg.Source = mediaArt;
            artGlyph.IsVisible = mediaArt == null;
            playIcon.Data = Icon(media?.Playing == true ? "IcPause" : "IcPlay");
            var (pos, len) = Timeline();
            bar.Value = len > TimeSpan.Zero ? pos / len : 0;
            elapsed.Text = Fmt(pos);
            total.Text = Fmt(len);
        });
    }

    (Control, Action) VolumeWidget()
    {
        var pct = Txt("—", 28, bold: true);
        var muteIcon = new PathIcon { Data = Icon("IcVolume"), Width = 16, Height = 16 };
        var mute = new Button { Classes = { "icon" }, Padding = new Thickness(6), Content = muteIcon, VerticalAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(mute, "Silenciar");
        mute.Click += (_, _) => Task.Run(Volume.ToggleMute);
        var top = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 4, 0, 0), Children = { mute, pct } };
        var slider = new Slider { Minimum = 0, Maximum = 100, Margin = new Thickness(0, 4, 0, 0) };
        var updating = false;
        double target = -1;
        var setting = false;
        slider.ValueChanged += (_, e) =>
        {
            if (updating) return;
            pct.Text = $"{e.NewValue:0}%";
            // dragging fires many changes: one worker applies only the latest value
            target = e.NewValue;
            if (setting) return;
            setting = true;
            _ = Task.Run(() =>
            {
                double done = -1;
                while (target != done) { done = target; Volume.Set(done); }
                setting = false;
            });
        };
        var p = new StackPanel { Children = { top, slider } };
        return (p, () =>
        {
            if (homeSample?.Volume is not { } v) { pct.Text = homeSample == null ? "…" : "—"; return; } // "—": no wpctl/pactl
            muteIcon.Data = Icon(v.Muted ? "IcMute" : "IcVolume");
            if (slider.IsPointerOver || setting) return; // the user is moving it
            pct.Text = $"{v.Level:0}%";
            updating = true;
            slider.Value = v.Level;
            updating = false;
        });
    }

    (Control, Action) PomoWidget()
    {
        TextBlock phase = Txt("", 11, DimB), time = Txt("", 26, bold: true);
        var bar = Bar(PurpleB, 2);
        var start = new Button { Classes = { "pill" }, Background = B("#E6FFFFFF"), Foreground = Brushes.Black, FontWeight = FontWeight.SemiBold, FontSize = 11 };
        start.Click += (_, _) => { pomo.Toggle(DateTime.Now); RefreshHome(); };
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Children = { start, IconBtn("IcRefresh", 12, () => { pomo.Reset(); ConfigurePomo(); RefreshHome(); }, "Reiniciar") } };
        var p = new StackPanel { Children = { phase, time, bar, row } };
        return (p, () =>
        {
            var now = DateTime.Now;
            time.Text = Mmss(pomo.Remaining(now));
            phase.Text = pomo.Current.Focus ? $"Concentración · {pomo.FocusNumber}/{pomo.Sessions}" : pomo.Current.Long ? "Descanso largo" : "Descanso";
            bar.Value = 1 - pomo.Remaining(now) / pomo.Current.Len;
            start.Content = pomo.Running ? "Pausa" : pomo.Elapsed(now) > TimeSpan.Zero ? "Seguir" : "Iniciar";
        });
    }

    (Control, Action) WaterWidget()
    {
        var amount = new TextBlock();
        var status = Txt("", 11, DimB);
        var bar = Bar(BlueB, 6);
        var drink = new Button { Classes = { "pill" }, Background = BlueB, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 9, 0, 0) };
        drink.Click += (_, _) => { Drink_Click(null, new RoutedEventArgs()); RefreshHome(); };
        var p = new StackPanel { Children = { amount, status, bar, drink } };
        return (p, () =>
        {
            InitWater();
            amount.Inlines = new InlineCollection
            {
                new Run((WaterTotal / 1000.0).ToString("0.0#", Es)) { FontSize = 24, FontWeight = FontWeight.Bold },
                new Run($" / {(data.WaterGoal / 1000.0).ToString("0.0#", Es)} L") { FontSize = 12, Foreground = DimB },
            };
            var done = Math.Min(100, WaterTotal * 100 / Math.Max(1, data.WaterGoal));
            status.Text = done >= 100 ? "Meta cumplida" : $"{done}% · faltan {data.WaterGoal - WaterTotal} ml";
            bar.Value = Math.Min(1, WaterTotal / (double)data.WaterGoal);
            drink.Content = $"+{data.WaterSip} ml";
        });
    }

    (Control, Action) TasksWidget()
    {
        var count = Txt("", 11, DimB);
        var list = new StackPanel { Margin = new Thickness(0, 3, 0, 0) };
        var p = new StackPanel { Children = { count, list } };
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
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 0), Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent };
                var check = new Panel { Width = 15, Height = 15, Margin = new Thickness(0, 0, 8, 0) };
                check.Children.Add(new Ellipse { Fill = t.Done ? GreenB : Brushes.Transparent, Stroke = DimB, StrokeThickness = t.Done ? 0 : 1.2 });
                if (t.Done) check.Children.Add(new PathIcon { Data = Icon("IcCheck"), Width = 8, Height = 8 });
                row.Children.Add(check);
                if (t.At != null) { var at = Txt(t.At, 10, FaintB); DockPanel.SetDock(at, Dock.Right); row.Children.Add(at); }
                var text = Txt(t.Text, 12, t.Done ? DimB : Brushes.White);
                if (t.Done) text.TextDecorations = TextDecorations.Strikethrough;
                row.Children.Add(text);
                var item = t;
                row.Tapped += (_, _) => { item.Done = !item.Done; item.Day = Today; Store.Save(data); RefreshHome(); };
                list.Children.Add(row);
            }
        });
    }

    static readonly string[] DayLetters = { "L", "M", "X", "J", "V", "S", "D" };

    (Control, Action) WeekWidget()
    {
        var week = new Grid { Margin = new Thickness(0, 2, 0, 0), ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto") };
        TextBlock pending = Txt("", 12, bold: true), next = Txt("", 10.5, DimB);
        pending.Margin = new Thickness(0, 6, 0, 0);
        var p = new StackPanel { Children = { week, pending, next } };
        var drawn = DateTime.MinValue;
        return (p, () =>
        {
            var today = DateTime.Today;
            if (drawn != today)
            {
                drawn = today;
                week.Children.Clear();
                var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
                for (int i = 0; i < 7; i++)
                {
                    var day = monday.AddDays(i);
                    week.Children.Add(Col(new TextBlock { Text = DayLetters[i], FontSize = 9, Foreground = DimB, HorizontalAlignment = HorizontalAlignment.Center }, i));
                    var cell = new Panel { Width = 20, Height = 20, Margin = new Thickness(0, 2, 0, 0) };
                    if (day == today) cell.Children.Add(new Ellipse { Fill = BlueB });
                    cell.Children.Add(new TextBlock
                    {
                        Text = day.Day.ToString(), FontSize = 10.5, FontWeight = day == today ? FontWeight.Bold : FontWeight.Normal,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    });
                    week.Children.Add(Row(Col(cell, i), 1));
                }
            }
            var left = data.Todos.Where(t => !t.Done).ToList();
            pending.Text = left.Count == 1 ? "1 tarea pendiente" : $"{left.Count} tareas pendientes";
            var nowHm = DateTime.Now.ToString("HH:mm");
            var n = left.Where(t => t.At != null && string.CompareOrdinal(t.At, nowHm) >= 0).OrderBy(t => t.At).FirstOrDefault();
            next.Text = n != null ? $"Siguiente: {n.Text}, {n.At}" : "Sin recordatorios pendientes";
        });
    }

    (Control, Action) CalendarWidget(TextBlock title)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*") };
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
            var days = DateTime.DaysInMonth(today.Year, today.Month);
            var weeks = (offset + days + 6) / 7;
            for (int r = 0; r <= weeks; r++) g.RowDefinitions.Add(new RowDefinition(weeks > 5 ? 11 : 13, GridUnitType.Pixel));
            for (int i = 0; i < 7; i++) g.Children.Add(Col(new TextBlock { Text = DayLetters[i], FontSize = 8.5, Foreground = FaintB, HorizontalAlignment = HorizontalAlignment.Center }, i));
            for (int d = 1; d <= days; d++)
            {
                var k = offset + d - 1;
                var isToday = d == today.Day;
                var cell = new Border
                {
                    Background = isToday ? BlueB : Brushes.Transparent, CornerRadius = new CornerRadius(5), Padding = new Thickness(3, 0), HorizontalAlignment = HorizontalAlignment.Center,
                    Child = new TextBlock { Text = d.ToString(), FontSize = 9, Foreground = k % 7 >= 5 && !isToday ? DimB : Brushes.White, FontWeight = isToday ? FontWeight.Bold : FontWeight.Normal },
                };
                g.Children.Add(Row(Col(cell, k % 7), k / 7 + 1));
            }
        });
    }

    static readonly (string City, string Zone)[] WorldClocks = { ("Nueva York", "America/New_York"), ("Londres", "Europe/London"), ("Tokio", "Asia/Tokyo") };

    (Control, Action) ClocksWidget()
    {
        var p = new StackPanel { Margin = new Thickness(0, 2, 0, 0) };
        var rows = new List<(TimeZoneInfo Zone, TextBlock Time, TextBlock Diff)>();
        foreach (var (city, id) in WorldClocks)
        {
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(id); }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException) { continue; } // no tzdata
            TextBlock time = Txt("", 15, bold: true), diff = Txt("", 10, FaintB);
            diff.Margin = new Thickness(6, 1, 0, 0);
            var name = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { Txt(city, 11.5), diff } };
            time.HorizontalAlignment = HorizontalAlignment.Right;
            time.VerticalAlignment = VerticalAlignment.Center;
            p.Children.Add(new Grid { Height = 25, Children = { name, time } });
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

    (Control, Action) ProgressWidget()
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
            var label = Txt(name, 11.5, B("#CCFFFFFF"));
            label.VerticalAlignment = VerticalAlignment.Center;
            var bar = Bar(PurpleB, 0);
            bar.VerticalAlignment = VerticalAlignment.Center;
            var pct = Txt("", 11, bold: true);
            pct.HorizontalAlignment = HorizontalAlignment.Right;
            pct.VerticalAlignment = VerticalAlignment.Center;
            p.Children.Add(new Grid { Height = 20, ColumnDefinitions = new ColumnDefinitions("52,*,36"), Children = { label, Col(bar, 1), Col(pct, 2) } });
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

    (Control, Action) DisksWidget()
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
                // real disks only: the root, /home and removable/extra mounts, not the many pseudo and snap mounts
                var disks = DriveInfo.GetDrives()
                    .Where(d => d.IsReady && d.DriveType == DriveType.Fixed && d.TotalSize > 1L << 30
                        && (d.Name is "/" or "/home" || d.Name.StartsWith("/mnt/") || d.Name.StartsWith("/media/") || d.Name.StartsWith("/run/media/")))
                    .DistinctBy(d => (d.TotalSize, d.TotalFreeSpace)).Take(3);
                foreach (var d in disks)
                {
                    double total = d.TotalSize / 1073741824.0, used = total - d.TotalFreeSpace / 1073741824.0;
                    p.Children.Add(KeyValue(d.Name, Txt(total >= 1000 ? $"{used / 1024:0.0}/{total / 1024:0.0} TB" : $"{used:0}/{total:0} GB", 11, bold: true), p.Children.Count == 0 ? 0 : 5));
                    var bar = Bar(used / total > 0.9 ? OrangeB : BlueB);
                    bar.Value = used / total;
                    p.Children.Add(bar);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
        });
    }

    (Control, Action) PcWidget()
    {
        var ring = new Avalonia.Controls.Shapes.Path { Stroke = GreenB, StrokeThickness = 4, StrokeLineCap = PenLineCap.Round };
        var glyph = new PathIcon { Width = 15, Height = 15 };
        var dial = new Panel { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center, Children = { new Ellipse { Stroke = TrackB, StrokeThickness = 4 }, ring, glyph } };
        TextBlock value = Txt("", 20, bold: true), state = Txt("", 11, GreenB), uptime = Txt("", 10.5, DimB);
        var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Children = { value, state, uptime } };
        var g = new DockPanel { Children = { dial, text } };
        return (g, () =>
        {
            if (SysInfo.TryBattery(out var charging, out var pct))
            {
                value.Text = $"{pct}%";
                state.Text = charging ? "Cargando" : "Con batería";
                glyph.Data = Icon("IcBolt");
                ring.Data = RingGeometry(23, 21, pct / 100.0);
            }
            else
            {
                var (load, _, _) = SysInfo.Memory();
                value.Text = $"{load}%";
                state.Text = "Memoria en uso";
                glyph.Data = Icon("IcChip");
                ring.Data = RingGeometry(23, 21, load / 100.0);
            }
            var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
            uptime.Text = up.TotalDays >= 1 ? $"Encendido {(int)up.TotalDays} d {up.Hours} h" : $"Encendido {up.Hours} h {up.Minutes} min";
        });
    }

    (Control, Action) AgentWidget(ActivityPage feed, IImage icon, (Control Body, Action Refresh) limits)
    {
        var state = new TextBlock { FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var head = new DockPanel
        {
            Children = { new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), Child = new Image { Source = icon } }, state },
        };
        var note = new TextBlock { Foreground = B("#CCFFFFFF"), FontSize = 11, TextWrapping = TextWrapping.Wrap, LineHeight = 15, MaxHeight = 30, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 5, 0, 0) };
        // limits pinned to the bottom of the card, the feed above takes what is left
        limits.Body.Margin = new Thickness(0, 4, 0, 0);
        DockPanel.SetDock(limits.Body, Dock.Bottom);
        DockPanel.SetDock(head, Dock.Top);
        var p = new DockPanel { Children = { limits.Body, head, note } };
        return (p, () =>
        {
            limits.Refresh();
            var (count, working, last) = feed.Overview();
            if (last == null) { state.Inlines = new InlineCollection { new Run("Sin actividad") { Foreground = DimB } }; note.Text = ""; return; }
            var (label, dot) = ActivityPage.Look(last.EffectiveState);
            state.Inlines = new InlineCollection
            {
                new Run("● ") { Foreground = count > 1 ? (working > 0 ? GreenB : FaintB) : dot },
                new Run(count > 1 ? $"{count} sesiones · {working} trabajando" : $"{label} · {last.Name}"),
            };
            note.Text = last.Summary;
        });
    }

    (Control, Action) ClipWidget()
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*") };
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
                var age = Txt(Ago(at), 9.5, FaintB);
                DockPanel.SetDock(age, Dock.Bottom);
                var flat = Regex.Replace(text, @"\s+", " ").Trim();
                var card = new Border
                {
                    Background = CardB, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 6, 8, 5), Margin = new Thickness(i == 0 ? 0 : 6, 0, 0, 0),
                    BorderThickness = new Thickness(1), BorderBrush = i == 0 ? PurpleB : Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                    Child = new DockPanel { Children = { age, new TextBlock { Text = flat.Length > 140 ? flat[..140] : flat, FontSize = 11, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis } } },
                };
                ToolTip.SetTip(card, "Copiar");
                card.Tapped += async (_, _) => { if (Clipboard != null) { lastClip = text; await Clipboard.SetTextAsync(text); } };
                row.Children.Add(Col(card, i));
            }
        });
    }

    (Control, Action) NoteWidget()
    {
        var box = new TextBox
        {
            Text = data.Note, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Background = Brushes.Transparent, BorderThickness = new Thickness(0),
            FontSize = 12, Padding = new Thickness(0), MinHeight = 0, Watermark = "Escribe algo para no olvidarlo…",
        };
        // Fluent paints its own background on hover and focus: keep the card's
        box.Resources["TextControlBackgroundPointerOver"] = Brushes.Transparent;
        box.Resources["TextControlBackgroundFocused"] = Brushes.Transparent;
        box.Resources["TextControlBorderBrushFocused"] = Brushes.Transparent;
        box.Resources["TextControlBorderBrushPointerOver"] = Brushes.Transparent;
        box.TextChanged += (_, _) => data.Note = box.Text ?? "";
        box.AddHandler(PointerPressedEvent, (_, _) => TakeKeyboard(), RoutingStrategies.Tunnel); // a dock window only gets the keyboard when it asks
        box.KeyDown += (_, e) => { if (e.Key == Key.Escape) Focus(); };
        box.LostFocus += (_, _) =>
        {
            Store.Save(data);
            if (!IsPointerOver && mode == Mode.Expanded) { graceStart = DateTime.Now; graceTimer.Interval = TimeSpan.FromMilliseconds(50); graceTimer.Start(); } // typing kept it open; now let it close
        };
        homeNote = box;
        return (box, () => { });
    }
}

/// System volume through PipeWire (wpctl) or PulseAudio (pactl), whichever is installed. Runs off the UI thread.
static class Volume
{
    const string Sink = "@DEFAULT_AUDIO_SINK@", PaSink = "@DEFAULT_SINK@";

    /// (level 0-100, muted), or null with neither tool.
    public static (double Level, bool Muted)? Read()
    {
        if (Run("wpctl", "get-volume", Sink) is { } w && Regex.Match(w, @"Volume:\s*([\d.]+)") is { Success: true } m)
            return (double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) * 100, w.Contains("MUTED"));
        if (Run("pactl", "get-sink-volume", PaSink) is { } p && Regex.Match(p, @"(\d+)%") is { Success: true } pm)
            return (double.Parse(pm.Groups[1].Value, CultureInfo.InvariantCulture), Run("pactl", "get-sink-mute", PaSink)?.Contains("yes") == true);
        return null;
    }

    public static void Set(double level)
    {
        if (Run("wpctl", "set-volume", Sink, (level / 100).ToString("0.00", CultureInfo.InvariantCulture)) == null)
            Run("pactl", "set-sink-volume", PaSink, $"{level:0}%");
    }

    public static void ToggleMute()
    {
        if (Run("wpctl", "set-mute", Sink, "toggle") == null) Run("pactl", "set-sink-mute", PaSink, "toggle");
    }

    /// Its output, or null when the tool is missing or fails.
    static string? Run(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return p.ExitCode == 0 ? output : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { return null; } // not installed
    }
}
