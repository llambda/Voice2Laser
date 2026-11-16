using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using NAudio.Wave;
using Voice2Laser.Models;
using Voice2Laser.Services;

namespace Voice2Laser.ViewModels;

public sealed record AudioInputDevice(int DeviceNumber, string Name);

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly VoskSpeechService _speech = new();
    private string _status = "Pronto";
    private string _modelPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "models", "vosk-model-small-it-0.22"));
    private string _partialText = "In attesa di riconoscimento";
    private string _modelStatusText = "Percorso del modello non ancora verificato.";
    private bool? _modelExists;
    private bool _isListening;
    private AudioInputDevice? _selectedAudioDevice;
    private double _audioLevelDb = -60.0;
    private double _audioPeakDb = -60.0;

    public ObservableCollection<RecognitionEvent> Events { get; } = [];
    public ObservableCollection<AudioInputDevice> AudioInputDevices { get; } = [];

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
        set => Set(ref _selectedAudioDevice, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainViewModel()
    {
        _speech.RecognitionReceived += OnRecognition;
        _speech.AudioLevelChanged += OnAudioLevel;
        _speech.StatusChanged += message => Application.Current.Dispatcher.Invoke(() => Status = message);
        CheckModelAvailability();
        RefreshAudioDevices();
    }

    public void RefreshAudioDevices()
    {
        var previousDeviceNumber = SelectedAudioDevice?.DeviceNumber;
        AudioInputDevices.Clear();

        try
        {
            for (var i = 0; i < WaveIn.DeviceCount; i++)
            {
                var capabilities = WaveIn.GetCapabilities(i);
                AudioInputDevices.Add(new AudioInputDevice(i, capabilities.ProductName));
            }

            SelectedAudioDevice = AudioInputDevices.FirstOrDefault(d => d.DeviceNumber == previousDeviceNumber)
                                  ?? AudioInputDevices.FirstOrDefault();

            if (AudioInputDevices.Count == 0)
                Status = "Nessun microfono rilevato. Collega un dispositivo e aggiorna l'elenco.";
            else
                Status = $"Microfoni rilevati: {AudioInputDevices.Count}. Seleziona quello da utilizzare.";
        }
        catch (Exception ex)
        {
            Status = $"Errore durante la ricerca dei microfoni: {ex.Message}";
        }
    }

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

        var missingFiles = requiredFiles
            .Where(file => !File.Exists(Path.Combine(path, file)))
            .ToArray();

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
            _speech.Start(ModelPath, SelectedAudioDevice.DeviceNumber);
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
        OnPropertyChanged(name);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose() => _speech.Dispose();
}