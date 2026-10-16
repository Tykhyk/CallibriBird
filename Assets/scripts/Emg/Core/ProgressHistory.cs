using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CallibriEmg
{
    // Picks out one person's earlier sessions for the progress comparison.
    // The file reading lives in the session manager; this part is plain C#
    // so the matching rules can be unit-tested.
    //
    // Sessions match on the person's ID (patientId). Sessions saved before
    // IDs existed fall back to full name + date of birth.
    public static class ProgressHistory
    {
        public const string SimulatedKind = "Simulated";
        public const string StandardFilterLabel = "standard (10 Hz)";
        public const string StrongFilterLabel = "strong (80 Hz)";

        // A patient is matched on full name and date of birth together. The
        // name alone could belong to two people; the date of birth alone
        // even more so. Case and extra spaces are ignored so "jane
        // smith" matches "Jane  Smith".
        public static bool SamePatient(string nameA, string dobA, string nameB, string dobB)
        {
            string a = NormalizeName(nameA);
            if (a.Length == 0)
            {
                return false;
            }
            return a == NormalizeName(nameB) && (dobA ?? "").Trim() == (dobB ?? "").Trim();
        }

        public static string NormalizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "";
            }
            StringBuilder s = new StringBuilder(name.Length);
            bool space = false;
            foreach (char c in name.Trim())
            {
                if (char.IsWhiteSpace(c))
                {
                    space = true;
                    continue;
                }
                if (space)
                {
                    s.Append(' ');
                    space = false;
                }
                s.Append(char.ToLowerInvariant(c));
            }
            return s.ToString();
        }

        public static string FilterLabel(string appliedFilters)
        {
            if (string.IsNullOrEmpty(appliedFilters))
            {
                return "";
            }
            if (appliedFilters.Contains("80Hz"))
            {
                return StrongFilterLabel;
            }
            if (appliedFilters.Contains("10Hz"))
            {
                return StandardFilterLabel;
            }
            return appliedFilters.StartsWith("simulated", StringComparison.Ordinal) ? "simulated" : appliedFilters;
        }

        public static bool SamePerson(SessionResults a, SessionResults b)
        {
            if (!string.IsNullOrEmpty(a.patientId) && !string.IsNullOrEmpty(b.patientId))
            {
                return a.patientId == b.patientId;
            }
            return SamePatient(a.fullName, a.dateOfBirth, b.fullName, b.dateOfBirth);
        }

        // Simulated sessions are fake demo data: they are only ever compared
        // with other simulated sessions, never with real measurements.
        public static bool IsSimulated(SessionResults results)
        {
            if (!string.IsNullOrEmpty(results.sourceKind))
            {
                return results.sourceKind == SimulatedKind;
            }
            return results.blinkFilter == "simulated";
        }

        // The current session's earlier sessions (same person, same kind of
        // sensor), oldest first, followed by the current session itself.
        // Entries with the current session's ID are skipped so a re-run never
        // lists it twice.
        public static List<SessionResults> Build(IEnumerable<SessionResults> others, SessionResults current)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }
            List<SessionResults> matches = new List<SessionResults>();
            if (others != null)
            {
                foreach (SessionResults other in others)
                {
                    if (other == null || other.sessionId == current.sessionId)
                    {
                        continue;
                    }
                    if (SamePerson(other, current) && IsSimulated(other) == IsSimulated(current))
                    {
                        matches.Add(other);
                    }
                }
            }
            matches.Sort((x, y) => StartTime(x).CompareTo(StartTime(y)));
            matches.Add(current);
            return matches;
        }

        private static DateTime StartTime(SessionResults results)
        {
            if (DateTime.TryParse(results.startedAt, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime time))
            {
                return time;
            }
            return DateTime.MinValue;
        }
    }
}
