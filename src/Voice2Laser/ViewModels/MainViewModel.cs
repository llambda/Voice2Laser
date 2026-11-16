using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Voice2Laser.Models;
using Voice2Laser.Services;

namespace Voice2Laser.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly VoskSpeechService _speech = new();
    private string _status = "Pronto";
    private string _modelPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "models", "vosk-model-small-it-0.22"));
    private string _partialText = "In attesa di riconoscimento";
    private bool _isListening;

    public ObservableCollection<RecognitionEvent> Events { get; } = [];

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string ModelPath { get => _modelPath; set => Set(ref _modelPath, value); }
    public string PartialText { get => _partialText; private set => Set(ref _partialText, value); }
    public bool IsListening { get => _isListening; private set => Set(ref _isListening, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        _speech.RecognitionReceived += OnRecognition;
        _speech.StatusChanged += message => Application.Current.Dispatcher.Invoke(() => Status = message);
    }

    public void Start()
    {
        try
        {
            _speech.Start(ModelPath);
            IsListening = true;
        }
        catch (Exception ex)
        {
            Status = $"Impossibile avviare: {ex.Message}";
            MessageBox.Show(ex.Message, "Voice2Laser", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    public void Stop()
    {
        _speech.Stop();
        IsListening = false;
        Status = "Ascolto fermato";
    }

    private void OnRecognition(string text, bool isFinal)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (isFinal)
            {
                PartialText = $"Riconosciuto: {text}";
                Events.Insert(0, new RecognitionEvent(DateTime.Now, text, true));
                while (Events.Count > 200) Events.RemoveAt(Events.Count - 1);
            }
            else
            {
                PartialText = $"In ascolto: {text}";
            }
        });
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public void Dispose() => _speech.Dispose();
}
