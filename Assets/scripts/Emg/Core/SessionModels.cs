using System;
using System.Globalization;
using UnityEngine;

namespace CallibriEmg
{
    public enum SessionPhase
    {
        Connect,
        Intake,
        Meditation,
        RestTest,
        StrengthTest,
        Calibration,
        Game,
        Ended,
    }

    public enum FlapSource
    {
        None,
        Emg,
        Mouse,
    }

    // One person in the "Patients' reports" folder, saved as patient.json in
    // their own folder. Sessions are linked to the person by patientId, so
    // their history survives a name typed differently ("jane smith" vs
    // "Jane Smith") and two different people with the same name stay apart.
    [Serializable]
    public class PatientRecord
    {
        public string patientId = "";
        public string fullName = "";
        public string dateOfBirth = "";
        public string targetMuscle = "Frontalis";
        public string clinicalIndication = "";
        public string createdAt = "";
        public string updatedAt = "";

        public static PatientRecord FromIntake(IntakeData intake, DateTime now)
        {
            string stamp = now.ToString("o", CultureInfo.InvariantCulture);
            return new PatientRecord
            {
                patientId = Guid.NewGuid().ToString("N"),
                fullName = intake.fullName,
                dateOfBirth = intake.dateOfBirth,
                targetMuscle = intake.targetMuscle,
                clinicalIndication = intake.clinicalIndication,
                createdAt = stamp,
                updatedAt = stamp,
            };
        }
    }

    [Serializable]
    public class IntakeData
    {
        public string patientId = "";
        public string fullName = "";
        public string dateOfBirth = "";
        public string targetMuscle = "Frontalis";
        public string clinicalIndication = "";
        public string submittedAt = "";

        public const string DateFormat = "MM/dd/yyyy";

        // Returns an empty string when valid, otherwise a message to show
        // the patient. Kept here (not in the UI) so the rules are testable.
        public string Validate(DateTime today)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return "Please enter the full name.";
            }
            if (!DateTime.TryParseExact(dateOfBirth.Trim(), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dob))
            {
                return "Please enter the date of birth as MM/DD/YYYY, for example 04/23/1985.";
            }
            if (dob > today || dob.Year < 1900)
            {
                return "The date of birth doesn't look right. Please check it.";
            }
            if (string.IsNullOrWhiteSpace(targetMuscle))
            {
                return "Please enter the target muscle.";
            }
            return "";
        }
    }

    [Serializable]
    public class DifficultySettings
    {
        // "Normal" is the starting point for every session (Tykhyk,
        // 2026-10-16). Pipe speed is the original game's. The gap and spacing
        // were widened because the originals were too hard with muscle
        // control, and the flap pace was slowed because the original pace
        // meant about 77 squeezes a minute, enough to tire and hurt a
        // patient's forehead.
        public const float NormalFlapSeconds = 2.0f;
        public const float NormalPipeSpeed = 0.96f;
        public const float NormalGapSize = 2.2f;
        public const float NormalSpawnInterval = 2.2f;

        // The original game, for reference: a flap every 0.78 s (gravity
        // scale 0.6, jump velocity 2.3, jump height 0.45), a 1.8 gap and a
        // new pipe every 1.72 s.
        public const float OriginalFlapSeconds = 0.78f;
        public const float OriginalGapSize = 1.8f;
        public const float OriginalSpawnInterval = 1.72f;

        // How high one flap lifts the bird, in units, at every pace. Lowering
        // gravity alone would make each squeeze launch the bird far higher
        // (and into the top pipes), so the pace changes gravity and jump
        // strength together and the jump stays the same size.
        public const float JumpHeight = 0.5f;

        public const float MinFlapSeconds = 0.8f;
        public const float MaxFlapSeconds = 3f;
        public const float MinPipeSpeed = 0.4f;
        public const float MaxPipeSpeed = 1.4f;
        public const float MinGapSize = 1.2f;
        public const float MaxGapSize = 2.8f;
        public const float MinSpawnInterval = 1.2f;
        public const float MaxSpawnInterval = 4f;

        // Seconds one squeeze keeps the bird up before it falls back to the
        // same height: the number that decides how often the patient must
        // squeeze.
        public float flapSeconds = NormalFlapSeconds;
        public float pipeSpeed = NormalPipeSpeed;
        public float gapSize = NormalGapSize;
        public float spawnInterval = NormalSpawnInterval;

        private static readonly DifficultySettings NormalDefaults = new DifficultySettings();

        // Game scripts read this once per round. Without a running session
        // (pressing Play directly in GameScene) the Normal values apply.
        public static DifficultySettings Current
        {
            get
            {
                EmgSessionManager manager = EmgSessionManager.Instance;
                return manager != null ? manager.Difficulty : NormalDefaults;
            }
        }

        // A jump that rises JumpHeight and lands back at the same height
        // after flapSeconds spends half that time rising, which gives
        // v = 4h / T and g = 8h / T^2.
        public float JumpVelocity => 4f * JumpHeight / SafeFlapSeconds;

        public float GravityUnitsPerSecondSquared => 8f * JumpHeight / (SafeFlapSeconds * SafeFlapSeconds);

        // Rigidbody2D.gravityScale that produces the gravity above.
        public float GravityScale => GravityUnitsPerSecondSquared / Mathf.Abs(Physics2D.gravity.y);

        public float SqueezesPerMinute => 60f / SafeFlapSeconds;

        private float SafeFlapSeconds => Mathf.Max(0.1f, flapSeconds);

        public DifficultySettings Clone()
        {
            return (DifficultySettings)MemberwiseClone();
        }

        public void ResetToNormal()
        {
            flapSeconds = NormalFlapSeconds;
            pipeSpeed = NormalPipeSpeed;
            gapSize = NormalGapSize;
            spawnInterval = NormalSpawnInterval;
        }

        public string Describe()
        {
            return string.Format(CultureInfo.InvariantCulture, "flap every {0:0.00} s, speed {1:0.00}, gap {2:0.00}, spacing {3:0.00} s", flapSeconds, pipeSpeed, gapSize, spawnInterval);
        }
    }

    [Serializable]
    public class DeviceInfo
    {
        public string sourceKind = "";
        public string serialNumber = "";
        public int batteryAtStartPercent = -1;
        public int sampleRateHz;
        public string hardwareFilters = "";
        public int envelopeWindowMs = EmgConfig.EnvelopeWindowMs;
        public string recordingStartedAt = "";
    }

    [Serializable]
    public class CalibrationFile
    {
        public CalibrationResult calibration;
        public int refractoryMs = EmgConfig.RefractoryMs;
        public float releaseFraction = EmgConfig.ReleaseFraction;
        public DifficultySettings difficultyForNextRound;
        public string savedAt = "";
    }

    // assessment.json: the eyes-closed rest test and the strength test.
    [Serializable]
    public class AssessmentFile
    {
        public bool skipped;
        public MeditationResult restTest;
        public StrengthTestResult strengthTest;
        public string savedAt = "";
    }

    // session_results.json: one flat line of this session's key numbers.
    // Written at the end of every session so later sessions can build the
    // progress comparison without re-reading the raw data. Missing values
    // are -1, with the has* flags saying which parts were done.
    [Serializable]
    public class SessionResults
    {
        public string sessionId = "";
        public string patientId = "";
        public string startedAt = "";
        // "Callibri" or "Simulated". Progress only compares sessions of the
        // same kind: fake demo data must never show up in a real history.
        public string sourceKind = "";
        public float measuredSampleRateHz = -1f;
        public string fullName = "";
        public string dateOfBirth = "";
        public bool hasMeditation;
        public float meditationMeanUv = -1f;
        public float meditationLowestUv = -1f;
        public bool hasRestTest;
        public float restTestMeanUv = -1f;
        public float restTestLowestUv = -1f;
        public bool hasStrengthTest;
        // The best squeeze's steady level (see StrengthTrialResult).
        public float strengthBestUv = -1f;
        public float restPctOfStrongest = -1f;
        public bool hasCalibration;
        public float calibrationRestUv = -1f;
        public float calibrationMaxUv = -1f;
        public int rounds;
        public int bestScore;
        public float averageScore;
        public int emgFlaps;
        public int emgTriggers;
        // "standard (10 Hz)" or "strong (80 Hz)". The strong blink filter
        // lowers every reading, so the progress table shows which was used.
        public string blinkFilter = "";
        // True when this entry was rebuilt from an older session that had no
        // session_results.json (only its meditation and calibration files).
        public bool fromOlderSession;
    }

    [Serializable]
    public class RoundRecord
    {
        public int round;
        public int score;
        public float startSeconds;
        public float durationSeconds;
        public int flaps;
        public string endReason = "";
        public DifficultySettings difficulty;
    }
}
