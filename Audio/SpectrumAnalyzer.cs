namespace VoiceWorkbench.Audio;

public sealed class SpectrumAnalyzer
{
    private const int FftSize = 8192;
    private const int BarCount = 256;
    private const int SampleRate = 44100;
    private const double MinimumDisplayFrequency = 40d;
    private const double MaximumDisplayFrequency = 20000d;
    private readonly double[] _samples = new double[FftSize];
    private int _sampleIndex;

    public SpectrumFrame AddSamples(ReadOnlySpan<float> samples)
    {
        var peak = 0d;
        foreach (var sample in samples)
        {
            var monoSample = Math.Clamp(sample, -1f, 1f);
            _samples[_sampleIndex] = monoSample;
            _sampleIndex = (_sampleIndex + 1) % FftSize;
            peak = Math.Max(peak, Math.Abs(monoSample));
        }

        var real = new double[FftSize];
        var imaginary = new double[FftSize];
        for (var i = 0; i < FftSize; i++)
        {
            var sourceIndex = (_sampleIndex + i) % FftSize;
            var window = 0.5d * (1d - Math.Cos(2d * Math.PI * i / (FftSize - 1)));
            real[i] = _samples[sourceIndex] * window;
        }

        FastFourierTransform(real, imaginary);
        return new SpectrumFrame(CreateBars(real, imaginary), peak);
    }

    private static double[] CreateBars(double[] real, double[] imaginary)
    {
        var bars = new double[BarCount];
        var maxBin = real.Length / 2;

        for (var bar = 0; bar < BarCount; bar++)
        {
            var startFrequency = FrequencyForBar(bar);
            var endFrequency = FrequencyForBar(bar + 1);
            var startBin = Math.Max(1, FrequencyToBin(startFrequency, real.Length));
            var endBin = Math.Max(startBin + 1, FrequencyToBin(endFrequency, real.Length));
            endBin = Math.Min(endBin, maxBin);

            var magnitudeSum = 0d;
            var binCount = 0;
            for (var bin = startBin; bin < endBin; bin++)
            {
                var magnitude = Math.Sqrt(real[bin] * real[bin] + imaginary[bin] * imaginary[bin]);
                magnitudeSum += magnitude;
                binCount++;
            }

            var averageMagnitude = binCount == 0 ? 0d : magnitudeSum / binCount;
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

    private static int FrequencyToBin(double frequency, int fftSize)
    {
        return (int)Math.Round(frequency * fftSize / SampleRate);
    }

    private static void FastFourierTransform(double[] real, double[] imaginary)
    {
        var n = real.Length;
        var bits = (int)Math.Log2(n);

        for (var i = 0; i < n; i++)
        {
            var j = ReverseBits(i, bits);
            if (j <= i)
            {
                continue;
            }

            (real[i], real[j]) = (real[j], real[i]);
            (imaginary[i], imaginary[j]) = (imaginary[j], imaginary[i]);
        }

        for (var length = 2; length <= n; length <<= 1)
        {
            var angle = -2d * Math.PI / length;
            var wLengthReal = Math.Cos(angle);
            var wLengthImaginary = Math.Sin(angle);

            for (var i = 0; i < n; i += length)
            {
                var wReal = 1d;
                var wImaginary = 0d;

                for (var j = 0; j < length / 2; j++)
                {
                    var evenIndex = i + j;
                    var oddIndex = evenIndex + length / 2;
                    var oddReal = real[oddIndex] * wReal - imaginary[oddIndex] * wImaginary;
                    var oddImaginary = real[oddIndex] * wImaginary + imaginary[oddIndex] * wReal;

                    real[oddIndex] = real[evenIndex] - oddReal;
                    imaginary[oddIndex] = imaginary[evenIndex] - oddImaginary;
                    real[evenIndex] += oddReal;
                    imaginary[evenIndex] += oddImaginary;

                    var nextWReal = wReal * wLengthReal - wImaginary * wLengthImaginary;
                    wImaginary = wReal * wLengthImaginary + wImaginary * wLengthReal;
                    wReal = nextWReal;
                }
            }
        }
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

