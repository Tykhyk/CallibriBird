using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CallibriEmg
{
    // One line of game_events.csv, kept in memory for the report.
    public sealed class ReportEvent
    {
        public double TimeMs;
        public string Phase = "";
        public int Round;
        public string Name = "";
        public string Detail = "";
    }

    // Everything the HTML report needs, gathered by the session manager at
    // the end of a session. Kept free of Unity types so the builder can be
    // unit-tested outside Unity.
    public sealed class SessionReportData
    {
        public string SessionId = "";
        // What the sensor really sent per second (see SampleClock); 0 = unknown.
        public double MeasuredRateHz;
        public string DateText = "";
        public string DurationText = "";
        public IntakeData Intake;
        public DeviceInfo Device;
        public MeditationResult Meditation;
        public MeditationResult RestTest;
        public StrengthTestResult Strength;
        public CalibrationResult Calibration;
        public bool TestsSkipped;
        public List<RoundRecord> Rounds = new List<RoundRecord>();
        public int EmgFlaps;
        public int MouseFlaps;
        public int EmgTriggers;
        public int Disconnects;
        public long LostSamples;
        public float GameAverageUv = -1f;
        public float GamePeakUv = -1f;
        // One point per BucketMs of recording; negative = no data (a gap).
        public List<float> Envelope = new List<float>();
        public double BucketMs = EmgConfig.ReportBucketMs;
        public List<ReportEvent> Events = new List<ReportEvent>();
        // This patient's sessions, oldest first, ending with this one.
        public List<SessionResults> History = new List<SessionResults>();
    }

    // Builds report.html: a self-contained page (no scripts, no internet)
    // with the session's numbers and SVG charts of muscle tension, each test
    // shaded and labelled. It opens in any browser and prints to PDF.
    public static class SessionReportBuilder
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private const double Width = 1000;
        private const double PadLeft = 58;
        private const double PadRight = 18;
        private const double PadTop = 16;
        private const double PadBottom = 30;
        private const int MaxColumns = 900;

        private const string TensionColor = "#E07B12";
        private const string GoodColor = "#2E9E5B";
        private const string BadColor = "#D1433A";
        private const string InkColor = "#222B38";
        private const string MutedColor = "#5F6B7A";
        private const string GridColor = "#E3E7EC";

        private sealed class Band
        {
            public double Start;
            public double End;
            public string Label;
            public string Color;
        }

        private sealed class Marker
        {
            public double Time;
            public string Color;
            public bool Dashed;
            public bool Tick;
            public string Label;
            // Draws the label left of the line. A drop and its return are
            // usually seconds apart, so their labels go on opposite sides.
            public bool LabelLeft;
        }

        private sealed class Level
        {
            public float Value;
            public string Label;
            public string Color;
        }

        public static string BuildHtml(SessionReportData d)
        {
            if (d == null)
            {
                throw new ArgumentNullException(nameof(d));
            }

            StringBuilder s = new StringBuilder(64 * 1024);
            string name = d.Intake != null ? d.Intake.fullName : "";
            s.Append("<!DOCTYPE html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n");
            s.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            s.Append("<title>Session Report – ").Append(Html(name)).Append(" – ").Append(Html(d.DateText)).Append("</title>\n");
            s.Append("<style>\n").Append(Css).Append("</style>\n</head>\n<body>\n<main>\n");

            s.Append("<header><p class=\"eyebrow\">CallibriBird · Session report</p>");
            s.Append("<h1>").Append(Html(string.IsNullOrEmpty(name) ? "Unnamed patient" : name)).Append("</h1>");
            s.Append("<p class=\"meta\">").Append(Html(d.DateText)).Append(" · ").Append(Html(d.DurationText)).Append(" · Session ").Append(Html(d.SessionId)).Append("</p></header>\n");

            AppendKeyNumbers(s, d);
            AppendWholeSession(s, d);
            AppendRestTest(s, d);
            AppendStrengthTest(s, d);
            AppendMeditation(s, d);
            AppendCalibration(s, d);
            AppendGame(s, d);
            AppendProgress(s, d);
            AppendDetails(s, d);

            s.Append("<footer><p class=\"credit\">Made with ").Append(Html(Credits.Product)).Append(", created by ").Append(Html(Credits.Author)).Append(" · ").Append(Html(Credits.Copyright)).Append("</p>");
            s.Append("These are measurements, not a diagnosis. Microvolt values depend on electrode placement and skin contact, so compare them within the same patient and setup, and have a qualified clinician interpret them.</footer>\n");
            s.Append("</main>\n</body>\n</html>\n");
            return s.ToString();
        }

        private static void AppendKeyNumbers(StringBuilder s, SessionReportData d)
        {
            s.Append("<section><h2>Key numbers</h2><div class=\"cards\">");
            Card(s, "Resting tension (eyes closed)", d.RestTest != null && d.RestTest.valid ? Uv(d.RestTest.mean_uV) : "not measured", "Rest test average. Lower = more relaxed.");
            Card(s, "Strongest squeeze", d.Strength != null && d.Strength.valid ? Uv(d.Strength.bestSteadyUv) : "not measured", "Steady level held, best of the strength test.");
            Card(s, "Rest as % of strongest", d.Strength != null && d.Strength.valid && d.Strength.restTestAvailable ? Pct(d.Strength.restPctOfStrongest) : "not measured", "Lower = relaxes further relative to own strength.");
            Card(s, "Meditation average", d.Meditation != null && d.Meditation.valid ? Uv(d.Meditation.mean_uV) : "not measured", "Guided relaxation, eyes open.");
            int best = 0;
            foreach (RoundRecord round in d.Rounds)
            {
                best = Math.Max(best, round.score);
            }
            Card(s, "Game", d.Rounds.Count + " rounds · best " + best, d.EmgFlaps + " muscle flaps, " + d.MouseFlaps + " mouse flaps");
            s.Append("</div></section>\n");
        }

        private static void AppendWholeSession(StringBuilder s, SessionReportData d)
        {
            double end = d.Envelope.Count * d.BucketMs;
            if (end <= 0)
            {
                return;
            }

            List<Band> bands = PhaseBands(d.Events, end);
            List<Marker> markers = new List<Marker>();
            foreach (ReportEvent e in d.Events)
            {
                switch (e.Name)
                {
                    case "round_start":
                        markers.Add(new Marker { Time = e.TimeMs, Color = GoodColor });
                        break;
                    case "death":
                    case "round_stopped":
                        markers.Add(new Marker { Time = e.TimeMs, Color = BadColor });
                        break;
                    case "flap":
                        markers.Add(new Marker { Time = e.TimeMs, Color = InkColor, Tick = true });
                        break;
                }
            }

            markers.AddRange(SignalMarkers(d));

            List<Level> levels = new List<Level>();
            if (d.Calibration != null && d.Calibration.valid)
            {
                levels.Add(new Level { Value = d.Calibration.onUv, Label = "flap trigger", Color = InkColor });
            }

            s.Append("<section><h2>Whole session</h2>");
            s.Append("<p class=\"note\">Muscle tension (100 ms RMS envelope) over the whole session. Shaded areas are the steps of the session. ");
            s.Append("Green lines = round start, red = crash, dashed = Bluetooth drop and return, small ticks at the bottom = bird flaps.</p>");
            AppendChart(s, d, 0, end, 300, bands, markers, levels);
            s.Append("</section>\n");
        }

        private static void AppendRestTest(StringBuilder s, SessionReportData d)
        {
            s.Append("<section><h2>Rest test (eyes closed, 30 s)</h2>");
            if (d.RestTest == null || !d.RestTest.valid)
            {
                s.Append("<p class=\"note\">").Append(d.TestsSkipped ? "The tests were skipped this session." : "Not completed this session.").Append("</p></section>\n");
                return;
            }
            AppendRelaxationTable(s, d.RestTest, d.Strength != null && d.Strength.valid && d.Strength.restTestAvailable ? d.Strength.restPctOfStrongest : -1f, "of strongest squeeze (strength test)");
            AppendPeriodChart(s, d, "rest_test_start", "rest_test_end", "Rest test");
            s.Append("</section>\n");
        }

        private static void AppendStrengthTest(StringBuilder s, SessionReportData d)
        {
            s.Append("<section><h2>Strength test (3 maximum squeezes, 5 s each)</h2>");
            if (d.Strength == null || !d.Strength.valid)
            {
                s.Append("<p class=\"note\">").Append(d.TestsSkipped ? "The tests were skipped this session." : "Not completed this session.").Append("</p></section>\n");
                return;
            }

            s.Append("<table><thead><tr><th>Squeeze</th><th>Steady level (1–4 s)</th><th>Change while holding</th><th>Time to steady level</th><th>Short burst</th></tr></thead><tbody>");
            foreach (StrengthTrialResult trial in d.Strength.trials)
            {
                bool best = trial.valid && trial.trial == d.Strength.bestTrial;
                s.Append("<tr").Append(best ? " class=\"best\"" : "").Append("><td>").Append(trial.trial).Append(best ? " (best)" : "").Append("</td>");
                if (trial.valid)
                {
                    s.Append("<td>").Append(Uv(trial.steadyUv)).Append("</td><td>").Append(Signed(trial.fadePct)).Append("%</td><td>")
                        .Append(trial.timeToSteadyMs >= 0 ? trial.timeToSteadyMs.ToString(Inv) + " ms" : "–").Append("</td><td>")
                        .Append(trial.burstDetected ? "to " + Uv(trial.peakUv) + ", ignored" : "none").Append("</td>");
                }
                else
                {
                    s.Append("<td colspan=\"4\">").Append(Html(trial.message)).Append("</td>");
                }
                s.Append("</tr>");
            }
            s.Append("</tbody></table>");
            s.Append("<p class=\"note\">Strongest squeeze: <b>").Append(Uv(d.Strength.bestSteadyUv)).Append("</b> (squeeze ").Append(d.Strength.bestTrial)
                .Append("), average ").Append(Uv(d.Strength.averageSteadyUv)).Append(". Strength is the steady level held from second 1 to 4 (the median, so a short spike doesn't count). ")
                .Append("A short burst is a moment more than twice the steady level, usually a blink, squeezing the eyes or an electrode moving, which the forehead sensor also picks up. ")
                .Append("A negative change while holding means the squeeze faded before the 5 s were up.</p>");

            List<Band> bands = new List<Band>();
            double first = double.MaxValue;
            double last = double.MinValue;
            double trialStart = -1;
            int trialNumber = 0;
            foreach (ReportEvent e in d.Events)
            {
                if (e.Name == "strength_trial_start")
                {
                    trialStart = e.TimeMs;
                    int.TryParse(e.Detail, NumberStyles.Integer, Inv, out trialNumber);
                }
                else if (e.Name == "strength_trial_end" && trialStart >= 0)
                {
                    bands.Add(new Band { Start = trialStart, End = e.TimeMs, Label = "Squeeze " + trialNumber, Color = "#F5C08F" });
                    first = Math.Min(first, trialStart);
                    last = Math.Max(last, e.TimeMs);
                    trialStart = -1;
                }
            }
            if (bands.Count > 0)
            {
                AppendChart(s, d, Math.Max(0, first - 3000), last + 3000, 220, bands, SignalMarkers(d), new List<Level>());
            }
            s.Append("</section>\n");
        }

        private static void AppendMeditation(StringBuilder s, SessionReportData d)
        {
            s.Append("<section><h2>Meditation (guided, eyes open, 30 s)</h2>");
            if (d.Meditation == null || !d.Meditation.valid)
            {
                s.Append("<p class=\"note\">Not completed this session.</p></section>\n");
                return;
            }
            AppendRelaxationTable(s, d.Meditation, d.Meditation.calibrationApplied ? d.Meditation.pct_of_max : -1f, "of strongest calibration squeeze");
            AppendPeriodChart(s, d, "meditation_start", "meditation_end", "Meditation");
            s.Append("</section>\n");
        }

        private static void AppendCalibration(StringBuilder s, SessionReportData d)
        {
            s.Append("<section><h2>Calibration (game trigger levels)</h2>");
            CalibrationResult c = d.Calibration;
            if (c == null || !c.valid)
            {
                s.Append("<p class=\"note\">Not completed this session.</p></section>\n");
                return;
            }
            s.Append("<table><tbody>");
            Row(s, "Resting level", Uv(c.restUv) + " (wobble ± " + Uv(c.restSdUv) + ")");
            Row(s, "Strongest calibration squeeze (95th percentile)", Uv(c.maxUv));
            Row(s, "Squeeze needed", Pct(c.thresholdFraction * 100f) + " of the way from rest to strongest");
            Row(s, "Flap trigger / release", Uv(c.onUv) + " / " + Uv(c.offUv) + (c.triggerRaisedByFloor ? " (trigger raised to stay above resting noise)" : ""));
            s.Append("</tbody></table>");

            ReportEvent start = null;
            ReportEvent end = null;
            FindLastPeriod(d.Events, "calibration_start", "calibration_end", out start, out end);
            if (start != null && end != null)
            {
                List<Level> levels = new List<Level>
                {
                    new Level { Value = c.onUv, Label = "trigger", Color = InkColor },
                    new Level { Value = c.offUv, Label = "release", Color = "#4A6F9B" },
                };
                AppendChart(s, d, Math.Max(0, start.TimeMs - 1000), end.TimeMs + 1000, 200, new List<Band>(), SignalMarkers(d), levels);
            }
            s.Append("</section>\n");
        }

        private static void AppendGame(StringBuilder s, SessionReportData d)
        {
            s.Append("<section><h2>Game</h2>");
            if (d.Rounds.Count == 0)
            {
                s.Append("<p class=\"note\">No rounds were played.</p></section>\n");
                return;
            }
            s.Append("<table><thead><tr><th>Round</th><th>Score</th><th>Length</th><th>Flaps</th><th>Ended by</th><th>Settings</th></tr></thead><tbody>");
            foreach (RoundRecord round in d.Rounds)
            {
                s.Append("<tr><td>").Append(round.round).Append("</td><td>").Append(round.score).Append("</td><td>").Append(round.durationSeconds.ToString("0.0", Inv)).Append(" s</td><td>")
                    .Append(round.flaps).Append("</td><td>").Append(Html(round.endReason)).Append("</td><td>").Append(Html(round.difficulty != null ? round.difficulty.Describe() : "")).Append("</td></tr>");
            }
            s.Append("</tbody></table><p class=\"note\">Bird flaps: ").Append(d.EmgFlaps + d.MouseFlaps).Append(" (muscle ").Append(d.EmgFlaps).Append(", mouse ").Append(d.MouseFlaps)
                .Append("). Muscle triggers detected: ").Append(d.EmgTriggers).Append(".");
            if (d.GameAverageUv >= 0)
            {
                s.Append(" Tension during play: average ").Append(Uv(d.GameAverageUv)).Append(", peak ").Append(Uv(d.GamePeakUv)).Append(".");
            }
            s.Append("</p></section>\n");
        }

        private static void AppendProgress(StringBuilder s, SessionReportData d)
        {
            s.Append("<section><h2>Progress across sessions</h2>");
            if (d.History.Count < 2)
            {
                s.Append("<p class=\"note\">This is the first session for this person with this kind of sensor. The comparison appears from the second session on.</p></section>\n");
                return;
            }

            s.Append("<table><thead><tr><th>Session</th><th>Rest test</th><th>Meditation</th><th>Strongest squeeze</th><th>Rest % of strongest</th><th>Best score</th><th>Blink filter</th></tr></thead><tbody>");
            for (int i = 0; i < d.History.Count; i++)
            {
                SessionResults h = d.History[i];
                bool current = i == d.History.Count - 1;
                // Earlier sessions link to their own report: every session of
                // a person sits side by side in the person's folder.
                string date = Html(ShortDate(h.startedAt));
                string dateCell = current || string.IsNullOrEmpty(h.sessionId) ? date : "<a href=\"../" + Html(Uri.EscapeDataString(h.sessionId)) + "/report.html\">" + date + "</a>";
                s.Append("<tr").Append(current ? " class=\"best\"" : "").Append("><td>").Append(dateCell).Append(current ? " (this one)" : "").Append("</td>")
                    .Append("<td>").Append(h.hasRestTest ? Uv(h.restTestMeanUv) : "–").Append("</td>")
                    .Append("<td>").Append(h.hasMeditation ? Uv(h.meditationMeanUv) : "–").Append("</td>")
                    .Append("<td>").Append(h.hasStrengthTest ? Uv(h.strengthBestUv) : (h.hasCalibration ? Uv(h.calibrationMaxUv) + "*" : "–")).Append("</td>")
                    .Append("<td>").Append(h.restPctOfStrongest >= 0 ? Pct(h.restPctOfStrongest) : "–").Append("</td>")
                    .Append("<td>").Append(h.rounds > 0 ? h.bestScore.ToString(Inv) : "–").Append("</td>")
                    .Append("<td>").Append(Html(string.IsNullOrEmpty(h.blinkFilter) ? "–" : h.blinkFilter)).Append("</td></tr>");
            }
            s.Append("</tbody></table><p class=\"note\">Click a date to open that session's report. * No strength test that session; the calibration squeeze is shown instead (a rougher measure). ");
            s.Append("Falling resting tension and a rising strongest squeeze generally mean the muscle relaxes and activates better. ");
            s.Append("Only compare sessions with the same blink filter and electrode placement: the strong filter lowers every reading.</p>");

            s.Append("<div class=\"two\"><div><h3>Resting tension (µV)</h3>");
            AppendTrendChart(s, d.History, h => h.hasRestTest ? h.restTestMeanUv : -1f, "Rest test", "#3B82C4", h => h.hasMeditation ? h.meditationMeanUv : -1f, "Meditation", "#2E9E8F");
            s.Append("</div><div><h3>Strongest squeeze (µV)</h3>");
            AppendTrendChart(s, d.History, h => h.hasStrengthTest ? h.strengthBestUv : -1f, "Strength test", TensionColor, h => h.hasCalibration ? h.calibrationMaxUv : -1f, "Calibration", "#8A94A3");
            s.Append("</div></div></section>\n");
        }

        private static void AppendDetails(StringBuilder s, SessionReportData d)
        {
            s.Append("<section><h2>Person and sensor</h2><table><tbody>");
            if (d.Intake != null)
            {
                Row(s, "Full name", Html(d.Intake.fullName));
                Row(s, "Date of birth", Html(d.Intake.dateOfBirth));
                Row(s, "Target muscle", Html(d.Intake.targetMuscle));
                Row(s, "Clinical indication", Html(string.IsNullOrWhiteSpace(d.Intake.clinicalIndication) ? "(none given)" : d.Intake.clinicalIndication));
            }
            if (d.Device != null)
            {
                Row(s, "Sensor", Html(d.Device.sourceKind + (string.IsNullOrEmpty(d.Device.serialNumber) ? "" : " · serial " + d.Device.serialNumber)));
                Row(s, "Sampling rate", "set " + d.Device.sampleRateHz.ToString(Inv) + " Hz" + (d.MeasuredRateHz > 0 ? ", measured " + d.MeasuredRateHz.ToString("0.0", Inv) + " Hz" : "")
                    + " · envelope " + d.Device.envelopeWindowMs.ToString(Inv) + " ms RMS");
                Row(s, "Hardware filters", Html(string.IsNullOrEmpty(d.Device.hardwareFilters) ? "(none)" : d.Device.hardwareFilters));
            }
            Row(s, "Disconnections / lost samples", d.Disconnects.ToString(Inv) + " / " + d.LostSamples.ToString(Inv));
            s.Append("</tbody></table></section>\n");
        }

        private static void AppendRelaxationTable(StringBuilder s, MeditationResult r, float pctOfMax, string pctLabel)
        {
            s.Append("<table><tbody>");
            Row(s, "Average tension (seconds 5–30)", Uv(r.mean_uV));
            Row(s, "Deepest 1-second relaxation", Uv(r.lowest_1s_uV));
            Row(s, "Change from start to end", Signed(r.change_pct) + "% " + (r.change_pct < 0 ? "(relaxed further over time)" : "(did not relax further)"));
            Row(s, "Steadiness (standard deviation)", Uv(r.steadiness_sd_uV));
            Row(s, "Tension spikes", r.tension_spikes.ToString(Inv));
            if (pctOfMax >= 0)
            {
                Row(s, "Resting tension as % " + pctLabel, Pct(pctOfMax));
            }
            s.Append("</tbody></table>");
        }

        // Chart of one timed period (the last completed run of it), with the
        // unscored settling seconds shaded separately.
        private static void AppendPeriodChart(StringBuilder s, SessionReportData d, string startEvent, string endEvent, string label)
        {
            FindLastPeriod(d.Events, startEvent, endEvent, out ReportEvent start, out ReportEvent end);
            if (start == null || end == null)
            {
                return;
            }
            double settleEnd = Math.Min(end.TimeMs, start.TimeMs + EmgConfig.MeditationSettleSeconds * 1000.0);
            List<Band> bands = new List<Band>
            {
                new Band { Start = start.TimeMs, End = settleEnd, Label = "settling (not scored)", Color = "#DDE3EA" },
                new Band { Start = settleEnd, End = end.TimeMs, Label = label + " (scored)", Color = "#9CC9F5" },
            };
            AppendChart(s, d, Math.Max(0, start.TimeMs - 2000), end.TimeMs + 2000, 200, bands, SignalMarkers(d), new List<Level>());
        }

        // Bluetooth drops and returns, drawn on every chart so a gap in the
        // line (or a repeated squeeze) is explained where it happens.
        private static List<Marker> SignalMarkers(SessionReportData d)
        {
            List<Marker> markers = new List<Marker>();
            foreach (ReportEvent e in d.Events)
            {
                if (e.Name == "disconnect")
                {
                    markers.Add(new Marker { Time = e.TimeMs, Color = BadColor, Dashed = true, Label = "signal lost", LabelLeft = true });
                }
                else if (e.Name == "reconnect")
                {
                    markers.Add(new Marker { Time = e.TimeMs, Color = GoodColor, Dashed = true, Label = "signal back" });
                }
            }
            return markers;
        }

        private static void FindLastPeriod(List<ReportEvent> events, string startEvent, string endEvent, out ReportEvent start, out ReportEvent end)
        {
            start = null;
            end = null;
            ReportEvent open = null;
            foreach (ReportEvent e in events)
            {
                if (e.Name == startEvent)
                {
                    open = e;
                }
                else if (e.Name == endEvent && open != null)
                {
                    start = open;
                    end = e;
                    open = null;
                }
            }
        }

        private static List<Band> PhaseBands(List<ReportEvent> events, double endMs)
        {
            List<Band> bands = new List<Band>();
            ReportEvent current = null;
            foreach (ReportEvent e in events)
            {
                if (e.Name != "phase_start")
                {
                    continue;
                }
                if (current != null)
                {
                    AddPhaseBand(bands, current, e.TimeMs);
                }
                current = e;
            }
            if (current != null)
            {
                AddPhaseBand(bands, current, endMs);
            }
            return bands;
        }

        private static void AddPhaseBand(List<Band> bands, ReportEvent phaseStart, double endMs)
        {
            string label;
            string color;
            switch (phaseStart.Detail)
            {
                case "intake": label = "Intake"; color = "#C9D3E2"; break;
                case "meditation": label = "Meditation"; color = "#A8DFD1"; break;
                case "rest_test": label = "Rest test"; color = "#A9D0F5"; break;
                case "strength_test": label = "Strength test"; color = "#F5C99D"; break;
                case "calibration": label = "Calibration"; color = "#DCC6F2"; break;
                case "game": label = "Game"; color = "#D3EDB4"; break;
                default: return;
            }
            if (endMs > phaseStart.TimeMs)
            {
                bands.Add(new Band { Start = phaseStart.TimeMs, End = endMs, Label = label, Color = color });
            }
        }

        private static void AppendChart(StringBuilder s, SessionReportData d, double fromMs, double toMs, double height, List<Band> bands, List<Marker> markers, List<Level> levels)
        {
            if (toMs <= fromMs)
            {
                return;
            }
            double plotW = Width - PadLeft - PadRight;
            double plotH = height - PadTop - PadBottom;

            int first = Math.Max(0, (int)Math.Floor(fromMs / d.BucketMs));
            int last = Math.Min(d.Envelope.Count, (int)Math.Ceiling(toMs / d.BucketMs));
            int span = Math.Max(1, last - first);
            int perColumn = Math.Max(1, (int)Math.Ceiling(span / (double)MaxColumns));

            List<double> times = new List<double>();
            List<float> means = new List<float>();
            List<float> peaks = new List<float>();
            float highest = 0f;
            for (int i = first; i < last; i += perColumn)
            {
                double sum = 0;
                int n = 0;
                float peak = -1f;
                for (int j = i; j < Math.Min(last, i + perColumn); j++)
                {
                    float v = d.Envelope[j];
                    if (v < 0)
                    {
                        continue;
                    }
                    sum += v;
                    n++;
                    peak = Math.Max(peak, v);
                }
                times.Add((i + perColumn / 2.0) * d.BucketMs);
                means.Add(n > 0 ? (float)(sum / n) : -1f);
                peaks.Add(peak);
                highest = Math.Max(highest, peak);
            }
            foreach (Level level in levels)
            {
                highest = Math.Max(highest, level.Value);
            }
            double yMax = 4 * NiceStep(Math.Max(10.0, highest * 1.08) / 4);

            Func<double, double> X = t => PadLeft + (t - fromMs) / (toMs - fromMs) * plotW;
            Func<double, double> Y = v => PadTop + plotH - Math.Min(v, yMax) / yMax * plotH;

            s.Append("<svg class=\"chart\" viewBox=\"0 0 ").Append(F(Width)).Append(' ').Append(F(height)).Append("\" role=\"img\" aria-label=\"Muscle tension chart\">");

            foreach (Band band in bands)
            {
                double x0 = Math.Max(PadLeft, X(band.Start));
                double x1 = Math.Min(PadLeft + plotW, X(band.End));
                if (x1 <= x0)
                {
                    continue;
                }
                s.Append("<rect x=\"").Append(F(x0)).Append("\" y=\"").Append(F(PadTop)).Append("\" width=\"").Append(F(x1 - x0)).Append("\" height=\"").Append(F(plotH))
                    .Append("\" fill=\"").Append(band.Color).Append("\" fill-opacity=\"0.55\"/>");
                if (x1 - x0 > 46)
                {
                    s.Append("<text x=\"").Append(F(x0 + 4)).Append("\" y=\"").Append(F(PadTop + 13)).Append("\" class=\"band\">").Append(Html(band.Label)).Append("</text>");
                }
            }

            for (int i = 0; i <= 4; i++)
            {
                double v = yMax * i / 4.0;
                double y = Y(v);
                s.Append("<line x1=\"").Append(F(PadLeft)).Append("\" x2=\"").Append(F(PadLeft + plotW)).Append("\" y1=\"").Append(F(y)).Append("\" y2=\"").Append(F(y))
                    .Append("\" stroke=\"").Append(GridColor).Append("\" stroke-width=\"1\"/>");
                s.Append("<text x=\"").Append(F(PadLeft - 6)).Append("\" y=\"").Append(F(y + 4)).Append("\" class=\"axis\" text-anchor=\"end\">").Append(v.ToString("0", Inv)).Append("</text>");
            }
            s.Append("<text x=\"12\" y=\"").Append(F(PadTop + plotH / 2)).Append("\" class=\"axis\" transform=\"rotate(-90 12 ").Append(F(PadTop + plotH / 2)).Append(")\" text-anchor=\"middle\">µV</text>");

            double stepSeconds = NiceTimeStep((toMs - fromMs) / 1000.0);
            double firstTick = Math.Ceiling(fromMs / 1000.0 / stepSeconds) * stepSeconds;
            for (double t = firstTick; t * 1000.0 <= toMs; t += stepSeconds)
            {
                double x = X(t * 1000.0);
                s.Append("<line x1=\"").Append(F(x)).Append("\" x2=\"").Append(F(x)).Append("\" y1=\"").Append(F(PadTop + plotH)).Append("\" y2=\"").Append(F(PadTop + plotH + 4))
                    .Append("\" stroke=\"").Append(MutedColor).Append("\"/>");
                s.Append("<text x=\"").Append(F(x)).Append("\" y=\"").Append(F(PadTop + plotH + 18)).Append("\" class=\"axis\" text-anchor=\"middle\">").Append(Clock(t)).Append("</text>");
            }

            AppendPath(s, times, peaks, X, Y, TensionColor, 1.0, 0.3);
            AppendPath(s, times, means, X, Y, TensionColor, 1.6, 1.0);

            foreach (Level level in levels)
            {
                double y = Y(level.Value);
                s.Append("<line x1=\"").Append(F(PadLeft)).Append("\" x2=\"").Append(F(PadLeft + plotW)).Append("\" y1=\"").Append(F(y)).Append("\" y2=\"").Append(F(y))
                    .Append("\" stroke=\"").Append(level.Color).Append("\" stroke-width=\"1.2\" stroke-dasharray=\"6 4\"/>");
                s.Append("<text x=\"").Append(F(PadLeft + 4)).Append("\" y=\"").Append(F(y - 4)).Append("\" class=\"axis level\">").Append(Html(level.Label)).Append(' ').Append(Uv(level.Value)).Append("</text>");
            }

            foreach (Marker marker in markers)
            {
                if (marker.Time < fromMs || marker.Time > toMs)
                {
                    continue;
                }
                double x = X(marker.Time);
                if (marker.Tick)
                {
                    s.Append("<line x1=\"").Append(F(x)).Append("\" x2=\"").Append(F(x)).Append("\" y1=\"").Append(F(PadTop + plotH - 7)).Append("\" y2=\"").Append(F(PadTop + plotH))
                        .Append("\" stroke=\"").Append(marker.Color).Append("\" stroke-width=\"1\"/>");
                    continue;
                }
                s.Append("<line x1=\"").Append(F(x)).Append("\" x2=\"").Append(F(x)).Append("\" y1=\"").Append(F(PadTop)).Append("\" y2=\"").Append(F(PadTop + plotH))
                    .Append("\" stroke=\"").Append(marker.Color).Append("\" stroke-width=\"1.2\"").Append(marker.Dashed ? " stroke-dasharray=\"4 3\"" : "").Append("/>");
                if (!string.IsNullOrEmpty(marker.Label))
                {
                    s.Append("<text x=\"").Append(F(marker.LabelLeft ? x - 3 : x + 3)).Append("\" y=\"").Append(F(PadTop + 30)).Append("\" class=\"axis\"")
                        .Append(marker.LabelLeft ? " text-anchor=\"end\"" : "").Append('>').Append(Html(marker.Label)).Append("</text>");
                }
            }

            s.Append("</svg>");
        }

        private static void AppendPath(StringBuilder s, List<double> times, List<float> values, Func<double, double> X, Func<double, double> Y, string color, double width, double opacity)
        {
            StringBuilder path = new StringBuilder();
            bool drawing = false;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] < 0)
                {
                    drawing = false;
                    continue;
                }
                path.Append(drawing ? 'L' : 'M').Append(F(X(times[i]))).Append(' ').Append(F(Y(values[i]))).Append(' ');
                drawing = true;
            }
            if (path.Length == 0)
            {
                return;
            }
            s.Append("<path d=\"").Append(path.ToString().TrimEnd()).Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"").Append(F(width))
                .Append("\" stroke-opacity=\"").Append(F(opacity)).Append("\" stroke-linejoin=\"round\"/>");
        }

        private static void AppendTrendChart(StringBuilder s, List<SessionResults> history, Func<SessionResults, float> first, string firstLabel, string firstColor,
            Func<SessionResults, float> second, string secondLabel, string secondColor)
        {
            const double w = 480;
            const double h = 220;
            const double left = 44;
            const double right = 12;
            const double top = 12;
            const double bottom = 36;
            double plotW = w - left - right;
            double plotH = h - top - bottom;

            float highest = 0f;
            foreach (SessionResults r in history)
            {
                highest = Math.Max(highest, Math.Max(first(r), second(r)));
            }
            double yMax = 4 * NiceStep(Math.Max(5.0, highest * 1.1) / 4);
            int count = history.Count;
            const double inset = 36;
            Func<int, double> X = i => left + inset + (count == 1 ? (plotW - 2 * inset) / 2 : (plotW - 2 * inset) * i / (count - 1));
            Func<double, double> Y = v => top + plotH - v / yMax * plotH;

            s.Append("<svg class=\"chart\" viewBox=\"0 0 ").Append(F(w)).Append(' ').Append(F(h)).Append("\" role=\"img\" aria-label=\"Progress chart\">");
            for (int i = 0; i <= 4; i++)
            {
                double v = yMax * i / 4.0;
                s.Append("<line x1=\"").Append(F(left)).Append("\" x2=\"").Append(F(left + plotW)).Append("\" y1=\"").Append(F(Y(v))).Append("\" y2=\"").Append(F(Y(v)))
                    .Append("\" stroke=\"").Append(GridColor).Append("\"/>");
                s.Append("<text x=\"").Append(F(left - 6)).Append("\" y=\"").Append(F(Y(v) + 4)).Append("\" class=\"axis\" text-anchor=\"end\">").Append(v.ToString("0", Inv)).Append("</text>");
            }
            // With many sessions, label only every n-th point so the dates
            // never overlap (about 80 px per label), and always the latest.
            int labelEvery = Math.Max(1, (int)Math.Ceiling(count * 80.0 / (plotW - 2 * inset)));
            for (int i = 0; i < count; i++)
            {
                bool last = i == count - 1;
                if (!last && (i % labelEvery != 0 || count - 1 - i < labelEvery))
                {
                    continue;
                }
                string anchor = count > 1 && i == 0 ? "start" : count > 1 && i == count - 1 ? "end" : "middle";
                double labelX = anchor == "start" ? X(i) - inset : anchor == "end" ? X(i) + inset : X(i);
                s.Append("<text x=\"").Append(F(labelX)).Append("\" y=\"").Append(F(h - 14)).Append("\" class=\"axis\" text-anchor=\"").Append(anchor).Append("\">").Append(Html(ShortDate(history[i].startedAt))).Append("</text>");
            }
            AppendSeries(s, history, first, X, Y, firstColor);
            AppendSeries(s, history, second, X, Y, secondColor);
            s.Append("</svg><p class=\"legend\"><span style=\"color:").Append(firstColor).Append("\">●</span> ").Append(Html(firstLabel))
                .Append(" &nbsp; <span style=\"color:").Append(secondColor).Append("\">●</span> ").Append(Html(secondLabel)).Append("</p>");
        }

        private static void AppendSeries(StringBuilder s, List<SessionResults> history, Func<SessionResults, float> value, Func<int, double> X, Func<double, double> Y, string color)
        {
            StringBuilder path = new StringBuilder();
            bool drawing = false;
            for (int i = 0; i < history.Count; i++)
            {
                float v = value(history[i]);
                if (v < 0)
                {
                    drawing = false;
                    continue;
                }
                path.Append(drawing ? 'L' : 'M').Append(F(X(i))).Append(' ').Append(F(Y(v))).Append(' ');
                drawing = true;
                s.Append("<circle cx=\"").Append(F(X(i))).Append("\" cy=\"").Append(F(Y(v))).Append("\" r=\"4\" fill=\"").Append(color).Append("\"/>");
            }
            if (path.Length > 0)
            {
                s.Append("<path d=\"").Append(path.ToString().TrimEnd()).Append("\" fill=\"none\" stroke=\"").Append(color).Append("\" stroke-width=\"2\"/>");
            }
        }

        private static void Card(StringBuilder s, string title, string value, string note)
        {
            s.Append("<div class=\"card\"><p class=\"card-title\">").Append(Html(title)).Append("</p><p class=\"card-value\">").Append(Html(value)).Append("</p><p class=\"card-note\">").Append(Html(note)).Append("</p></div>");
        }

        private static void Row(StringBuilder s, string label, string valueHtml)
        {
            s.Append("<tr><th scope=\"row\">").Append(Html(label)).Append("</th><td>").Append(valueHtml).Append("</td></tr>");
        }

        public static string Html(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");
        }

        private static string Uv(float value)
        {
            return value.ToString("0.0", Inv) + " µV";
        }

        private static string Pct(float value)
        {
            return value.ToString("0.0", Inv) + "%";
        }

        private static string Signed(float value)
        {
            return value.ToString("+0.0;-0.0;0.0", Inv);
        }

        private static string F(double value)
        {
            return value.ToString("0.#", Inv);
        }

        private static string Clock(double seconds)
        {
            int total = (int)Math.Round(seconds);
            return (total / 60).ToString(Inv) + ":" + (total % 60).ToString("00", Inv);
        }

        private static string ShortDate(string isoDate)
        {
            if (DateTime.TryParse(isoDate, Inv, DateTimeStyles.RoundtripKind, out DateTime date))
            {
                return date.ToString("MMM d, HH:mm", Inv);
            }
            return isoDate ?? "";
        }

        // Rounds up to 1, 2 or 5 times a power of ten. Used for the gap
        // between axis lines, so every axis label is a round number.
        private static double NiceStep(double value)
        {
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
            foreach (double step in new[] { 1.0, 2.0, 5.0, 10.0 })
            {
                if (step * magnitude >= value)
                {
                    return step * magnitude;
                }
            }
            return 10 * magnitude;
        }

        private static double NiceTimeStep(double spanSeconds)
        {
            foreach (double step in new[] { 1.0, 2.0, 5.0, 10.0, 15.0, 30.0, 60.0, 120.0, 300.0, 600.0, 900.0, 1800.0 })
            {
                if (spanSeconds / step <= 10)
                {
                    return step;
                }
            }
            return 3600.0;
        }

        private const string Css = @"
:root { color-scheme: light; }
* { box-sizing: border-box; }
body { margin: 0; background: #F4F6F8; color: #222B38; font: 15px/1.5 -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif; }
main { max-width: 1080px; margin: 0 auto; padding: 28px 20px 48px; }
header { margin-bottom: 20px; }
.eyebrow { margin: 0; color: #5F6B7A; font-size: 13px; font-weight: 600; letter-spacing: .04em; text-transform: uppercase; }
h1 { margin: 4px 0; font-size: 28px; }
.meta { margin: 0; color: #5F6B7A; }
section { background: #fff; border: 1px solid #E3E7EC; border-radius: 10px; padding: 18px 20px; margin: 16px 0; break-inside: avoid; }
h2 { margin: 0 0 10px; font-size: 19px; }
h3 { margin: 4px 0 6px; font-size: 15px; color: #5F6B7A; }
.note, .legend { color: #5F6B7A; font-size: 13px; margin: 8px 0; }
.cards { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 12px; }
.card { border: 1px solid #E3E7EC; border-radius: 8px; padding: 12px; }
.card-title { margin: 0; color: #5F6B7A; font-size: 13px; }
.card-value { margin: 4px 0; font-size: 22px; font-weight: 700; font-variant-numeric: tabular-nums; }
.card-note { margin: 0; color: #5F6B7A; font-size: 12px; }
table { width: 100%; border-collapse: collapse; font-variant-numeric: tabular-nums; margin: 6px 0; }
th, td { text-align: left; padding: 6px 8px; border-bottom: 1px solid #EEF1F4; vertical-align: top; }
thead th { color: #5F6B7A; font-size: 13px; font-weight: 600; }
tbody th { font-weight: 500; width: 46%; }
tr.best td, tr.best th { background: #FFF6EB; }
td a { color: #2F5F96; }
.chart { width: 100%; height: auto; display: block; margin: 8px 0; }
.chart text { font-size: 11px; fill: #5F6B7A; }
.chart text.band { font-size: 11px; fill: #222B38; font-weight: 600; }
.chart text.level { fill: #222B38; paint-order: stroke; stroke: #fff; stroke-width: 3px; }
.two { display: grid; grid-template-columns: repeat(auto-fit, minmax(320px, 1fr)); gap: 16px; }
footer { color: #5F6B7A; font-size: 12px; margin-top: 20px; }
footer .credit { color: #222B38; font-size: 13px; font-weight: 600; margin: 0 0 6px; }
@media print { body { background: #fff; } section { border-color: #ccc; } }
";
    }
}
