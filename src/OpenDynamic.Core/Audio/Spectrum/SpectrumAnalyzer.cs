using System;

namespace OpenDynamic.Core.Audio.Spectrum;

/// <summary>
/// High-performance, zero-heap-allocation audio spectrum analyzer in pure C#.
/// Implements an in-house iterative Cooley-Tukey Radix-2 FFT (N = 1024) with Hann windowing,
/// 50% overlap (512 hop size), logarithmic band grouping (12 Compact, 24 Expanded),
/// noise floor gating, perceptual dynamic range scaling, and fast-attack / slow-decay temporal smoothing.
/// Strictly fulfills Golden Rule 5 (isolated pure logic in Core) and Golden Rule 11 (0 per-frame allocations).
/// </summary>
public sealed class SpectrumAnalyzer
{
    public const int FftSize = 1024;
    public const int HopSize = 512; // 50% overlap
    public const int HalfFftSize = FftSize / 2; // 512 spectrum bins
    public const int CompactBandCount = 12;
    public const int ExpandedBandCount = 24;

    private readonly int _sampleRate;
    private readonly float _fastAttackRate;
    private readonly float _slowDecayRate;
    private readonly float _noiseFloorThreshold;

    // Precomputed tables (allocated once during construction)
    private readonly float[] _hannWindow = new float[FftSize];
    private readonly int[] _bitReversal = new int[FftSize];
    private readonly float[] _twiddleCos;
    private readonly float[] _twiddleSin;
    private readonly int[] _twiddleOffsets = new int[11]; // log2(1024) = 10 stages

    // Precalculated logarithmic band bin boundaries and perceptual weighting
    private readonly int[] _compactBandStartBins = new int[CompactBandCount];
    private readonly int[] _compactBandEndBins = new int[CompactBandCount];
    private readonly float[] _compactBandGain = new float[CompactBandCount];

    private readonly int[] _expandedBandStartBins = new int[ExpandedBandCount];
    private readonly int[] _expandedBandEndBins = new int[ExpandedBandCount];
    private readonly float[] _expandedBandGain = new float[ExpandedBandCount];

    // Reusable internal working buffers (Zero heap allocation per frame)
    private readonly float[] _fifoBuffer = new float[FftSize * 2];
    private int _fifoCount;
    private readonly float[] _real = new float[FftSize];
    private readonly float[] _imag = new float[FftSize];
    private readonly float[] _magnitudes = new float[HalfFftSize];

    private readonly float[] _compactBands = new float[CompactBandCount];
    private readonly float[] _compactSmoothed = new float[CompactBandCount];

    private readonly float[] _expandedBands = new float[ExpandedBandCount];
    private readonly float[] _expandedSmoothed = new float[ExpandedBandCount];

    /// <summary>
    /// Gets the current smoothed band levels for Compact display (12 bands, [0.0, 1.0]).
    /// </summary>
    public ReadOnlySpan<float> CompactBands => _compactSmoothed;

    /// <summary>
    /// Gets the current smoothed band levels for Expanded display (24 bands, [0.0, 1.0]).
    /// </summary>
    public ReadOnlySpan<float> ExpandedBands => _expandedSmoothed;

    /// <summary>
    /// Initializes a new instance of <see cref="SpectrumAnalyzer"/>.
    /// </summary>
    /// <param name="sampleRate">Audio sampling rate in Hz (default 48000 Hz).</param>
    /// <param name="fastAttackRate">Temporal smoothing attack rate for rising amplitudes (default 0.65f).</param>
    /// <param name="slowDecayRate">Temporal smoothing decay rate for falling amplitudes (default 0.85f).</param>
    /// <param name="noiseFloorThreshold">Linear magnitude noise floor gate (default 0.001f).</param>
    public SpectrumAnalyzer(
        int sampleRate = 48000,
        float fastAttackRate = 0.65f,
        float slowDecayRate = 0.85f,
        float noiseFloorThreshold = 0.001f)
    {
        _sampleRate = sampleRate > 0 ? sampleRate : 48000;
        _fastAttackRate = Math.Clamp(fastAttackRate, 0.05f, 1.0f);
        _slowDecayRate = Math.Clamp(slowDecayRate, 0.05f, 0.99f);
        _noiseFloorThreshold = Math.Max(0.00001f, noiseFloorThreshold);

        // 1. Precalculate Hann window: w[n] = 0.5 * (1 - cos(2*PI*n / (N - 1)))
        for (int i = 0; i < FftSize; i++)
        {
            _hannWindow[i] = 0.5f * (1.0f - MathF.Cos(2.0f * MathF.PI * i / (FftSize - 1)));
        }

        // 2. Precalculate 10-bit reversal lookup for N = 1024
        for (int i = 0; i < FftSize; i++)
        {
            int rev = 0;
            for (int bit = 0; bit < 10; bit++)
            {
                if ((i & (1 << bit)) != 0)
                {
                    rev |= (1 << (9 - bit));
                }
            }
            _bitReversal[i] = rev;
        }

        // 3. Precalculate Twiddle Factors for in-place Cooley-Tukey butterflies
        // Total twiddles needed: sum_{s=1..10} (2^(s-1)) = 1 + 2 + 4 + ... + 512 = 1023
        int totalTwiddles = FftSize - 1;
        _twiddleCos = new float[totalTwiddles];
        _twiddleSin = new float[totalTwiddles];

        int currentOffset = 0;
        for (int stage = 1; stage <= 10; stage++)
        {
            _twiddleOffsets[stage] = currentOffset;
            int len = 1 << stage;
            int halfLen = len >> 1;
            float angleStep = -2.0f * MathF.PI / len;

            for (int j = 0; j < halfLen; j++)
            {
                float angle = j * angleStep;
                _twiddleCos[currentOffset + j] = MathF.Cos(angle);
                _twiddleSin[currentOffset + j] = MathF.Sin(angle);
            }
            currentOffset += halfLen;
        }

        // 4. Precalculate Logarithmic Band Boundaries and Gains
        ComputeBandBoundaries(CompactBandCount, _compactBandStartBins, _compactBandEndBins, _compactBandGain);
        ComputeBandBoundaries(ExpandedBandCount, _expandedBandStartBins, _expandedBandEndBins, _expandedBandGain);
    }

    private void ComputeBandBoundaries(
        int bandCount,
        int[] startBins,
        int[] endBins,
        float[] gains)
    {
        // Acoustic range: 45 Hz (sub-bass) to 16000 Hz (high presence)
        float minFreq = 45.0f;
        float maxFreq = Math.Min(16500.0f, _sampleRate * 0.45f);
        float binWidth = (float)_sampleRate / FftSize;

        float logRatio = MathF.Pow(maxFreq / minFreq, 1.0f / bandCount);

        for (int i = 0; i < bandCount; i++)
        {
            float fStart = minFreq * MathF.Pow(logRatio, i);
            float fEnd = minFreq * MathF.Pow(logRatio, i + 1);

            int startBin = (int)MathF.Floor(fStart / binWidth);
            int endBin = (int)MathF.Ceiling(fEnd / binWidth);

            if (startBin < 1) startBin = 1;
            if (endBin < startBin + 1) endBin = startBin + 1;
            if (endBin >= HalfFftSize) endBin = HalfFftSize - 1;
            if (startBin >= endBin) startBin = Math.Max(1, endBin - 1);

            startBins[i] = startBin;
            endBins[i] = endBin;

            // Tilt compensation: human perception & musical spectrum slope (pink noise ~ -3dB/octave)
            // Equalize upper bands so treble isn't perpetually flat compared to sub-bass
            float bandProgress = (float)i / Math.Max(1, bandCount - 1);
            gains[i] = 1.0f + 2.5f * MathF.Pow(bandProgress, 1.2f);
        }
    }

    /// <summary>
    /// Feeds incoming mono PCM samples into the analyzer FIFO, running FFT analysis
    /// and updating band levels for every 512 samples with 50% overlap.
    /// Operates with ZERO heap allocations.
    /// </summary>
    /// <param name="samples">Read-only span of normalized mono audio samples [-1.0f, 1.0f].</param>
    public void AddSamples(ReadOnlySpan<float> samples)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        int inputOffset = 0;
        int remaining = samples.Length;

        while (remaining > 0)
        {
            int spaceInFifo = _fifoBuffer.Length - _fifoCount;
            if (spaceInFifo <= 0)
            {
                // Shift remaining back
                Array.Copy(_fifoBuffer, HopSize, _fifoBuffer, 0, _fifoCount - HopSize);
                _fifoCount -= HopSize;
                spaceInFifo = _fifoBuffer.Length - _fifoCount;
            }

            int toCopy = Math.Min(remaining, spaceInFifo);
            samples.Slice(inputOffset, toCopy).CopyTo(_fifoBuffer.AsSpan(_fifoCount, toCopy));
            _fifoCount += toCopy;
            inputOffset += toCopy;
            remaining -= toCopy;

            // While we have at least FftSize samples in FIFO, process a window
            while (_fifoCount >= FftSize)
            {
                ProcessWindow(_fifoBuffer.AsSpan(0, FftSize));

                // Slide forward by HopSize (50% overlap)
                int leftover = _fifoCount - HopSize;
                Array.Copy(_fifoBuffer, HopSize, _fifoBuffer, 0, leftover);
                _fifoCount = leftover;
            }
        }
    }

    /// <summary>
    /// Processes a single contiguous block of 1024 mono samples directly through the FFT.
    /// </summary>
    public void ProcessWindow(ReadOnlySpan<float> windowSamples)
    {
        if (windowSamples.Length < FftSize)
        {
            return;
        }

        // 1. Apply Hann window and bit-reversal reordering
        for (int i = 0; i < FftSize; i++)
        {
            int revIndex = _bitReversal[i];
            _real[revIndex] = windowSamples[i] * _hannWindow[i];
            _imag[revIndex] = 0.0f;
        }

        // 2. Iterative Cooley-Tukey Radix-2 FFT Butterflies
        for (int stage = 1; stage <= 10; stage++)
        {
            int len = 1 << stage;
            int halfLen = len >> 1;
            int twiddleBase = _twiddleOffsets[stage];

            for (int i = 0; i < FftSize; i += len)
            {
                for (int j = 0; j < halfLen; j++)
                {
                    float cos = _twiddleCos[twiddleBase + j];
                    float sin = _twiddleSin[twiddleBase + j];

                    int uIdx = i + j;
                    int vIdx = i + j + halfLen;

                    float uR = _real[uIdx];
                    float uI = _imag[uIdx];
                    float vR = _real[vIdx] * cos - _imag[vIdx] * sin;
                    float vI = _real[vIdx] * sin + _imag[vIdx] * cos;

                    _real[uIdx] = uR + vR;
                    _imag[uIdx] = uI + vI;
                    _real[vIdx] = uR - vR;
                    _imag[vIdx] = uI - vI;
                }
            }
        }

        // 3. Compute normalized magnitudes for positive half-spectrum (bins 0..511)
        float normFactor = 2.0f / FftSize;
        for (int k = 0; k < HalfFftSize; k++)
        {
            float r = _real[k];
            float im = _imag[k];
            _magnitudes[k] = MathF.Sqrt(r * r + im * im) * normFactor;
        }

        // 4. Aggregate and smooth logarithmic bands
        AggregateBands(CompactBandCount, _compactBandStartBins, _compactBandEndBins, _compactBandGain, _compactBands, _compactSmoothed);
        AggregateBands(ExpandedBandCount, _expandedBandStartBins, _expandedBandEndBins, _expandedBandGain, _expandedBands, _expandedSmoothed);
    }

    private void AggregateBands(
        int bandCount,
        int[] startBins,
        int[] endBins,
        float[] gains,
        float[] rawBands,
        float[] smoothedBands)
    {
        for (int b = 0; b < bandCount; b++)
        {
            int start = startBins[b];
            int end = endBins[b];

            float sum = 0.0f;
            float max = 0.0f;
            int count = end - start;

            for (int k = start; k < end; k++)
            {
                float mag = _magnitudes[k];
                sum += mag;
                if (mag > max) max = mag;
            }

            // Combine average and peak for punchy yet representative dynamics
            float bandMag = (count > 0 ? (sum / count) * 0.6f + max * 0.4f : max) * gains[b];

            // Noise floor gate
            if (bandMag < _noiseFloorThreshold)
            {
                bandMag = 0.0f;
            }

            // Perceptual dynamic range mapping (dB-like curve mapped to [0.0, 1.0])
            float targetLevel = 0.0f;
            if (bandMag > 0.0001f)
            {
                // 20*log10 mapping over 46 dB range from -46 dBFS to 0 dBFS
                float db = 20.0f * MathF.Log10(bandMag);
                targetLevel = Math.Clamp((db + 46.0f) / 46.0f, 0.0f, 1.0f);
            }

            rawBands[b] = targetLevel;

            // Temporal smoothing: Fast attack, slow decay
            float current = smoothedBands[b];
            if (targetLevel > current)
            {
                current += (targetLevel - current) * _fastAttackRate;
            }
            else
            {
                current *= _slowDecayRate;
                if (current < 0.001f)
                {
                    current = 0.0f;
                }
            }
            smoothedBands[b] = Math.Clamp(current, 0.0f, 1.0f);
        }
    }

    /// <summary>
    /// Smoothly decays all frequency bands towards zero.
    /// Invoked during audio silence or when loopback audio packets temporarily pause.
    /// Operates with ZERO heap allocations.
    /// </summary>
    public void DecayOnly()
    {
        for (int i = 0; i < CompactBandCount; i++)
        {
            float val = _compactSmoothed[i] * _slowDecayRate;
            _compactSmoothed[i] = val < 0.001f ? 0.0f : val;
        }

        for (int i = 0; i < ExpandedBandCount; i++)
        {
            float val = _expandedSmoothed[i] * _slowDecayRate;
            _expandedSmoothed[i] = val < 0.001f ? 0.0f : val;
        }
    }

    /// <summary>
    /// Resets all internal buffers and smoothed band levels back to zero.
    /// </summary>
    public void Reset()
    {
        _fifoCount = 0;
        Array.Clear(_fifoBuffer);
        Array.Clear(_real);
        Array.Clear(_imag);
        Array.Clear(_magnitudes);
        Array.Clear(_compactBands);
        Array.Clear(_compactSmoothed);
        Array.Clear(_expandedBands);
        Array.Clear(_expandedSmoothed);
    }

    /// <summary>
    /// Copies current compact bands (12 floats) into the destination span.
    /// </summary>
    public void GetCompactBands(Span<float> destination)
    {
        int count = Math.Min(CompactBandCount, destination.Length);
        _compactSmoothed.AsSpan(0, count).CopyTo(destination);
    }

    /// <summary>
    /// Copies current expanded bands (24 floats) into the destination span.
    /// </summary>
    public void GetExpandedBands(Span<float> destination)
    {
        int count = Math.Min(ExpandedBandCount, destination.Length);
        _expandedSmoothed.AsSpan(0, count).CopyTo(destination);
    }
}
