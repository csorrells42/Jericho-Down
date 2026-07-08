namespace JerichoDown.Audio;

public sealed class SpectrumAnalyzer
{
    private const int FftSize = 8192;
    private const int BarCount = 256;
    private const double MinimumDisplayFrequency = 40d;
    private const double MaximumDisplayFrequency = 20000d;
    private static readonly int[] BitReversedIndices = CreateBitReversedIndices();
    private static readonly FftStage[] FftStages = CreateFftStages();
    private readonly double[] _samples = new double[FftSize];
    private readonly double[] _real = new double[FftSize];
    private readonly double[] _imaginary = new double[FftSize];
    private readonly double[] _window = new double[FftSize];
    private readonly int[] _barStartBins = new int[BarCount];
    private readonly int[] _barEndBins = new int[BarCount];
    private readonly int[] _barBinCounts = new int[BarCount];
    private readonly int _sampleRate;
    private int _sampleIndex;

    private readonly struct FftStage
    {
        public FftStage(int length, double stepReal, double stepImaginary)
        {
            Length = length;
            HalfLength = length / 2;
            StepReal = stepReal;
            StepImaginary = stepImaginary;
        }

        public int Length { get; }

        public int HalfLength { get; }

        public double StepReal { get; }

        public double StepImaginary { get; }
    }

    public SpectrumAnalyzer(int sampleRate = 44100)
    {
        _sampleRate = Math.Max(8000, sampleRate);
        for (var i = 0; i < FftSize; i++)
        {
            _window[i] = 0.5d * (1d - Math.Cos(2d * Math.PI * i / (FftSize - 1)));
        }

        var maxBin = FftSize / 2;
        for (var bar = 0; bar < BarCount; bar++)
        {
            var startFrequency = FrequencyForBar(bar);
            var endFrequency = FrequencyForBar(bar + 1);
            _barStartBins[bar] = Math.Max(1, FrequencyToBin(startFrequency, FftSize));
            _barEndBins[bar] = Math.Min(Math.Max(_barStartBins[bar] + 1, FrequencyToBin(endFrequency, FftSize)), maxBin);
            _barBinCounts[bar] = Math.Max(1, _barEndBins[bar] - _barStartBins[bar]);
        }
    }

    public SpectrumFrame AddSamples(ReadOnlySpan<float> samples)
    {
        var result = AnalyzeSamples(samples);
        return new SpectrumFrame(result.Magnitudes, result.PeakLevel);
    }

    public (double[] Magnitudes, double PeakLevel) AnalyzeSamples(ReadOnlySpan<float> samples)
    {
        var peak = 0d;
        foreach (var sample in samples)
        {
            var monoSample = SanitizeSample(sample);
            _samples[_sampleIndex] = monoSample;
            _sampleIndex = (_sampleIndex + 1) % FftSize;
            peak = Math.Max(peak, Math.Abs(monoSample));
        }

        Array.Clear(_imaginary);
        var sourceIndex = _sampleIndex;
        for (var i = 0; i < FftSize; i++)
        {
            _real[i] = _samples[sourceIndex] * _window[i];
            sourceIndex++;
            if (sourceIndex == FftSize)
            {
                sourceIndex = 0;
            }
        }

        FastFourierTransform(_real, _imaginary);
        return (CreateBars(), peak);
    }

    private double[] CreateBars()
    {
        var bars = new double[BarCount];

        for (var bar = 0; bar < BarCount; bar++)
        {
            var magnitudeSum = 0d;
            for (var bin = _barStartBins[bar]; bin < _barEndBins[bar]; bin++)
            {
                var magnitude = Math.Sqrt(_real[bin] * _real[bin] + _imaginary[bin] * _imaginary[bin]);
                magnitudeSum += double.IsFinite(magnitude) ? magnitude : 0d;
            }

            var averageMagnitude = magnitudeSum / _barBinCounts[bar];
            var db = 20d * Math.Log10(averageMagnitude / FftSize + 0.000000001d);
            bars[bar] = Math.Clamp((db + 95d) / 70d, 0d, 1d);
        }

        return bars;
    }

    private static double FrequencyForBar(int bar)
    {
        var position = bar / (double)BarCount;
        return MinimumDisplayFrequency * Math.Pow(MaximumDisplayFrequency / MinimumDisplayFrequency, position);
    }

    private static double SanitizeSample(float sample)
    {
        return float.IsFinite(sample)
            ? Math.Clamp(sample, -1f, 1f)
            : 0d;
    }

    private int FrequencyToBin(double frequency, int fftSize)
    {
        return (int)Math.Round(frequency * fftSize / _sampleRate);
    }

    private static void FastFourierTransform(double[] real, double[] imaginary)
    {
        for (var i = 0; i < FftSize; i++)
        {
            var j = BitReversedIndices[i];
            if (j <= i)
            {
                continue;
            }

            (real[i], real[j]) = (real[j], real[i]);
            (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
        }

        foreach (var stage in FftStages)
        {
            for (var i = 0; i < FftSize; i += stage.Length)
            {
                var wReal = 1d;
                var wImaginary = 0d;

                for (var j = 0; j < stage.HalfLength; j++)
                {
                    var evenIndex = i + j;
                    var oddIndex = evenIndex + stage.HalfLength;
                    var oddReal = real[oddIndex] * wReal - imaginary[oddIndex] * wImaginary;
                    var oddImaginary = real[oddIndex] * wImaginary + imaginary[oddIndex] * wReal;

                    real[oddIndex] = real[evenIndex] - oddReal;
                    imaginary[oddIndex] = imaginary[evenIndex] - oddImaginary;
                    real[evenIndex] += oddReal;
                    imaginary[evenIndex] += oddImaginary;

                    var nextWReal = wReal * stage.StepReal - wImaginary * stage.StepImaginary;
                    wImaginary = wReal * stage.StepImaginary + wImaginary * stage.StepReal;
                    wReal = nextWReal;
                }
            }
        }
    }

    private static int[] CreateBitReversedIndices()
    {
        var indices = new int[FftSize];
        var bits = (int)Math.Log2(FftSize);
        for (var i = 0; i < indices.Length; i++)
        {
            indices[i] = ReverseBits(i, bits);
        }

        return indices;
    }

    private static FftStage[] CreateFftStages()
    {
        var stages = new List<FftStage>();
        for (var length = 2; length <= FftSize; length <<= 1)
        {
            var angle = -2d * Math.PI / length;
            stages.Add(new FftStage(length, Math.Cos(angle), Math.Sin(angle)));
        }

        return [.. stages];
    }

    private static int ReverseBits(int value, int bits)
    {
        var reversed = 0;
        for (var i = 0; i < bits; i++)
        {
            reversed = (reversed << 1) | (value & 1);
            value >>= 1;
        }

        return reversed;
    }
}


