using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Cato;

/// "Claude" and "Antigravity" pages: a live feed of what each coding agent is doing (port of the Windows pages).
/// Claude Code reports through its hooks (`cato --activity`, over the Unix socket); Antigravity is read from its transcripts (Antigravity.cs).
/// One session shows its detail (the action plus a short explanation) and recent actions; several sessions show one row each.
public partial class NotchWindow
{
    static readonly IBrush OrangeB = B("#FF9F0A");
    static IImage Asset(string name) => new Bitmap(AssetLoader.Open(new Uri($"avares://Cato/Assets/{name}")));
    static readonly IImage ClaudeIcon = Asset("claude.png"), AntigravityIcon = Asset("antigravity.png");

    readonly ActivityPage claudePage = new("Claude", ClaudeIcon, "Aún no ha llegado nada. Conecta los hooks de Claude Code (ver el README).");
    readonly ActivityPage agPage = new("Antigravity", AntigravityIcon, "Aún no hay actividad. Se lee sola de las conversaciones de Antigravity.");

    sealed class AgentSession
    {
        public string Id = "", Name = "";
        public string State = "idle"; // working | waiting | idle
        public DateTime At;
        public string Action = "", Note = "";
        public List<(DateTime At, string Action)> Log = new();
        // no events for a while while "working" usually means the session was closed
        public string EffectiveState => State == "working" && (DateTime.Now - At).TotalSeconds > 120 ? "idle" : State;
        public string Summary => Note != "" ? Note : Action; // the explanation if there is one, else the action
    }

    sealed class ActivityPage
    {
        public readonly string Title;
        public readonly Grid Root = new() { IsVisible = false };
        readonly string empty;
        readonly Dictionary<string, AgentSession> sessions = new();
        readonly Grid single = new();
        readonly StackPanel multi = new() { IsVisible = false, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock action = new() { FontSize = 13, FontWeight = FontWeight.Bold, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 225, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock note = new() { Foreground = B("#CCFFFFFF"), FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxHeight = 64, LineHeight = 15, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 0) };
        readonly TextBlock state = new() { Foreground = B("#8CFFFFFF"), FontSize = 10, Margin = new Thickness(0, 4, 0, 0) };
        readonly StackPanel list = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

        public ActivityPage(string title, IImage icon, string empty)
        {
            Title = title;
            this.empty = empty;
            single.ColumnDefinitions.Add(new ColumnDefinition(260, GridUnitType.Pixel));
            single.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new Border { CornerRadius = new CornerRadius(5), ClipToBounds = true, Width = 18, Height = 18, Margin = new Thickness(0, 0, 8, 0), Child = new Image { Source = icon } });
            head.Children.Add(action);
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(head);
            left.Children.Add(note);
            left.Children.Add(state);
            Grid.SetColumn(list, 1);
            single.Children.Add(left);
            single.Children.Add(list);
            Root.Children.Add(single);
            Root.Children.Add(multi);
        }

        public void Add(string evt, string id, string project, string act, string text)
        {
            var now = DateTime.Now;
            if (!sessions.TryGetValue(id, out var s)) sessions[id] = s = new AgentSession { Id = id };
            s.Name = project;
            s.At = now;
            s.State = evt switch { "Stop" => "idle", "Notification" => "waiting", _ => "working" };
            // a tool call without its own explanation keeps the last one of this turn; a new prompt or the end starts clean
            if (text != "" || evt is "UserPromptSubmit" or "Stop" or "Notification") s.Note = text;
            s.Action = act;
            s.Log.Add((now, act));
            if (s.Log.Count > 30) s.Log.RemoveAt(0);
            foreach (var old in sessions.Where(p => (now - p.Value.At).TotalMinutes > 30).Select(p => p.Key).ToList()) sessions.Remove(old);
        }

        static (string Label, IBrush Dot) Look(string st) => st switch
        {
            "working" => ("Trabajando", GreenB),
            "waiting" => ("Te espera", OrangeB),
            _ => ("En espera", FaintB),
        };

        /// Redraws the page and returns the accent for the header title.
        public string Refresh()
        {
            var all = sessions.Values.OrderByDescending(s => s.At).ToList();
            single.IsVisible = all.Count <= 1;
            multi.IsVisible = all.Count > 1;
            var working = all.Count(s => s.EffectiveState == "working");
            var accent = all.Count > 1 ? $"{all.Count} sesiones" : working > 0 ? "en vivo" : "";
            if (all.Count > 1) { Rows(all); return accent; }

            var s = all.FirstOrDefault();
            var (label, dot) = Look(s?.EffectiveState ?? "idle");
            action.Text = s == null ? label : s.Action;
            note.Text = s == null ? empty : s.Note;
            note.IsVisible = note.Text != "";
            state.Inlines = new InlineCollection { new Run("● ") { Foreground = dot }, new Run(s == null ? label : label + " · " + s.Name) };
            list.Children.Clear();
            if (s == null) return accent;
            foreach (var (at, act) in s.Log.AsEnumerable().Reverse().Skip(1).Take(4))
                list.Children.Add(new TextBlock
                {
                    Foreground = DimB, FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 1),
                    Inlines = new InlineCollection { new Run(at.ToString("HH:mm:ss  ")) { Foreground = FaintB }, new Run(act) },
                });
            return accent;
        }

        void Rows(List<AgentSession> all)
        {
            multi.Children.Clear();
            foreach (var s in all.Take(4))
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                row.ColumnDefinitions.Add(new ColumnDefinition(100, GridUnitType.Pixel));
                row.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                row.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                var dup = all.Count(o => o.Name == s.Name) > 1; // same name twice: tell them apart by id
                var dot = new Ellipse { Width = 9, Height = 9, Fill = Look(s.EffectiveState).Dot, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
                var name = new TextBlock { Text = dup ? s.Name + " " + s.Id[..Math.Min(4, s.Id.Length)] : s.Name, FontSize = 12, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 8, 0) };
                var now = new TextBlock { Text = s.Summary, Foreground = DimB, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis };
                ToolTip.SetTip(now, s.Action);
                var time = new TextBlock { Text = s.At.ToString("HH:mm:ss"), Foreground = FaintB, FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(name, 1); Grid.SetColumn(now, 2); Grid.SetColumn(time, 3);
                foreach (var e in new Control[] { dot, name, now, time }) row.Children.Add(e);
                multi.Children.Add(row);
            }
        }
    }

    // ---- Claude Code (hooks over the socket) ----
    void OnActivity(string evt, string sessionId, string project, string action, string note)
    {
        claudePage.Add(evt, sessionId, project, action, note);
        if (PageVisible(9)) UpdateActivity();
    }

    void UpdateActivity() => SetTitle("Claude", claudePage.Refresh(), GreenB);

    /// `claude://` and `antigravity://` go through xdg-open, so the desktop (or the Flatpak portal) picks the app that registered them.
    static void OpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo("xdg-open", uri) { UseShellExecute = false, RedirectStandardError = true }); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException) { Debug.WriteLine(ex); } // no xdg-open
    }

    void OnClaudeDone(string note) => ShowAlert("", GreenB, "Claude terminó", note, onClick: () => OpenUri("claude://"), image: ClaudeIcon, chat: true);

    // ---- Antigravity (transcripts) ----
    void OnAntigravity(string evt, string conversation, string action, string note)
    {
        agPage.Add(evt, conversation, "Antigravity", action, note);
        if (PageVisible(10)) UpdateAntigravity();
    }

    void OnAntigravityDone(string note) =>
        ShowAlert("", GreenB, "Antigravity terminó", note, onClick: () => OpenUri("antigravity://"), image: AntigravityIcon, chat: true);

    void UpdateAntigravity() => SetTitle("Antigravity", agPage.Refresh(), GreenB);

    /// Commands arriving over the socket: `notify<TAB>text` and `activity<TAB>event<TAB>session<TAB>project<TAB>action<TAB>note`.
    void OnAgentCommand(string kind, string rest)
    {
        if (kind == "notify") OnClaudeDone(rest);
        else if (kind == "activity") { var f = rest.Split('\t', 5); if (f.Length == 5) OnActivity(f[0], f[1], f[2], f[3], f[4]); }
    }
}
