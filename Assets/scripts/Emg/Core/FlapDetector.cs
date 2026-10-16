using System;

namespace CallibriEmg
{
    // Decides when a squeeze counts as one flap.
    //
    // It uses hysteresis: a high trigger level to fire, and a lower release
    // level the envelope must fall back under before the next flap is
    // allowed. A single trigger level would fire several times as the
    // envelope wobbles around it during one squeeze. The refractory time
    // is a second guard against two flaps from one squeeze.
    //
    // The minimum hold rejects short bursts: the envelope must stay above
    // the trigger for minHoldSamples in a row before the flap fires. A blink
    // or twitch crosses the trigger only briefly, a deliberate squeeze stays
    // above it. The hold is measured with the sample index, not by counting
    // calls, so it means the same time whether the detector is fed every
    // sample (the game) or once per frame (the calibration practice light).
    public sealed class FlapDetector
    {
        private readonly long refractorySamples;
        private readonly long minHoldSamples;
        private long lastFlapSample;
        private long aboveSince;
        private bool armed;

        public FlapDetector(double onThresholdUv, double offThresholdUv, long refractorySamples, long minHoldSamples = 0)
        {
            if (refractorySamples < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(refractorySamples), "Refractory time cannot be negative.");
            }
            if (minHoldSamples < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minHoldSamples), "Minimum hold cannot be negative.");
            }
            this.refractorySamples = refractorySamples;
            this.minHoldSamples = Math.Max(1, minHoldSamples);
            SetThresholds(onThresholdUv, offThresholdUv);
            Reset();
        }

        public double OnThresholdUv { get; private set; }

        public double OffThresholdUv { get; private set; }

        public bool IsArmed => armed;

        public void SetThresholds(double onThresholdUv, double offThresholdUv)
        {
            if (offThresholdUv > onThresholdUv)
            {
                throw new ArgumentException("The release level must not be above the trigger level.");
            }
            OnThresholdUv = onThresholdUv;
            OffThresholdUv = offThresholdUv;
        }

        // Returns true exactly once per squeeze, on the sample where the
        // envelope has been above the trigger level for the minimum hold.
        public bool Process(double envelopeUv, long sampleIndex)
        {
            bool refractoryOver = sampleIndex - lastFlapSample >= refractorySamples;

            if (armed)
            {
                if (envelopeUv >= OnThresholdUv && refractoryOver)
                {
                    if (aboveSince < 0)
                    {
                        aboveSince = sampleIndex;
                    }
                    if (sampleIndex - aboveSince + 1 >= minHoldSamples)
                    {
                        armed = false;
                        aboveSince = -1;
                        lastFlapSample = sampleIndex;
                        return true;
                    }
                }
                else
                {
                    aboveSince = -1;
                }
                return false;
            }

            if (envelopeUv <= OffThresholdUv && refractoryOver)
            {
                armed = true;
            }
            return false;
        }

        public void Reset()
        {
            armed = true;
            aboveSince = -1;
            lastFlapSample = long.MinValue / 2;
        }

        // Makes the next flap wait until the envelope has first dropped below
        // the release level. Used whenever play (re)starts: a patient who is
        // still tensing from the previous screen, or when the signal comes
        // back, must relax once before a squeeze counts.
        public void Disarm()
        {
            armed = false;
            aboveSince = -1;
            lastFlapSample = long.MinValue / 2;
        }
    }
}
