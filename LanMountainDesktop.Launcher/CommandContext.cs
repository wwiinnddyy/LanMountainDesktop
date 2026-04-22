using System.Globalization;

namespace LanMountainDesktop.Launcher;

internal sealed class CommandContext
{
    private const string LaunchSourceOptionName = "launch-source";

    private static readonly string[] GuiCommands =
    [
        "launch",
        "apply-update",
        "preview-splash",
        "preview-error",
        "preview-update",
        "preview-oobe",
        "preview-debug"
    ];

    public string Command { get; }

    public string SubCommand { get; }

    public IReadOnlyDictionary<string, string> Options { get; }

    /// <summary>
    /// 原始命令行参数，用于转发给主程序
    /// </summary>
    public IReadOnlyList<string> RawArgs { get; }

    public bool IsLegacyPluginInstall =>
        Options.ContainsKey("source") &&
        Options.ContainsKey("plugins-dir") &&
        Options.ContainsKey("result");

    public string LaunchSource => NormalizeLaunchSource(GetOption(LaunchSourceOptionName)) ?? InferLaunchSource();

    /// <summary>
    /// 是否处于调试模式（从 Rider/VS 等 IDE 启动）
    /// 仅当明确指定 --debug 参数或调试器附加时才启用
    /// </summary>
    public bool IsDebugMode =>
        Options.ContainsKey("debug") ||
        System.Diagnostics.Debugger.IsAttached;

    public bool IsPreviewCommand =>
        Command.StartsWith("preview-", StringComparison.OrdinalIgnoreCase);

    public bool IsGuiCommand =>
        GuiCommands.Contains(Command, StringComparer.OrdinalIgnoreCase);

    public bool IsMaintenanceCommand =>
        string.Equals(LaunchSource, "apply-update", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(LaunchSource, "plugin-install", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Command, "update", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Command, "plugin", StringComparison.OrdinalIgnoreCase);

    public string? ExplicitAppRoot => GetOption("app-root");

    private CommandContext(string command, string subCommand, Dictionary<string, string> options, string[] rawArgs)
    {
        Command = command;
        SubCommand = subCommand;
        Options = options;
        RawArgs = rawArgs;
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

        return new CommandContext(command, subCommand, options, args);
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

    private string InferLaunchSource()
    {
        if (IsPreviewCommand)
        {
            return "debug-preview";
        }

        if (string.Equals(Command, "apply-update", StringComparison.OrdinalIgnoreCase))
        {
            return "apply-update";
        }

        if (IsLegacyPluginInstall || string.Equals(Command, "plugin", StringComparison.OrdinalIgnoreCase))
        {
            return "plugin-install";
        }

        return "normal";
    }

    private static string? NormalizeLaunchSource(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return raw.Trim().ToLowerInvariant() switch
        {
            "normal" => "normal",
            "postinstall" => "postinstall",
            "apply-update" => "apply-update",
            "plugin-install" => "plugin-install",
            "debug-preview" => "debug-preview",
            _ => null
        };
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
