using System.Text.Json;
using Voice2Laser.Models;

namespace Voice2Laser.Services;

/// <summary>
/// Catalogo delle frasi vocali. Le azioni sono solo descrittive:
/// in questa fase non viene inviato alcun tasto a Windows o EZCAD2.
/// </summary>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandAction> AvailableActions { get; } =
    [
        new("open_file", "Apri file"),
        new("open_project", "Apri progetto"),
        new("save", "Salva"),
        new("cancel", "Annulla"),
        new("stop", "Stop"),
        new("confirm", "Conferma"),
        new("zoom_in", "Zoom avanti"),
        new("zoom_out", "Zoom indietro")
    ];

    public static IReadOnlyList<VoiceCommand> CreateDefaults() =>
    [
        new("apri file", "open_file"),
        new("apri progetto", "open_project"),
        new("salva", "save"),
        new("annulla", "cancel"),
        new("stop", "stop"),
        new("conferma", "confirm"),
        new("zoom avanti", "zoom_in"),
        new("zoom indietro", "zoom_out")
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
