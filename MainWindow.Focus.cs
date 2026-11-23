using System.Media;

namespace Cato;

// Pomodoro, rezo y agua.
public partial class MainWindow
{
    Pomodoro pomo = new(25, 5, 15, 4);

    void ConfigurePomo()
    {
        var same = pomo.Sessions == data.Sessions && pomo.Plan[0].Len == TimeSpan.FromMinutes(data.FocusMin)
            && pomo.Plan[1].Len == TimeSpan.FromMinutes(pomo.Sessions > 1 ? data.ShortMin : data.LongMin)
            && pomo.Plan[^1].Len == TimeSpan.FromMinutes(data.LongMin);
        if (!same && !pomo.Running) pomo = new(data.FocusMin, data.ShortMin, data.LongMin, data.Sessions);
    }

    static string Mmss(TimeSpan t) => $"{(int)t.TotalMinutes:00}:{t.Seconds:00}";

    void PomoTick(DateTime now)
    {
        if (pomo.Tick(now))
        {
            SystemSounds.Exclamation.Play();
            if (!pomo.Running) ShowAlert("", PurpleB, "Ronda completada", $"{pomo.Sessions} sesiones · buen trabajo");
            else if (pomo.Current.Focus) ShowAlert("", PurpleB, "Concentración", $"Sesión {pomo.FocusNumber} de {pomo.Sessions}", Mmss(pomo.Current.Len));
            else ShowAlert("", GreenB, pomo.Current.Long ? "Descanso largo" : "Descanso", "Levántate y estira", Mmss(pomo.Current.Len));
            ConfigurePomo(); // apply settings changed while it was running
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

    void StartPause_Click(object s, RoutedEventArgs e) { pomo.Toggle(DateTime.Now); UpdatePomo(DateTime.Now); }
    void Reset_Click(object s, RoutedEventArgs e) { pomo.Reset(); ConfigurePomo(); UpdatePomo(DateTime.Now); }
    void RoundTrack_SizeChanged(object s, SizeChangedEventArgs e) => DrawRoundTrack(DateTime.Now);

    /// The whole round on one line: a segment per phase, filled up to a glowing head.
    void DrawRoundTrack(DateTime now)
    {
        var c = RoundTrack;
        c.Children.Clear();
        double w = c.ActualWidth, total = pomo.Total.TotalSeconds, y = 7, acc = 0;
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
                var label = ph.Long ? "Larga" : (++n).ToString();
                var cur = i == pomo.Index;
                var tb = new TextBlock
                {
                    Text = label, FontSize = 10, Width = 34, TextAlignment = TextAlignment.Center,
                    Foreground = cur ? Brushes.White : DimB, FontWeight = cur ? FontWeights.Bold : FontWeights.Normal,
                };
                Canvas.SetLeft(tb, Math.Clamp(X(acc + ph.Len.TotalSeconds / 2) - 17, -8, w - 26));
                Canvas.SetTop(tb, 15);
                c.Children.Add(tb);
            }
            acc += ph.Len.TotalSeconds;
        }
        var dot = new Ellipse
        {
            Width = 11, Height = 11, Fill = Brushes.White,
            Effect = new DropShadowEffect { Color = Color.FromRgb(0x8B, 0x7C, 0xF6), BlurRadius = 14, ShadowDepth = 0, Opacity = 1 },
        };
        Canvas.SetLeft(dot, head - 5.5);
        Canvas.SetTop(dot, y - 5.5);
        c.Children.Add(dot);
    }

    static Line Seg(double x1, double x2, double y, Brush b, double thick) => new()
    {
        X1 = x1, X2 = Math.Max(x1, x2), Y1 = y, Y2 = y, Stroke = b, StrokeThickness = thick,
        StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
    };

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
            WeatherMeta.Text = "";
            return;
        }
        var (glyph, text) = weather.Describe();
        WeatherGlyph.Text = glyph;
        WeatherTemp.Text = $"{weather.Temp:0}°";
        WeatherDesc.Text = $"{text} · Máx {weather.Max:0}° · Mín {weather.Min:0}°";
        WeatherMeta.Text = "";
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
            using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(url));
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
            using var doc = System.Text.Json.JsonDocument.Parse(await http.GetStringAsync(url));
            if (!doc.RootElement.TryGetProperty("results", out var r) || r.GetArrayLength() == 0) return null;
            var x = r[0];
            return (x.GetProperty("name").GetString() ?? city, x.GetProperty("latitude").GetDouble(), x.GetProperty("longitude").GetDouble());
        }
        catch (Exception ex) { Debug.WriteLine(ex); return null; }
    }

    void SunArc_SizeChanged(object s, SizeChangedEventArgs e) { if (PageVisible(2)) UpdateDay(DateTime.Now); }

    /// The sun's path across the day with each phase marked on it.
    void DrawSunArc(DayPhases d, DateTime now, string next)
    {
        var c = SunArc;
        c.Children.Clear();
        double w = c.ActualWidth, horizon = 42, amp = 35;
        if (w <= 0) return;
        DateTime start = d.Dawn.AddMinutes(-70), end = d.Dusk.AddMinutes(70);
        double X(DateTime t) => (t - start) / (end - start) * (w - 24) + 12;
        double Y(DateTime t) => Math.Min(horizon + 12, horizon - amp * Math.Sin(Math.PI * ((t - d.Sunrise) / (d.Sunset - d.Sunrise))));

        c.Children.Add(new Line { X1 = 4, X2 = w - 4, Y1 = horizon, Y2 = horizon, Stroke = Hex("#22FFFFFF"), StrokeThickness = 1 });
        var arc = new Polyline
        {
            StrokeThickness = 2, StrokeLineJoin = PenLineJoin.Round,
            Stroke = new LinearGradientBrush(Color.FromRgb(0x1E, 0x5B, 0xFF), Color.FromRgb(0x8E, 0xC5, 0xFF), 0),
        };
        for (int i = 0; i <= 60; i++) { var t = start + (end - start) * (i / 60.0); arc.Points.Add(new Point(X(t), Y(t))); }
        c.Children.Add(arc);

        var events = d.Events;
        // Label centres 52px apart at least, kept inside the canvas: squeeze from the right, then push from the left.
        var lxs = events.Select(p => X(p.At)).ToArray();
        for (int k = lxs.Length - 1; k >= 0; k--) lxs[k] = Math.Min(lxs[k], k == lxs.Length - 1 ? w - 26 : lxs[k + 1] - 52);
        for (int k = 0; k < lxs.Length; k++) lxs[k] = Math.Max(lxs[k], k == 0 ? 26 : lxs[k - 1] + 52);
        foreach (var ((name, at), lx) in events.Zip(lxs))
        {
            var isNext = name == next;
            var dot = new Ellipse { Width = 5, Height = 5, Fill = isNext ? Brushes.White : DimB };
            Canvas.SetLeft(dot, X(at) - 2.5); Canvas.SetTop(dot, Y(at) - 2.5);
            c.Children.Add(dot);
            foreach (var (text, top) in new[] { (name, horizon + 14), (at.ToString("HH:mm"), horizon + 25) })
            {
                var tb = new TextBlock
                {
                    Text = text, FontSize = 9, Width = 52, TextAlignment = TextAlignment.Center,
                    Foreground = isNext ? Brushes.White : DimB, FontWeight = isNext ? FontWeights.Bold : FontWeights.Normal,
                };
                Canvas.SetLeft(tb, lx - 26); Canvas.SetTop(tb, top);
                c.Children.Add(tb);
            }
        }
        if (now > start && now < end)
        {
            var isDay = now > d.Sunrise && now < d.Sunset;
            var sun = new Ellipse
            {
                Width = 13, Height = 13, Fill = Hex(isDay ? "#FFC24B" : "#C9D6FF"),
                Effect = new DropShadowEffect { Color = isDay ? Color.FromRgb(0xFF, 0xB0, 0x30) : Color.FromRgb(0x8E, 0xA8, 0xFF), BlurRadius = 16, ShadowDepth = 0, Opacity = 1 },
            };
            Canvas.SetLeft(sun, X(now) - 6.5); Canvas.SetTop(sun, Y(now) - 6.5);
            c.Children.Add(sun);
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
    }

    void Drink_Click(object s, RoutedEventArgs e)
    {
        InitWater();
        data.WaterLog.Add(new[] { DateTime.Now.Hour, data.WaterSip });
        lastDrink = DateTime.Now;
        Store.Save(data);
        UpdateWater();
    }

    void WaterGoal_Click(object s, RoutedEventArgs e)
    {
        int[] goals = { 1000, 1500, 2000, 2500, 3000 };
        data.WaterGoal = goals[(Array.IndexOf(goals, data.WaterGoal) + 1) % goals.Length];
        Store.Save(data);
        UpdateWater();
    }

    void WaterRemind_Click(object s, RoutedEventArgs e) { data.WaterRemind = WaterRemind.IsChecked == true; Store.Save(data); }
    void WaterChart_SizeChanged(object s, SizeChangedEventArgs e) => DrawWaterChart();

    void UpdateWater()
    {
        InitWater();
        WaterAmount.Text = (WaterTotal / 1000.0).ToString("0.0#", Es);
        WaterGoalText.Text = $"/ {(data.WaterGoal / 1000.0).ToString("0.0#", Es)} L";
        var pct = Math.Min(100, WaterTotal * 100 / Math.Max(1, data.WaterGoal));
        WaterStatus.Inlines.Clear();
        WaterStatus.Inlines.Add(new Run("Hoy · "));
        WaterStatus.Inlines.Add(new Run($"{pct}%") { Foreground = BlueB, FontWeight = FontWeights.Bold });
        WaterStatus.Inlines.Add(new Run(pct >= 100 ? " · Meta cumplida" : $" · faltan {data.WaterGoal - WaterTotal} ml"));
        WaterBar.Value = Math.Min(1, WaterTotal / (double)data.WaterGoal);
        DrawWaterChart();
    }

    /// Hourly bars from 08 to 22.
    void DrawWaterChart()
    {
        var c = WaterChart;
        c.Children.Clear();
        double w = c.ActualWidth, h = 58, top = 6;
        if (w <= 0) return;
        const int from = 8, n = 15;
        var perHour = Enumerable.Range(from, n).Select(hr => data.WaterLog.Where(e => e[0] == hr).Sum(e => e[1])).ToArray();
        double max = Math.Max(500, perHour.Max()), chartW = w - 34, bw = chartW / n;
        var fill = new LinearGradientBrush(Color.FromRgb(0x5A, 0xB4, 0xFF), Color.FromRgb(0x0A, 0x5C, 0xFF), 90);
        for (int i = 0; i < n; i++)
        {
            var bh = Math.Max(3, perHour[i] / max * h);
            var r = new Rectangle { Width = Math.Max(2, bw - 3), Height = bh, RadiusX = 2, RadiusY = 2, Fill = perHour[i] > 0 ? fill : Hex("#1FFFFFFF") };
            Canvas.SetLeft(r, i * bw); Canvas.SetTop(r, top + h - bh);
            c.Children.Add(r);
            if ((from + i) % 3 == 0) AddText(c, $"{from + i:00}", i * bw - 2, top + h + 3);
        }
        AddText(c, $"{max:0}", chartW + 4, top - 6);
        AddText(c, $"{max / 2:0}", chartW + 4, top + h / 2 - 6);
        AddText(c, "0 ml", chartW + 4, top + h - 6);
    }

    static void AddText(Canvas c, string text, double x, double y)
    {
        var tb = new TextBlock { Text = text, FontSize = 9, Foreground = DimB };
        Canvas.SetLeft(tb, x); Canvas.SetTop(tb, y);
        c.Children.Add(tb);
    }

    void WaterReminder(DateTime now)
    {
        if (!data.WaterRemind || now.Hour < 9 || now.Hour >= 22) return;
        InitWater();
        if (WaterTotal >= data.WaterGoal) return;
        if ((now - lastDrink).TotalMinutes < data.WaterEveryMin || (now - lastWaterAlert).TotalMinutes < data.WaterEveryMin) return;
        lastWaterAlert = now;
        ShowAlert("💧", BlueB, "Hora de beber agua", $"Llevas {WaterTotal} de {data.WaterGoal} ml", $"{data.WaterSip} ml");
    }
}
