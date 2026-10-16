using System;

namespace CallibriEmg
{
    // Turns raw EMG (which swings positive and negative hundreds of times a
    // second) into a smooth "how hard is the muscle working" value: the RMS
    // (root mean square) over a sliding window. Every threshold and metric
    // in the project uses this envelope, never raw samples.
    public sealed class EnvelopeCalculator
    {
        private readonly double[] squares;
        private int index;
        private int filled;
        private double sumSquares;

        public EnvelopeCalculator(int windowSamples)
        {
            if (windowSamples < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(windowSamples), "Window must hold at least one sample.");
            }
            squares = new double[windowSamples];
        }

        public static EnvelopeCalculator ForSampleRate(int sampleRateHz, int windowMs)
        {
            return new EnvelopeCalculator(Math.Max(1, sampleRateHz * windowMs / 1000));
        }

        public int WindowSamples => squares.Length;

        public double Current { get; private set; }

        public double Add(double sampleUv)
        {
            // A single NaN would poison the running sum for a whole window;
            // a dropped value is far less harmful than a frozen envelope.
            if (double.IsNaN(sampleUv) || double.IsInfinity(sampleUv))
            {
                sampleUv = 0;
            }

            double square = sampleUv * sampleUv;
            sumSquares += square - squares[index];
            squares[index] = square;
            index++;

            if (index == squares.Length)
            {
                index = 0;
                // Adding and subtracting squares for hours slowly accumulates
                // floating-point error. Re-summing once per window keeps the
                // value exact for the cost of one pass per window.
                double exact = 0;
                for (int i = 0; i < squares.Length; i++)
                {
                    exact += squares[i];
                }
                sumSquares = exact;
            }

            if (filled < squares.Length)
            {
                filled++;
            }

            Current = Math.Sqrt(Math.Max(0.0, sumSquares / filled));
            return Current;
        }

        public void Reset()
        {
            Array.Clear(squares, 0, squares.Length);
            index = 0;
            filled = 0;
            sumSquares = 0;
            Current = 0;
        }
    }
}
