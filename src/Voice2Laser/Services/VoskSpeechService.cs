using System;
using System.IO;
using System.Text.Json;
using NAudio.Wave;
using Vosk;
using Voice2Laser.Models;

namespace Voice2Laser.Services;

/// <summary>Acquisizione continua mono 16 kHz e riconoscimento Vosk offline.</summary>
public sealed class VoskSpeechService : IDisposable
{
    private readonly object _sync = new();
    private Model? _model;
    private VoskRecognizer? _recognizer;
    private WaveInEvent? _waveIn;
    private bool _disposed;
    private int _voiceHangoverFrames;
    private double _voiceActivationThresholdDb = -42.0;
    private double _minimumRecognitionConfidence = 0.55;

    // Valori regolabili e persistiti dal ViewModel.
    private const int VoiceHangoverFrameCount = 5; // 5 x 50 ms = 250 ms

    public event Action<string, bool, double>? RecognitionReceived;

    public double VoiceActivationThresholdDb
    {
        get { lock (_sync) return _voiceActivationThresholdDb; }
        set { lock (_sync) _voiceActivationThresholdDb = Math.Clamp(value, -60.0, -20.0); }
    }

    public double MinimumRecognitionConfidence
    {
        get { lock (_sync) return _minimumRecognitionConfidence; }
        set { lock (_sync) _minimumRecognitionConfidence = Math.Clamp(value, 0.0, 1.0); }
    }
    public event Action<string>? StatusChanged;
    public event Action<double, double>? AudioLevelChanged;

    public bool IsListening { get; private set; }

    public void Start(string modelPath, int deviceNumber = 0, IReadOnlyList<VoiceCommand>? commands = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsListening) return;
        _voiceHangoverFrames = 0;
        if (!Directory.Exists(modelPath))
            throw new DirectoryNotFoundException($"Cartella modello non trovata: {modelPath}");

        Vosk.Vosk.SetLogLevel(-1);
        _model = new Model(modelPath);
        _recognizer = new VoskRecognizer(_model, 16000.0f, CommandCatalog.BuildGrammar(commands ?? CommandCatalog.CreateDefaults()));
        _recognizer.SetMaxAlternatives(0);
        _recognizer.SetWords(true);

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
        _voiceHangoverFrames = 0;
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
        AudioLevelChanged?.Invoke(-60.0, -60.0);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        var levels = CalculateAudioLevelsDb(e.Buffer, e.BytesRecorded);
        AudioLevelChanged?.Invoke(levels.RmsDb, levels.PeakDb);

        lock (_sync)
        {
            if (!IsListening || _recognizer is null) return;

            // Apre il gate sopra soglia e lo mantiene per 250 ms per non troncare
            // le code delle parole. Nei periodi di rumore debole invia silenzio a Vosk.
            var voiceDetected = levels.RmsDb >= _voiceActivationThresholdDb;
            bool keepAudioFrame;
            if (voiceDetected)
            {
                _voiceHangoverFrames = VoiceHangoverFrameCount;
                keepAudioFrame = true;
            }
            else if (_voiceHangoverFrames > 0)
            {
                keepAudioFrame = true;
                _voiceHangoverFrames--;
            }
            else
            {
                keepAudioFrame = false;
            }

            var audioBuffer = keepAudioFrame ? e.Buffer : new byte[e.BytesRecorded];

            if (_recognizer.AcceptWaveform(audioBuffer, e.BytesRecorded))
            {
                var resultJson = _recognizer.Result();
                var text = ReadText(resultJson);
                if (!string.IsNullOrWhiteSpace(text) && text != "[unk]")
                    RecognitionReceived?.Invoke(text, true, ReadConfidence(resultJson));
            }
            else
            {
                var text = ReadText(_recognizer.PartialResult());
                if (!string.IsNullOrWhiteSpace(text))
                    RecognitionReceived?.Invoke(text, false, 1.0);
            }
        }
    }

    private static (double RmsDb, double PeakDb) CalculateAudioLevelsDb(byte[] buffer, int bytesRecorded)
    {
        if (bytesRecorded < 2) return (-60.0, -60.0);
        double sumSquares = 0;
        double peak = 0;
        var sampleCount = bytesRecorded / 2;

        for (var i = 0; i < sampleCount; i++)
        {
            var sample = BitConverter.ToInt16(buffer, i * 2) / 32768.0;
            var absoluteSample = Math.Abs(sample);
            sumSquares += sample * sample;
            if (absoluteSample > peak) peak = absoluteSample;
        }

        var rms = Math.Sqrt(sumSquares / sampleCount);
        var rmsDb = rms <= 0.000001 ? -60.0 : Math.Max(-60.0, 20.0 * Math.Log10(rms));
        var peakDb = peak <= 0.000001 ? -60.0 : Math.Max(-60.0, 20.0 * Math.Log10(peak));
        return (rmsDb, peakDb);
    }

    private static double ReadConfidence(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("result", out var words) || words.ValueKind != JsonValueKind.Array)
                return 0.0;

            var confidences = words.EnumerateArray()
                .Where(word => word.TryGetProperty("conf", out var confidence) && confidence.TryGetDouble(out _))
                .Select(word => { word.TryGetProperty("conf", out var confidence); return confidence.GetDouble(); })
                .ToArray();
            return confidences.Length == 0 ? 0.0 : confidences.Average();
        }
        catch (JsonException) { return 0.0; }
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
        IsListening = false;
        if (e.Exception is not null)
            StatusChanged?.Invoke($"Errore audio: {e.Exception.Message}. Ascolto interrotto.");
        else
            StatusChanged?.Invoke("Acquisizione audio terminata.");
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
