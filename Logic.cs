using System.Text.RegularExpressions;

namespace Cato;

public record LyricLine(TimeSpan At, string Text);

static class Lrc
{
    static readonly Regex Line = new(@"^\[(\d+):(\d+(?:\.\d+)?)\](.*)$");

    public static List<LyricLine> Parse(string lrc) =>
        lrc.Split('\n')
           .Select(l => Line.Match(l.Trim()))
           .Where(m => m.Success)
           .Select(m => new LyricLine(
               TimeSpan.FromMinutes(int.Parse(m.Groups[1].Value)) +
               TimeSpan.FromSeconds(double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture)),
               m.Groups[3].Value.Trim()))
           .OrderBy(l => l.At)
           .ToList();
}

/// A full round: N focus sessions with short breaks, then one long break.
public sealed class Pomodoro
{
    public readonly record struct Phase(bool Focus, bool Long, TimeSpan Len);

    public Phase[] Plan { get; }
    public int Index { get; private set; }
    TimeSpan left;
    DateTime? deadline;

    public Pomodoro(int focusMin, int shortMin, int longMin, int sessions)
    {
        var p = new List<Phase>();
        for (int i = 1; i <= sessions; i++)
        {
            p.Add(new(true, false, TimeSpan.FromMinutes(focusMin)));
            p.Add(i < sessions ? new(false, false, TimeSpan.FromMinutes(shortMin)) : new(false, true, TimeSpan.FromMinutes(longMin)));
        }
        Plan = p.ToArray();
        left = Plan[0].Len;
    }

    public int Sessions => Plan.Count(x => x.Focus);
    public bool Running => deadline != null;
    public Phase Current => Plan[Index];
    public TimeSpan Total => Sum(Plan);
    public int FocusNumber => Plan.Take(Index + 1).Count(x => x.Focus);

    public TimeSpan Remaining(DateTime now)
    {
        var r = deadline is { } d ? d - now : left;
        return r < TimeSpan.Zero ? TimeSpan.Zero : r;
    }

    public TimeSpan Elapsed(DateTime now) => Sum(Plan.Take(Index)) + Current.Len - Remaining(now);

    public void Toggle(DateTime now)
    {
        if (deadline != null) { left = Remaining(now); deadline = null; }
        else deadline = now + left;
    }

    public void Reset() { Index = 0; left = Plan[0].Len; deadline = null; }

    /// Moves to the next phase when the current one ends; true on a change. Stops after the last phase.
    public bool Tick(DateTime now)
    {
        if (deadline is not { } d || now < d) return false;
        if (Index + 1 >= Plan.Length) { Reset(); return true; }
        Index++;
        left = Current.Len;
        deadline = d + left; // chain from the old deadline so no time is lost
        return true;
    }

    static TimeSpan Sum(IEnumerable<Phase> ps) => ps.Aggregate(TimeSpan.Zero, (a, x) => a + x.Len);
}

/// Times of the sun's main events for one day.
public record DayPhases(DateTime Dawn, DateTime Sunrise, DateTime Noon, DateTime Sunset, DateTime Dusk)
{
    public (string Name, DateTime At)[] Events =>
        new[] { ("Alba", Dawn), ("Amanecer", Sunrise), ("Mediodía", Noon), ("Atardecer", Sunset), ("Anochecer", Dusk) };

    static readonly string[] Between = { "Noche", "Alba", "Mañana", "Tarde", "Crepúsculo", "Noche" };

    /// Current phase, the next event and when the current phase started.
    public (string Phase, string Next, DateTime NextAt, DateTime From) At(DateTime now)
    {
        var ev = Events;
        var i = Array.FindIndex(ev, e => e.At > now);
        if (i < 0) return (Between[5], "Alba", Dawn.AddDays(1), Dusk);
        return (Between[i], ev[i].Name, ev[i].At, i == 0 ? Dusk.AddDays(-1) : ev[i - 1].At);
    }
}

/// Sun position maths (NOAA-style, as in praytimes.org): civil twilight at 6°, sunrise/sunset at 0.833°.
static class SunCalc
{
    public static DayPhases For(DateTime date, double lat, double lon, double tz)
    {
        double jd = Julian(date.Year, date.Month, date.Day) - lon / (15 * 24.0);
        double Mid(double t) => Fix(12 - Sun(jd + t).Eqt, 24);
        double Angle(double angle, double t, bool ccw)
        {
            var decl = Sun(jd + t).Decl;
            var T = Acos((-Sin(angle) - Sin(decl) * Sin(lat)) / (Cos(decl) * Cos(lat))) / 15;
            return Mid(t) + (ccw ? -T : T);
        }
        var hours = new[] { Angle(6, 6 / 24.0, true), Angle(0.833, 6 / 24.0, true), Mid(12 / 24.0), Angle(0.833, 18 / 24.0, false), Angle(6, 18 / 24.0, false) };
        // shortcut: polar days/nights give NaN; they fall back to midnight, fine for normal latitudes
        var t = hours.Select(h => date.Date + TimeSpan.FromHours(double.IsNaN(h) ? 0 : h + tz - lon / 15)).ToArray();
        return new(t[0], t[1], t[2], t[3], t[4]);
    }

    static (double Decl, double Eqt) Sun(double jd)
    {
        double D = jd - 2451545.0;
        double g = Fix(357.529 + 0.98560028 * D, 360);
        double q = Fix(280.459 + 0.98564736 * D, 360);
        double L = Fix(q + 1.915 * Sin(g) + 0.020 * Sin(2 * g), 360);
        double e = 23.439 - 0.00000036 * D;
        double ra = Atan2(Cos(e) * Sin(L), Cos(L)) / 15;
        return (Asin(Sin(e) * Sin(L)), q / 15 - Fix(ra, 24));
    }

    static double Julian(int y, int m, int d)
    {
        if (m <= 2) { y--; m += 12; }
        double a = Math.Floor(y / 100.0), b = 2 - a + Math.Floor(a / 4);
        return Math.Floor(365.25 * (y + 4716)) + Math.Floor(30.6001 * (m + 1)) + d + b - 1524.5;
    }

    static double Fix(double a, double b) { a -= b * Math.Floor(a / b); return a < 0 ? a + b : a; }
    const double Rad = Math.PI / 180;
    static double Sin(double d) => Math.Sin(d * Rad);
    static double Cos(double d) => Math.Cos(d * Rad);
    static double Asin(double x) => Math.Asin(x) / Rad;
    static double Acos(double x) => Math.Acos(x) / Rad;
    static double Atan2(double y, double x) => Math.Atan2(y, x) / Rad;
}

public record Weather(double Temp, double Feels, double Max, double Min, int Code, bool IsDay, double Wind, int Humidity)
{
    /// WMO weather code → glyph (Segoe UI Symbol) and Spanish text.
    public (string Glyph, string Text) Describe() => Describe(Code, IsDay);

    public static (string Glyph, string Text) Describe(int code, bool isDay) => code switch
    {
        0 => (isDay ? "☀" : "☾", "Despejado"),
        1 or 2 => ("⛅", code == 1 ? "Mayormente despejado" : "Parcialmente nublado"),
        3 => ("☁", "Nublado"),
        45 or 48 => ("☁", "Niebla"),
        >= 51 and <= 57 => ("☂", "Llovizna"),
        >= 61 and <= 67 or >= 80 and <= 82 => ("☔", "Lluvia"),
        >= 71 and <= 77 or 85 or 86 => ("❄", "Nieve"),
        >= 95 => ("⚡", "Tormenta"),
        _ => ("☁", "Variable"),
    };
}

static class TaskText
{
    /// "Llamar a Ana @18:30" → ("Llamar a Ana", "18:30")
    public static (string Text, string? At) Parse(string s)
    {
        var m = Regex.Match(s.Trim(), @"^(.*?)\s*@\s*([0-9]{1,2}):([0-9]{2})$"); // [0-9], not \d: int.Parse rejects non-Latin digits
        if (m.Success && int.Parse(m.Groups[2].Value) < 24 && int.Parse(m.Groups[3].Value) < 60)
            return (m.Groups[1].Value.Trim(), $"{int.Parse(m.Groups[2].Value):00}:{m.Groups[3].Value}");
        return (s.Trim(), null);
    }
}

static class SelfCheck
{
    /// Runs in Debug builds only; throws if core logic regresses.
    [Conditional("DEBUG")]
    public static void Run()
    {
        var l = Lrc.Parse("[00:01.50] Hola\r\n[01:02.00]Adiós\nbasura\n[00:00.00]");
        Ensure(l.Count == 3 && l[0].Text == "" && l[1].Text == "Hola" && l[2].At == TimeSpan.FromSeconds(62), "Lrc");

        var t0 = new DateTime(2026, 1, 1, 9, 0, 0);
        var p = new Pomodoro(25, 5, 15, 4);
        Ensure(p.Total == TimeSpan.FromMinutes(130), "Pomodoro total");
        p.Toggle(t0);
        Ensure(!p.Tick(t0.AddMinutes(24)) && p.Tick(t0.AddMinutes(25)) && !p.Current.Focus && p.FocusNumber == 1, "Pomodoro break");
        Ensure(p.Elapsed(t0.AddMinutes(27)) == TimeSpan.FromMinutes(27), "Pomodoro elapsed");
        p.Toggle(t0.AddMinutes(27)); // pause with 3 min left
        Ensure(p.Remaining(t0.AddHours(5)) == TimeSpan.FromMinutes(3), "Pomodoro pause");

        // Madrid, 23 Nov 2026 (UTC+1): sunrise ≈ 08:14, solar noon ≈ 13:00, sunset ≈ 17:52
        var d = SunCalc.For(new DateTime(2026, 11, 23), 40.4168, -3.7038, 1);
        Ensure(d.Sunrise.TimeOfDay > new TimeSpan(7, 55, 0) && d.Sunrise.TimeOfDay < new TimeSpan(8, 30, 0), "Sunrise " + d.Sunrise);
        Ensure(d.Sunset.TimeOfDay > new TimeSpan(17, 40, 0) && d.Sunset.TimeOfDay < new TimeSpan(18, 5, 0), "Sunset " + d.Sunset);
        Ensure(d.Dawn < d.Sunrise && d.Sunrise < d.Noon && d.Noon < d.Sunset && d.Sunset < d.Dusk, "Sun order");
        var at = d.At(new DateTime(2026, 11, 23, 12, 0, 0));
        Ensure(at.Phase == "Mañana" && at.Next == "Mediodía", "Phase " + at.Phase);
        Ensure(d.At(new DateTime(2026, 11, 23, 23, 0, 0)).Next == "Alba", "Night phase");

        Ensure(TaskText.Parse("Llamar a Ana @ 9:05") == ("Llamar a Ana", "09:05") && TaskText.Parse("Comprar pan") == ("Comprar pan", null) && TaskText.Parse("x @٩:٠٥") == ("x @٩:٠٥", null), "ParseTask");
    }

    static void Ensure(bool ok, string what) { if (!ok) throw new InvalidOperationException("Self-check failed: " + what); }
}
