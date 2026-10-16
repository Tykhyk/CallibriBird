using System;
using System.Collections.Generic;

namespace CallibriEmg
{
    // Field names are written to calibration.json as-is, so they match the
    // names used in CLAUDE.md and the session summary.
    [Serializable]
    public class CalibrationResult
    {
        public bool valid;
        public string message = "";
        public float restUv;
        public float restSdUv;
        public float maxUv;
        public float thresholdFraction;
        public float onUv;
        public float offUv;
        // True when the trigger was lifted above the k-based level to stay
        // clear of resting noise (see ApplyThresholdFraction).
        public bool triggerRaisedByFloor;
        public int restSampleCount;
        public int flexSampleCount;

        // k can be changed by the therapist mid-session, so the thresholds
        // are always derived from the stored rest/max rather than saved
        // independently, which keeps them consistent with each other.
        //
        // The trigger is also kept above the resting signal's normal wobble
        // (resting average + 3 standard deviations). A calibration with weak
        // squeezes would otherwise put it so close to rest that blinks and
        // small twitches flap the bird. The floor is capped at 90% of the way
        // to the strongest squeeze so the patient can always reach it.
        public void ApplyThresholdFraction(float k)
        {
            thresholdFraction = k;
            float range = maxUv - restUv;
            float fromK = restUv + k * range;
            float floor = Math.Min(restUv + EmgConfig.TriggerFloorRestSds * restSdUv, restUv + EmgConfig.MaxTriggerFraction * range);
            onUv = Math.Max(fromK, floor);
            triggerRaisedByFloor = onUv > fromK;
            offUv = restUv + EmgConfig.ReleaseFraction * range;
            if (offUv > onUv)
            {
                offUv = onUv;
            }
        }
    }

    public static class CalibrationCalculator
    {
        public static CalibrationResult Compute(IReadOnlyList<float> restEnvelope, IReadOnlyList<float> flexEnvelope, float thresholdFraction)
        {
            if (restEnvelope == null)
            {
                throw new ArgumentNullException(nameof(restEnvelope));
            }
            if (flexEnvelope == null)
            {
                throw new ArgumentNullException(nameof(flexEnvelope));
            }

            CalibrationResult result = new CalibrationResult
            {
                restSampleCount = restEnvelope.Count,
                flexSampleCount = flexEnvelope.Count,
            };

            if (restEnvelope.Count == 0 || flexEnvelope.Count == 0)
            {
                result.valid = false;
                result.message = "No signal was recorded. Check the sensor connection and try again.";
                return result;
            }

            result.restUv = (float)Stats.Mean(restEnvelope, 0, restEnvelope.Count);
            result.restSdUv = (float)Stats.StdDev(restEnvelope, 0, restEnvelope.Count);
            // The 95th percentile instead of the single highest value: one
            // blink or electrode bump would otherwise set V_max far above
            // what the patient can actually repeat.
            result.maxUv = (float)Stats.Percentile(flexEnvelope, 0, flexEnvelope.Count, EmgConfig.FlexPercentile);
            result.ApplyThresholdFraction(thresholdFraction);

            float range = result.maxUv - result.restUv;
            if (range < EmgConfig.MinCalibrationRangeUv || result.maxUv < result.restUv * 1.5f)
            {
                result.valid = false;
                result.message = "The squeezes were too weak to tell apart from resting. Check that the electrodes are pressed firmly to the skin, then squeeze harder.";
                return result;
            }

            result.valid = true;
            return result;
        }
    }
}
