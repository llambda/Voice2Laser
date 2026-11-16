using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using NAudio.Wave;
using Voice2Laser.Models;
using Voice2Laser.Services;

namespace Voice2Laser.ViewModels;

public sealed record AudioInputDevice(int DeviceNumber, string Name);

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly VoskSpeechService _speech = new();
    private readonly CommandSettingsService _commandSettings = new();
    private readonly KeyboardCommandService _keyboardCommands = new();
    private readonly DispatcherTimer _deviceMonitor;
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voice2Laser", "audio-device.json");

    private string _status = "Pronto";
    private string _modelPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "models", "vosk-model-small-it-0.22"));
    private string _partialText = "In attesa di riconoscimento";
    private string _modelStatusText = "Percorso del modello non ancora verificato.";
    private bool? _modelExists;
    private bool _isListening;
    private AudioInputDevice? _selectedAudioDevice;
    private double _audioLevelDb = -60.0;
    private double _audioPeakDb = -60.0;
    private string? _preferredAudioDeviceName;
    private bool _isRefreshingDevices;
    private bool _disposed;
    private VoiceCommand? _selectedCommand;

    public ObservableCollection<RecognitionEvent> Events { get; } = [];
    public ObservableCollection<AudioInputDevice> AudioInputDevices { get; } = [];
    public ObservableCollection<VoiceCommand> Commands { get; } = [];
    public IReadOnlyList<CommandAction> AvailableActions => CommandCatalog.AvailableActions;
    public VoiceCommand? SelectedCommand { get => _selectedCommand; set => Set(ref _selectedCommand, value); }

    public string Status { get => _status; private set => Set(ref _status, value); }
    public string ModelPath
    {
        get => _modelPath;
        set
        {
            if (string.Equals(_modelPath, value, StringComparison.Ordinal)) return;
            _modelPath = value;
            OnPropertyChanged();
            CheckModelAvailability();
        }
    }
    public string PartialText { get => _partialText; private set => Set(ref _partialText, value); }
    public string ModelStatusText { get => _modelStatusText; private set => Set(ref _modelStatusText, value); }
    public bool? ModelExists { get => _modelExists; private set => Set(ref _modelExists, value); }
    public bool IsListening
    {
        get => _isListening;
        private set
        {
            if (_isListening == value) return;
            _isListening = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSelectAudioDevice));
        }
    }
    public bool CanSelectAudioDevice => !IsListening;
    public double AudioLevelDb
    {
        get => _audioLevelDb;
        private set
        {
            if (EqualityComparer<double>.Default.Equals(_audioLevelDb, value)) return;
            _audioLevelDb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AudioLevelPercent));
            OnPropertyChanged(nameof(AudioLevelText));
            OnPropertyChanged(nameof(AudioCalibrationHint));
        }
    }
    public double AudioPeakDb
    {
        get => _audioPeakDb;
        private set
        {
            if (EqualityComparer<double>.Default.Equals(_audioPeakDb, value)) return;
            _audioPeakDb = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AudioPeakText));
            OnPropertyChanged(nameof(AudioCalibrationHint));
        }
    }
    public double AudioLevelPercent => Math.Clamp((AudioLevelDb + 60.0) / 60.0 * 100.0, 0.0, 100.0);
    public string AudioLevelText => AudioLevelDb <= -59.9 ? "Segnale assente o molto debole" : $"{AudioLevelDb:0} dBFS RMS";
    public string AudioPeakText => AudioPeakDb <= -59.9 ? "Picco: —" : $"Picco: {AudioPeakDb:0} dBFS";
    public string AudioCalibrationHint => AudioPeakDb >= -1.0
        ? "ATTENZIONE: segnale vicino alla saturazione. Riduci il volume del microfono."
        : AudioLevelDb <= -50.0
            ? "Segnale molto debole: avvicina il microfono o aumenta il guadagno."
            : "Livello rilevato. Prova a parlare a volume normale e poi più forte.";

    public AudioInputDevice? SelectedAudioDevice
    {
        get => _selectedAudioDevice;
        set
        {
            if (Set(ref _selectedAudioDevice, value) && !_isRefreshingDevices && value is not null)
            {
                _preferredAudioDeviceName = value.Name;
                SavePreferredAudioDevice();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        LoadPreferredAudioDevice();
        foreach (var command in _commandSettings.Load()) Commands.Add(command);
        _speech.RecognitionReceived += OnRecognition;
        _speech.AudioLevelChanged += OnAudioLevel;
        _speech.StatusChanged += message => Application.Current.Dispatcher.BeginInvoke(() => Status = message);

        CheckModelAvailability();
        RefreshAudioDevices();

        _deviceMonitor = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _deviceMonitor.Tick += (_, _) => RefreshAudioDevices(automatic: true);
        _deviceMonitor.Start();
    }

    public void RefreshAudioDevices() => RefreshAudioDevices(automatic: false);

    private void RefreshAudioDevices(bool automatic)
    {
        if (_disposed || _isRefreshingDevices) return;

        try
        {
            var detected = new List<AudioInputDevice>();
            for (var i = 0; i < WaveIn.DeviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                detected.Add(new AudioInputDevice(i, capabilities.ProductName));
            }

            var currentNames = detected.Select(d => d.Name).ToArray();
            var oldNames = AudioInputDevices.Select(d => d.Name).ToArray();
            var deviceListChanged = !currentNames.SequenceEqual(oldNames, StringComparer.OrdinalIgnoreCase);

            if (!deviceListChanged && automatic) return;

            var selectedName = _preferredAudioDeviceName ?? SelectedAudioDevice?.Name;
            var selectedStillPresent = selectedName is not null &&
                detected.Any(d => string.Equals(d.Name, selectedName, StringComparison.OrdinalIgnoreCase));

            var microphoneWasDisconnected = IsListening && !selectedStillPresent;
            if (microphoneWasDisconnected)
            {
                _speech.Stop();
                IsListening = false;
                AudioLevelDb = -60.0;
                AudioPeakDb = -60.0;
            }

            _isRefreshingDevices = true;
            AudioInputDevices.Clear();
            foreach (var device in detected)
                AudioInputDevices.Add(device);

            var restored = selectedName is null
                ? null
                : AudioInputDevices.FirstOrDefault(d => string.Equals(d.Name, selectedName, StringComparison.OrdinalIgnoreCase));

            SelectedAudioDevice = restored ?? AudioInputDevices.FirstOrDefault();
            _isRefreshingDevices = false;

            if (AudioInputDevices.Count == 0)
            {
                SelectedAudioDevice = null;
                if (!IsListening) Status = "Nessun microfono rilevato. Collega il ricevitore USB delle cuffie.";
            }
            else if (!automatic && !IsListening)
            {
                Status = restored is not null
                    ? $"Microfono selezionato: {restored.Name}"
                    : $"Microfoni rilevati: {AudioInputDevices.Count}. Seleziona quello da utilizzare.";
            }

            if (microphoneWasDisconnected)
                Application.Current.Dispatcher.BeginInvoke(() =>
                    Status = "Microfono scollegato. Ascolto fermato; ricollega le cuffie e aggiorna la selezione.");
        }
        catch (Exception ex)
        {
            _isRefreshingDevices = false;
            if (!automatic) Status = $"Errore durante la ricerca dei microfoni: {ex.Message}";
        }
    }

    private void LoadPreferredAudioDevice()
    {
        try
        {
            if (File.Exists(_settingsPath))
                _preferredAudioDeviceName = JsonSerializer.Deserialize<AudioDeviceSettings>(File.ReadAllText(_settingsPath))?.DeviceName;
        }
        catch
        {
            _preferredAudioDeviceName = null;
        }
    }

    private void SavePreferredAudioDevice()
    {
        try
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new AudioDeviceSettings(_preferredAudioDeviceName)));
        }
        catch (Exception ex)
        {
            Status = $"Impossibile salvare il microfono preferito: {ex.Message}";
        }
    }

    private sealed record AudioDeviceSettings(string? DeviceName);

    private void CheckModelAvailability()
    {
        var path = ModelPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            ModelExists = false;
            ModelStatusText = "Modello Vosk non trovato nel percorso indicato.";
            Status = ModelStatusText;
            return;
        }

        string[] requiredFiles =
        [
            Path.Combine("am", "final.mdl"),
            Path.Combine("conf", "model.conf"),
            Path.Combine("graph", "Gr.fst"),
            Path.Combine("graph", "HCLr.fst")
        ];

        var missingFiles = requiredFiles.Where(file => !File.Exists(Path.Combine(path, file))).ToArray();
        if (missingFiles.Length > 0)
        {
            ModelExists = false;
            ModelStatusText = $"Modello incompleto: mancano {missingFiles.Length} file essenziali.";
            Status = ModelStatusText;
            return;
        }

        ModelExists = true;
        ModelStatusText = "Modello Vosk presente e struttura essenziale verificata.";
        Status = ModelStatusText;
    }


    public void AddCommand()
    {
        var command = new VoiceCommand("nuova frase", "save");
        Commands.Add(command);
        SelectedCommand = command;
    }

    public void RemoveSelectedCommand()
    {
        if (SelectedCommand is null) return;
        Commands.Remove(SelectedCommand);
        SelectedCommand = null;
    }

    public void SaveCommandConfiguration()
    {
        if (!ValidateCommands()) return;
        try
        {
            _commandSettings.Save(Commands);
            Status = "Comandi vocali salvati. Le modifiche saranno applicate al prossimo avvio dell'ascolto.";
            MessageBox.Show(Status, "Voice2Laser", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            Status = $"Impossibile salvare i comandi: {ex.Message}";
            MessageBox.Show(Status, "Voice2Laser", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool ValidateCommands()
    {
        var phrases = Commands.Select(command => command.Phrase.Trim()).ToList();
        if (phrases.Any(string.IsNullOrWhiteSpace))
        {
            Status = "Ogni comando deve avere una frase vocale.";
            MessageBox.Show(Status, "Comandi non validi", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (phrases.Distinct(StringComparer.OrdinalIgnoreCase).Count() != phrases.Count)
        {
            Status = "Ci sono frasi duplicate. Ogni frase deve essere univoca; puoi associare più frasi alla stessa azione.";
            MessageBox.Show(Status, "Comandi duplicati", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (Commands.Any(command => !AvailableActions.Any(action => action.Id == command.ActionId)))
        {
            Status = "Seleziona un'azione valida per ogni frase.";
            MessageBox.Show(Status, "Azione non valida", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        if (Commands.Count == 0)
        {
            Status = "Aggiungi almeno una frase vocale.";
            MessageBox.Show(Status, "Nessun comando", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        return true;
    }

    public void Start()
    {
        if (SelectedAudioDevice is null)
        {
            Status = "Seleziona un microfono prima di avviare l'ascolto.";
            MessageBox.Show(Status, "Microfono non selezionato", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        try
        {
            if (!ValidateCommands()) return;
            _speech.Start(ModelPath, SelectedAudioDevice.DeviceNumber, Commands.ToList());
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
        AudioLevelDb = -60.0;
        AudioPeakDb = -60.0;
        IsListening = false;
        Status = "Ascolto fermato";
    }

    private void OnAudioLevel(double rmsDb, double peakDb)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            AudioLevelDb = rmsDb;
            AudioPeakDb = peakDb;
        });
    }

    private void OnRecognition(string text, bool isFinal)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (isFinal)
            {
                PartialText = $"Riconosciuto: {text}";
                var matchedCommand = Commands.FirstOrDefault(command =>
                    string.Equals(command.Phrase.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase));
                var actionName = matchedCommand is null
                    ? "Non associato"
                    : CommandCatalog.GetActionName(matchedCommand.ActionId);

                if (matchedCommand is not null &&
                    _keyboardCommands.TryExecute(matchedCommand.ActionId, out var executionStatus))
                {
                    Status = executionStatus;
                    actionName = $"{actionName} — {executionStatus}";
                }

                Events.Insert(0, new RecognitionEvent(DateTime.Now, text, actionName));
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
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _deviceMonitor.Stop();
        _speech.Dispose();
    }
}