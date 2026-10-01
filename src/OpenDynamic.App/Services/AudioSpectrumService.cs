using System;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using OpenDynamic.Core.Audio;
using OpenDynamic.Core.Audio.Spectrum;
using OpenDynamic.Core.Settings;
using Serilog;

#pragma warning disable CS0618 // WasapiLoopbackCapture required by specification

namespace OpenDynamic.App.Services;

/// <summary>
/// High-performance system audio spectrum analyzer service using NAudio WASAPI Loopback Capture.
/// Implements resilient lifecycle management conforming strictly to:
/// - Golden Rule 1: 0% CPU at rest; capture and processing start and stop immediately per <see cref="VisualizerActivationPolicy"/>.
/// - Golden Rule 4: Isolated fault handling; COM exceptions degrade cleanly to Simulated mode without application crashing.
/// - Golden Rule 10: Strict in-memory PCM processing; audio samples are never persisted to disk, temp files, or logs.
/// - Golden Rule 11: Zero heap allocations per frame via preallocated buffers and lock-free double buffering.
/// </summary>
public sealed class AudioSpectrumService : IAudioSpectrumService
{
    private readonly object _captureLock = new();
    private readonly IVolumeController _volumeController;
    private readonly AppSettings _settings;

    private SpectrumAnalyzer _analyzer;
    private WasapiLoopbackCapture? _capture;
    private bool _isCapturing;
    private bool _fallbackToSimulated;
    private AudioVisualizerMode _activeMode = AudioVisualizerMode.Real;

    private long _lastDataTimestamp;
    private float[] _monoBuffer = new float[4096];

    // Lock-free double buffers for publishing analyzed bands to UI / render thread
    private readonly float[][] _compactDoubleBuffer =
    [
        new float[SpectrumAnalyzer.CompactBandCount],
        new float[SpectrumAnalyzer.CompactBandCount]
    ];

    private readonly float[][] _expandedDoubleBuffer =
    [
        new float[SpectrumAnalyzer.ExpandedBandCount],
        new float[SpectrumAnalyzer.ExpandedBandCount]
    ];

    private int _activeBufferIndex;
    private bool _disposed;

    /// <inheritdoc />
    public AudioVisualizerMode ActiveMode => _fallbackToSimulated ? AudioVisualizerMode.Simulated : _activeMode;

    /// <inheritdoc />
    public bool IsCapturing
    {
        get
        {
            lock (_captureLock)
            {
                return _isCapturing;
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler<AudioVisualizerMode>? ModeDegraded;

    public AudioSpectrumService(IVolumeController volumeController, AppSettings settings)
    {
        _volumeController = volumeController ?? throw new ArgumentNullException(nameof(volumeController));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _analyzer = new SpectrumAnalyzer(sampleRate: 48000);
        _activeMode = _settings.VisualizerMode;

        // Re-hook default audio endpoint dynamically upon hot device switching
        _volumeController.DefaultDeviceChanged += OnDefaultDeviceChanged;
    }

    /// <inheritdoc />
    public void UpdateActivation(in VisualizerActivationContext context)
    {
        _activeMode = context.Mode;

        // Reset fallback flag if user explicitly switched mode in settings
        if (context.Mode != AudioVisualizerMode.Real)
        {
            _fallbackToSimulated = false;
        }

        bool shouldCapture = !_fallbackToSimulated && VisualizerActivationPolicy.ShouldCapture(in context);

        lock (_captureLock)
        {
            if (shouldCapture && !_isCapturing)
            {
                StartCapture_NoLock();
            }
            else if (!shouldCapture && _isCapturing)
            {
                StopCapture_NoLock();
            }
        }
    }

    private void StartCapture_NoLock()
    {
        if (_isCapturing || _disposed)
        {
            return;
        }

        try
        {
            Log.Debug("Starting WASAPI audio loopback capture for spectrum visualizer.");
            _capture = new WasapiLoopbackCapture();

            int sampleRate = _capture.WaveFormat.SampleRate;
            if (sampleRate > 0)
            {
                _analyzer = new SpectrumAnalyzer(sampleRate: sampleRate);
            }

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += OnRecordingStopped;

            _capture.StartRecording();
            _isCapturing = true;
            Interlocked.Exchange(ref _lastDataTimestamp, Environment.TickCount64);

            Log.Information("WASAPI audio loopback capture started successfully (SampleRate: {SampleRate} Hz, Channels: {Channels}).",
                _capture.WaveFormat.SampleRate, _capture.WaveFormat.Channels);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to initialize WASAPI loopback capture. Gracefully degrading to Simulated mode.");
            DegradeToSimulated_NoLock();
        }
    }

    private void StopCapture_NoLock()
    {
        if (!_isCapturing && _capture == null)
        {
            return;
        }

        try
        {
            Log.Debug("Stopping and disposing WASAPI audio loopback capture.");
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                _capture.RecordingStopped -= OnRecordingStopped;
                try
                {
                    _capture.StopRecording();
                }
                catch (Exception ex)
                {
                    Log.Debug(ex, "Ignored exception while stopping WASAPI loopback capture.");
                }
                finally
                {
                    _capture.Dispose();
                    _capture = null;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error occurred during WASAPI loopback release.");
        }
        finally
        {
            _isCapturing = false;
            _analyzer.Reset();
            ClearDoubleBuffers();
        }
    }

    private void DegradeToSimulated_NoLock()
    {
        StopCapture_NoLock();
        _fallbackToSimulated = true;
        ModeDegraded?.Invoke(this, AudioVisualizerMode.Simulated);
    }

    private void OnDefaultDeviceChanged(object? sender, EventArgs e)
    {
        lock (_captureLock)
        {
            if (_isCapturing)
            {
                Log.Information("Default audio endpoint changed. Re-initializing WASAPI loopback capture.");
                StopCapture_NoLock();
                StartCapture_NoLock();
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        if (e.Exception != null)
        {
            Log.Warning(e.Exception, "WASAPI loopback recording stopped due to device/COM error.");
            lock (_captureLock)
            {
                DegradeToSimulated_NoLock();
            }
        }
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0 || _disposed)
        {
            return;
        }

        Interlocked.Exchange(ref _lastDataTimestamp, Environment.TickCount64);

        var format = _capture?.WaveFormat;
        if (format == null)
        {
            return;
        }

        int channels = format.Channels;
        if (channels <= 0)
        {
            return;
        }

        try
        {
            if (format.Encoding == WaveFormatEncoding.IeeeFloat && format.BitsPerSample == 32)
            {
                var floatSpan = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
                int frameCount = floatSpan.Length / channels;
                EnsureMonoCapacity(frameCount);

                if (channels == 2)
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        int baseIdx = i * 2;
                        _monoBuffer[i] = (floatSpan[baseIdx] + floatSpan[baseIdx + 1]) * 0.5f;
                    }
                }
                else if (channels == 1)
                {
                    floatSpan.Slice(0, frameCount).CopyTo(_monoBuffer.AsSpan(0, frameCount));
                }
                else
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        float sum = 0.0f;
                        int baseIdx = i * channels;
                        for (int ch = 0; ch < channels; ch++)
                        {
                            sum += floatSpan[baseIdx + ch];
                        }
                        _monoBuffer[i] = sum / channels;
                    }
                }

                _analyzer.AddSamples(_monoBuffer.AsSpan(0, frameCount));
            }
            else if (format.Encoding == WaveFormatEncoding.Pcm && format.BitsPerSample == 16)
            {
                var shortSpan = MemoryMarshal.Cast<byte, short>(e.Buffer.AsSpan(0, e.BytesRecorded));
                int frameCount = shortSpan.Length / channels;
                EnsureMonoCapacity(frameCount);

                const float invScale = 1.0f / 32768.0f;
                if (channels == 2)
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        int baseIdx = i * 2;
                        _monoBuffer[i] = (shortSpan[baseIdx] + shortSpan[baseIdx + 1]) * 0.5f * invScale;
                    }
                }
                else
                {
                    for (int i = 0; i < frameCount; i++)
                    {
                        _monoBuffer[i] = shortSpan[i * channels] * invScale;
                    }
                }

                _analyzer.AddSamples(_monoBuffer.AsSpan(0, frameCount));
            }

            // Publish to double buffer with atomic swap
            PublishDoubleBuffer();
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Error processing audio loopback buffer.");
        }
    }

    private void EnsureMonoCapacity(int requiredFrames)
    {
        if (_monoBuffer.Length < requiredFrames)
        {
            _monoBuffer = new float[Math.Max(requiredFrames, _monoBuffer.Length * 2)];
        }
    }

    private void PublishDoubleBuffer()
    {
        int currentActive = Volatile.Read(ref _activeBufferIndex);
        int writeIdx = 1 - currentActive;
        _analyzer.GetCompactBands(_compactDoubleBuffer[writeIdx]);
        _analyzer.GetExpandedBands(_expandedDoubleBuffer[writeIdx]);

        Interlocked.Exchange(ref _lastDataTimestamp, Environment.TickCount64);
        Interlocked.Exchange(ref _activeBufferIndex, writeIdx);
    }

    /// <inheritdoc />
    public void GetCompactBands(Span<float> destination)
    {
        var currentMode = ActiveMode;
        if (currentMode == AudioVisualizerMode.Disabled)
        {
            destination.Clear();
            return;
        }

        if (currentMode == AudioVisualizerMode.Simulated)
        {
            GenerateSimulatedBands(destination, SpectrumAnalyzer.CompactBandCount);
            return;
        }

        long lastData = Interlocked.Read(ref _lastDataTimestamp);
        bool isSilent = _isCapturing && (Environment.TickCount64 - lastData > 60);

        int readIdx = Volatile.Read(ref _activeBufferIndex);
        int count = Math.Min(SpectrumAnalyzer.CompactBandCount, destination.Length);
        var source = _compactDoubleBuffer[readIdx];

        if (isSilent)
        {
            for (int i = 0; i < count; i++)
            {
                source[i] *= 0.85f;
                if (source[i] < 0.001f) source[i] = 0.0f;
                destination[i] = source[i];
            }
        }
        else
        {
            source.AsSpan(0, count).CopyTo(destination);
        }
    }

    /// <inheritdoc />
    public void GetExpandedBands(Span<float> destination)
    {
        var currentMode = ActiveMode;
        if (currentMode == AudioVisualizerMode.Disabled)
        {
            destination.Clear();
            return;
        }

        if (currentMode == AudioVisualizerMode.Simulated)
        {
            GenerateSimulatedBands(destination, SpectrumAnalyzer.ExpandedBandCount);
            return;
        }

        long lastData = Interlocked.Read(ref _lastDataTimestamp);
        bool isSilent = _isCapturing && (Environment.TickCount64 - lastData > 60);

        int readIdx = Volatile.Read(ref _activeBufferIndex);
        int count = Math.Min(SpectrumAnalyzer.ExpandedBandCount, destination.Length);
        var source = _expandedDoubleBuffer[readIdx];

        if (isSilent)
        {
            for (int i = 0; i < count; i++)
            {
                source[i] *= 0.85f;
                if (source[i] < 0.001f) source[i] = 0.0f;
                destination[i] = source[i];
            }
        }
        else
        {
            source.AsSpan(0, count).CopyTo(destination);
        }
    }

    private static void GenerateSimulatedBands(Span<float> destination, int totalBands)
    {
        // Smooth, organic procedural oscillation based on system tick
        float time = (float)(Environment.TickCount64 % 100000) * 0.006f;
        int count = Math.Min(totalBands, destination.Length);

        for (int i = 0; i < count; i++)
        {
            float wave1 = MathF.Sin(time * 2.8f + i * 0.75f) * 0.35f;
            float wave2 = MathF.Cos(time * 1.9f + i * 1.2f) * 0.25f;
            float wave3 = MathF.Sin(time * 4.3f + i * 0.4f) * 0.15f;

            float val = Math.Clamp(0.35f + wave1 + wave2 + wave3, 0.05f, 0.95f);
            destination[i] = val;
        }
    }

    private void ClearDoubleBuffers()
    {
        Array.Clear(_compactDoubleBuffer[0]);
        Array.Clear(_compactDoubleBuffer[1]);
        Array.Clear(_expandedDoubleBuffer[0]);
        Array.Clear(_expandedDoubleBuffer[1]);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _volumeController.DefaultDeviceChanged -= OnDefaultDeviceChanged;

        lock (_captureLock)
        {
            StopCapture_NoLock();
        }
    }
}
