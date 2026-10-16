using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace CallibriEmg.Tests
{
    public sealed class EnvelopeCalculatorTests
    {
        [Test]
        public void SineWaveRmsIsAmplitudeOverRootTwo()
        {
            // 50 Hz at 1000 Hz sampling: a 100-sample window holds exactly
            // 5 full cycles, so the RMS is exact.
            EnvelopeCalculator envelope = EnvelopeCalculator.ForSampleRate(1000, 100);
            double last = 0;
            for (int i = 0; i < 1000; i++)
            {
                last = envelope.Add(100.0 * Math.Sin(2 * Math.PI * 50 * i / 1000.0));
            }
            Assert.That(last, Is.EqualTo(100.0 / Math.Sqrt(2)).Within(0.01));
        }

        [Test]
        public void ConstantSignalOfSwingingSignGivesItsMagnitude()
        {
            EnvelopeCalculator envelope = new EnvelopeCalculator(10);
            double last = 0;
            for (int i = 0; i < 50; i++)
            {
                last = envelope.Add(i % 2 == 0 ? 30.0 : -30.0);
            }
            Assert.That(last, Is.EqualTo(30.0).Within(1e-9));
        }

        [Test]
        public void WarmUpAveragesOnlyTheSamplesSeenSoFar()
        {
            EnvelopeCalculator envelope = new EnvelopeCalculator(100);
            Assert.That(envelope.Add(10.0), Is.EqualTo(10.0).Within(1e-9));
        }

        [Test]
        public void NaNSamplesDoNotPoisonTheEnvelope()
        {
            EnvelopeCalculator envelope = new EnvelopeCalculator(4);
            envelope.Add(double.NaN);
            double last = 0;
            for (int i = 0; i < 4; i++)
            {
                last = envelope.Add(5.0);
            }
            Assert.That(last, Is.EqualTo(5.0).Within(1e-9));
        }
    }

    public sealed class FlapDetectorTests
    {
        private const long Refractory = 250;

        [Test]
        public void OneSustainedSqueezeGivesOneFlap()
        {
            FlapDetector detector = new FlapDetector(50, 25, Refractory);
            int flaps = Feed(detector, 0, Repeat(5, 200), Repeat(80, 1000), Repeat(5, 200));
            Assert.That(flaps, Is.EqualTo(1));
        }

        [Test]
        public void TwoSeparateSqueezesGiveTwoFlaps()
        {
            FlapDetector detector = new FlapDetector(50, 25, Refractory);
            int flaps = Feed(detector, 0, Repeat(80, 300), Repeat(5, 400), Repeat(80, 300));
            Assert.That(flaps, Is.EqualTo(2));
        }

        [Test]
        public void WobbleBetweenReleaseAndTriggerDoesNotRetrigger()
        {
            // The envelope dips below the trigger but never below the release
            // level, so it's still one squeeze.
            FlapDetector detector = new FlapDetector(50, 25, Refractory);
            List<double> wobble = new List<double>();
            for (int i = 0; i < 20; i++)
            {
                wobble.AddRange(Repeat(i % 2 == 0 ? 60 : 35, 100));
            }
            Assert.That(Feed(detector, 0, wobble), Is.EqualTo(1));
        }

        [Test]
        public void QuickReleaseInsideRefractoryTimeDoesNotRetrigger()
        {
            FlapDetector detector = new FlapDetector(50, 25, Refractory);
            // Up for 50 samples, fully down for 50, up again: all within
            // 250 samples of the first flap, so it must not count twice.
            int flaps = Feed(detector, 0, Repeat(80, 50), Repeat(5, 50), Repeat(80, 50));
            Assert.That(flaps, Is.EqualTo(1));
        }

        [Test]
        public void DisarmedDetectorWaitsForReleaseFirst()
        {
            FlapDetector detector = new FlapDetector(50, 25, Refractory);
            detector.Disarm();
            // Still squeezing from before: no flap until the forehead relaxes.
            Assert.That(Feed(detector, 0, Repeat(80, 500)), Is.EqualTo(0));
            Assert.That(Feed(detector, 500, Repeat(5, 100), Repeat(80, 100)), Is.EqualTo(1));
        }

        [Test]
        public void BurstShorterThanMinimumHoldIsIgnored()
        {
            // A 100 ms blink above the trigger, then a 200 ms squeeze, with
            // a 150 ms minimum hold: only the squeeze counts.
            FlapDetector detector = new FlapDetector(50, 25, Refractory, 150);
            Assert.That(Feed(detector, 0, Repeat(80, 100), Repeat(5, 100)), Is.EqualTo(0));
            Assert.That(Feed(detector, 200, Repeat(80, 200)), Is.EqualTo(1));
        }

        [Test]
        public void FlapFiresExactlyWhenTheHoldIsReached()
        {
            FlapDetector detector = new FlapDetector(50, 25, Refractory, 150);
            long fired = -1;
            for (long i = 0; i < 400; i++)
            {
                if (detector.Process(80, i) && fired < 0)
                {
                    fired = i;
                }
            }
            Assert.That(fired, Is.EqualTo(149));
        }

        [Test]
        public void HoldIsMeasuredInSamplesNotCalls()
        {
            // Fed once per frame (16 samples apart), as the calibration
            // practice light does: the flap must still wait ~150 samples,
            // not 150 calls.
            FlapDetector detector = new FlapDetector(50, 25, Refractory, 150);
            long fired = -1;
            for (long i = 0; i < 1000 && fired < 0; i += 16)
            {
                if (detector.Process(80, i))
                {
                    fired = i;
                }
            }
            Assert.That(fired, Is.EqualTo(160));
        }

        [Test]
        public void ReleaseAboveTriggerIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new FlapDetector(20, 30, Refractory));
        }

        private static IEnumerable<double> Repeat(double value, int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return value;
            }
        }

        private static int Feed(FlapDetector detector, long startIndex, params IEnumerable<double>[] segments)
        {
            int flaps = 0;
            long index = startIndex;
            foreach (IEnumerable<double> segment in segments)
            {
                foreach (double value in segment)
                {
                    if (detector.Process(value, index))
                    {
                        flaps++;
                    }
                    index++;
                }
            }
            return flaps;
        }
    }

    public sealed class CalibrationTests
    {
        [Test]
        public void ThresholdsFollowTheDocumentedFormula()
        {
            List<float> rest = new List<float>();
            for (int i = 0; i < 4000; i++)
            {
                rest.Add(5f);
            }
            // Values 0..100: the 95th percentile is exactly 95.
            List<float> squeeze = new List<float>();
            for (int i = 0; i <= 100; i++)
            {
                squeeze.Add(i);
            }

            CalibrationResult result = CalibrationCalculator.Compute(rest, squeeze, 0.5f);

            Assert.That(result.valid, Is.True);
            Assert.That(result.restUv, Is.EqualTo(5f).Within(1e-4));
            Assert.That(result.maxUv, Is.EqualTo(95f).Within(1e-4));
            Assert.That(result.onUv, Is.EqualTo(5f + 0.5f * 90f).Within(1e-4));
            Assert.That(result.offUv, Is.EqualTo(5f + 0.25f * 90f).Within(1e-4));
        }

        [Test]
        public void SqueezeBarelyAboveRestIsInvalid()
        {
            List<float> rest = new List<float> { 10f, 10f, 10f };
            List<float> squeeze = new List<float> { 12f, 13f, 14f };
            Assert.That(CalibrationCalculator.Compute(rest, squeeze, 0.5f).valid, Is.False);
        }

        [Test]
        public void ChangingKMovesOnlyTheTrigger()
        {
            CalibrationResult result = new CalibrationResult { restUv = 10f, maxUv = 110f };
            result.ApplyThresholdFraction(0.7f);
            Assert.That(result.onUv, Is.EqualTo(80f).Within(1e-4));
            Assert.That(result.offUv, Is.EqualTo(35f).Within(1e-4));
        }
    }

    public sealed class MeditationAnalyzerTests
    {
        private const int Rate = 1000;

        [Test]
        public void RelaxingOverTimeGivesNegativeChange()
        {
            float[] envelope = Constant(30, 10f);
            for (int i = 15 * Rate; i < 30 * Rate; i++)
            {
                envelope[i] = 5f;
            }
            MeditationResult result = MeditationAnalyzer.Analyze(envelope, Rate);

            Assert.That(result.valid, Is.True);
            Assert.That(result.change_pct, Is.EqualTo(-50f).Within(0.01f));
            Assert.That(result.lowest_1s_uV, Is.EqualTo(5f).Within(1e-4));
        }

        [Test]
        public void SettlingPeriodIsExcluded()
        {
            float[] envelope = Constant(30, 4f);
            for (int i = 0; i < 5 * Rate; i++)
            {
                envelope[i] = 500f;
            }
            Assert.That(MeditationAnalyzer.Analyze(envelope, Rate).mean_uV, Is.EqualTo(4f).Within(1e-4));
        }

        [Test]
        public void CountsBurstsLongerThan100Milliseconds()
        {
            float[] envelope = Constant(30, 4f);
            AddBurst(envelope, 8.0, 0.15);
            AddBurst(envelope, 14.0, 0.2);
            AddBurst(envelope, 22.0, 0.12);
            AddBurst(envelope, 26.0, 0.05);
            Assert.That(MeditationAnalyzer.Analyze(envelope, Rate).tension_spikes, Is.EqualTo(3));
        }

        [Test]
        public void TooShortRecordingIsInvalid()
        {
            Assert.That(MeditationAnalyzer.Analyze(Constant(10, 4f), Rate).valid, Is.False);
        }

        [Test]
        public void CalibrationFillsRelativeMetrics()
        {
            MeditationResult result = MeditationAnalyzer.Analyze(Constant(30, 6f), Rate);
            result.ApplyCalibration(new CalibrationResult { valid = true, restUv = 8f, maxUv = 120f });
            Assert.That(result.pct_of_max, Is.EqualTo(5f).Within(1e-4));
            Assert.That(result.ratio_to_calib_rest, Is.EqualTo(0.75f).Within(1e-4));
        }

        private static float[] Constant(int seconds, float value)
        {
            float[] values = new float[seconds * Rate];
            for (int i = 0; i < values.Length; i++)
            {
                values[i] = value;
            }
            return values;
        }

        private static void AddBurst(float[] envelope, double atSecond, double lengthSeconds)
        {
            int start = (int)(atSecond * Rate);
            int end = start + (int)(lengthSeconds * Rate);
            for (int i = start; i < end; i++)
            {
                envelope[i] = 40f;
            }
        }
    }

    public sealed class IntakeValidationTests
    {
        private static readonly DateTime Today = new DateTime(2026, 10, 14);

        [Test]
        public void ValidFormPasses()
        {
            IntakeData intake = new IntakeData { fullName = "Jane Smith", dateOfBirth = "04/23/1985", targetMuscle = "Frontalis" };
            Assert.That(intake.Validate(Today), Is.Empty);
        }

        [TestCase("1985-04-23")]
        [TestCase("23/04/1985")]
        [TestCase("13/01/1985")]
        [TestCase("")]
        public void WrongDateFormatIsRejected(string dob)
        {
            IntakeData intake = new IntakeData { fullName = "Jane Smith", dateOfBirth = dob, targetMuscle = "Frontalis" };
            Assert.That(intake.Validate(Today), Is.Not.Empty);
        }

        [Test]
        public void FutureBirthDateIsRejected()
        {
            IntakeData intake = new IntakeData { fullName = "Jane Smith", dateOfBirth = "01/01/2030", targetMuscle = "Frontalis" };
            Assert.That(intake.Validate(Today), Is.Not.Empty);
        }

        [Test]
        public void MissingNameIsRejected()
        {
            IntakeData intake = new IntakeData { fullName = "  ", dateOfBirth = "04/23/1985", targetMuscle = "Frontalis" };
            Assert.That(intake.Validate(Today), Is.Not.Empty);
        }
    }

    public sealed class SessionNamingTests
    {
        private static readonly DateTime Started = new DateTime(2026, 10, 16, 14, 32, 5);

        [Test]
        public void FolderIsNameCalibriDate()
        {
            Assert.That(SessionNaming.FolderNameFor("Jane", Started), Is.EqualTo("Jane_Calibri_2026-10-16"));
            Assert.That(SessionNaming.FolderNameFor("Jane Smith", Started), Is.EqualTo("Jane_Smith_Calibri_2026-10-16"));
        }

        [TestCase("Jane Calibri")]
        [TestCase("jane calibri")]
        public void CalibriIsNotRepeated(string typed)
        {
            Assert.That(SessionNaming.FolderNameFor(typed, Started), Does.Not.Contain("Calibri_Calibri").IgnoreCase);
            Assert.That(SessionNaming.FolderNameFor(typed, Started), Does.EndWith("_2026-10-16"));
        }

        [Test]
        public void FolderBeforeIntakeIsReadable()
        {
            Assert.That(SessionNaming.FolderNameFor(null, Started), Is.EqualTo("Patient_Calibri_2026-10-16"));
        }

        [TestCase("  Jane   Smith  ", "Jane_Smith")]
        [TestCase("Jane/Smith: 2", "JaneSmith_2")]
        [TestCase("Anne-Marie O'Neil", "Anne-Marie_ONeil")]
        [TestCase("Олег Петренко", "Олег_Петренко")]
        [TestCase("../../etc", "etc")]
        public void UnsafeCharactersAreRemoved(string typed, string expected)
        {
            Assert.That(SessionNaming.SanitizeName(typed), Is.EqualTo(expected));
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("///")]
        public void EmptyNamesFallBack(string typed)
        {
            Assert.That(SessionNaming.SanitizeName(typed), Is.EqualTo(SessionNaming.FallbackName));
        }

        [Test]
        public void VeryLongNamesAreShortened()
        {
            Assert.That(SessionNaming.SanitizeName(new string('a', 200)).Length, Is.EqualTo(SessionNaming.MaxNameLength));
        }
    }

    public sealed class DifficultyTests
    {
        [TestCase(0.8f)]
        [TestCase(2.0f)]
        [TestCase(3.0f)]
        public void JumpHeightStaysTheSameAtEveryPace(float flapSeconds)
        {
            DifficultySettings d = new DifficultySettings { flapSeconds = flapSeconds };
            float g = d.GravityUnitsPerSecondSquared;
            float v = d.JumpVelocity;
            // Rise time is half the flap interval, and the jump peaks at JumpHeight.
            Assert.That(2f * v / g, Is.EqualTo(flapSeconds).Within(1e-4));
            Assert.That(v * v / (2f * g), Is.EqualTo(DifficultySettings.JumpHeight).Within(1e-4));
        }

        [Test]
        public void NormalPaceIsAboutThirtySqueezesAMinute()
        {
            Assert.That(new DifficultySettings().SqueezesPerMinute, Is.EqualTo(30f).Within(1e-3));
        }
    }

    public sealed class StatsTests
    {
        [Test]
        public void PercentileInterpolatesBetweenRanks()
        {
            List<float> values = new List<float> { 1f, 2f, 3f, 4f, 5f };
            Assert.That(Stats.Percentile(values, 0, values.Count, 50), Is.EqualTo(3.0).Within(1e-9));
            Assert.That(Stats.Percentile(values, 0, values.Count, 25), Is.EqualTo(2.0).Within(1e-9));
            Assert.That(Stats.Percentile(values, 0, values.Count, 90), Is.EqualTo(4.6).Within(1e-6));
        }

        [Test]
        public void EmptyRangeThrows()
        {
            List<float> values = new List<float> { 1f };
            Assert.Throws<ArgumentOutOfRangeException>(() => Stats.Mean(values, 0, 0));
        }
    }

    public sealed class TriggerFloorTests
    {
        private static List<float> Alternating(float a, float b, int count)
        {
            List<float> values = new List<float>(count);
            for (int i = 0; i < count; i++)
            {
                values.Add(i % 2 == 0 ? a : b);
            }
            return values;
        }

        private static List<float> Constant(float value, int count)
        {
            List<float> values = new List<float>(count);
            for (int i = 0; i < count; i++)
            {
                values.Add(value);
            }
            return values;
        }

        [Test]
        public void NoisyRestRaisesTheTrigger()
        {
            // Rest 20 ± 10 µV, so the floor is 20 + 3 × 10 = 50. With k = 0.3
            // and a strongest squeeze of 100, k alone would give 44.
            CalibrationResult result = CalibrationCalculator.Compute(Alternating(10f, 30f, 4000), Constant(100f, 2000), 0.3f);
            Assert.That(result.valid, Is.True);
            Assert.That(result.restSdUv, Is.EqualTo(10f).Within(1e-3));
            Assert.That(result.onUv, Is.EqualTo(50f).Within(1e-3));
            Assert.That(result.triggerRaisedByFloor, Is.True);
            Assert.That(result.offUv, Is.EqualTo(40f).Within(1e-3));
        }

        [Test]
        public void QuietRestKeepsTheTriggerFromK()
        {
            CalibrationResult result = CalibrationCalculator.Compute(Constant(10f, 4000), Constant(100f, 2000), 0.5f);
            Assert.That(result.onUv, Is.EqualTo(55f).Within(1e-3));
            Assert.That(result.triggerRaisedByFloor, Is.False);
        }

        [Test]
        public void FloorNeverGoesAboveNinetyPercentOfTheRange()
        {
            // Floor would be 50, but the strongest squeeze is only 40, so the
            // trigger stops at 20 + 0.9 × 20 = 38 and stays reachable.
            CalibrationResult result = CalibrationCalculator.Compute(Alternating(10f, 30f, 4000), Constant(40f, 2000), 0.5f);
            Assert.That(result.valid, Is.True);
            Assert.That(result.onUv, Is.EqualTo(38f).Within(1e-3));
            Assert.That(result.offUv, Is.LessThanOrEqualTo(result.onUv));
        }
    }

    public sealed class StrengthAnalyzerTests
    {
        // 5 s at 1000 Hz: a 0.5 s ramp up to 200 µV, held to 4 s, then 150 µV
        // for the last second (the squeeze fades).
        private static List<float> FadingSqueeze()
        {
            List<float> values = new List<float>(5000);
            for (int i = 0; i < 5000; i++)
            {
                values.Add(i < 500 ? 200f * i / 500f : i < 4000 ? 200f : 150f);
            }
            return values;
        }

        [Test]
        public void TrialMetricsFollowTheSqueeze()
        {
            StrengthTrialResult trial = StrengthAnalyzer.AnalyzeTrial(FadingSqueeze(), 1000, 2);
            Assert.That(trial.valid, Is.True);
            Assert.That(trial.trial, Is.EqualTo(2));
            Assert.That(trial.steadyUv, Is.EqualTo(200f).Within(1e-3));
            Assert.That(trial.peakUv, Is.EqualTo(200f).Within(1e-3));
            Assert.That(trial.burstDetected, Is.False);
            Assert.That(trial.fadePct, Is.EqualTo(-25f).Within(1e-3));
            Assert.That(trial.timeToSteadyMs, Is.EqualTo(450));
        }

        [Test]
        public void ShortBurstDoesNotCountAsStrength()
        {
            // Held at 50 µV, with an 0.8 s burst to 300 µV in the middle, like
            // the blink / eye-squeeze bursts seen with the real sensor.
            List<float> values = new List<float>(5000);
            for (int i = 0; i < 5000; i++)
            {
                values.Add(i < 300 ? 5f : i >= 2500 && i < 3300 ? 300f : 50f);
            }
            StrengthTrialResult trial = StrengthAnalyzer.AnalyzeTrial(values, 1000, 1);
            Assert.That(trial.steadyUv, Is.EqualTo(50f).Within(1e-3));
            Assert.That(trial.peakUv, Is.EqualTo(300f).Within(1e-3));
            Assert.That(trial.burstDetected, Is.True);
        }

        [Test]
        public void TooShortTrialIsInvalid()
        {
            List<float> values = new List<float>();
            for (int i = 0; i < 1000; i++)
            {
                values.Add(100f);
            }
            Assert.That(StrengthAnalyzer.AnalyzeTrial(values, 1000, 1).valid, Is.False);
        }

        [Test]
        public void SummaryPicksTheStrongestTrialAndComparesRest()
        {
            // Squeeze 1 has the highest short peak, but squeeze 2 held more:
            // the steady level decides.
            List<StrengthTrialResult> trials = new List<StrengthTrialResult>
            {
                new StrengthTrialResult { trial = 1, valid = true, steadyUv = 100f, peakUv = 400f, burstDetected = true },
                new StrengthTrialResult { trial = 2, valid = true, steadyUv = 150f, peakUv = 160f },
                new StrengthTrialResult { trial = 3, valid = false, message = "too short" },
            };
            MeditationResult rest = new MeditationResult { valid = true, mean_uV = 15f };
            StrengthTestResult result = StrengthAnalyzer.Summarize(trials, rest);
            Assert.That(result.valid, Is.True);
            Assert.That(result.trialsCompleted, Is.EqualTo(2));
            Assert.That(result.bestTrial, Is.EqualTo(2));
            Assert.That(result.bestSteadyUv, Is.EqualTo(150f));
            Assert.That(result.averageSteadyUv, Is.EqualTo(125f).Within(1e-3));
            Assert.That(result.burstTrials, Is.EqualTo(1));
            Assert.That(result.restTestAvailable, Is.True);
            Assert.That(result.restPctOfStrongest, Is.EqualTo(10f).Within(1e-3));
            Assert.That(result.trials.Length, Is.EqualTo(3));
        }

        [Test]
        public void SummaryWithoutAValidTrialIsInvalid()
        {
            List<StrengthTrialResult> trials = new List<StrengthTrialResult> { new StrengthTrialResult { trial = 1, valid = false } };
            StrengthTestResult result = StrengthAnalyzer.Summarize(trials, null);
            Assert.That(result.valid, Is.False);
            Assert.That(result.restTestAvailable, Is.False);
        }
    }

    public sealed class ProgressHistoryTests
    {
        [Test]
        public void SamePatientIgnoresCaseAndExtraSpaces()
        {
            Assert.That(ProgressHistory.SamePatient("Jane  Smith ", "04/23/1985", "jane smith", "04/23/1985"), Is.True);
            Assert.That(ProgressHistory.SamePatient("Jane Smith", "04/23/1985", "Jane Smith", "04/24/1985"), Is.False);
            Assert.That(ProgressHistory.SamePatient("", "04/23/1985", "", "04/23/1985"), Is.False);
        }

        [Test]
        public void HistoryIsOldestFirstWithTheCurrentSessionLast()
        {
            SessionResults current = new SessionResults { sessionId = "c", fullName = "Ann Lee", dateOfBirth = "01/02/1990", startedAt = "2026-10-16T10:00:00" };
            List<SessionResults> others = new List<SessionResults>
            {
                new SessionResults { sessionId = "b", fullName = "ann lee", dateOfBirth = "01/02/1990", startedAt = "2026-10-10T10:00:00" },
                new SessionResults { sessionId = "a", fullName = "Ann Lee", dateOfBirth = "01/02/1990", startedAt = "2026-10-01T10:00:00" },
                new SessionResults { sessionId = "x", fullName = "Bob Ray", dateOfBirth = "01/02/1990", startedAt = "2026-10-05T10:00:00" },
                new SessionResults { sessionId = "c", fullName = "Ann Lee", dateOfBirth = "01/02/1990", startedAt = "2026-10-16T10:00:00" },
            };
            List<SessionResults> history = ProgressHistory.Build(others, current);
            Assert.That(history.ConvertAll(h => h.sessionId), Is.EqualTo(new List<string> { "a", "b", "c" }));
            Assert.That(history[2], Is.SameAs(current));
        }

        [Test]
        public void PersonIdBeatsTheTypedName()
        {
            // Same person typed three ways, plus a different person who
            // happens to share the name and birthday.
            SessionResults current = new SessionResults { sessionId = "now", patientId = "p1", fullName = "Jane Smith", dateOfBirth = "03/14/1992", sourceKind = "Callibri", startedAt = "2026-10-16T12:00:00" };
            List<SessionResults> others = new List<SessionResults>
            {
                new SessionResults { sessionId = "a", patientId = "p1", fullName = "jane smith", dateOfBirth = "03/14/1992", sourceKind = "Callibri", startedAt = "2026-10-16T11:00:00" },
                new SessionResults { sessionId = "b", patientId = "p1", fullName = "Jane Smith-Lee", dateOfBirth = "03/14/1992", sourceKind = "Callibri", startedAt = "2026-10-16T10:00:00" },
                new SessionResults { sessionId = "c", patientId = "p2", fullName = "Jane Smith", dateOfBirth = "03/14/1992", sourceKind = "Callibri", startedAt = "2026-10-16T09:00:00" },
            };
            List<SessionResults> history = ProgressHistory.Build(others, current);
            Assert.That(history.ConvertAll(h => h.sessionId), Is.EqualTo(new List<string> { "b", "a", "now" }));
        }

        [Test]
        public void SimulatedSessionsNeverMixWithRealOnes()
        {
            SessionResults current = new SessionResults { sessionId = "now", patientId = "p1", sourceKind = "Callibri", startedAt = "2026-10-16T12:00:00" };
            List<SessionResults> others = new List<SessionResults>
            {
                new SessionResults { sessionId = "real", patientId = "p1", sourceKind = "Callibri", startedAt = "2026-10-16T11:00:00" },
                new SessionResults { sessionId = "fake", patientId = "p1", sourceKind = "Simulated", startedAt = "2026-10-16T10:00:00" },
                // An older entry with no sourceKind is recognised by its filter label.
                new SessionResults { sessionId = "fake-old", patientId = "p1", blinkFilter = "simulated", startedAt = "2026-10-16T09:00:00" },
            };
            Assert.That(ProgressHistory.Build(others, current).ConvertAll(h => h.sessionId), Is.EqualTo(new List<string> { "real", "now" }));
        }

        [TestCase("FilterHPFBwhLvl2CutoffFreq80Hz FilterBSFBwhLvl2CutoffFreq55_65Hz", ProgressHistory.StrongFilterLabel)]
        [TestCase("FilterHPFBwhLvl2CutoffFreq10Hz FilterLPFBwhLvl2CutoffFreq400Hz", ProgressHistory.StandardFilterLabel)]
        [TestCase("simulated (no hardware filters)", "simulated")]
        [TestCase("", "")]
        public void FilterLabelNamesTheBlinkFilter(string applied, string expected)
        {
            Assert.That(ProgressHistory.FilterLabel(applied), Is.EqualTo(expected));
        }
    }

    public sealed class SessionReportTests
    {
        private static SessionReportData SampleSession()
        {
            SessionReportData d = new SessionReportData
            {
                SessionId = "Ann_Calibri_2026-10-16",
                DateText = "October 16, 2026, 10:00",
                DurationText = "1 min 30 s",
                Intake = new IntakeData { fullName = "<script>alert(1)</script> & Co", dateOfBirth = "01/02/1990", clinicalIndication = "Bell's palsy" },
                Device = new DeviceInfo { sourceKind = "Simulated", sampleRateHz = 1000, hardwareFilters = "simulated (no hardware filters)" },
                Meditation = new MeditationResult { valid = true, mean_uV = 12f, lowest_1s_uV = 8f, change_pct = -10f },
                RestTest = new MeditationResult { valid = true, mean_uV = 9f, lowest_1s_uV = 6f, change_pct = -5f },
                Strength = new StrengthTestResult
                {
                    valid = true,
                    bestSteadyUv = 160f,
                    bestTrial = 1,
                    averageSteadyUv = 150f,
                    restTestAvailable = true,
                    restPctOfStrongest = 5f,
                    trials = new[] { new StrengthTrialResult { trial = 1, valid = true, steadyUv = 160f, peakUv = 400f, burstDetected = true, fadePct = -8f, timeToSteadyMs = 400 } },
                },
                Calibration = new CalibrationResult { valid = true, restUv = 10f, restSdUv = 2f, maxUv = 150f, thresholdFraction = 0.5f, onUv = 80f, offUv = 45f },
            };
            for (int i = 0; i < 900; i++)
            {
                d.Envelope.Add(i >= 400 && i < 420 ? -1f : 10f + (i % 50));
            }
            AddEvent(d, 0, "phase_start", "intake");
            AddEvent(d, 10000, "phase_start", "meditation");
            AddEvent(d, 11000, "meditation_start", "");
            AddEvent(d, 41000, "meditation_end", "");
            AddEvent(d, 42000, "phase_start", "rest_test");
            AddEvent(d, 42500, "rest_test_start", "");
            AddEvent(d, 72500, "rest_test_end", "");
            AddEvent(d, 73000, "phase_start", "strength_test");
            AddEvent(d, 76000, "strength_trial_start", "1");
            AddEvent(d, 81000, "strength_trial_end", "1");
            AddEvent(d, 82000, "phase_start", "game");
            AddEvent(d, 83000, "round_start", "");
            AddEvent(d, 84000, "flap", "");
            AddEvent(d, 85000, "disconnect", "");
            AddEvent(d, 86000, "reconnect", "");
            AddEvent(d, 88000, "death", "");
            d.Rounds.Add(new RoundRecord { round = 1, score = 3, durationSeconds = 5f, flaps = 1, endReason = "death", difficulty = new DifficultySettings() });
            d.History.Add(new SessionResults { sessionId = "Ann_Calibri_2026-10-01", startedAt = "2026-10-01T10:00:00", hasRestTest = true, restTestMeanUv = 14f, hasStrengthTest = true, strengthBestUv = 120f, rounds = 2, bestScore = 1 });
            d.History.Add(new SessionResults { sessionId = d.SessionId, startedAt = "2026-10-16T10:00:00", hasRestTest = true, restTestMeanUv = 9f, hasStrengthTest = true, strengthBestUv = 160f, rounds = 1, bestScore = 3 });
            return d;
        }

        private static void AddEvent(SessionReportData d, double timeMs, string name, string detail)
        {
            d.Events.Add(new ReportEvent { TimeMs = timeMs, Name = name, Detail = detail });
        }

        [Test]
        public void ReportHasChartsAndLabelledTests()
        {
            string html = SessionReportBuilder.BuildHtml(SampleSession());
            Assert.That(html, Does.StartWith("<!DOCTYPE html>"));
            Assert.That(html.TrimEnd(), Does.EndWith("</html>"));
            Assert.That(html, Does.Contain("<svg"));
            Assert.That(html, Does.Contain(">Rest test<"));
            Assert.That(html, Does.Contain(">Strength test<"));
            Assert.That(html, Does.Contain(">Squeeze 1<"));
            Assert.That(html, Does.Contain("signal lost"));
            Assert.That(html, Does.Contain("Progress across sessions"));
            Assert.That(html, Does.Contain("(this one)"));
            // Strength is the steady level; the burst is shown but ignored.
            Assert.That(html, Does.Contain("160.0 µV"));
            Assert.That(html, Does.Contain("to 400.0 µV, ignored"));
        }

        [Test]
        public void EveryReportCreditsTheCreator()
        {
            Assert.That(SessionReportBuilder.BuildHtml(SampleSession()), Does.Contain("created by Tykhyk Zolotarov"));
            Assert.That(SessionReportBuilder.BuildHtml(new SessionReportData()), Does.Contain("created by Tykhyk Zolotarov"));
        }

        [Test]
        public void EarlierSessionsLinkToTheirReports()
        {
            string html = SessionReportBuilder.BuildHtml(SampleSession());
            Assert.That(html, Does.Contain("<a href=\"../Ann_Calibri_2026-10-01/report.html\">"));
        }

        [Test]
        public void PatientTextIsEscaped()
        {
            string html = SessionReportBuilder.BuildHtml(SampleSession());
            Assert.That(html, Does.Not.Contain("<script>"));
            Assert.That(html, Does.Contain("&lt;script&gt;alert(1)&lt;/script&gt; &amp; Co"));
            Assert.That(html, Does.Contain("Bell&#39;s palsy"));
        }

        [Test]
        public void GapsInTheSignalBreakTheLine()
        {
            // The 2 s gap must start a new path segment ("M") instead of
            // drawing a straight line across the missing data.
            string html = SessionReportBuilder.BuildHtml(SampleSession());
            int pathStart = html.IndexOf("<path d=\"", StringComparison.Ordinal);
            Assert.That(pathStart, Is.GreaterThan(0));
            string path = html.Substring(pathStart, html.IndexOf('"', pathStart + 9) - pathStart);
            Assert.That(path.Split('M').Length - 1, Is.EqualTo(2));
        }

        [Test]
        public void EmptySessionStillBuilds()
        {
            string html = SessionReportBuilder.BuildHtml(new SessionReportData());
            Assert.That(html, Does.Contain("Unnamed patient"));
            Assert.That(html, Does.Contain("first session for this person"));
        }
    }

    public sealed class SampleClockTests
    {
        // Feeds a sensor that really sends trueRateHz in 10-sample packets,
        // observed every 50 ms of session time, for the given seconds.
        private static long Run(SampleClock clock, double trueRateHz, double seconds)
        {
            long index = 0;
            for (double ms = 50; ms <= seconds * 1000.0; ms += 50)
            {
                index = (long)(ms * trueRateHz / 1000.0);
                clock.Observe(index, ms);
            }
            return index;
        }

        [Test]
        public void LearnsTheSensorsRealRate()
        {
            // The Callibri set to 1000 Hz sends about 1008 samples a second.
            SampleClock clock = new SampleClock(1000);
            clock.Restart(0, 0);
            Run(clock, 1008, 120);
            Assert.That(clock.RateHz, Is.EqualTo(1008).Within(0.5));
        }

        [Test]
        public void TimelineStaysOnTheSessionClock()
        {
            // With plain counting, 300 s of a 1008 Hz sensor would read
            // 302.4 s: 2.4 s ahead. The clock keeps it within the first 10 s
            // of drift before the rate was known (80 ms).
            SampleClock clock = new SampleClock(1000);
            clock.Restart(0, 0);
            long index = Run(clock, 1008, 300);
            Assert.That(clock.TimeMs(index), Is.EqualTo(300000).Within(100));
        }

        [Test]
        public void TimeNeverJumpsWhenTheRateIsCorrected()
        {
            SampleClock clock = new SampleClock(1000);
            clock.Restart(0, 0);
            double before = clock.TimeMs(10080);
            clock.Observe(10080, 10000);
            Assert.That(clock.TimeMs(10080), Is.EqualTo(before).Within(1e-6));
            Assert.That(clock.TimeMs(10080 + 1008), Is.EqualTo(before + 1000).Within(1e-6));
        }

        [Test]
        public void ImplausibleRatesAreIgnored()
        {
            // A 30 s freeze with no samples would read as a far-too-slow
            // sensor; it must not become the rate.
            SampleClock clock = new SampleClock(1000);
            clock.Restart(0, 0);
            clock.Observe(1000, 31000);
            Assert.That(clock.RateHz, Is.EqualTo(1000));
        }
    }
}
