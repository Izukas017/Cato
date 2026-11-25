using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cato;

/// Turns a Claude Code hook payload (JSON on stdin) into lines for the Claude page:
/// `Action` is what is being done ("Edita Notify.cs"), `Note` explains it in a sentence or two.
static class Activity
{
    /// `Session` tells Claude Code sessions apart; `Project` is the last folder of their working directory.
    public record Event(string Name, string Action, string Note, string Session, string Project);

    public static Event? FromHookJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var evt = Str(r, "hook_event_name");
            var transcript = Str(r, "transcript_path");
            var input = r.TryGetProperty("tool_input", out var i) ? i : default;
            (string Action, string Note)? e = evt switch
            {
                "UserPromptSubmit" => ("Nueva petición", Clean(Str(r, "prompt"), 160)),
                "Stop" => ("Terminó", Str(r, "last_assistant_message") is { Length: > 0 } m ? Clean(m, 200) : Said(transcript)),
                "Notification" => ("Te espera", Clean(Str(r, "message"), 160)),
                "PreToolUse" => (Describe(Str(r, "tool_name"), input), Str(input, "description") is { Length: > 0 } d ? Clean(d, 160) : Said(transcript)),
                _ => null,
            };
            if (e is not { } x) return null;
            var project = System.IO.Path.GetFileName(Str(r, "cwd").TrimEnd('/', '\\'));
            return new Event(evt, x.Action, x.Note, Str(r, "session_id"), project == "" ? "sesión" : project);
        }
        catch (JsonException) { return null; }
    }

    static string Describe(string tool, JsonElement input)
    {
        string F(string key) => Str(input, key);
        string File(string key) => System.IO.Path.GetFileName(F(key).TrimEnd('/', '\\'));
        return tool switch
        {
            "Bash" or "PowerShell" => "Ejecuta " + Trim(F("command").Split('\n')[0], 70),
            "Read" => "Lee " + File("file_path"),
            "Edit" or "MultiEdit" or "NotebookEdit" => "Edita " + File("file_path"),
            "Write" => "Escribe " + File("file_path"),
            "Grep" => "Busca " + Trim(F("pattern"), 50),
            "Glob" => "Busca archivos " + Trim(F("pattern"), 50),
            "WebFetch" => "Abre " + (Uri.TryCreate(F("url"), UriKind.Absolute, out var u) ? u.Host : "una web"),
            "WebSearch" => "Busca en la web " + Trim(F("query"), 50),
            "Agent" or "Task" => "Subagente: " + Trim(F("description"), 50),
            "TodoWrite" => "Planifica los pasos",
            _ => tool.Contains("__") ? tool[(tool.LastIndexOf("__") + 2)..] : tool,
        };
    }

    /// What Claude last said in the current turn, from the end of the session transcript (JSONL).
    /// Stops at the user's last prompt, so text from an earlier turn is never shown.
    static string Said(string path)
    {
        if (path == "" || !File.Exists(path)) return "";
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var take = (int)Math.Min(fs.Length, 300_000); // the transcript can be tens of MB: read only the end
            var buf = new byte[take];
            fs.Seek(-take, SeekOrigin.End);
            fs.ReadExactly(buf);
            var lines = Encoding.UTF8.GetString(buf).Split('\n');
            for (var k = lines.Length - 1; k >= (take < fs.Length ? 1 : 0); k--) // the first line may be cut in half
            {
                if (lines[k].Length == 0) continue;
                using var doc = JsonDocument.Parse(lines[k]);
                var o = doc.RootElement;
                var type = Str(o, "type");
                if (!o.TryGetProperty("message", out var msg) || !msg.TryGetProperty("content", out var content)) continue;
                if (type == "user" && (content.ValueKind == JsonValueKind.String || content.EnumerateArray().Any(b => Str(b, "type") == "text"))) return ""; // the prompt itself
                if (type != "assistant" || content.ValueKind != JsonValueKind.Array) continue;
                var text = content.EnumerateArray().Where(b => Str(b, "type") == "text").Select(b => Str(b, "text")).FirstOrDefault(t => t.Trim().Length > 0);
                if (text != null) return Clean(text, 200);
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { Debug.WriteLine(ex); }
        return "";
    }

    /// One line, no markdown noise, cut at a sentence or word boundary.
    static string Clean(string s, int max)
    {
        s = Regex.Replace(s, @"[`*_#>]+", "");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (s.Length <= max) return s;
        var cut = s[..max];
        var end = cut.LastIndexOfAny(new[] { '.', ':', ';', '!', '?' });
        return end > max / 2 ? cut[..(end + 1)] : cut[..(cut.LastIndexOf(' ') is > 0 and var sp ? sp : max)] + "…";
    }

    static string Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string Trim(string s, int max) => s.Length > max ? s[..max] + "…" : s;
}
