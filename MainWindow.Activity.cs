namespace Cato;

/// Claude page: a live feed of what Claude Code is doing, fed by its hooks (`Cato.exe --activity`).
/// One session shows its detail and recent actions; several sessions show one row each.
public partial class MainWindow
{
    sealed class ClaudeSession
    {
        public string Name = "";
        public string State = "idle"; // working | waiting | idle
        public DateTime At;
        public string Now = "";
        public List<(DateTime At, string Text)> Log = new();
        // no events for a while while "working" usually means the session was closed
        public string EffectiveState => State == "working" && (DateTime.Now - At).TotalSeconds > 120 ? "idle" : State;
    }

    readonly Dictionary<string, ClaudeSession> claudeSessions = new();

    void OnActivity(string evt, string sessionId, string project, string text)
    {
        var now = DateTime.Now;
        if (!claudeSessions.TryGetValue(sessionId, out var s)) claudeSessions[sessionId] = s = new ClaudeSession();
        s.Name = project;
        s.At = now;
        switch (evt)
        {
            case "Stop": s.State = "idle"; text = "Terminó"; break;
            case "Notification": s.State = "waiting"; if (text == "") text = "Necesita tu atención"; break;
            default: s.State = "working"; break;
        }
        s.Now = text;
        s.Log.Add((now, text));
        if (s.Log.Count > 30) s.Log.RemoveAt(0);
        foreach (var old in claudeSessions.Where(p => (now - p.Value.At).TotalMinutes > 30).Select(p => p.Key).ToList()) claudeSessions.Remove(old);
        if (PageVisible(9)) UpdateActivity();
    }

    static (string Label, Brush Dot) StateLook(string state) => state switch
    {
        "working" => ("Trabajando", GreenB),
        "waiting" => ("Te espera", OrangeB),
        _ => ("En espera", FaintB),
    };

    void UpdateActivity()
    {
        var all = claudeSessions.Values.OrderByDescending(s => s.At).ToList();
        ActSingle.Visibility = all.Count > 1 ? Visibility.Collapsed : Visibility.Visible;
        ActMulti.Visibility = all.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        var working = all.Count(s => s.EffectiveState == "working");
        SetTitle("Claude", all.Count > 1 ? $"{all.Count} sesiones" : working > 0 ? "en vivo" : "", GreenB);
        if (all.Count > 1) { UpdateActivityRows(all); return; }

        var s = all.FirstOrDefault();
        var (label, dot) = StateLook(s?.EffectiveState ?? "idle");
        (ActState.Text, ActDot.Fill) = (label, dot);
        ActNow.Text = s == null ? "Aún no ha llegado nada. Conecta los hooks de Claude Code (ver el README)." : s.Now;
        ActList.Children.Clear();
        if (s == null) return;
        foreach (var (at, text) in s.Log.AsEnumerable().Reverse().Skip(1).Take(4))
            ActList.Children.Add(new TextBlock
            {
                Foreground = DimB, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1),
                Inlines = { new Run(at.ToString("HH:mm:ss  ")) { Foreground = FaintB }, new Run(text) },
            });
    }

    void UpdateActivityRows(List<ClaudeSession> all)
    {
        ActMulti.Children.Clear();
        foreach (var s in all.Take(4))
        {
            var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var dot = new System.Windows.Shapes.Ellipse { Width = 9, Height = 9, Fill = StateLook(s.EffectiveState).Dot, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = s.Name, Foreground = Brushes.White, FontSize = 12, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
            var now = new TextBlock { Text = s.Now, Foreground = DimB, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
            var time = new TextBlock { Text = s.At.ToString("HH:mm:ss"), Foreground = FaintB, FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(name, 1); Grid.SetColumn(now, 2); Grid.SetColumn(time, 3);
            foreach (UIElement e in new UIElement[] { dot, name, now, time }) row.Children.Add(e);
            ActMulti.Children.Add(row);
        }
    }
}
