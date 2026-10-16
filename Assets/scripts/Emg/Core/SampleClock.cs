using System;

namespace CallibriEmg
{
    // Turns a sample number into a time on the session clock.
    //
    // Bluetooth delivers samples in bursts, so a sample's arrival time is a
    // poor timestamp; counting samples gives smooth, exact spacing. But
    // counting assumes the sensor's rate is exactly what it was set to, and
    // the Callibri set to 1000 Hz really sends about 1008 samples a second
    // (measured on every real session, 2026-10-16). Counting alone made the
    // EMG timeline run 0.8% fast: 2.6 s ahead of the event log after
    // 5 minutes, about 15 s after 30.
    //
    // So this keeps the sample spacing from counting, but measures the real
    // rate from how many samples have arrived over a long stretch of the
    // session clock (at least 10 s, where arrival jitter of a few tens of ms
    // no longer matters). Each new estimate takes over from the current
    // time, so timestamps never jump backwards or skip.
    public sealed class SampleClock
    {
        private const double MinFitMs = 10000.0;
        // Estimates further than this from the set rate are treated as a
        // stall (a frozen app, a paused debugger), not as the sensor's rate.
        private const double MaxRateError = 0.05;

        private readonly double nominalRateHz;
        private long anchorIndex;
        private double anchorMs;
        private long fitIndex;
        private double fitMs;

        public SampleClock(double nominalRateHz)
        {
            if (nominalRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(nominalRateHz));
            }
            this.nominalRateHz = nominalRateHz;
            RateHz = nominalRateHz;
        }

        // The current best estimate of how many samples the sensor really
        // sends per second.
        public double RateHz { get; private set; }

        public double NominalRateHz => nominalRateHz;

        public double TimeMs(long sampleIndex)
        {
            return anchorMs + (sampleIndex - anchorIndex) * 1000.0 / RateHz;
        }

        // Starts timing from sampleIndex at sessionMs: at the start of the
        // recording and after a reconnect. The measured rate is kept, since
        // it belongs to the sensor, not to one stretch of streaming.
        public void Restart(long sampleIndex, double sessionMs)
        {
            anchorIndex = sampleIndex;
            anchorMs = sessionMs;
            fitIndex = sampleIndex;
            fitMs = sessionMs;
        }

        // Called after a block of samples has been counted: sampleIndex is
        // the index the next sample will get, sessionMs the session clock now.
        public void Observe(long sampleIndex, double sessionMs)
        {
            double span = sessionMs - fitMs;
            if (span < MinFitMs)
            {
                return;
            }
            double measured = (sampleIndex - fitIndex) * 1000.0 / span;
            if (Math.Abs(measured / nominalRateHz - 1.0) > MaxRateError)
            {
                return;
            }
            double now = TimeMs(sampleIndex);
            RateHz = measured;
            anchorIndex = sampleIndex;
            anchorMs = now;
        }
    }
}
