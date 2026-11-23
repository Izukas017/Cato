using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace Cato;

/// Settings, built in code: each row helper adds one setting to the current card. Changes apply at once.
public class SettingsWindow : Window
{
    readonly NotchWindow main;
    readonly StackPanel body = new() { Margin = new Thickness(24, 0, 24, 24) };
    StackPanel card = new();
    StackPanel pageRows = new();
    Data D => main.data;
    static readonly IBrush Dim = NotchWindow.B("#8CFFFFFF");

    public SettingsWindow(NotchWindow main)
    {
        this.main = main;
        Title = "Configuración de Cato";
        Width = 540;
        Height = 720;
        Background = NotchWindow.B("#161618");
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://Cato/Assets/cato.png"))); } catch (Exception ex) { Debug.WriteLine(ex); }

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(24, 18, 24, 8),
            Children =
            {
                new Image { Source = new Bitmap(AssetLoader.Open(new Uri("avares://Cato/Assets/cato.png"))), Width = 46, Height = 46 },
                new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Children =
                    {
                        new TextBlock { Text = "Cato", FontSize = 24, FontWeight = FontWeight.Bold },
                        new TextBlock { Text = "Configuración · los cambios se aplican al momento", FontSize = 11, Foreground = Dim },
                    },
                },
            },
        };
        Build();
        var root = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        root.Children.Add(new ScrollViewer { Content = body });
        Content = root;
    }

    void Save() => main.SaveSettings();

    void Build()
    {
        Section("Apariencia");
        Choice("Estilo", new[] { "Fusionado al marco", "Dynamic Island" }, D.Island ? 1 : 0, i => { D.Island = i == 1; Save(); });
        Choice("Animación del notch", new[] { "Suave", "Elástica", "Rebote", "Rápida", "Ninguna" }, (int)D.Anim, i => { D.Anim = (AnimStyle)i; Save(); });
        Choice("Transición entre páginas", new[] { "Deslizar", "Fundido", "Zoom" }, (int)D.PageAnim, i => { D.PageAnim = (PageAnim)i; Save(); });
        Slide("Velocidad", 0.5, 2, 0.25, D.AnimSpeed, v => $"{v:0.##}×", v => { D.AnimSpeed = v; Save(); });
        Swatches("Color de acento", new[] { "#30D158", "#0A84FF", "#8B7CF6", "#FF9F0A", "#FF375F", "#FFFFFF" }, D.Accent, c => { D.Accent = c; Save(); });

        Section("Fundido de color");
        Toggle("Fundido en todas las pestañas", D.Glow, on => { D.Glow = on; Save(); });
        var palettes = NotchWindow.GlowPalettes.Keys.ToArray();
        Choice("Colores", palettes, Math.Max(0, Array.IndexOf(palettes, D.GlowPalette)), i => { D.GlowPalette = palettes[i]; Save(); });
        Slide("Intensidad", 0.2, 1, 0.1, D.GlowIntensity, v => $"{v * 100:0}%", v => { D.GlowIntensity = v; Save(); });
        Toggle("También con el notch cerrado", D.GlowClosed, on => { D.GlowClosed = on; Save(); });

        Section("Comportamiento");
        Slide("Zona de gracia", 0, 120, 4, D.GraceZone, v => $"{v:0} px", v => { D.GraceZone = (int)v; Save(); });
        Slide("Espera antes de cerrar", 0, 2000, 100, D.GraceDelayMs, v => v == 0 ? "al instante" : $"{v / 1000:0.0} s", v => { D.GraceDelayMs = (int)v; Save(); });
        Note("El notch sigue abierto mientras el ratón esté a esa distancia, y solo se cierra tras la espera fuera de ella.");

        Section("Páginas");
        Note("Ordena con las flechas y oculta las que no uses.");
        card.Children.Add(pageRows);
        BuildPageRows();

        Section("Pomodoro");
        Slide("Concentración", 5, 90, 5, D.FocusMin, v => $"{v:0} min", v => { D.FocusMin = (int)v; Save(); });
        Slide("Descanso corto", 1, 30, 1, D.ShortMin, v => $"{v:0} min", v => { D.ShortMin = (int)v; Save(); });
        Slide("Descanso largo", 5, 60, 5, D.LongMin, v => $"{v:0} min", v => { D.LongMin = (int)v; Save(); });
        Slide("Sesiones por ronda", 1, 8, 1, D.Sessions, v => $"{v:0}", v => { D.Sessions = (int)v; Save(); });

        Section("Agua");
        Toggle("Recordatorios", D.WaterRemind, on => { D.WaterRemind = on; Save(); });
        Slide("Meta diaria", 500, 4000, 250, D.WaterGoal, v => $"{v / 1000:0.##} L", v => { D.WaterGoal = (int)v; Save(); });
        Slide("Cada vaso", 100, 750, 50, D.WaterSip, v => $"{v:0} ml", v => { D.WaterSip = (int)v; Save(); });
        Slide("Avisar cada", 15, 180, 15, D.WaterEveryMin, v => $"{v:0} min", v => { D.WaterEveryMin = (int)v; Save(); });

        Section("Tiempo y ubicación");
        var city = Field("Ciudad", D.City, _ => true);
        var lat = Field("Latitud", D.Lat.ToString(CultureInfo.InvariantCulture), v => { if (TryNum(v, -90, 90, out var n)) { D.Lat = n; Save(); return true; } return false; });
        var lon = Field("Longitud", D.Lon.ToString(CultureInfo.InvariantCulture), v => { if (TryNum(v, -180, 180, out var n)) { D.Lon = n; Save(); return true; } return false; });
        async void LookUp()
        {
            var name = city.Text?.Trim() ?? "";
            if (name == "" || name == D.City) return;
            city.Foreground = Dim;
            if (await NotchWindow.Geocode(name) is not { } h) { city.Foreground = NotchWindow.B("#FF453A"); return; }
            (D.City, D.Lat, D.Lon) = (h.Name, Math.Round(h.Lat, 4), Math.Round(h.Lon, 4));
            city.Text = D.City;
            lat.Text = D.Lat.ToString(CultureInfo.InvariantCulture);
            lon.Text = D.Lon.ToString(CultureInfo.InvariantCulture);
            city.Foreground = Brushes.White;
            Save();
        }
        city.LostFocus += (_, _) => LookUp();
        city.KeyDown += (_, e) => { if (e.Key == Key.Enter) LookUp(); };
        Note("Escribe tu ciudad y pulsa Intro: las coordenadas se rellenan solas. El tiempo viene de open-meteo.com, que recibe estas coordenadas.");

        Section("Extras");
        Toggle("Letras sincronizadas (lrclib.net)", D.Lyrics, on => { D.Lyrics = on; Save(); });
        Toggle("Aviso al conectar el cargador", D.ChargeAlerts, on => { D.ChargeAlerts = on; Save(); });
        Toggle("Iniciar con la sesión", Autostart.Enabled, on => Autostart.Enabled = on);

        Section("Atajos y modo juego");
        Note("Ctrl + Alt + N  ·  ocultar o mostrar el notch");
        Note("Ctrl + Alt + G  ·  modo juego");
        Note("En Wayland (GNOME, KDE) el escritorio puede pedirte confirmar los atajos la primera vez. Si tu escritorio no los admite, crea los atajos tú con estos comandos:");
        var run = Autostart.InFlatpak ? "flatpak run io.github.Izukas017.Cato" : "cato";
        Command("Ocultar / mostrar", run + " --toggle");
        Command("Modo juego", run + " --game");
        Slide("Opacidad del modo juego", 0.2, 1, 0.05, D.GameOpacity, v => $"{v * 100:0}%", v => { D.GameOpacity = v; Save(); });
        Toggle("Color de acento en el modo juego", D.GameAccent, on => { D.GameAccent = on; Save(); });
        Note("La GPU y la VRAM se muestran en gráficas AMD. Los FPS no se pueden medir en Linux desde Cato.");
    }

    static bool TryNum(string s, double min, double max, out double v) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= min && v <= max;

    // ---------- Constructores de filas ----------
    void Section(string title)
    {
        card = new StackPanel { Spacing = 2 };
        body.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeight.SemiBold, Margin = new Thickness(4, body.Children.Count == 0 ? 8 : 22, 0, 8) });
        body.Children.Add(new Border { Background = NotchWindow.B("#12FFFFFF"), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 12, 16, 8), Child = card });
    }

    static TextBlock Label(string text) => new() { Text = text, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };

    void Row(string label, Control right)
    {
        var g = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 4, 0, 8) };
        g.Children.Add(Label(label));
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        card.Children.Add(g);
    }

    void Small(string text) => card.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = Dim, Margin = new Thickness(0, 4, 0, 6) });

    void Choice(string label, string[] options, int selected, Action<int> onPick)
    {
        Small(label);
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var buttons = new List<ToggleButton>();
        for (int i = 0; i < options.Length; i++)
        {
            var idx = i;
            var b = new ToggleButton { Content = options[i], IsChecked = i == selected, Classes = { "seg" } };
            b.Click += (_, _) => { foreach (var o in buttons) o.IsChecked = o == b; onPick(idx); };
            buttons.Add(b);
            wrap.Children.Add(b);
        }
        card.Children.Add(wrap);
    }

    void Slide(string label, double min, double max, double step, double value, Func<double, string> fmt, Action<double> onChange)
    {
        var valueText = new TextBlock { Text = fmt(value), FontSize = 12, Foreground = Dim, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        card.Children.Add(new Grid { Children = { Label(label), valueText } });
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, IsSnapToTickEnabled = true, Value = value, Margin = new Thickness(0, -6, 0, 2) };
        slider.ValueChanged += (_, e) => { valueText.Text = fmt(e.NewValue); onChange(e.NewValue); };
        card.Children.Add(slider);
    }

    void Toggle(string label, bool value, Action<bool> onChange)
    {
        var t = new ToggleSwitch { IsChecked = value, OnContent = "", OffContent = "", MinWidth = 0 };
        t.IsCheckedChanged += (_, _) => onChange(t.IsChecked == true);
        Row(label, t);
    }

    void Swatches(string label, string[] colors, string current, Action<string> onPick)
    {
        Small(label);
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Margin = new Thickness(0, 0, 0, 8) };
        var rings = new List<Ellipse>();
        foreach (var c in colors)
        {
            var ring = new Ellipse { Stroke = Brushes.White, StrokeThickness = 2, Opacity = string.Equals(c, current, StringComparison.OrdinalIgnoreCase) ? 1 : 0 };
            rings.Add(ring);
            var dot = new Panel
            {
                Width = 28, Height = 28, Cursor = new Cursor(StandardCursorType.Hand), Background = Brushes.Transparent,
                Children = { ring, new Ellipse { Margin = new Thickness(4), Fill = NotchWindow.B(c) } },
            };
            dot.PointerPressed += (_, _) => { foreach (var r in rings) r.Opacity = r == ring ? 1 : 0; onPick(c); };
            panel.Children.Add(dot);
        }
        card.Children.Add(panel);
    }

    TextBox Field(string label, string value, Func<string, bool> apply)
    {
        var tb = new TextBox { Text = value, Width = 180, FontSize = 12 };
        void Commit() => tb.Foreground = apply(tb.Text?.Trim() ?? "") ? Brushes.White : NotchWindow.B("#FF453A");
        tb.LostFocus += (_, _) => Commit();
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        Row(label, tb);
        return tb;
    }

    void Command(string label, string cmd)
    {
        Small(label);
        card.Children.Add(new SelectableTextBlock { Text = cmd, FontFamily = new FontFamily("monospace"), FontSize = 12, Margin = new Thickness(0, 0, 0, 8) });
    }

    void Note(string text) =>
        card.Children.Add(new TextBlock { Text = text, FontSize = 10, Foreground = Dim, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });

    List<string> CurrentOrder() => D.PageOrder.Where(NotchWindow.PageNames.Contains).Concat(NotchWindow.PageNames).Distinct().ToList();

    void BuildPageRows()
    {
        pageRows.Children.Clear();
        var order = CurrentOrder();
        for (int i = 0; i < order.Count; i++)
        {
            var name = order[i];
            var idx = i;
            var up = Arrow("M7.41,15.41L12,10.83L16.59,15.41L18,14L12,8L6,14L7.41,15.41Z", idx > 0, () => Move(order, idx, -1));
            var down = Arrow("M7.41,8.58L12,13.17L16.59,8.58L18,10L12,16L6,10L7.41,8.58Z", idx < order.Count - 1, () => Move(order, idx, 1));
            var t = new ToggleSwitch { IsChecked = !D.HiddenPages.Contains(name), OnContent = "", OffContent = "", MinWidth = 0 };
            t.IsCheckedChanged += (_, _) => { D.HiddenPages.Remove(name); if (t.IsChecked != true) D.HiddenPages.Add(name); Save(); };
            var g = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(0, 2, 0, 4) };
            g.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0), Children = { up, down } });
            var l = Label(name); Grid.SetColumn(l, 1); g.Children.Add(l);
            Grid.SetColumn(t, 2); g.Children.Add(t);
            pageRows.Children.Add(g);
        }
    }

    static Button Arrow(string path, bool enabled, Action onClick)
    {
        var b = new Button { Classes = { "icon" }, Padding = new Thickness(4), IsEnabled = enabled, Opacity = enabled ? 1 : 0.25, Content = new PathIcon { Data = StreamGeometry.Parse(path), Width = 12, Height = 12 } };
        b.Click += (_, _) => onClick();
        return b;
    }

    void Move(List<string> order, int i, int d)
    {
        (order[i], order[i + d]) = (order[i + d], order[i]);
        D.PageOrder = order;
        Save();
        BuildPageRows();
    }
}
