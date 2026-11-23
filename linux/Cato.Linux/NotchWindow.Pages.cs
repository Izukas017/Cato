using System.Net.Http;
using System.Text.Json;
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
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using IOPath = System.IO.Path;

namespace Cato;

public partial class NotchWindow
{
    static readonly HttpClient http = CreateHttp();

    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Cato/0.1 (Linux)");
        return h;
    }

    void InitTools()
    {
        InitShelf();
        WaterRemind.IsCheckedChanged += (_, _) => { data.WaterRemind = WaterRemind.IsChecked == true; Store.Save(data); };
        TaskInput.KeyDown += TaskInput_KeyDown;
        RoundTrack.SizeChanged += (_, _) => DrawRoundTrack(DateTime.Now);
        SunArc.SizeChanged += (_, _) => { if (PageVisible(2)) UpdateDay(DateTime.Now); };
        WaterChart.SizeChanged += (_, _) => DrawWaterChart();
        var clipTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
        clipTimer.Tick += async (_, _) => await PollClipboard();
        clipTimer.Start();
        data.Todos.RemoveAll(t => t.Done && t.Day != Today);
    }

    // ================= Música (MPRIS) y letras (lrclib.net: recibe título y artista) =================
    readonly Mpris mpris = new();
    MediaInfo? media;
    DateTime mediaAt;
    string artUrl = "", lyricsKey = "";
    List<LyricLine> lyrics = new();

    void StartMedia() => _ = Task.Run(async () =>
    {
        while (true)
        {
            var m = await mpris.ReadAsync();
            Dispatcher.UIThread.Post(() => ShowMedia(m));
            await Task.Delay(1000);
        }
    });

    void ShowMedia(MediaInfo? m)
    {
        media = m;
        mediaAt = DateTime.Now;
        MediaTitle.Text = m?.Title is { Length: > 0 } t ? t : "Nada sonando";
        MediaArtist.Text = m?.Artist ?? "";
        PlayIcon.Data = Icon(m?.Playing == true ? "IcPause" : "IcPlay");
        SetWave(m?.Playing == true);
        var key = m == null ? "" : m.Title + "|" + m.Artist;
        if (key != lyricsKey)
        {
            lyricsKey = key;
            lyrics = new();
            LyricLine.IsVisible = false;
            if (m != null && data.Lyrics) _ = LoadLyrics(key, m.Title, m.Artist);
        }
        if ((m?.ArtUrl ?? "") != artUrl) { artUrl = m?.ArtUrl ?? ""; _ = LoadArt(artUrl); }
        if (mode == Mode.Expanded) UpdateMediaProgress();
    }

    (TimeSpan Pos, TimeSpan Len) Timeline()
    {
        if (media == null) return (TimeSpan.Zero, TimeSpan.Zero);
        var pos = media.Position + (media.Playing ? DateTime.Now - mediaAt : TimeSpan.Zero); // players report position now and then
        return (media.Length > TimeSpan.Zero && pos > media.Length ? media.Length : pos, media.Length);
    }

    static string Fmt(TimeSpan t) => $"{(int)t.TotalMinutes}:{t.Seconds:00}";

    void UpdateMediaProgress()
    {
        var (pos, len) = Timeline();
        MediaProgress.Value = len > TimeSpan.Zero ? pos / len : 0;
        Elapsed.Text = Fmt(pos);
        Remaining.Text = "-" + Fmt(len > pos ? len - pos : TimeSpan.Zero);
        var line = lyrics.LastOrDefault(l => l.At <= pos)?.Text;
        LyricLine.IsVisible = data.Lyrics && !string.IsNullOrEmpty(line);
        LyricLine.Text = line ?? "";
    }

    async Task LoadArt(string url)
    {
        Bitmap? bmp = null;
        try
        {
            if (url.StartsWith("file://")) bmp = new Bitmap(new Uri(url).LocalPath);
            else if (url.StartsWith("http")) bmp = new Bitmap(new MemoryStream(await http.GetByteArrayAsync(url)));
        }
        catch (Exception ex) { Debug.WriteLine(ex); } // unreachable or not an image: keep the music glyph
        if (url != artUrl) return;
        Art.Source = bmp;
        ArtGlyph.IsVisible = bmp == null;
    }

    async Task LoadLyrics(string key, string title, string artist)
    {
        try
        {
            var url = $"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(title)}&artist_name={Uri.EscapeDataString(artist)}";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
            var synced = doc.RootElement.EnumerateArray()
                .Select(e => e.TryGetProperty("syncedLyrics", out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null)
                .FirstOrDefault(v => !string.IsNullOrEmpty(v));
            if (key == lyricsKey && synced != null) lyrics = Lrc.Parse(synced);
        }
        catch (Exception ex) { Debug.WriteLine(ex); } // offline or no match: no lyrics
    }

    async void Prev_Click(object? s, RoutedEventArgs e) { await mpris.Previous(); ShowMedia(await mpris.ReadAsync()); }
    async void Play_Click(object? s, RoutedEventArgs e) { await mpris.PlayPause(); ShowMedia(await mpris.ReadAsync()); }
    async void Next_Click(object? s, RoutedEventArgs e) { await mpris.Next(); ShowMedia(await mpris.ReadAsync()); }

    // ================= Pomodoro =================
    Pomodoro pomo = new(25, 5, 15, 4);

    void ConfigurePomo()
    {
        var same = pomo.Sessions == data.Sessions && pomo.Plan[0].Len == TimeSpan.FromMinutes(data.FocusMin)
            && pomo.Plan[^1].Len == TimeSpan.FromMinutes(data.LongMin)
            && (pomo.Sessions == 1 || pomo.Plan[1].Len == TimeSpan.FromMinutes(data.ShortMin));
        if (!same && !pomo.Running) pomo = new(data.FocusMin, data.ShortMin, data.LongMin, data.Sessions);
    }

    static string Mmss(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    void PomoTick(DateTime now)
    {
        if (pomo.Tick(now))
        {
            if (!pomo.Running) ShowAlert("IcTimer", PurpleB, "Ronda completada", $"{pomo.Sessions} sesiones · buen trabajo");
            else if (pomo.Current.Focus) ShowAlert("IcTimer", PurpleB, "Concentración", $"Sesión {pomo.FocusNumber} de {pomo.Sessions}", Mmss(pomo.Current.Len));
            else ShowAlert("IcTimer", GreenB, pomo.Current.Long ? "Descanso largo" : "Descanso", "Levántate y estira", Mmss(pomo.Current.Len));
            ConfigurePomo();
        }
        UpdatePomo(now);
    }

    void UpdatePomo(DateTime now)
    {
        SetRings(pomo.Running ? pomo.Remaining(now) / pomo.Current.Len : 1);
        if (!PageVisible(1)) return;
        TimerText.Text = Mmss(pomo.Remaining(now));
        PomoPhase.Text = pomo.Current.Focus ? $"Concentración · {pomo.FocusNumber} de {pomo.Sessions}"
            : pomo.Current.Long ? "Descanso largo" : $"Descanso · {pomo.FocusNumber} de {pomo.Sessions} hechas";
        PomoEnd.Text = (now + pomo.Total - pomo.Elapsed(now)).ToString("HH:mm");
        PomoPhaseBar.Value = 1 - pomo.Remaining(now) / pomo.Current.Len;
        StartBtn.Content = pomo.Running ? "Pausa" : pomo.Elapsed(now) > TimeSpan.Zero ? "Seguir" : "Iniciar";
        var tot = pomo.Total;
        PomoTotal.Text = (tot.TotalHours >= 1 ? $"{(int)tot.TotalHours} h {tot.Minutes} min" : $"{tot.Minutes} min") + $" · {pomo.Sessions} sesiones";
        DrawRoundTrack(now);
    }

    void StartPause_Click(object? s, RoutedEventArgs e) { pomo.Toggle(DateTime.Now); UpdatePomo(DateTime.Now); }
    void Reset_Click(object? s, RoutedEventArgs e) { pomo.Reset(); ConfigurePomo(); UpdatePomo(DateTime.Now); }

    static Line Seg(double x1, double x2, double y, IBrush b, double thick) => new()
    {
        StartPoint = new Point(x1, y), EndPoint = new Point(Math.Max(x1, x2), y), Stroke = b, StrokeThickness = thick, StrokeLineCap = PenLineCap.Round,
    };

    static TextBlock Label(string text, double width, IBrush fg, bool bold, double size = 10) => new()
    {
        Text = text, FontSize = size, Width = width, TextAlignment = TextAlignment.Center, Foreground = fg, FontWeight = bold ? FontWeight.Bold : FontWeight.Normal,
    };

    static void Place(Canvas c, Control el, double x, double y) { Canvas.SetLeft(el, x); Canvas.SetTop(el, y); c.Children.Add(el); }

    /// The whole round on one line: a segment per phase, filled up to a glowing head.
    void DrawRoundTrack(DateTime now)
    {
        var c = RoundTrack;
        c.Children.Clear();
        double w = c.Bounds.Width, total = pomo.Total.TotalSeconds, y = 7, acc = 0;
        if (w <= 0) return;
        double X(double sec) => sec / total * w;
        var head = X(pomo.Elapsed(now).TotalSeconds);
        int n = 0;
        foreach (var (ph, i) in pomo.Plan.Select((p, i) => (p, i)))
        {
            double a = X(acc) + 2, b = X(acc + ph.Len.TotalSeconds) - 2;
            c.Children.Add(Seg(a, b, y, TrackB, ph.Focus ? 4 : 2));
            if (head > a) c.Children.Add(Seg(a, Math.Min(head, b), y, PurpleB, ph.Focus ? 4 : 2));
            if (ph.Focus || ph.Long)
            {
                var cur = i == pomo.Index;
                Place(c, Label(ph.Long ? "Larga" : (++n).ToString(), 34, cur ? Brushes.White : DimB, cur),
                    Math.Clamp(X(acc + ph.Len.TotalSeconds / 2) - 17, -8, w - 26), 15);
            }
            acc += ph.Len.TotalSeconds;
        }
        Place(c, new Ellipse
        {
            Width = 11, Height = 11, Fill = Brushes.White,
            Effect = new DropShadowEffect { Color = Color.Parse("#8B7CF6"), BlurRadius = 14, OffsetX = 0, OffsetY = 0, Opacity = 1 },
        }, head - 5.5, y - 5.5);
    }

    // ================= Fases del día y tiempo (open-meteo.com: recibe las coordenadas) =================
    Weather? weather;
    DateTime weatherAt = DateTime.MinValue;
    (double, double) weatherFor;
    bool weatherLoading;

    DayPhases Phases(DateTime day) => SunCalc.For(day.Date, data.Lat, data.Lon, TimeZoneInfo.Local.GetUtcOffset(day).TotalHours);

    static string Until(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours} h {t.Minutes} min" : $"{Math.Max(1, t.Minutes)} min";

    void UpdateDay(DateTime now)
    {
        var d = Phases(now);
        var (phase, next, nextAt, from) = d.At(now);
        DayPhase.Text = phase;
        DayNext.Text = $"{next} en {Until(nextAt - now)}";
        DayBar.Value = (now - from) / (nextAt - from);
        DrawSunArc(d, now, next);
        ShowWeather();
        if (!weatherLoading && (weatherFor != (data.Lat, data.Lon) || (now - weatherAt).TotalMinutes > 15)) _ = LoadWeather();
    }

    void ShowWeather()
    {
        if (weather == null)
        {
            WeatherGlyph.Text = "";
            WeatherTemp.Text = "--°";
            WeatherDesc.Text = weatherLoading ? "Cargando el tiempo…" : "Sin datos del tiempo";
            return;
        }
        var (glyph, text) = weather.Describe();
        WeatherGlyph.Text = glyph;
        WeatherTemp.Text = $"{weather.Temp:0}°";
        WeatherDesc.Text = $"{text} · Máx {weather.Max:0}° · Mín {weather.Min:0}°";
    }

    async Task LoadWeather()
    {
        weatherLoading = true;
        var at = (data.Lat, data.Lon);
        try
        {
            var inv = CultureInfo.InvariantCulture;
            var url = $"https://api.open-meteo.com/v1/forecast?latitude={data.Lat.ToString(inv)}&longitude={data.Lon.ToString(inv)}"
                + "&current=temperature_2m,apparent_temperature,weather_code,is_day,wind_speed_10m,relative_humidity_2m"
                + "&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
            var c = doc.RootElement.GetProperty("current");
            var dl = doc.RootElement.GetProperty("daily");
            weather = new Weather(
                c.GetProperty("temperature_2m").GetDouble(), c.GetProperty("apparent_temperature").GetDouble(),
                dl.GetProperty("temperature_2m_max")[0].GetDouble(), dl.GetProperty("temperature_2m_min")[0].GetDouble(),
                c.GetProperty("weather_code").GetInt32(), c.GetProperty("is_day").GetInt32() == 1,
                c.GetProperty("wind_speed_10m").GetDouble(), c.GetProperty("relative_humidity_2m").GetInt32());
            weatherAt = DateTime.Now;
            weatherFor = at;
        }
        catch (Exception ex) { Debug.WriteLine(ex); weatherAt = DateTime.Now.AddMinutes(-10); } // offline: retry in ~5 min
        finally { weatherLoading = false; }
        if (PageVisible(2)) ShowWeather();
    }

    /// city → (name, lat, lon) via Open-Meteo geocoding.
    internal static async Task<(string Name, double Lat, double Lon)?> Geocode(string city)
    {
        try
        {
            var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(city)}&count=1&language=es&format=json";
            using var doc = JsonDocument.Parse(await http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var r) || r.GetArrayLength() == 0) return null;
            var x = r[0];
            return (x.GetProperty("name").GetString() ?? city, x.GetProperty("latitude").GetDouble(), x.GetProperty("longitude").GetDouble());
        }
        catch (Exception ex) { Debug.WriteLine(ex); return null; }
    }

    /// The sun's path across the day with each phase marked on it.
    void DrawSunArc(DayPhases d, DateTime now, string next)
    {
        var c = SunArc;
        c.Children.Clear();
        double w = c.Bounds.Width, horizon = 42, amp = 35;
        if (w <= 0) return;
        DateTime start = d.Dawn.AddMinutes(-70), end = d.Dusk.AddMinutes(70);
        double X(DateTime t) => (t - start) / (end - start) * (w - 24) + 12;
        double Y(DateTime t) => Math.Min(horizon + 12, horizon - amp * Math.Sin(Math.PI * ((t - d.Sunrise) / (d.Sunset - d.Sunrise))));

        c.Children.Add(new Line { StartPoint = new Point(4, horizon), EndPoint = new Point(w - 4, horizon), Stroke = B("#22FFFFFF"), StrokeThickness = 1 });
        var pts = new List<Point>();
        for (int i = 0; i <= 60; i++) { var t = start + (end - start) * (i / 60.0); pts.Add(new Point(X(t), Y(t))); }
        c.Children.Add(new Polyline
        {
            Points = pts, StrokeThickness = 2, StrokeJoin = PenLineJoin.Round,
            Stroke = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#1E5BFF"), 0), new GradientStop(Color.Parse("#8EC5FF"), 1) },
            },
        });

        var events = d.Events;
        // label centres 52px apart at least, kept inside: squeeze from the right, then push from the left
        var lxs = events.Select(p => X(p.At)).ToArray();
        for (int k = lxs.Length - 1; k >= 0; k--) lxs[k] = Math.Min(lxs[k], k == lxs.Length - 1 ? w - 26 : lxs[k + 1] - 52);
        for (int k = 0; k < lxs.Length; k++) lxs[k] = Math.Max(lxs[k], k == 0 ? 26 : lxs[k - 1] + 52);
        foreach (var ((name, at), lx) in events.Zip(lxs))
        {
            var isNext = name == next;
            Place(c, new Ellipse { Width = 5, Height = 5, Fill = isNext ? Brushes.White : DimB }, X(at) - 2.5, Y(at) - 2.5);
            Place(c, Label(name, 52, isNext ? Brushes.White : DimB, isNext, 9), lx - 26, horizon + 14);
            Place(c, Label(at.ToString("HH:mm"), 52, isNext ? Brushes.White : DimB, isNext, 9), lx - 26, horizon + 25);
        }
        if (now > start && now < end)
        {
            var isDay = now > d.Sunrise && now < d.Sunset;
            Place(c, new Ellipse
            {
                Width = 13, Height = 13, Fill = B(isDay ? "#FFC24B" : "#C9D6FF"),
                Effect = new DropShadowEffect { Color = Color.Parse(isDay ? "#FFB030" : "#8EA8FF"), BlurRadius = 16, OffsetX = 0, OffsetY = 0, Opacity = 1 },
            }, X(now) - 6.5, Y(now) - 6.5);
        }
    }

    // ================= Agua =================
    static string Today => DateTime.Now.ToString("yyyy-MM-dd");
    int WaterTotal => data.WaterLog.Sum(e => e[1]);
    DateTime lastDrink = DateTime.Now, lastWaterAlert = DateTime.MinValue;

    void InitWater()
    {
        if (data.WaterDay != Today) { data.WaterDay = Today; data.WaterLog.Clear(); }
        WaterRemind.IsChecked = data.WaterRemind;
        DrinkBtn.Content = $"Beber {data.WaterSip} ml";
    }

    void Drink_Click(object? s, RoutedEventArgs e)
    {
        InitWater();
        data.WaterLog.Add(new[] { DateTime.Now.Hour, data.WaterSip });
        lastDrink = DateTime.Now;
        Store.Save(data);
        UpdateWater();
    }

    void WaterGoal_Click(object? s, RoutedEventArgs e)
    {
        int[] goals = { 1000, 1500, 2000, 2500, 3000 };
        data.WaterGoal = goals[(Array.IndexOf(goals, data.WaterGoal) + 1) % goals.Length];
        Store.Save(data);
        UpdateWater();
    }

    void UpdateWater()
    {
        InitWater();
        WaterAmount.Text = (WaterTotal / 1000.0).ToString("0.0#", Es);
        WaterGoalText.Text = $"/ {(data.WaterGoal / 1000.0).ToString("0.0#", Es)} L";
        var pct = Math.Min(100, WaterTotal * 100 / Math.Max(1, data.WaterGoal));
        WaterStatus.Inlines = new InlineCollection
        {
            new Run("Hoy · "),
            new Run($"{pct}%") { Foreground = BlueB, FontWeight = FontWeight.Bold },
            new Run(pct >= 100 ? " · Meta cumplida" : $" · faltan {data.WaterGoal - WaterTotal} ml"),
        };
        WaterBar.Value = Math.Min(1, WaterTotal / (double)data.WaterGoal);
        DrawWaterChart();
    }

    /// Hourly bars from 08 to 22.
    void DrawWaterChart()
    {
        var c = WaterChart;
        c.Children.Clear();
        double w = c.Bounds.Width, h = 58, top = 6;
        if (w <= 0) return;
        const int from = 8, n = 15;
        var perHour = Enumerable.Range(from, n).Select(hr => data.WaterLog.Where(e => e[0] == hr).Sum(e => e[1])).ToArray();
        double max = Math.Max(500, perHour.Max()), chartW = w - 34, bw = chartW / n;
        var fill = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#5AB4FF"), 0), new GradientStop(Color.Parse("#0A5CFF"), 1) },
        };
        for (int i = 0; i < n; i++)
        {
            var bh = Math.Max(3, perHour[i] / max * h);
            Place(c, new Rectangle { Width = Math.Max(2, bw - 3), Height = bh, RadiusX = 2, RadiusY = 2, Fill = perHour[i] > 0 ? fill : B("#1FFFFFFF") }, i * bw, top + h - bh);
            if ((from + i) % 3 == 0) Place(c, new TextBlock { Text = $"{from + i:00}", FontSize = 9, Foreground = DimB }, i * bw - 2, top + h + 3);
        }
        Place(c, new TextBlock { Text = $"{max:0}", FontSize = 9, Foreground = DimB }, chartW + 4, top - 6);
        Place(c, new TextBlock { Text = $"{max / 2:0}", FontSize = 9, Foreground = DimB }, chartW + 4, top + h / 2 - 6);
        Place(c, new TextBlock { Text = "0 ml", FontSize = 9, Foreground = DimB }, chartW + 4, top + h - 6);
    }

    void WaterReminder(DateTime now)
    {
        if (!data.WaterRemind || now.Hour < 9 || now.Hour >= 22) return;
        InitWater();
        if (WaterTotal >= data.WaterGoal) return;
        if ((now - lastDrink).TotalMinutes < data.WaterEveryMin || (now - lastWaterAlert).TotalMinutes < data.WaterEveryMin) return;
        lastWaterAlert = now;
        ShowAlert("IcDrop", BlueB, "Hora de beber agua", $"Llevas {WaterTotal} de {data.WaterGoal} ml", $"{data.WaterSip} ml");
    }
}
