namespace Cato;

/// Claude page: a live feed of what Claude Code is doing, fed by its hooks (`Cato.exe --activity`).
public partial class MainWindow
{
    readonly List<(DateTime At, string Text)> activity = new();
    string actState = "idle"; // working | waiting | idle
    DateTime actAt;

    void OnActivity(string evt, string text)
    {
        actAt = DateTime.Now;
        switch (evt)
        {
            case "Stop": actState = "idle"; text = "Terminó"; break;
            case "Notification": actState = "waiting"; if (text == "") text = "Necesita tu atención"; break;
            default: actState = "working"; break;
        }
        activity.Add((actAt, text));
        if (activity.Count > 30) activity.RemoveAt(0);
        if (PageVisible(9)) UpdateActivity();
    }

    void UpdateActivity()
    {
        // no events for a while while "working" usually means the session was closed
        var state = actState == "working" && (DateTime.Now - actAt).TotalSeconds > 120 ? "idle" : actState;
        (ActState.Text, ActDot.Fill) = state switch
        {
            "working" => ("Trabajando", GreenB),
            "waiting" => ("Te espera", OrangeB),
            _ => ("En espera", FaintB),
        };
        SetTitle("Claude", state == "working" ? "en vivo" : "", GreenB);
        ActNow.Text = activity.Count == 0 ? "Aún no ha llegado nada. Conecta los hooks de Claude Code (ver el README)." : activity[^1].Text;
        ActList.Children.Clear();
        foreach (var (at, text) in activity.AsEnumerable().Reverse().Skip(1).Take(4))
            ActList.Children.Add(new TextBlock
            {
                Foreground = DimB, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1),
                Inlines = { new Run(at.ToString("HH:mm:ss  ")) { Foreground = FaintB }, new Run(text) },
            });
    }
}
