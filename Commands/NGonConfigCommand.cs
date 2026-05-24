using CommandSystem;
using TriangleScpSl.Core.Decomposition.NGonDecomposition;

namespace TriangleScpSl.Commands;

[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class NGonConfigCommand : ICommand
{
    public string Command { get; } = "NGonConfig";
    public string[] Aliases { get; } = ["ngoncfg"];
    public string Description { get; } = "Get/set NGon model processing defaults for this session. Usage: NGonConfig [property] [value]";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        NGonModelConfig config = NGonModelConfig.Session;

        // No args: list all current values
        if (arguments.Count == 0)
        {
            var lines = new List<string> { "NGon session config:" };

            foreach (string name in NGonModelConfig.PropertyNames)
            {
                string? val = config.TryGetValue(name);
                lines.Add($"  {name} = {val}");
            }

            lines.Add("");
            lines.Add("Usage: NGonConfig <property> <value> to change a setting.");

            response = string.Join("\n", lines);
            return true;
        }

        string propName = arguments.Array?[arguments.Offset] ?? string.Empty;

        // One arg: get specific value
        if (arguments.Count == 1)
        {
            string? val = config.TryGetValue(propName);

            if (val == null)
            {
                response = $"Unknown property '{propName}'. Run NGonConfig with no arguments to see all properties.";
                return false;
            }

            response = $"{propName} = {val}";
            return true;
        }

        // Two args: set value
        string newValue = arguments.Array?[arguments.Offset + 1] ?? string.Empty;

        if (!config.TrySetValue(propName, newValue))
        {
            string? currentVal = config.TryGetValue(propName);

            if (currentVal == null)
            {
                response = $"Unknown property '{propName}'. Run NGonConfig with no arguments to see all properties.";
                return false;
            }

            response = $"Invalid value '{newValue}' for property '{propName}' (current: {currentVal}).";
            return false;
        }

        string? updatedVal = config.TryGetValue(propName);
        response = $"{propName} = {updatedVal} (updated for this session)";
        return true;
    }
}