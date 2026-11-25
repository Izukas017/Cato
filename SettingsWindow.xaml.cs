namespace Cato;

public partial class SettingsWindow : Window
{
    readonly MainWindow main;
    StackPanel card = new();
    int sectionIndex;
    Data D => main.data;

    public SettingsWindow(MainWindow main)
    {
        this.main = main;
        InitializeComponent();
        Build();
    }

    void Save() => main.SaveSettings();
    void Title_Down(object s, MouseButtonEventArgs e) => DragMove();
    void Close_Click(object s, RoutedEventArgs e) => Close();

    void Build()
    {
        Section("Apariencia", "");
        Choice("Estilo", new[] { "Fusionado al marco", "Dynamic Island" }, D.Island ? 1 : 0,
            i => { D.Island = i == 1; Save(); });
        Choice("Animación del notch", new[] { "Suave", "Elástica", "Rebote", "Rápida", "Ninguna" }, (int)D.Anim,
            i => { D.Anim = (AnimStyle)i; Save(); main.PreviewAnimation(); });
        Choice("Transición entre páginas", new[] { "Deslizar", "Fundido", "Zoom" }, (int)D.PageAnim,
            i => { D.PageAnim = (PageAnim)i; Save(); });
        Slide("Velocidad", 0.5, 2, 0.25, D.AnimSpeed, v => $"{v:0.##}×", v => { D.AnimSpeed = v; Save(); });
        Swatches("Color de acento", new[] { "#30D158", "#0A84FF", "#8B7CF6", "#FF9F0A", "#FF375F", "#FFFFFF" }, D.Accent,
            c => { D.Accent = c; Save(); });
        Action("Probar animación", main.PreviewAnimation);

        Section("Fundido de color", "");
        Toggle("Fundido en todas las pestañas", D.Glow, on => { D.Glow = on; Save(); });
        var palettes = MainWindow.GlowPalettes.Keys.ToArray();
        Choice("Colores", palettes, Math.Max(0, Array.IndexOf(palettes, D.GlowPalette)), i => { D.GlowPalette = palettes[i]; Save(); main.PreviewAnimation(); });
        Slide("Intensidad", 0.2, 1, 0.1, D.GlowIntensity, v => $"{v * 100:0}%", v => { D.GlowIntensity = v; Save(); });
        Toggle("También con el notch cerrado", D.GlowClosed, on => { D.GlowClosed = on; Save(); });

        Section("Comportamiento", "");
        Slide("Zona de gracia", 0, 120, 4, D.GraceZone, v => $"{v:0} px", v => { D.GraceZone = (int)v; Save(); });
        Slide("Espera antes de cerrar", 0, 2000, 100, D.GraceDelayMs, v => v == 0 ? "al instante" : $"{v / 1000:0.0} s", v => { D.GraceDelayMs = (int)v; Save(); });
        Note("El notch sigue abierto mientras el ratón esté a esa distancia, y solo se cierra tras la espera fuera de ella.");
        Slide("Duración de los mensajes", 3, 20, 1, D.MessageSecs, v => $"{v:0} s", v => { D.MessageSecs = (int)v; Save(); });
        Note("Tiempo que se queda en el notch un mensaje de WhatsApp o el aviso de Claude. Haz clic en él para abrir la app.");

        Section("Páginas", "");
        Note("Ordena con las flechas y oculta las que no uses.");
        pageRows = new StackPanel();
        card.Children.Add(pageRows);
        BuildPageRows();

        Section("Pomodoro", "");
        Slide("Concentración", 5, 90, 5, D.FocusMin, v => $"{v:0} min", v => { D.FocusMin = (int)v; Save(); });
        Slide("Descanso corto", 1, 30, 1, D.ShortMin, v => $"{v:0} min", v => { D.ShortMin = (int)v; Save(); });
        Slide("Descanso largo", 5, 60, 5, D.LongMin, v => $"{v:0} min", v => { D.LongMin = (int)v; Save(); });
        Slide("Sesiones por ronda", 1, 8, 1, D.Sessions, v => $"{v:0}", v => { D.Sessions = (int)v; Save(); });
        Note("Si hay una ronda en marcha, los cambios se aplican al terminarla.");

        Section("Agua", "");
        Toggle("Recordatorios", D.WaterRemind, on => { D.WaterRemind = on; Save(); });
        Slide("Meta diaria", 500, 4000, 250, D.WaterGoal, v => $"{v / 1000:0.##} L", v => { D.WaterGoal = (int)v; Save(); });
        Slide("Cada vaso", 100, 750, 50, D.WaterSip, v => $"{v:0} ml", v => { D.WaterSip = (int)v; Save(); });
        Slide("Avisar cada", 15, 180, 15, D.WaterEveryMin, v => $"{v:0} min", v => { D.WaterEveryMin = (int)v; Save(); });

        Section("Tiempo y ubicación", "\uE753");
        var city = Field("Ciudad", D.City, _ => true);
        var lat = Field("Latitud", D.Lat.ToString(CultureInfo.InvariantCulture), v => { if (TryNum(v, -90, 90, out var n)) { D.Lat = n; Save(); return true; } return false; });
        var lon = Field("Longitud", D.Lon.ToString(CultureInfo.InvariantCulture), v => { if (TryNum(v, -180, 180, out var n)) { D.Lon = n; Save(); return true; } return false; });
        async void LookUp()
        {
            var name = city.Text.Trim();
            if (name == "" || name == D.City) return;
            city.Foreground = MainWindow.Hex("#8CFFFFFF");
            var hit = await MainWindow.Geocode(name);
            if (hit is not { } h) { city.Foreground = MainWindow.Hex("#FF453A"); return; }
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

        Section("Extras", "");
        Toggle("Letras sincronizadas (lrclib.net)", D.Lyrics, on => { D.Lyrics = on; Save(); });
        Toggle("Barra de volumen y brillo", D.VolumeOverlay, on => { D.VolumeOverlay = on; Save(); });
        Toggle("Aviso al conectar el cargador", D.ChargeAlerts, on => { D.ChargeAlerts = on; Save(); });
        Toggle("Aviso al cambiar de auriculares", D.AudioAlerts, on => { D.AudioAlerts = on; Save(); });
        startupButtons = Choice("Iniciar con Windows", new[] { "No", "Normal", "Como administrador" }, StartupMode(), SetStartup);
        Note("«Como administrador» pide permiso a Windows una sola vez y después arranca sin preguntar: activa los FPS y la temperatura de la CPU desde el inicio. Se quita volviendo a «No» o «Normal».");

        Section("Atajos y modo juego", "\uE765");
        Note("Ctrl + Alt + N  ·  ocultar o mostrar el notch");
        Note("Ctrl + Alt + G  ·  modo juego: barra fina con las métricas que elijas abajo");
        Slide("Opacidad del modo juego", 0.2, 1, 0.05, D.GameOpacity, v => $"{v * 100:0}%", v => { D.GameOpacity = v; Save(); });
        Toggle("Color de acento en el modo juego", D.GameAccent, on => { D.GameAccent = on; Save(); });
        Note("Qué aparece en la barra del modo juego:");
        foreach (var metric in MainWindow.GameMetrics)
            Toggle(metric, !D.GameHidden.Contains(metric), on => { D.GameHidden.Remove(metric); if (!on) D.GameHidden.Add(metric); Save(); });
        Note("Las temperaturas de la GPU funcionan siempre. La de la CPU necesita ejecutar Cato como administrador y tener instalado el driver PawnIO (pawnio.eu); si no está disponible, no se muestra.");
        if (FpsCounter.IsAdmin) Note("Cato se está ejecutando como administrador: los FPS están activos.");
        else
        {
            Note("Para medir los FPS de los juegos, Windows exige ejecutar Cato como administrador. Sin eso, el modo juego muestra el resto de datos y FPS como «—». Como administrador, no podrás arrastrar archivos desde el Explorador a la bandeja.");
            Action("Reiniciar como administrador", main.RestartAsAdmin);
        }
    }

    StackPanel pageRows = new();

    List<string> CurrentOrder() => D.PageOrder.Where(MainWindow.PageNames.Contains).Concat(MainWindow.PageNames).Distinct().ToList();

    void BuildPageRows()
    {
        pageRows.Children.Clear();
        var order = CurrentOrder();
        for (int i = 0; i < order.Count; i++)
        {
            var name = order[i];
            var idx = i;
            var g = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var arrows = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 10, 0) };
            arrows.Children.Add(Arrow("", idx > 0, () => Move(order, idx, -1)));
            arrows.Children.Add(Arrow("", idx < order.Count - 1, () => Move(order, idx, 1)));
            g.Children.Add(arrows);
            var label = Label(name);
            Grid.SetColumn(label, 1);
            g.Children.Add(label);
            var cb = new CheckBox { IsChecked = !D.HiddenPages.Contains(name), Style = (Style)FindResource("Toggle"), VerticalAlignment = VerticalAlignment.Center };
            cb.Click += (_, _) => { D.HiddenPages.Remove(name); if (cb.IsChecked != true) D.HiddenPages.Add(name); Save(); };
            Grid.SetColumn(cb, 2);
            g.Children.Add(cb);
            pageRows.Children.Add(g);
        }
    }

    Button Arrow(string glyph, bool enabled, Action onClick)
    {
        var b = new Button { Content = glyph, Style = (Style)FindResource("IconBtn"), FontSize = 10, Padding = new Thickness(5), IsEnabled = enabled, Opacity = enabled ? 1 : 0.25 };
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

    static bool TryNum(string s, double min, double max, out double v) =>
        double.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v >= min && v <= max;

    // ---------- Constructores de filas ----------
    void Section(string title, string glyph)
    {
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, sectionIndex == 0 ? 8 : 22, 0, 8) };
        head.Children.Add(new TextBlock { Text = glyph, FontFamily = (FontFamily)FindResource("Icons"), Foreground = Brushes.White, FontSize = 13, Margin = new Thickness(0, 2, 8, 0) });
        head.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 15, FontWeight = FontWeights.SemiBold });
        card = new StackPanel();
        var box = new Border { Background = MainWindow.Hex("#12FFFFFF"), CornerRadius = new CornerRadius(16), Padding = new Thickness(16, 12, 16, 8), Child = card };
        var wrap = new StackPanel();
        wrap.Children.Add(head);
        wrap.Children.Add(box);
        Body.Children.Add(wrap);
        Entrance(wrap, sectionIndex++);
    }

    /// Cards slide up and fade in one after another when the panel opens.
    static void Entrance(FrameworkElement el, int i)
    {
        var move = new TranslateTransform(0, 16);
        el.RenderTransform = move;
        el.Opacity = 0;
        var begin = TimeSpan.FromMilliseconds(60 * i);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(16, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = begin, EasingFunction = ease });
        el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(380)) { BeginTime = begin });
    }

    static TextBlock Label(string text) => new() { Text = text, Foreground = Brushes.White, FontSize = 13, VerticalAlignment = VerticalAlignment.Center };

    void Row(string label, FrameworkElement right)
    {
        var g = new Grid { Margin = new Thickness(0, 4, 0, 8) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        g.Children.Add(Label(label));
        Grid.SetColumn(right, 1);
        g.Children.Add(right);
        card.Children.Add(g);
    }

    RadioButton[] Choice(string label, string[] options, int selected, Action<int> onPick)
    {
        card.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Small"), Margin = new Thickness(0, 4, 0, 6) });
        var wrap = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
        var group = Guid.NewGuid().ToString();
        var buttons = new RadioButton[options.Length];
        for (int i = 0; i < options.Length; i++)
        {
            var idx = i;
            var rb = buttons[i] = new RadioButton { Content = options[i], GroupName = group, IsChecked = i == selected, Style = (Style)FindResource("Seg") };
            rb.Checked += (_, _) => onPick(idx);
            wrap.Children.Add(rb);
        }
        card.Children.Add(wrap);
        return buttons;
    }

    RadioButton[]? startupButtons;

    static int StartupMode() => Native.AdminStartupTask ? 2 : Native.StartupEnabled ? 1 : 0; // 0 no, 1 normal, 2 as administrator

    /// 2 = scheduled task with the highest privileges (Windows asks for permission once); 1 = Run key. Only one at a time,
    /// because an elevated and a normal copy of Cato must not both be started. If Windows' permission is refused, the previous choice comes back.
    void SetStartup(int mode)
    {
        var before = StartupMode();
        if (mode == before) return;
        if ((before == 2) != (mode == 2) && !Native.SetAdminStartupTask(mode == 2))
        {
            startupButtons![before].IsChecked = true; // refused or failed: back to what it was
            return;
        }
        Native.StartupEnabled = mode == 1;
    }

    void Slide(string label, double min, double max, double step, double value, Func<double, string> fmt, Action<double> onChange)
    {
        var valueText = new TextBlock { Text = fmt(value), Foreground = MainWindow.Hex("#8CFFFFFF"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
        var head = new Grid();
        head.Children.Add(Label(label));
        valueText.HorizontalAlignment = HorizontalAlignment.Right;
        head.Children.Add(valueText);
        var slider = new Slider { Minimum = min, Maximum = max, TickFrequency = step, Value = value, Margin = new Thickness(0, 4, 0, 8) };
        slider.ValueChanged += (_, e) => { valueText.Text = fmt(e.NewValue); onChange(e.NewValue); };
        card.Children.Add(head);
        card.Children.Add(slider);
    }

    void Toggle(string label, bool value, Action<bool> onChange)
    {
        var cb = new CheckBox { IsChecked = value, Style = (Style)FindResource("Toggle") };
        cb.Click += (_, _) => onChange(cb.IsChecked == true);
        Row(label, cb);
    }

    void Swatches(string label, string[] colors, string current, Action<string> onPick)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var group = Guid.NewGuid().ToString();
        foreach (var c in colors)
        {
            var rb = new RadioButton { GroupName = group, Background = MainWindow.Hex(c), IsChecked = string.Equals(c, current, StringComparison.OrdinalIgnoreCase), Style = (Style)FindResource("Swatch") };
            rb.Checked += (_, _) => onPick(c);
            panel.Children.Add(rb);
        }
        card.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("Small"), Margin = new Thickness(0, 4, 0, 6) });
        card.Children.Add(new Border { Child = panel, Margin = new Thickness(0, 0, 0, 8) });
    }

    void Action(string label, Action act)
    {
        var b = new Button { Content = label, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 8), Padding = new Thickness(14, 6, 14, 6) };
        b.Click += (_, _) => act();
        card.Children.Add(b);
    }

    /// Applies on Enter or when focus leaves; turns red when the value is rejected.
    TextBox Field(string label, string value, Func<string, bool> apply)
    {
        var tb = new TextBox { Text = value, Width = 180, Style = (Style)FindResource("Field") };
        void Commit() => tb.Foreground = apply(tb.Text.Trim()) ? Brushes.White : MainWindow.Hex("#FF453A");
        tb.LostFocus += (_, _) => Commit();
        tb.KeyDown += (_, e) => { if (e.Key == Key.Enter) Commit(); };
        Row(label, tb);
        return tb;
    }

    void Note(string text) =>
        card.Children.Add(new TextBlock { Text = text, Style = (Style)FindResource("Small"), FontSize = 10, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
}
