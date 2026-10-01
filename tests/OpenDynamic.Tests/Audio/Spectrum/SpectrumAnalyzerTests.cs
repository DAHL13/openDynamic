using System;
using OpenDynamic.Core.Audio.Spectrum;
using Xunit;

namespace OpenDynamic.Tests.Audio.Spectrum;

public class SpectrumAnalyzerTests
{
    [Fact]
    public void Silence_ProducesZeroBands()
    {
        var analyzer = new SpectrumAnalyzer(sampleRate: 48000);
        float[] silence = new float[2048]; // 2 windows of silence

        analyzer.AddSamples(silence);

        Span<float> compact = stackalloc float[SpectrumAnalyzer.CompactBandCount];
        analyzer.GetCompactBands(compact);

        for (int i = 0; i < compact.Length; i++)
        {
            Assert.Equal(0.0f, compact[i]);
        }

        Span<float> expanded = stackalloc float[SpectrumAnalyzer.ExpandedBandCount];
        analyzer.GetExpandedBands(expanded);

        for (int i = 0; i < expanded.Length; i++)
        {
            Assert.Equal(0.0f, expanded[i]);
        }
    }

    [Fact]
    public void PureSineWave_ConcentratesEnergyInExpectedBand()
    {
        int sampleRate = 48000;
        var analyzer = new SpectrumAnalyzer(sampleRate: sampleRate);

        // Generate 1000 Hz pure sine wave
        float targetFreq = 1000.0f;
        float[] samples = new float[2048];
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = 0.8f * MathF.Sin(2.0f * MathF.PI * targetFreq * i / sampleRate);
        }

        analyzer.AddSamples(samples);

        Span<float> compact = stackalloc float[SpectrumAnalyzer.CompactBandCount];
        analyzer.GetCompactBands(compact);

        // Find band with maximum energy
        int maxIndex = -1;
        float maxVal = 0.0f;
        for (int i = 0; i < compact.Length; i++)
        {
            if (compact[i] > maxVal)
            {
                maxVal = compact[i];
                maxIndex = i;
            }
        }

        // Band covering 1000 Hz in a 12-band log scale (45 Hz to 16000 Hz)
        // Log ratio per band: (16000/45)^(1/12) ~ 1.63
        // Band 0: 45..73 Hz
        // Band 1: 73..119 Hz
        // Band 2: 119..194 Hz
        // Band 3: 194..316 Hz
        // Band 4: 316..516 Hz
        // Band 5: 516..841 Hz
        // Band 6: 841..1370 Hz  <-- 1000 Hz is here!
        Assert.True(maxVal > 0.3f, $"Expected prominent energy peak, got {maxVal}");
        Assert.InRange(maxIndex, 5, 7);

        // Remote bands (e.g. sub-bass band 0 or ultra-high band 11) should have minimal/zero energy
        Assert.True(compact[0] < maxVal * 0.2f, $"Sub-bass band should be significantly lower than peak. Band 0: {compact[0]}, Peak: {maxVal}");
        Assert.True(compact[11] < maxVal * 0.2f, $"Treble band should be significantly lower than peak. Band 11: {compact[11]}, Peak: {maxVal}");
    }

    [Fact]
    public void Smoothing_DecaysGraduallyTowardsZero()
    {
        int sampleRate = 48000;
        var analyzer = new SpectrumAnalyzer(sampleRate: sampleRate, fastAttackRate: 0.8f, slowDecayRate: 0.7f);

        // Impulse signal to charge bands
        float[] impulse = new float[1024];
        for (int i = 0; i < impulse.Length; i++)
        {
            impulse[i] = MathF.Sin(2.0f * MathF.PI * 440.0f * i / sampleRate);
        }

        analyzer.AddSamples(impulse);

        Span<float> initial = stackalloc float[SpectrumAnalyzer.CompactBandCount];
        analyzer.GetCompactBands(initial);

        float initialMax = 0.0f;
        for (int i = 0; i < initial.Length; i++)
        {
            if (initial[i] > initialMax) initialMax = initial[i];
        }
        Assert.True(initialMax > 0.0f, "Analyzer should have reacted to signal.");

        // Call DecayOnly repeatedly and assert monotonic decay
        float prevMax = initialMax;
        Span<float> decayed = stackalloc float[SpectrumAnalyzer.CompactBandCount];
        for (int step = 0; step < 5; step++)
        {
            analyzer.DecayOnly();
            analyzer.GetCompactBands(decayed);

            float currentMax = 0.0f;
            for (int i = 0; i < decayed.Length; i++)
            {
                if (decayed[i] > currentMax) currentMax = decayed[i];
            }

            Assert.True(currentMax <= prevMax, $"Decay should be monotonic: current={currentMax} <= prev={prevMax}");
            prevMax = currentMax;
        }

        // Long decay should eventually reach zero
        for (int step = 0; step < 50; step++)
        {
            analyzer.DecayOnly();
        }

        Span<float> final = stackalloc float[SpectrumAnalyzer.CompactBandCount];
        analyzer.GetCompactBands(final);
        for (int i = 0; i < final.Length; i++)
        {
            Assert.Equal(0.0f, final[i]);
        }
    }

    [Fact]
    public void Reset_ClearsAllInternalState()
    {
        int sampleRate = 48000;
        var analyzer = new SpectrumAnalyzer(sampleRate: sampleRate);

        float[] signal = new float[1024];
        Array.Fill(signal, 0.5f);
        analyzer.AddSamples(signal);

        analyzer.Reset();

        Span<float> compact = stackalloc float[SpectrumAnalyzer.CompactBandCount];
        analyzer.GetCompactBands(compact);
        for (int i = 0; i < compact.Length; i++)
        {
            Assert.Equal(0.0f, compact[i]);
        }
    }

    [Fact]
    public void ProcessWindow_And_Decay_ZeroHeapAllocations()
    {
        var analyzer = new SpectrumAnalyzer(sampleRate: 48000);
        float[] window = new float[SpectrumAnalyzer.FftSize];
        Span<float> compact = stackalloc float[SpectrumAnalyzer.CompactBandCount];

        // Warm up JIT execution paths
        analyzer.ProcessWindow(window);
        analyzer.DecayOnly();
        analyzer.GetCompactBands(compact);

        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        analyzer.ProcessWindow(window);
        analyzer.DecayOnly();
        analyzer.GetCompactBands(compact);
        long allocatedAfter = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, allocatedAfter - allocatedBefore);
    }
}
