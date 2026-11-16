using System.Text.Json;
using NAudio.Wave;
using Vosk;

namespace Voice2Laser.Services;

/// <summary>Acquisizione continua mono 16 kHz e riconoscimento Vosk offline.</summary>
public sealed class VoskSpeechService : IDisposable
{
    private readonly object _sync = new();
    private Model? _model;
    private VoskRecognizer? _recognizer;
    private WaveInEvent? _waveIn;
    private bool _disposed;

    public event Action<string, bool>? RecognitionReceived;
    public event Action<string>? StatusChanged;

    public bool IsListening { get; private set; }

    public void Start(string modelPath, int deviceNumber = 0)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsListening) return;
        if (!Directory.Exists(modelPath))
            throw new DirectoryNotFoundException($"Cartella modello non trovata: {modelPath}");

        Vosk.Vosk.SetLogLevel(-1);
        _model = new Model(modelPath);
        _recognizer = new VoskRecognizer(_model, 16000.0f, CommandCatalog.GrammarJson);
        _recognizer.SetMaxAlternatives(0);

        try
        {
            _waveIn = new WaveInEvent
            {
                DeviceNumber = deviceNumber,
                WaveFormat = new WaveFormat(16000, 16, 1),
                BufferMilliseconds = 50,
                NumberOfBuffers = 3
            };
            _waveIn.DataAvailable += OnDataAvailable;
            _waveIn.RecordingStopped += OnRecordingStopped;
            _waveIn.StartRecording();
            IsListening = true;
            StatusChanged?.Invoke("Ascolto attivo – riconoscimento offline");
        }
        catch
        {
            CleanupAudio();
            _recognizer?.Dispose();
            _recognizer = null;
            _model?.Dispose();
            _model = null;
            throw;
        }
    }

    public void Stop()
    {
        if (!IsListening && _waveIn is null) return;
        IsListening = false;
        try { _waveIn?.StopRecording(); }
        catch (InvalidOperationException) { }
        CleanupAudio();

        lock (_sync)
        {
            _recognizer?.Dispose();
            _recognizer = null;
            _model?.Dispose();
            _model = null;
        }
        StatusChanged?.Invoke("Ascolto fermato");
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_sync)
        {
            if (!IsListening || _recognizer is null) return;
            if (_recognizer.AcceptWaveform(e.Buffer, e.BytesRecorded))
            {
                var text = ReadText(_recognizer.Result());
                if (!string.IsNullOrWhiteSpace(text) && text != "[unk]")
                    RecognitionReceived?.Invoke(text, true);
            }
            else
            {
                var text = ReadText(_recognizer.PartialResult());
                if (!string.IsNullOrWhiteSpace(text))
                    RecognitionReceived?.Invoke(text, false);
            }
        }
    }

    private static string ReadText(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("text", out var finalValue))
                return finalValue.GetString() ?? string.Empty;
            if (doc.RootElement.TryGetProperty("partial", out var partialValue))
                return partialValue.GetString() ?? string.Empty;
            return string.Empty;
        }
        catch (JsonException) { return string.Empty; }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception is not null)
            StatusChanged?.Invoke($"Errore audio: {e.Exception.Message}");
    }

    private void CleanupAudio()
    {
        if (_waveIn is null) return;
        _waveIn.DataAvailable -= OnDataAvailable;
        _waveIn.RecordingStopped -= OnRecordingStopped;
        _waveIn.Dispose();
        _waveIn = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();
        _disposed = true;
    }
}
