using System.Text.Json;

namespace Cato;

/// Turns a Claude Code hook payload (JSON on stdin) into a short line for the Claude page.
static class Activity
{
    /// `Session` tells Claude Code sessions apart; `Project` is the last folder of their working directory.
    public record Event(string Name, string Text, string Session, string Project);

    public static Event? FromHookJson(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            var evt = Str(r, "hook_event_name");
            string? text = evt switch
            {
                "UserPromptSubmit" => "Nueva petición",
                "Stop" => "",
                "Notification" => Trim(Str(r, "message"), 90),
                "PreToolUse" => Describe(Str(r, "tool_name"), r.TryGetProperty("tool_input", out var i) ? i : default),
                _ => null,
            };
            if (text == null) return null;
            var project = System.IO.Path.GetFileName(Str(r, "cwd").TrimEnd('/', '\\'));
            return new Event(evt, text, Str(r, "session_id"), project == "" ? "sesión" : project);
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

    static string Str(JsonElement e, string key) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    static string Trim(string s, int max) => s.Length > max ? s[..max] + "…" : s;
}
