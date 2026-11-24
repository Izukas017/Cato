using System.Text.RegularExpressions;

namespace Cato;

public sealed record ShelfItem(string Path, string Name, string SizeText, ImageSource? Icon);
record ClipView(string Text, string Preview, string Age, Brush Border);
record TodoView(TodoItem Item, string Text, Brush Bg, TextDecorationCollection? Deco, Brush CheckFill, double CheckStroke,
    string CheckGlyph, string Time, Visibility DelVis);

// Bandeja, portapapeles, tareas y widgets.
public partial class MainWindow
{
    // ================= Bandeja =================
    readonly ObservableCollection<ShelfItem> shelf = new();
    Point dragStart;
    bool dragging;

    void InitShelf() => ShelfList.ItemsSource = shelf;

    void Notch_DragEnter(object s, DragEventArgs e)
    {
        if (mode == Mode.Game || notchHidden || morphing) return;
        GoTo(Mode.Expanded);
        if (VisiblePages().Contains(4)) ShowPage(4, 1);
        SetDropHighlight(true);
    }

    void Notch_DragLeave(object s, DragEventArgs e)
    {
        var p = e.GetPosition(Notch);
        if (p.X > 0 && p.Y > 0 && p.X < Notch.ActualWidth && p.Y < Notch.ActualHeight) return; // moving between children
        SetDropHighlight(false);
        GoTo(Mode.Closed);
    }

    void Notch_Drop(object s, DragEventArgs e)
    {
        SetDropHighlight(false);
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files) return;
        foreach (var f in files.Where(f => shelf.All(x => x.Path != f)))
        {
            try { shelf.Add(MakeShelfItem(f)); }
            catch (Exception ex) { Debug.WriteLine(ex); } // vanished or unreadable file
        }
        UpdateShelf();
    }

    static ShelfItem MakeShelfItem(string path)
    {
        var isDir = Directory.Exists(path);
        var name = System.IO.Path.GetFileName(path.TrimEnd('\\'));
        return new(path, name, isDir ? "Carpeta" : HumanSize(new FileInfo(path).Length), FileIcon(path));
    }

    static string HumanSize(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1 << 20 => $"{b / 1024.0:0} KB",
        < 1L << 30 => $"{b / 1048576.0:0.0} MB",
        _ => $"{b / 1073741824.0:0.0} GB",
    };

    static ImageSource? FileIcon(string path)
    {
        try
        {
            var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif")
            {
                var b = new BitmapImage();
                b.BeginInit(); b.UriSource = new Uri(path); b.DecodePixelWidth = 80; b.CacheOption = BitmapCacheOption.OnLoad; b.EndInit(); b.Freeze();
                return b;
            }
            return Native.ShellIcon(path);
        }
        catch (Exception ex) { Debug.WriteLine(ex); return null; }
    }

    void UpdateShelf()
    {
        var has = shelf.Count > 0;
        ShelfList.Visibility = ClearShelfBtn.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
        DropRect.Visibility = DropHint.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
    }

    void SetDropHighlight(bool on)
    {
        DropRect.Stroke = on ? BlueB : Hex("#40FFFFFF");
        DropRect.Fill = on ? Hex("#1A0A84FF") : Brushes.Transparent;
        DropText.Text = on ? "Suelta para añadir" : "Suelta archivos aquí";
        ShelfList.Opacity = on ? 0.15 : 1;
        if (on) DropRect.Visibility = DropHint.Visibility = Visibility.Visible;
        else UpdateShelf();
    }

    void ShelfList_Down(object s, MouseButtonEventArgs e) { dragStart = e.GetPosition(null); dragging = false; }

    void ShelfList_Move(object s, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || dragging) return;
        var d = e.GetPosition(null) - dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not ShelfItem it) return;
        dragging = true;
        DragDrop.DoDragDrop(ShelfList, new DataObject(DataFormats.FileDrop, new[] { it.Path }), DragDropEffects.Copy | DragDropEffects.Move);
    }

    void ShelfList_Up(object s, MouseButtonEventArgs e)
    {
        if (dragging || (e.OriginalSource as FrameworkElement)?.DataContext is not ShelfItem it) return;
        if (File.Exists(it.Path) || Directory.Exists(it.Path))
            Process.Start(new ProcessStartInfo(it.Path) { UseShellExecute = true });
    }

    void ClearShelf_Click(object s, RoutedEventArgs e) { shelf.Clear(); UpdateShelf(); }

    // ================= Portapapeles (solo en memoria) =================
    const int MaxClips = 50;
    readonly List<(string Text, DateTime At)> clips = new();

    void OnClipboard()
    {
        try
        {
            if (!Clipboard.ContainsText()) return;
            var t = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(t)) return;
            clips.RemoveAll(c => c.Text == t);
            clips.Insert(0, (t, DateTime.Now));
            if (clips.Count > MaxClips) clips.RemoveRange(MaxClips, clips.Count - MaxClips);
            if (PageVisible(5)) UpdateClip();
        }
        catch (COMException) { } // another app holds the clipboard lock; skip this update
    }

    void UpdateClip()
    {
        ClipList.ItemsSource = clips.Select((c, i) => new ClipView(c.Text, Shorten(c.Text), Ago(c.At), i == 0 ? PurpleB : Brushes.Transparent)).ToList();
        ClipEmpty.Visibility = clips.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ClipCount.Text = clips.Count == 0 ? "" : $"{clips.Count} elementos · clic para copiar";
    }

    static string Shorten(string s)
    {
        var flat = Regex.Replace(s, @"\s+", " ").Trim();
        return flat.Length > 140 ? flat[..140] : flat;
    }

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "ahora" : d.TotalHours < 1 ? $"hace {(int)d.TotalMinutes} min" : d.TotalDays < 1 ? $"hace {(int)d.TotalHours} h" : t.ToString("d MMM", Es);
    }

    void ClipList_Click(object s, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is not ClipView v) return;
        try { Clipboard.SetText(v.Text); } catch (COMException) { }
    }

    // ================= Tareas =================
    bool editingTasks;

    void InitTodo() => data.Todos.RemoveAll(t => t.Done && t.Day != Today);

    void UpdateTodo()
    {
        TodoCount.Text = $"{data.Todos.Count(t => t.Done)}/{data.Todos.Count}";
        TodoList.ItemsSource = data.Todos.Select(t => new TodoView(t, t.Text,
            t.Done ? Hex("#2E30D158") : CardB, t.Done ? TextDecorations.Strikethrough : null,
            t.Done ? GreenB : Brushes.Transparent, t.Done ? 0 : 1.2, t.Done ? "" : "",
            t.At ?? "", editingTasks ? Visibility.Visible : Visibility.Collapsed)).ToList();
        EditTasksBtn.Content = editingTasks ? "Listo" : "Editar";
    }

    void AddTask_Click(object s, RoutedEventArgs e)
    {
        TaskInput.Visibility = Visibility.Visible;
        Activate();
        TaskInput.Focus();
    }

    void TaskInput_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseTaskInput(); return; }
        if (e.Key != Key.Enter) return;
        var (text, at) = TaskText.Parse(TaskInput.Text);
        if (text != "")
        {
            data.Todos.Add(new TodoItem { Text = text, At = at, Day = Today });
            Store.Save(data);
        }
        CloseTaskInput();
        UpdateTodo();
    }

    void CloseTaskInput()
    {
        TaskInput.Text = "";
        TaskInput.Visibility = Visibility.Collapsed;
        Keyboard.ClearFocus();
        if (!Notch.IsMouseOver) GoTo(Mode.Closed);
    }

    void TodoList_Click(object s, MouseButtonEventArgs e)
    {
        if (editingTasks || (e.OriginalSource as FrameworkElement)?.DataContext is not TodoView v) return;
        v.Item.Done = !v.Item.Done;
        v.Item.Day = Today;
        Store.Save(data);
        UpdateTodo();
    }

    void EditTasks_Click(object s, RoutedEventArgs e) { editingTasks = !editingTasks; UpdateTodo(); }

    void DeleteTask_Click(object s, RoutedEventArgs e)
    {
        if ((s as Button)?.Tag is TodoItem t) { data.Todos.Remove(t); Store.Save(data); UpdateTodo(); }
        e.Handled = true;
    }

    void TodoReminders(DateTime now)
    {
        foreach (var t in data.Todos.Where(t => !t.Done && !t.Notified && t.At != null && t.Day == Today))
        {
            if (!TimeSpan.TryParse(t.At, out var at) || now.TimeOfDay < at) continue;
            t.Notified = true;
            Store.Save(data);
            ShowAlert("", GreenB, t.Text, "Recordatorio de tarea", t.At!);
            break; // one at a time; the rest show on the next minute
        }
    }

    // ================= Widgets =================
    void UpdateWidgets()
    {
        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        WeekGrid.Children.Clear();
        WeekGrid.ColumnDefinitions.Clear();
        WeekGrid.RowDefinitions.Clear();
        WeekGrid.RowDefinitions.Add(new RowDefinition());
        WeekGrid.RowDefinitions.Add(new RowDefinition());
        string[] letters = { "L", "M", "X", "J", "V", "S", "D" };
        for (int i = 0; i < 7; i++)
        {
            WeekGrid.ColumnDefinitions.Add(new ColumnDefinition());
            var day = monday.AddDays(i);
            var isToday = day == today;
            var l = new TextBlock { Text = letters[i], FontSize = 9, Foreground = DimB, HorizontalAlignment = HorizontalAlignment.Center };
            Grid.SetColumn(l, i);
            WeekGrid.Children.Add(l);
            var cell = new Grid { Width = 18, Height = 18, Margin = new Thickness(0, 1, 0, 0) };
            if (isToday) cell.Children.Add(new Ellipse { Fill = BlueB });
            cell.Children.Add(new TextBlock
            {
                Text = day.Day.ToString(), FontSize = 10, Foreground = Brushes.White, FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            Grid.SetColumn(cell, i);
            Grid.SetRow(cell, 1);
            WeekGrid.Children.Add(cell);
        }
        var pending = data.Todos.Where(t => !t.Done).ToList();
        WeekTasks.Text = pending.Count == 1 ? "1 tarea pendiente" : $"{pending.Count} tareas pendientes";
        var nowHm = DateTime.Now.ToString("HH:mm");
        var next = pending.Where(t => t.At != null && string.CompareOrdinal(t.At, nowHm) >= 0).OrderBy(t => t.At).FirstOrDefault();
        WeekNext.Text = next != null ? $"Siguiente: {next.Text}, {next.At}" : "Sin recordatorios pendientes";

        if (Native.TryPower(out var charging, out var pct))
        {
            SysLabel.Text = "BATERÍA";
            SysValue.Text = $"{pct}%";
            SysState.Text = charging ? "Cargando" : "Con batería";
            SysSub.Text = charging ? "Enchufado" : "";
            SysIcon.Text = charging ? "" : "";
            SysRing.Data = RingGeometry(22, 20, pct / 100.0);
        }
        else
        {
            var load = Native.MemoryLoad();
            SysLabel.Text = "MEMORIA";
            SysValue.Text = $"{load}%";
            SysState.Text = "En uso";
            SysSub.Text = "Sin batería";
            SysIcon.Text = "";
            SysRing.Data = RingGeometry(22, 20, load / 100.0);
        }
    }
}
