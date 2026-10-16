using System;

namespace CallibriEmg
{
    // A short rolling record of the envelope for the live graph on the
    // Connect screen. Keeps one point every `decimation` samples, so 300
    // points at 1000 Hz / 10 cover the last 3 seconds.
    public sealed class EnvelopeHistory
    {
        private readonly float[] points;
        private readonly int decimation;
        private int next;
        private int counter;
        private float peakInBlock;

        public EnvelopeHistory(int capacity, int decimation)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }
            if (decimation < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(decimation));
            }
            points = new float[capacity];
            this.decimation = decimation;
        }

        public int Capacity => points.Length;

        // Keeps the largest value in each block rather than the last one, so
        // short spikes still show up on the graph after decimation.
        public void Add(float envelopeUv)
        {
            if (envelopeUv > peakInBlock)
            {
                peakInBlock = envelopeUv;
            }
            counter++;
            if (counter < decimation)
            {
                return;
            }
            points[next] = peakInBlock;
            next = (next + 1) % points.Length;
            counter = 0;
            peakInBlock = 0;
        }

        // index 0 = oldest point, Capacity - 1 = newest.
        public float Get(int index)
        {
            return points[(next + index) % points.Length];
        }

        public float Max()
        {
            float max = 0;
            for (int i = 0; i < points.Length; i++)
            {
                if (points[i] > max)
                {
                    max = points[i];
                }
            }
            return max;
        }

        public void Clear()
        {
            Array.Clear(points, 0, points.Length);
            next = 0;
            counter = 0;
            peakInBlock = 0;
        }
    }
}
