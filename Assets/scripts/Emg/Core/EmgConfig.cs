namespace CallibriEmg
{
    // Every tuning number in the EMG pipeline lives here, so the values
    // documented in CLAUDE.md map to exactly one place in code.
    public static class EmgConfig
    {
        public const int TargetSampleRateHz = 1000;
        public const int EnvelopeWindowMs = 100;

        public const float MeditationSeconds = 30f;
        public const float MeditationSettleSeconds = 5f;

        public const float CalibrationRestSeconds = 5f;
        // The first second of the opening rest is skipped: the patient has
        // just pressed Start and is still settling.
        public const float CalibrationRestSkipSeconds = 1f;
        public const float CalibrationFlexSeconds = 2f;
        public const float CalibrationBetweenRestSeconds = 3f;
        public const int CalibrationFlexCount = 3;
        public const float FlexPercentile = 95f;
        public const float MinCalibrationRangeUv = 10f;

        public const float DefaultThresholdFraction = 0.5f;
        public const float MinThresholdFraction = 0.3f;
        public const float MaxThresholdFraction = 0.8f;
        public const float ReleaseFraction = 0.25f;
        public const int RefractoryMs = 250;

        // A squeeze only counts once the envelope has stayed above the trigger
        // this long. Blinks and twitches are shorter than a deliberate
        // squeeze, so this removes most accidental flaps for ~0.15 s of lag.
        public const int MinSqueezeMs = 150;
        // The trigger never sits closer to rest than the resting average
        // plus this many standard deviations (the usual muscle-onset rule in
        // EMG research), but never further than this fraction of the way to
        // the strongest squeeze, so it always stays reachable.
        public const float TriggerFloorRestSds = 3f;
        public const float MaxTriggerFraction = 0.9f;

        public const float RestTestSeconds = 30f;
        public const int StrengthTrials = 3;
        public const float StrengthHoldSeconds = 5f;
        public const float StrengthRestSeconds = 30f;
        public const int StrengthPeakWindowMs = 500;
        // The first second of each hold is the build-up to full effort, so
        // the hold average and fade start after it.
        public const float StrengthSteadyFromSeconds = 1f;

        // The report chart keeps one point per this many ms.
        public const int ReportBucketMs = 100;

        public const float ElectrodeGoodSeconds = 3f;
        public const float ScanTimeoutSeconds = 15f;
        public const float AutoConnectAfterSeconds = 2f;
        public const int LowBatteryPercent = 20;
        public const float FlushIntervalSeconds = 1f;
        public const float ReconnectIntervalSeconds = 2f;
    }
}
