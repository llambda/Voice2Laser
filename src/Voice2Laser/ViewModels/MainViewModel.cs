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
    private readonly KeyboardCommandService _keyboardCommands = new();
    private readonly DispatcherTimer _deviceMonitor;
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voice2Laser", "audio-device.json");
    private readonly string _voiceSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voice2Laser", "voice-settings.json");
    private readonly string _modelSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Voice2Laser", "model-settings.json");
    private DispatcherTimer? _calibrationTimer;
    private readonly List<double> _calibrationSamples = [];

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
    private bool _ezCadConnected;
    private string _ezCadStatus = "Verifica non ancora eseguita.";
    private readonly Dictionary<string, DateTime> _lastRecognizedActionTimes = new(StringComparer.Ordinal);
    private double _voiceThresholdDb = -42.0;
    private double _minimumConfidence = 0.55;
    private int _duplicateCommandWindowMs = 1200;

    public ObservableCollection<RecognitionEvent> Events { get; } = [];
    public ObservableCollection<AudioInputDevice> AudioInputDevices { get; } = [];
    public ObservableCollection<VoiceCommand> Commands { get; } = new(CommandCatalog.CreateDefaults());
    public bool EzCadConnected { get => _ezCadConnected; private set => Set(ref _ezCadConnected, value); }
    public string EzCadStatus { get => _ezCadStatus; private set => Set(ref _ezCadStatus, value); }

    // Rimane disattivato a ogni avvio: l'invio reale richiede un'attivazione esplicita.
    private bool _isLiveInputEnabled;
    public bool IsLiveInputEnabled
    {
        get => _isLiveInputEnabled;
        set
        {
            if (_isLiveInputEnabled == value) return;
            _isLiveInputEnabled = value;
            OnPropertyChanged();
            Status = value
                ? "INVIO REALE ATTIVO: i comandi riconosciuti possono agire su EZCAD2."
                : "Modalità test attiva: nessun tasto viene inviato.";
        }
    }

    public string Status { get => _status; private set => Set(ref _status, value); }

    public double VoiceThresholdDb
    {
        get => _voiceThresholdDb;
        set
        {
            var adjusted = Math.Clamp(value, -55.0, -25.0);
            if (!Set(ref _voiceThresholdDb, adjusted)) return;
            _speech.VoiceActivationThresholdDb = adjusted;
            SaveVoiceSettings();
        }
    }

    public double MinimumConfidence
    {
        get => _minimumConfidence;
        set
        {
            var adjusted = Math.Clamp(value, 0.30, 0.95);
            if (!Set(ref _minimumConfidence, adjusted)) return;
            SaveVoiceSettings();
        }
    }

    public int DuplicateCommandWindowMs
    {
        get => _duplicateCommandWindowMs;
        set
        {
            var adjusted = Math.Clamp(value, 500, 3000);
            if (!Set(ref _duplicateCommandWindowMs, adjusted)) return;
            SaveVoiceSettings();
        }
    }
    public string ModelPath
    {
        get => _modelPath;
        set
        {
            if (string.Equals(_modelPath, value, StringComparison.Ordinal)) return;
            _modelPath = value;
            OnPropertyChanged();
            CheckModelAvailability();
            SaveModelPath();
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
            OnPropertyChanged(nameof(CanSelectModel));
        }
    }
    public bool CanSelectAudioDevice => !IsListening;
    public bool CanSelectModel => !IsListening;
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
            if (EqualityComparer<AudioInputDevice?>.Default.Equals(_selectedAudioDevice, value))
                return;

            Set(ref _selectedAudioDevice, value);

            if (!_isRefreshingDevices && value is not null)
            {
                _preferredAudioDeviceName = value.Name;
                SavePreferredAudioDevice();
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        LoadModelPath();
        LoadPreferredAudioDevice();
        LoadVoiceSettings();
        _speech.VoiceActivationThresholdDb = _voiceThresholdDb;
        _speech.RecognitionReceived += OnRecognition;
        _speech.AudioLevelChanged += OnAudioLevel;
        _speech.StatusChanged += message => Application.Current.Dispatcher.BeginInvoke(() => Status = message);

        CheckModelAvailability();
        CheckEzCadConnection();
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
            {
                Application.Current.Dispatcher.BeginInvoke(() =>
                    Status = "Microfono scollegato. Ascolto fermato; ricollega le cuffie e aggiorna la selezione.");
            }
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

    private sealed record VoiceSettings(double VoiceThresholdDb, double MinimumConfidence, int DuplicateCommandWindowMs);

    private void LoadVoiceSettings()
    {
        try
        {
            if (!File.Exists(_voiceSettingsPath)) return;
            var settings = JsonSerializer.Deserialize<VoiceSettings>(File.ReadAllText(_voiceSettingsPath));
            if (settings is null) return;
            _voiceThresholdDb = Math.Clamp(settings.VoiceThresholdDb, -55.0, -25.0);
            _minimumConfidence = Math.Clamp(settings.MinimumConfidence, 0.30, 0.95);
            _duplicateCommandWindowMs = Math.Clamp(settings.DuplicateCommandWindowMs, 500, 3000);
        }
        catch
        {
            // Se il file è danneggiato, vengono mantenuti i valori predefiniti.
        }
    }

    private void SaveVoiceSettings()
    {
        try
        {
            var directory = Path.GetDirectoryName(_voiceSettingsPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            var settings = new VoiceSettings(_voiceThresholdDb, _minimumConfidence, _duplicateCommandWindowMs);
            File.WriteAllText(_voiceSettingsPath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Status = $"Impossibile salvare le impostazioni: {ex.Message}";
        }
    }

    public void StartCalibration()
    {
        if (!IsListening)
        {
            Status = "Avvia l'ascolto prima di calibrare il rumore ambientale.";
            return;
        }

        _calibrationTimer?.Stop();
        _calibrationSamples.Clear();
        Status = "Calibrazione: resta in silenzio per 5 secondi.";
        _calibrationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _calibrationTimer.Tick += (_, _) =>
        {
            _calibrationTimer.Stop();
            if (_calibrationSamples.Count < 10)
            {
                Status = "Calibrazione non riuscita: pochi campioni audio.";
                return;
            }

            var ordered = _calibrationSamples.OrderBy(value => value).ToArray();
            var medianNoise = ordered[ordered.Length / 2];
            VoiceThresholdDb = Math.Clamp(medianNoise + 10.0, -55.0, -25.0);
            Status = $"Calibrazione completata. Rumore mediano {medianNoise:0} dBFS; soglia impostata a {VoiceThresholdDb:0} dBFS.";
        };
        _calibrationTimer.Start();
    }

    private void LoadModelPath()
    {
        try
        {
            if (!File.Exists(_modelSettingsPath)) return;
            var settings = JsonSerializer.Deserialize<ModelPathSettings>(File.ReadAllText(_modelSettingsPath));
            if (!string.IsNullOrWhiteSpace(settings?.Path))
                _modelPath = settings.Path;
        }
        catch
        {
            // Mantiene il percorso predefinito se le impostazioni non sono leggibili.
        }
    }

    private void SaveModelPath()
    {
        try
        {
            var directory = Path.GetDirectoryName(_modelSettingsPath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            File.WriteAllText(_modelSettingsPath,
                JsonSerializer.Serialize(new ModelPathSettings(_modelPath), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            Status = $"Impossibile salvare il modello selezionato: {ex.Message}";
        }
    }

    private sealed record ModelPathSettings(string Path);

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


    public void CheckEzCadConnection()
    {
        EzCadConnected = _keyboardCommands.CheckConnection(out var message);
        EzCadStatus = message;
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
        _calibrationTimer?.Stop();
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
            if (_calibrationTimer?.IsEnabled == true)
                _calibrationSamples.Add(rmsDb);
        });
    }

    private void OnRecognition(string text, bool isFinal, double confidence)
    {
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (!isFinal)
            {
                PartialText = $"In ascolto: {text}";
                return;
            }

            PartialText = $"Riconosciuto: {text} ({confidence:P0})";
            var matchedCommand = Commands.FirstOrDefault(command =>
                string.Equals(command.Phrase.Trim(), text.Trim(), StringComparison.OrdinalIgnoreCase));
            var actionName = matchedCommand is null
                ? "Non associato"
                : CommandCatalog.GetActionName(matchedCommand.ActionId);

            if (matchedCommand is not null)
            {
                if (confidence < MinimumConfidence)
                {
                    actionName = $"{actionName} — confidenza insufficiente ({confidence:P0}), ignorato";
                    Status = $"Comando ignorato: confidenza {confidence:P0}, minimo richiesto {MinimumConfidence:P0}.";
                }
                else
                {
                    var now = DateTime.UtcNow;
                    if (_lastRecognizedActionTimes.TryGetValue(matchedCommand.ActionId, out var lastTime) &&
                        now - lastTime < TimeSpan.FromMilliseconds(DuplicateCommandWindowMs))
                    {
                        actionName = $"{actionName} — ripetizione ravvicinata ignorata";
                    }
                    else
                    {
                        _lastRecognizedActionTimes[matchedCommand.ActionId] = now;

                        if (IsLiveInputEnabled)
                        {
                            _keyboardCommands.TryExecute(matchedCommand.ActionId, out var sendMessage);
                            Status = sendMessage;
                            actionName = $"{actionName} — {sendMessage}";
                        }
                        else
                        {
                            var connectionOk = _keyboardCommands.CheckConnection(out _);
                            var keyDescription = KeyboardCommandService.GetKeyDescription(matchedCommand.ActionId);
                            var targetStatus = connectionOk ? "EZCAD2 rilevato" : "EZCAD2 non rilevato";
                            var testMessage = $"TEST: sarebbe inviato {keyDescription} — {targetStatus}. Nessun tasto inviato.";
                            Status = testMessage;
                            actionName = $"{actionName} — {testMessage}";
                        }
                    }
                }
            }

            Events.Insert(0, new RecognitionEvent(DateTime.Now, text, actionName));
            while (Events.Count > 200) Events.RemoveAt(Events.Count - 1);
        });
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _deviceMonitor.Stop();
        _calibrationTimer?.Stop();
        _speech.Dispose();
    }
}