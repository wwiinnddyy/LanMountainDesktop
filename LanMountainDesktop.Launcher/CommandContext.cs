using System.Globalization;

namespace LanMountainDesktop.Launcher;

internal sealed class CommandContext
{
    public string Command { get; }

    public string SubCommand { get; }

    public IReadOnlyDictionary<string, string> Options { get; }

    public bool IsLegacyPluginInstall =>
        Options.ContainsKey("source") &&
        Options.ContainsKey("plugins-dir") &&
        Options.ContainsKey("result");

    private CommandContext(string command, string subCommand, Dictionary<string, string> options)
    {
        Command = command;
        SubCommand = subCommand;
        Options = options;
    }

    public static CommandContext FromArgs(string[] args)
    {
        var options = ParseOptions(args);
        var command = args.Length > 0 && !args[0].StartsWith("--", StringComparison.Ordinal)
            ? args[0]
            : "launch";
        var subCommand = args.Length > 1 && !args[1].StartsWith("--", StringComparison.Ordinal)
            ? args[1]
            : string.Empty;

        return new CommandContext(command, subCommand, options);
    }

    public string? GetOption(string key)
    {
        return Options.TryGetValue(key, out var value) ? value : null;
    }

    public int GetIntOption(string key, int fallback)
    {
        var raw = GetOption(key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : fallback;
    }

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var current = args[i];
            if (!current.StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }

            var key = current[2..];
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                values[key] = args[++i];
                continue;
            }

            values[key] = "true";
        }

        return values;
    }
}
