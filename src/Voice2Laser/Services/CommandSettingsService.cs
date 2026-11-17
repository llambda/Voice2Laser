using System.Text.Json;
using Voice2Laser.Models;

namespace Voice2Laser.Services;

public sealed class CommandSettingsService
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voice2Laser", "commands.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public List<VoiceCommand> Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return CommandCatalog.CreateDefaults().ToList();

            var saved = JsonSerializer.Deserialize<List<VoiceCommand>>(
                File.ReadAllText(_settingsPath), JsonOptions);

            if (saved is null)
                return CommandCatalog.CreateDefaults().ToList();

            return saved.Select(command => new VoiceCommand(
                command.Phrase ?? string.Empty,
                command.ActionId == "stop"
                    ? "stop_laser"
                    : CommandCatalog.AvailableActions.Any(action => action.Id == command.ActionId)
                        ? command.ActionId
                        : "stop_laser")).ToList();
        }
        catch
        {
            return CommandCatalog.CreateDefaults().ToList();
        }
    }

    public void Save(IEnumerable<VoiceCommand> commands)
    {
        var directory = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

        var data = commands
            .Select(command => new VoiceCommand(command.Phrase.Trim(), command.ActionId))
            .ToList();

        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(data, JsonOptions));
    }
}
