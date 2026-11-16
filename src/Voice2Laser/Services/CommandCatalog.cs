using System.Text.Json;
using Voice2Laser.Models;

namespace Voice2Laser.Services;

/// <summary>
/// Frasi vocali e azioni disponibili. Solo anteprima, start/go e stop
/// hanno un'associazione tasti attiva in questa versione.
/// </summary>
public static class CommandCatalog
{
    public static IReadOnlyList<CommandAction> AvailableActions { get; } =
    [
        new("open_file", "Apri file (non ancora attivo)"),
        new("open_project", "Apri progetto (non ancora attivo)"),
        new("save", "Salva (non ancora attivo)"),
        new("cancel", "Annulla (non ancora attivo)"),
        new("stop_laser", "Stop laser – ESC"),
        new("confirm", "Conferma (non ancora attivo)"),
        new("zoom_in", "Zoom avanti (non ancora attivo)"),
        new("zoom_out", "Zoom indietro (non ancora attivo)"),
        new("preview", "Anteprima – F1"),
        new("start_laser", "Avvia marcatura – F2")
    ];

    public static IReadOnlyList<VoiceCommand> CreateDefaults() =>
    [
        new("apri file", "open_file"),
        new("apri progetto", "open_project"),
        new("salva", "save"),
        new("annulla", "cancel"),
        new("stop", "stop_laser"),
        new("conferma", "confirm"),
        new("zoom avanti", "zoom_in"),
        new("zoom indietro", "zoom_out"),
        new("anteprima", "preview"),
        new("start", "start_laser"),
        new("go", "start_laser")
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
