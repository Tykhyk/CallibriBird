using System;
using System.Globalization;
using System.Text;

namespace CallibriEmg
{
    // Turns the name typed on the intake form into the session folder's
    // name: name + "Calibri" + date, e.g. "Jane Smith" -> "Jane_Smith_Calibri_2026-10-16"
    // (Tykhyk's format, 2026-10-16). A second session for the same patient
    // on the same day gets "_2" from SessionRecorder rather than
    // overwriting the first.
    public static class SessionNaming
    {
        public const string FallbackName = "Patient";
        public const string Label = "Calibri";
        public const int MaxNameLength = 60;

        // A name that already ends in "Calibri" ("Jane Calibri") doesn't
        // get the label twice.
        public static string FolderNameFor(string fullName, DateTime date)
        {
            string name = SanitizeName(fullName);
            bool hasLabel = name.Equals(Label, StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_" + Label, StringComparison.OrdinalIgnoreCase);
            return (hasLabel ? name : name + "_" + Label) + "_" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        // Keeps letters (any language), digits, '-' and '_'. Spaces become
        // '_', and everything else is dropped: '/' and ':' would break the
        // path on macOS, and the rest causes trouble when the folder is
        // copied to another computer.
        public static string SanitizeName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return FallbackName;
            }

            StringBuilder name = new StringBuilder(fullName.Length);
            bool lastWasSeparator = true;
            foreach (char c in fullName.Trim())
            {
                if (char.IsLetterOrDigit(c) || c == '-')
                {
                    name.Append(c);
                    lastWasSeparator = false;
                }
                else if ((char.IsWhiteSpace(c) || c == '_') && !lastWasSeparator)
                {
                    name.Append('_');
                    lastWasSeparator = true;
                }
            }

            string result = name.ToString().Trim('_', '-');
            if (result.Length > MaxNameLength)
            {
                result = result.Substring(0, MaxNameLength).TrimEnd('_', '-');
            }
            return result.Length > 0 ? result : FallbackName;
        }
    }
}
