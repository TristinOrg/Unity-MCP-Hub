using System.Text;
using System.Text.Json;

namespace Tristin.MCPManager.Unity;

/// <summary>Reads the recent tool activity written by Coplay inside the Unity project.</summary>
public static class CoplayActivityLog
{
    private const int MaxReadBytes = 256 * 1024;
    private const int MaxEntries = 60;

    public static async Task<string> ReadRecentAsync(string projectPath, CancellationToken cancellationToken)
    {
        var path = Path.Combine(projectPath, "Assets", "UnityMCP", "Log", "mcp.log");
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.Asynchronous);
            var length = stream.Length;
            var offset = Math.Max(0, length - MaxReadBytes);
            stream.Position = offset;
            var bytes = new byte[(int)(length - offset)];
            await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);

            var lines = Encoding.UTF8.GetString(bytes).Split('\n');
            var entries = lines.Skip(offset == 0 ? 0 : 1).Reverse()
                .Select(FormatEntry).OfType<string>().Take(MaxEntries);
            return string.Join(Environment.NewLine + Environment.NewLine, entries);
        }
        catch (FileNotFoundException) { return string.Empty; }
        catch (DirectoryNotFoundException) { return string.Empty; }
    }

    private static string? FormatEntry(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var entry = document.RootElement;
            var tool = GetString(entry, "tool");
            if (string.IsNullOrEmpty(tool) || GetString(entry, "type") == "resource")
                return null;

            var timestamp = DateTimeOffset.TryParse(GetString(entry, "ts"), out var time)
                ? $"[{time.ToLocalTime():MM-dd HH:mm:ss}] "
                : string.Empty;
            var action = GetString(entry, "action");
            var status = GetString(entry, "status") switch
            {
                "SUCCESS" => "Succeeded",
                "ERROR" => "Failed",
                var other => other
            };
            entry.TryGetProperty("params", out var parameters);
            var target = GetString(parameters, "prefabPath") ?? GetString(parameters, "prefab_path")
                         ?? GetString(parameters, "assetPath") ?? GetString(parameters, "path")
                         ?? GetString(parameters, "target") ?? GetString(parameters, "name");
            var header = timestamp + DescribeOperation(tool, action);
            if (!string.IsNullOrEmpty(target))
                header += $" · {Shorten(target, 240)}";
            if (!string.IsNullOrEmpty(status))
                header += $" · {status}";

            var details = new List<string> { action == null ? $"Tool: {tool}" : $"Tool: {tool} / {action}" };
            var error = GetString(entry, "error");
            if (!string.IsNullOrWhiteSpace(error))
                details.Add($"Error: {Shorten(error, 300)}");

            if (parameters.ValueKind == JsonValueKind.Object)
            {
                foreach (var parameter in parameters.EnumerateObject().Where(parameter => parameter.Name != "action"))
                {
                    var value = parameter.Value.ValueKind == JsonValueKind.String
                        ? parameter.Value.GetString() ?? string.Empty
                        : parameter.Value.GetRawText();
                    details.Add($"{parameter.Name}: {Shorten(value, 180)}");
                }
            }

            return header + Environment.NewLine + string.Join(Environment.NewLine, details);
        }
        catch (JsonException)
        {
            // The editor can be writing or rotating the file while it is being read.
            return null;
        }
    }

    private static string DescribeOperation(string tool, string? action)
        => (tool, action) switch
        {
            ("manage_prefabs", "create_from_gameobject") => "Create Prefab",
            ("manage_prefabs", "modify_contents") => "Edit Prefab contents",
            ("manage_prefabs", "open_prefab_stage") => "Open Prefab stage",
            ("manage_prefabs", "save_prefab_stage") => "Save Prefab stage",
            ("manage_prefabs", "close_prefab_stage") => "Close Prefab stage",
            ("manage_prefabs", "get_info") => "Inspect Prefab",
            ("manage_prefabs", "get_hierarchy") => "Read Prefab hierarchy",
            ("manage_gameobject", "create") => "Create GameObject",
            ("manage_gameobject", "modify") => "Edit GameObject",
            ("manage_gameobject", "delete") => "Delete GameObject",
            ("manage_gameobject", "duplicate") => "Duplicate GameObject",
            ("manage_components", "add") => "Add component",
            ("manage_components", "remove") => "Remove component",
            ("manage_components", "set_property") => "Set component property",
            ("manage_scene", "create") => "Create scene",
            ("manage_scene", "load") => "Load scene",
            ("manage_scene", "save") => "Save scene",
            (_, null) => tool.Replace('_', ' '),
            _ => $"{action.Replace('_', ' ')} ({tool.Replace('_', ' ')})"
        };

    private static string? GetString(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value)
           && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Shorten(string value, int limit)
    {
        value = value.Replace("\r", " ").Replace("\n", " ");
        return value.Length <= limit ? value : value[..limit] + "…";
    }
}
