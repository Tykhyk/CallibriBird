using System;
using System.Collections.Generic;

namespace CallibriEmg
{
    // Field names are written to meditation.json as-is and match the metric
    // names documented in CLAUDE.md, so the data file reads like the spec.
    [Serializable]
    public class MeditationResult
    {
        public bool valid;
        public string message = "";
        public float durationSeconds;
        public float analyzedFromSecond;
        public float mean_uV;
        public float median_uV;
        public float lowest_1s_uV;
        public float change_pct;
        public float steadiness_sd_uV;
        public int tension_spikes;
        public bool calibrationApplied;
        public float pct_of_max;
        public float ratio_to_calib_rest;

        // Meditation runs before calibration, so the two metrics that compare
        // against the patient's own rest/max can only be filled in later.
        public void ApplyCalibration(CalibrationResult calibration)
        {
            if (!valid || calibration == null || !calibration.valid)
            {
                return;
            }
            pct_of_max = calibration.maxUv > 0 ? mean_uV / calibration.maxUv * 100f : 0f;
            ratio_to_calib_rest = calibration.restUv > 0 ? mean_uV / calibration.restUv : 0f;
            calibrationApplied = true;
        }
    }

    public static class MeditationAnalyzer
    {
        private const float ComparisonWindowSeconds = 10f;
        private const float SpikeMultipleOfMedian = 2f;
        private const float SpikeMinSeconds = 0.1f;

        public static MeditationResult Analyze(IReadOnlyList<float> envelope, int sampleRateHz)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }
            if (sampleRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            }

            MeditationResult result = new MeditationResult
            {
                durationSeconds = envelope.Count / (float)sampleRateHz,
                analyzedFromSecond = EmgConfig.MeditationSettleSeconds,
            };

            int start = (int)(EmgConfig.MeditationSettleSeconds * sampleRateHz);
            int end = envelope.Count;
            int window = (int)(ComparisonWindowSeconds * sampleRateHz);
            if (end - start < window * 2)
            {
                result.valid = false;
                result.message = "Not enough signal was recorded to analyse the relaxation.";
                return result;
            }

            result.mean_uV = (float)Stats.Mean(envelope, start, end);
            result.median_uV = (float)Stats.Median(envelope, start, end);
            result.steadiness_sd_uV = (float)Stats.StdDev(envelope, start, end);
            result.lowest_1s_uV = LowestWindowMean(envelope, start, end, sampleRateHz);

            // For the standard 30 s run these windows are seconds 5-15 and
            // 20-30, exactly as documented.
            double early = Stats.Mean(envelope, start, start + window);
            double late = Stats.Mean(envelope, end - window, end);
            result.change_pct = early > 0 ? (float)((late - early) / early * 100.0) : 0f;

            result.tension_spikes = CountSpikes(envelope, start, end, result.median_uV * SpikeMultipleOfMedian, (int)(SpikeMinSeconds * sampleRateHz));
            result.valid = true;
            return result;
        }

        private static float LowestWindowMean(IReadOnlyList<float> envelope, int start, int end, int windowSamples)
        {
            if (end - start < windowSamples)
            {
                return (float)Stats.Mean(envelope, start, end);
            }

            double sum = 0;
            for (int i = start; i < start + windowSamples; i++)
            {
                sum += envelope[i];
            }
            double lowest = sum;
            for (int i = start + windowSamples; i < end; i++)
            {
                sum += envelope[i] - envelope[i - windowSamples];
                if (sum < lowest)
                {
                    lowest = sum;
                }
            }
            return (float)(lowest / windowSamples);
        }

        // A spike is a run of samples above the threshold lasting at least
        // minSamples: long enough to be an involuntary tensing or a blink,
        // not a single noisy sample.
        private static int CountSpikes(IReadOnlyList<float> envelope, int start, int end, float threshold, int minSamples)
        {
            int spikes = 0;
            int run = 0;
            for (int i = start; i < end; i++)
            {
                if (envelope[i] > threshold)
                {
                    run++;
                    if (run == minSamples)
                    {
                        spikes++;
                    }
                }
                else
                {
                    run = 0;
                }
            }
            return spikes;
        }
    }
}
