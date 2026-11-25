using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using IOPath = System.IO.Path;

namespace Cato;

// Bandeja, portapapeles, tareas y widgets.
public partial class NotchWindow
{
    // ================= Bandeja =================
    record ShelfItem(string Path, string Name, string Size);
    readonly List<ShelfItem> shelf = new();

    void InitShelf()
    {
        DragDrop.SetAllowDrop(Notch, true);
        Notch.AddHandler(DragDrop.DragEnterEvent, (_, e) =>
        {
            if (mode == Mode.Game || notchHidden) return;
            GoTo(Mode.Expanded);
            if (VisiblePages().Contains(4)) ShowPage(4, 1);
            SetDropHighlight(true);
        });
        Notch.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = DragDropEffects.Copy);
        Notch.AddHandler(DragDrop.DragLeaveEvent, (_, _) => SetDropHighlight(false));
        Notch.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            SetDropHighlight(false);
            foreach (var item in e.Data.GetFiles() ?? Enumerable.Empty<IStorageItem>())
            {
                if (item.TryGetLocalPath() is not { } p || shelf.Any(x => x.Path == p)) continue;
                var isDir = Directory.Exists(p);
                shelf.Add(new ShelfItem(p, IOPath.GetFileName(p.TrimEnd('/')), isDir ? "Carpeta" : HumanSize(new FileInfo(p).Length)));
            }
            UpdateShelf();
        });
    }

    static string HumanSize(long b) => b switch
    {
        < 1024 => $"{b} B",
        < 1 << 20 => $"{b / 1024.0:0} KB",
        < 1L << 30 => $"{b / 1048576.0:0.0} MB",
        _ => $"{b / 1073741824.0:0.0} GB",
    };

    void SetDropHighlight(bool on)
    {
        DropZone.BorderBrush = on ? BlueB : B("#40FFFFFF");
        DropZone.Background = on ? B("#1A0A84FF") : Brushes.Transparent;
        DropText.Text = on ? "Suelta para añadir" : "Suelta archivos aquí";
        ShelfScroll.Opacity = on ? 0.15 : 1;
        if (on) DropZone.IsVisible = DropHint.IsVisible = true;
        else UpdateShelf();
    }

    void UpdateShelf()
    {
        var has = shelf.Count > 0;
        ShelfScroll.IsVisible = ClearShelfBtn.IsVisible = has;
        DropZone.IsVisible = DropHint.IsVisible = !has;
        ShelfList.Children.Clear();
        foreach (var it in shelf) ShelfList.Children.Add(ShelfTile(it));
    }

    Control ShelfTile(ShelfItem it)
    {
        Control icon = new PathIcon { Data = Icon("IcFile"), Width = 30, Height = 30, Foreground = DimB };
        var ext = IOPath.GetExtension(it.Path).ToLowerInvariant();
        if (ext is ".jpg" or ".jpeg" or ".png" or ".bmp" or ".gif" or ".webp")
        {
            try { using var f = File.OpenRead(it.Path); icon = new Image { Source = Bitmap.DecodeToWidth(f, 80), Width = 40, Height = 40, Stretch = Stretch.UniformToFill }; }
            catch (Exception ex) { Debug.WriteLine(ex); }
        }
        var tile = new StackPanel
        {
            Width = 78, Margin = new Thickness(2, 4), Background = Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
            Children =
            {
                new Panel { Height = 40, Children = { icon } },
                new TextBlock { Text = it.Name, FontSize = 11, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 4, 0, 0) },
                new TextBlock { Text = it.Size, FontSize = 10, Foreground = DimB, TextAlignment = TextAlignment.Center },
            },
        };
        // drag out to any app; a plain click opens it
        Point? down = null;
        var dragged = false;
        tile.PointerPressed += (_, e) => { down = e.GetPosition(this); dragged = false; };
        tile.PointerMoved += async (_, e) =>
        {
            if (down is not { } d || dragged || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var p = e.GetPosition(this);
            if (Math.Abs(p.X - d.X) < 4 && Math.Abs(p.Y - d.Y) < 4) return;
            dragged = true;
            if (await StorageProvider.TryGetFileFromPathAsync(it.Path) is not { } file) return;
            var obj = new DataObject();
            obj.Set(DataFormats.Files, new IStorageItem[] { file });
            await DragDrop.DoDragDrop(e, obj, DragDropEffects.Copy | DragDropEffects.Move);
        };
        tile.PointerReleased += (_, _) =>
        {
            if (!dragged && down != null && (File.Exists(it.Path) || Directory.Exists(it.Path)))
            {
                var psi = new ProcessStartInfo("xdg-open") { UseShellExecute = false };
                psi.ArgumentList.Add(it.Path);
                try { Process.Start(psi); } catch (Exception ex) { Debug.WriteLine(ex); }
            }
            down = null;
        };
        return tile;
    }

    void ClearShelf_Click(object? s, RoutedEventArgs e) { shelf.Clear(); UpdateShelf(); }

    // ================= Portapapeles (solo en memoria) =================
    const int MaxClips = 50;
    readonly List<(string Text, DateTime At)> clips = new();
    string lastClip = "";

    async Task PollClipboard()
    {
        if (Clipboard == null) return;
        string? t;
        try { t = await Clipboard.GetTextAsync(); }
        catch (Exception ex) { Debug.WriteLine(ex); return; } // clipboard owner went away mid-read
        if (string.IsNullOrWhiteSpace(t) || t == lastClip) return;
        lastClip = t;
        clips.RemoveAll(c => c.Text == t);
        clips.Insert(0, (t, DateTime.Now));
        if (clips.Count > MaxClips) clips.RemoveRange(MaxClips, clips.Count - MaxClips);
        if (PageVisible(5)) UpdateClip();
    }

    void UpdateClip()
    {
        ClipList.Children.Clear();
        foreach (var (c, i) in clips.Select((c, i) => (c, i)))
        {
            var flat = Regex.Replace(c.Text, @"\s+", " ").Trim();
            var card = new Border
            {
                Width = 94, Height = 66, CornerRadius = new CornerRadius(10), Background = CardB, Padding = new Thickness(8, 6),
                BorderThickness = new Thickness(1), BorderBrush = i == 0 ? PurpleB : Brushes.Transparent, Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Grid
                {
                    RowDefinitions = new RowDefinitions("*,Auto"),
                    Children =
                    {
                        new TextBlock { Text = flat.Length > 140 ? flat[..140] : flat, FontSize = 11, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis },
                        Row(new TextBlock { Text = Ago(c.At), FontSize = 9, Foreground = DimB }, 1),
                    },
                },
            };
            var text = c.Text;
            card.PointerPressed += async (_, _) => { if (Clipboard != null) { lastClip = text; await Clipboard.SetTextAsync(text); } };
            ClipList.Children.Add(card);
        }
        ClipEmpty.IsVisible = clips.Count == 0;
        ClipCount.Text = clips.Count == 0 ? "" : $"{clips.Count} elementos · clic para copiar";
    }

    static Control Row(Control c, int row) { Grid.SetRow(c, row); return c; }
    static Control Col(Control c, int col) { Grid.SetColumn(c, col); return c; }

    static string Ago(DateTime t)
    {
        var d = DateTime.Now - t;
        return d.TotalMinutes < 1 ? "ahora" : d.TotalHours < 1 ? $"hace {(int)d.TotalMinutes} min" : d.TotalDays < 1 ? $"hace {(int)d.TotalHours} h" : t.ToString("d MMM", Es);
    }

    // ================= Tareas =================
    bool editingTasks;

    void UpdateTodo()
    {
        TodoCount.Text = $"{data.Todos.Count(t => t.Done)}/{data.Todos.Count}";
        EditTasksBtn.Content = editingTasks ? "Listo" : "Editar";
        TodoList.Children.Clear();
        foreach (var t in data.Todos.ToList())
        {
            var check = new Panel
            {
                Width = 16, Height = 16, Margin = new Thickness(0, 0, 8, 0),
                Children =
                {
                    new Ellipse { Fill = t.Done ? GreenB : Brushes.Transparent, Stroke = DimB, StrokeThickness = t.Done ? 0 : 1.2 },
                    new PathIcon { Data = Icon("IcCheck"), Width = 9, Height = 9, IsVisible = t.Done },
                },
            };
            var del = new Button { Classes = { "icon" }, Padding = new Thickness(3), IsVisible = editingTasks, Content = new PathIcon { Data = Icon("IcTrash"), Width = 11, Height = 11 } };
            del.Click += (_, e) => { data.Todos.Remove(t); Store.Save(data); UpdateTodo(); e.Handled = true; };
            var row = new Border
            {
                Background = t.Done ? B("#2E30D158") : CardB, CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 4), Cursor = new Cursor(StandardCursorType.Hand),
                Child = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"),
                    Children =
                    {
                        check,
                        Col(new TextBlock { Text = t.Text, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, TextDecorations = t.Done ? TextDecorations.Strikethrough : null }, 1),
                        Col(new TextBlock { Text = t.At ?? "", FontSize = 10, Foreground = DimB, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) }, 2),
                        Col(del, 3),
                    },
                },
            };
            row.Tapped += (_, _) =>
            {
                if (editingTasks) return;
                t.Done = !t.Done;
                t.Day = Today;
                Store.Save(data);
                UpdateTodo();
            };
            TodoList.Children.Add(row);
        }
    }

    void AddTask_Click(object? s, RoutedEventArgs e)
    {
        TaskInput.IsVisible = true;
        TakeKeyboard();
        TaskInput.Focus();
    }

    void EditTasks_Click(object? s, RoutedEventArgs e) { editingTasks = !editingTasks; UpdateTodo(); }

    void TaskInput_KeyDown(object? s, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CloseTaskInput(); return; }
        if (e.Key != Key.Enter) return;
        var (text, at) = TaskText.Parse(TaskInput.Text ?? "");
        if (text != "") { data.Todos.Add(new TodoItem { Text = text, At = at, Day = Today }); Store.Save(data); }
        CloseTaskInput();
        UpdateTodo();
    }

    void CloseTaskInput()
    {
        TaskInput.Text = "";
        TaskInput.IsVisible = false;
        Focus(); // leave the text box so the notch can close again
    }

    void TodoReminders(DateTime now)
    {
        foreach (var t in data.Todos.Where(t => !t.Done && !t.Notified && t.At != null && t.Day == Today))
        {
            if (!TimeSpan.TryParse(t.At, out var at) || now.TimeOfDay < at) continue;
            t.Notified = true;
            Store.Save(data);
            ShowAlert("IcBell", GreenB, t.Text, "Recordatorio de tarea", t.At!);
            break; // one at a time; the rest show on the next minute
        }
    }

    // ================= Widgets =================
    void UpdateWidgets()
    {
        var today = DateTime.Today;
        var monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        WeekGrid.Children.Clear();
        WeekGrid.ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*");
        WeekGrid.RowDefinitions = new RowDefinitions("Auto,Auto");
        string[] letters = { "L", "M", "X", "J", "V", "S", "D" };
        for (int i = 0; i < 7; i++)
        {
            var day = monday.AddDays(i);
            var isToday = day == today;
            WeekGrid.Children.Add(Col(new TextBlock { Text = letters[i], FontSize = 9, Foreground = DimB, HorizontalAlignment = HorizontalAlignment.Center }, i));
            var cell = new Panel { Width = 18, Height = 18, Margin = new Thickness(0, 1, 0, 0) };
            if (isToday) cell.Children.Add(new Ellipse { Fill = BlueB });
            cell.Children.Add(new TextBlock
            {
                Text = day.Day.ToString(), FontSize = 10, FontWeight = isToday ? FontWeight.Bold : FontWeight.Normal,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            });
            WeekGrid.Children.Add(Row(Col(cell, i), 1));
        }
        var pending = data.Todos.Count(t => !t.Done);
        WeekTasks.Text = pending == 1 ? "1 tarea pendiente" : $"{pending} tareas pendientes";

        if (SysInfo.TryBattery(out var charging, out var pct))
        {
            SysLabel.Text = "BATERÍA";
            SysValue.Text = $"{pct}%";
            SysState.Text = charging ? "Cargando" : "Con batería";
            SysIcon.Data = Icon("IcBolt");
            SysRing.Data = RingGeometry(22, 20, pct / 100.0);
        }
        else
        {
            var (load, used, total) = SysInfo.Memory();
            SysLabel.Text = "MEMORIA";
            SysValue.Text = $"{load}%";
            SysState.Text = $"{used:0.0} de {total:0} GB";
            SysIcon.Data = Icon("IcChip");
            SysRing.Data = RingGeometry(22, 20, load / 100.0);
        }
    }
}
