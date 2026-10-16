using System;
using System.Collections.Generic;

namespace CallibriEmg
{
    // One maximum-effort squeeze of the strength test. Field names are
    // written to assessment.json as-is.
    //
    // Strength is the steady level the patient holds, not the single highest
    // moment (Tykhyk's choice, 2026-10-16). In the first real-sensor tests a
    // squeeze could hold a steady level while blinks, squeezing the eyes and
    // electrode bumps added short bursts several times higher, and the old
    // "best half-second" reported the bursts instead of the squeeze. A forehead sensor picks up those bursts
    // from the eye muscles, so they say nothing about forehead strength.
    [Serializable]
    public class StrengthTrialResult
    {
        public int trial;
        public bool valid;
        public string message = "";
        // Median of seconds 1-4 of the hold: the level the patient keeps up
        // once at full effort. A median ignores a burst that fills less than
        // half of that window, where an average would not.
        public float steadyUv;
        // Highest half-second average anywhere in the hold, kept to show
        // what the bursts looked like.
        public float peakUv;
        // True when that short peak is more than twice the steady level: a
        // blink, eye squeeze or electrode movement, not a stronger squeeze.
        public bool burstDetected;
        // Median of the last second vs median of second 1-2. Negative = the
        // squeeze faded while holding.
        public float fadePct;
        // From the "squeeze" cue until the signal first reaches 90% of the
        // steady level: reaction plus how quickly the muscle gets to full
        // effort. -1 if it never got there.
        public int timeToSteadyMs;
    }

    [Serializable]
    public class StrengthTestResult
    {
        public bool valid;
        public string message = "";
        public int trialsCompleted;
        // The best trial's steady level: the patient's maximum voluntary
        // contraction (MVC) for this setup.
        public float bestSteadyUv;
        public int bestTrial;
        public float averageSteadyUv;
        public int burstTrials;
        // Resting tension from the eyes-closed rest test, and the same value
        // as a % of the strongest squeeze. Comparing two numbers from one
        // recording cancels out electrode contact and skin differences.
        public bool restTestAvailable;
        public float restMeanUv;
        public float restPctOfStrongest;
        public StrengthTrialResult[] trials = new StrengthTrialResult[0];
    }

    public static class StrengthAnalyzer
    {
        private const float MinUsableSeconds = 2f;
        private const float SteadyToSeconds = 4f;
        private const float BurstMultiple = 2f;
        private const float SteadyReachedFraction = 0.9f;

        public static StrengthTrialResult AnalyzeTrial(IReadOnlyList<float> envelope, int sampleRateHz, int trialNumber)
        {
            if (envelope == null)
            {
                throw new ArgumentNullException(nameof(envelope));
            }
            if (sampleRateHz <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRateHz));
            }

            StrengthTrialResult result = new StrengthTrialResult { trial = trialNumber };
            int count = envelope.Count;
            int second = sampleRateHz;
            if (count < MinUsableSeconds * sampleRateHz)
            {
                result.valid = false;
                result.message = "The squeeze was too short to measure.";
                return result;
            }

            int steadyFrom = (int)(EmgConfig.StrengthSteadyFromSeconds * sampleRateHz);
            int steadyTo = Math.Min(count, (int)(SteadyToSeconds * sampleRateHz));
            result.steadyUv = (float)Stats.Median(envelope, steadyFrom, steadyTo);

            int window = Math.Max(1, sampleRateHz * EmgConfig.StrengthPeakWindowMs / 1000);
            result.peakUv = HighestWindowMean(envelope, 0, count, window);
            result.burstDetected = result.steadyUv > 0 && result.peakUv > BurstMultiple * result.steadyUv;

            double early = Stats.Median(envelope, steadyFrom, Math.Min(count, steadyFrom + second));
            double late = Stats.Median(envelope, Math.Max(0, count - second), count);
            result.fadePct = early > 0 ? (float)((late - early) / early * 100.0) : 0f;

            float target = result.steadyUv * SteadyReachedFraction;
            int reached = -1;
            for (int i = 0; i < count; i++)
            {
                if (envelope[i] >= target)
                {
                    reached = i;
                    break;
                }
            }
            result.timeToSteadyMs = reached >= 0 ? (int)(reached * 1000L / sampleRateHz) : -1;

            result.valid = true;
            return result;
        }

        public static StrengthTestResult Summarize(IReadOnlyList<StrengthTrialResult> trials, MeditationResult restTest)
        {
            StrengthTestResult result = new StrengthTestResult();
            List<StrengthTrialResult> all = new List<StrengthTrialResult>();
            double steadySum = 0;
            int validCount = 0;
            StrengthTrialResult best = null;

            if (trials != null)
            {
                foreach (StrengthTrialResult trial in trials)
                {
                    all.Add(trial);
                    if (!trial.valid)
                    {
                        continue;
                    }
                    validCount++;
                    steadySum += trial.steadyUv;
                    if (trial.burstDetected)
                    {
                        result.burstTrials++;
                    }
                    if (best == null || trial.steadyUv > best.steadyUv)
                    {
                        best = trial;
                    }
                }
            }

            result.trials = all.ToArray();
            result.trialsCompleted = validCount;
            if (best == null)
            {
                result.valid = false;
                result.message = "No squeeze could be measured.";
                return result;
            }

            result.valid = true;
            result.bestSteadyUv = best.steadyUv;
            result.bestTrial = best.trial;
            result.averageSteadyUv = (float)(steadySum / validCount);

            if (restTest != null && restTest.valid)
            {
                result.restTestAvailable = true;
                result.restMeanUv = restTest.mean_uV;
                result.restPctOfStrongest = result.bestSteadyUv > 0 ? restTest.mean_uV / result.bestSteadyUv * 100f : 0f;
            }
            return result;
        }

        private static float HighestWindowMean(IReadOnlyList<float> envelope, int start, int end, int window)
        {
            if (end - start <= window)
            {
                return (float)Stats.Mean(envelope, start, end);
            }
            double sum = 0;
            for (int i = start; i < start + window; i++)
            {
                sum += envelope[i];
            }
            double highest = sum;
            for (int i = start + window; i < end; i++)
            {
                sum += envelope[i] - envelope[i - window];
                if (sum > highest)
                {
                    highest = sum;
                }
            }
            return (float)(highest / window);
        }
    }
}
