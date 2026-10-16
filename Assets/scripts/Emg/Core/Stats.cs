using System;
using System.Collections.Generic;

namespace CallibriEmg
{
    // Small statistics helpers over a [start, end) slice of a list, so
    // callers can analyse part of a capture without copying it first.
    public static class Stats
    {
        public static double Mean(IReadOnlyList<float> values, int start, int end)
        {
            CheckRange(values, start, end);
            double sum = 0;
            for (int i = start; i < end; i++)
            {
                sum += values[i];
            }
            return sum / (end - start);
        }

        public static double StdDev(IReadOnlyList<float> values, int start, int end)
        {
            double mean = Mean(values, start, end);
            double sumSq = 0;
            for (int i = start; i < end; i++)
            {
                double d = values[i] - mean;
                sumSq += d * d;
            }
            return Math.Sqrt(sumSq / (end - start));
        }

        // Linear interpolation between the two closest ranks, the same
        // definition spreadsheet PERCENTILE.INC uses, so results can be
        // checked by hand against the exported CSV.
        public static double Percentile(IReadOnlyList<float> values, int start, int end, double percentile)
        {
            CheckRange(values, start, end);
            if (percentile < 0 || percentile > 100)
            {
                throw new ArgumentOutOfRangeException(nameof(percentile), "Percentile must be between 0 and 100.");
            }

            float[] sorted = new float[end - start];
            for (int i = 0; i < sorted.Length; i++)
            {
                sorted[i] = values[start + i];
            }
            Array.Sort(sorted);

            double rank = percentile / 100.0 * (sorted.Length - 1);
            int lower = (int)Math.Floor(rank);
            int upper = (int)Math.Ceiling(rank);
            double fraction = rank - lower;
            return sorted[lower] + (sorted[upper] - sorted[lower]) * fraction;
        }

        public static double Median(IReadOnlyList<float> values, int start, int end)
        {
            return Percentile(values, start, end, 50);
        }

        private static void CheckRange(IReadOnlyList<float> values, int start, int end)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }
            if (start < 0 || end > values.Count || start >= end)
            {
                throw new ArgumentOutOfRangeException(nameof(start), $"Invalid range [{start}, {end}) for {values.Count} values.");
            }
        }
    }
}
