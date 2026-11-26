using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cato;

public enum AnimStyle { Suave, Elastica, Rebote, Rapida, Ninguna, Liquida } // Liquida: removed, kept so old settings still load
public enum PageAnim { Deslizar, Fundido, Zoom, Liquida } // Liquida: removed, kept so old settings still load

public class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
    public string? At { get; set; }      // "HH:mm" reminder, optional
    public string Day { get; set; } = ""; // yyyy-MM-dd it was added / last toggled
    public bool Notified { get; set; }
}

public class Data
{
    // Apariencia
    public AnimStyle Anim { get; set; } = AnimStyle.Suave;
    public PageAnim PageAnim { get; set; } = PageAnim.Deslizar;
    public double AnimSpeed { get; set; } = 1.0;
    public string Accent { get; set; } = "#30D158";
    public bool Island { get; set; } // false = fused to the frame, true = floating Dynamic Island
    public List<string> HiddenPages { get; set; } = new();
    public List<string> PageOrder { get; set; } = new(); // empty = default order

    // Fundido de color (brillo inferior del notch)
    public bool Glow { get; set; } = true;
    public string GlowPalette { get; set; } = "Aurora";
    public double GlowIntensity { get; set; } = 0.9;
    public bool GlowClosed { get; set; }

    // Zona de gracia: margen alrededor del notch y espera antes de cerrarse
    public int GraceZone { get; set; } = 24;
    public int GraceDelayMs { get; set; } = 300;
    public int MessageSecs { get; set; } = 7; // how long a chat message stays in the notch

    // Modo juego
    public double GameOpacity { get; set; } = 0.6;
    public bool GameAccent { get; set; } = true;
    public List<string> GameHidden { get; set; } = new(); // game bar items the user turned off (MainWindow.GameMetrics)

    // Extras
    public bool Lyrics { get; set; } = true;
    public bool VolumeOverlay { get; set; } = true;
    public bool ChargeAlerts { get; set; } = true;
    public bool AudioAlerts { get; set; } = true;

    // Pomodoro
    public int FocusMin { get; set; } = 25;
    public int ShortMin { get; set; } = 5;
    public int LongMin { get; set; } = 15;
    public int Sessions { get; set; } = 4;

    // Agua
    public int WaterGoal { get; set; } = 1500;
    public int WaterSip { get; set; } = 250;
    public int WaterEveryMin { get; set; } = 60;
    public bool WaterRemind { get; set; } = true;
    public string WaterDay { get; set; } = "";
    public List<int[]> WaterLog { get; set; } = new(); // [hour, ml]

    // Rezo
    public string City { get; set; } = "Madrid";
    public double Lat { get; set; } = 40.4168;
    public double Lon { get; set; } = -3.7038;

    public List<TodoItem> Todos { get; set; } = new();

    // Home: widgets in the order they show (ids from MainWindow.HomeCatalog) and the quick note
    public List<string> HomeWidgets { get; set; } = new() { "sistema", "tiempo", "musica", "volumen" };
    public string Note { get; set; } = "";

    // Claude plan limits, last seen in the status line (Cato.exe --statusline): used %, -1 = never seen; reset in Unix seconds
    public double ClaudeFive { get; set; } = -1;
    public long ClaudeFiveReset { get; set; }
    public double ClaudeWeek { get; set; } = -1;
    public long ClaudeWeekReset { get; set; }
}

static class Store
{
    static readonly string Dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Cato");
    static readonly string FilePath = System.IO.Path.Combine(Dir, "data.json");
    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };

    public static Data Load()
    {
        try
        {
            return File.Exists(FilePath) ? JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath), Opts) ?? new() : new();
        }
        catch (Exception ex)
        {
            // Keep the unreadable file instead of overwriting it on the next save.
            Debug.WriteLine(ex);
            try { File.Copy(FilePath, FilePath + ".bak", true); } catch (IOException) { }
            return new();
        }
    }

    public static void Save(Data d)
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(d, Opts));
            File.Move(tmp, FilePath, true); // atomic swap so a crash mid-write can't corrupt the data
        }
        catch (Exception ex) { Debug.WriteLine(ex); }
    }
}
