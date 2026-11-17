using System.ComponentModel;
using System.Runtime.CompilerServices;
using Voice2Laser.Services;

namespace Voice2Laser.Models;

public sealed class VoiceCommand : INotifyPropertyChanged
{
    private string _phrase;
    private string _actionId;

    public VoiceCommand(string phrase, string actionId)
    {
        _phrase = phrase;
        _actionId = actionId;
    }

    public string Phrase
    {
        get => _phrase;
        set { if (_phrase == value) return; _phrase = value; OnPropertyChanged(); }
    }

    public string ActionId
    {
        get => _actionId;
        set
        {
            if (_actionId == value) return;
            _actionId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ActionName));
        }
    }

    public string ActionName => CommandCatalog.GetActionName(ActionId);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
