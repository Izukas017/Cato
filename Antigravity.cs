using System.Text;
using System.Text.Json;
using Path = System.IO.Path; // WPF's global using also brings in Shapes.Path

namespace Cato;

/// Antigravity writes every step of each conversation to ~/.gemini/antigravity/brain/<id>/.system_generated/logs/transcript_full.jsonl
/// (model text plus the tool calls it makes), so there is nothing to install: Cato just reads the end of those files.
/// Runs on the UI thread (awaits resume there); only the file reading is moved to the pool.
static class AntigravityFeed
{
    record Step(int Index, string Event, string Action, string Note);

    // CATO_AG_BRAIN: dev override, to test against a fake folder
    static readonly string Brain = Environment.GetEnvironmentVariable("CATO_AG_BRAIN")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gemini", "antigravity", "brain");

    /// `onEvent(event, conversation, action, note)` uses the same event names as Claude Code's hooks. `onDone(note)` fires when the agent finishes a turn.
    public static async void Start(Action<string, string, string, string> onEvent, Action<string> onDone)
    {
        var next = new Dictionary<string, int>(); // conversation → first step not seen yet
        while (true)
        {
            try
            {
                if (Directory.Exists(Brain))
                    foreach (var dir in Directory.EnumerateDirectories(Brain))
                    {
                        var file = Path.Combine(dir, ".system_generated", "logs", "transcript_full.jsonl");
                        var info = new FileInfo(file);
                        if (!info.Exists || (DateTime.Now - info.LastWriteTime).TotalMinutes > 30) continue;
                        var id = Path.GetFileName(dir);
                        var steps = await Task.Run(() => ReadSteps(file));
                        var first = !next.ContainsKey(id);
                        // first time we see a conversation: do not replay its history; show only its last step if it is fresh, and never alert
                        var from = first ? (steps.Count > 0 && (DateTime.Now - info.LastWriteTime).TotalSeconds < 120 ? steps[^1].Index : int.MaxValue) : next[id];
                        foreach (var s in steps.Where(s => s.Index >= from))
                        {
                            onEvent(s.Event, id, s.Action, s.Note);
                            if (s.Event == "Stop" && !first) onDone(Activity.Trim(s.Note, 70));
                        }
                        var last = steps.Count > 0 ? steps[^1].Index : -1;
                        next[id] = first ? last + 1 : Math.Max(from, last + 1);
                    }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
            await Task.Delay(2000);
        }
    }

    /// The meaningful steps in the last ~200 KB of a transcript. A half-written last line is skipped and read again next time.
    static List<Step> ReadSteps(string path)
    {
        var steps = new List<Step>();
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var take = (int)Math.Min(fs.Length, 200_000);
            var buf = new byte[take];
            fs.Seek(-take, SeekOrigin.End);
            fs.ReadExactly(buf);
            var lines = Encoding.UTF8.GetString(buf).Split('\n');
            for (var k = take < fs.Length ? 1 : 0; k < lines.Length; k++) // the first line may be cut in half
            {
                if (lines[k].Length == 0) continue;
                try
                {
                    using var doc = JsonDocument.Parse(lines[k]);
                    if (ToStep(doc.RootElement) is { } s) steps.Add(s);
                }
                catch (JsonException) { } // being written right now
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
        return steps;
    }

    static Step? ToStep(JsonElement o)
    {
        if (!o.TryGetProperty("step_index", out var si) || si.ValueKind != JsonValueKind.Number) return null;
        var index = si.GetInt32();
        var content = Activity.Str(o, "content");
        switch (Activity.Str(o, "type"))
        {
            case "USER_INPUT":
                var a = content.IndexOf("<USER_REQUEST>", StringComparison.Ordinal);
                var b = content.IndexOf("</USER_REQUEST>", StringComparison.Ordinal);
                var request = a >= 0 && b > a ? content[(a + 14)..b] : content;
                return new Step(index, "UserPromptSubmit", "Nueva petición", Activity.Clean(request, 160));
            case "PLANNER_RESPONSE":
                if (o.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array && calls.GetArrayLength() > 0)
                {
                    var call = calls[0];
                    var args = call.TryGetProperty("args", out var ar) ? ar : default;
                    var summary = Activity.Str(args, "toolSummary"); // the model's own one-line description of a command
                    return new Step(index, "PreToolUse", Describe(Activity.Str(call, "name"), args), summary != "" ? Activity.Clean(summary, 160) : Activity.Clean(content, 200));
                }
                return content.Trim() == "" ? null : new Step(index, "Stop", "Terminó", Activity.Clean(content, 200));
            default:
                return null;
        }
    }

    static string Describe(string tool, JsonElement args)
    {
        string Leaf(string key) => Path.GetFileName(Activity.Str(args, key).TrimEnd('/', '\\'));
        return tool switch
        {
            "read_file" => "Lee " + Leaf("Path"),
            "write_file" => "Escribe " + Leaf("Path"),
            "edit_file" => "Edita " + Leaf("Path"),
            "run_command" => "Ejecuta " + Activity.Trim(Activity.Str(args, "CommandLine").Split('\n')[0], 70),
            "update_steps" => "Actualiza el plan",
            "update_focus" => "Cambia de foco",
            _ => tool,
        };
    }
}
