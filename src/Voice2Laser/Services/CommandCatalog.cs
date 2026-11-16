using System.Text.Json;
using Voice2Laser.Models;

namespace Voice2Laser.Services;

public static class CommandCatalog
{
    public static IReadOnlyList<CommandAction> AvailableActions { get; } =
    [
        new("stop_laser", "Stop – ESC"),
        new("preview", "Anteprima – F1"),
        new("start_laser", "Avvia – F2"),
        new("move_up", "Su – Freccia su"),
        new("move_down", "Giù – Freccia giù"),
        new("move_left", "Sinistra – Freccia sinistra"),
        new("move_right", "Destra – Freccia destra")
    ];

    public static IReadOnlyList<VoiceCommand> CreateDefaults() =>
    [
        new("anteprima", "preview"),
        new("start", "start_laser"),
        new("go", "start_laser"),
        new("stop", "stop_laser"),
        new("su", "move_up"),
        new("giù", "move_down"),
        new("destra", "move_right"),
        new("sinistra", "move_left")
    ];

    public static string BuildGrammar(IEnumerable<VoiceCommand> commands)
    {
        var phrases = commands
            .Select(command => command.Phrase?.Trim())
            .Where(phrase => !string.IsNullOrWhiteSpace(phrase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Cast<string>()
            .Append("[unk]");

        return JsonSerializer.Serialize(phrases);
    }

    public static string GetActionName(string actionId) =>
        AvailableActions.FirstOrDefault(action => action.Id == actionId)?.Name
        ?? "Azione non riconosciuta";
}
