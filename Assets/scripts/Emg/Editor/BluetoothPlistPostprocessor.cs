using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using Debug = UnityEngine.Debug;

namespace CallibriEmg.Editor
{
    // macOS kills any app that touches Bluetooth without explaining why in
    // its Info.plist. Unity has no Player Settings field for this on macOS,
    // so the two keys from BrainBit's install guide are added after every
    // macOS build.
    //
    // The plist is edited with Apple's own plutil tool. .NET's XmlDocument
    // rewrites the plist header in a form macOS can no longer read.
    //
    // It also adds the copyright line (NSHumanReadableCopyright), which
    // Finder shows under "Get Info", so the creator's name travels with the
    // built app.
    //
    // Editing Info.plist breaks the app's existing code signature, so the
    // bundle is re-signed locally (ad-hoc) afterwards. Without that, an
    // Apple Silicon Mac refuses to start the app.
    //
    // Only the app itself is re-signed, without --deep. The Callibri library
    // inside it carries BrainBit's notarized Developer ID signature (since
    // 2026-10-30); --deep would replace that with an ad-hoc one.
    public sealed class BluetoothPlistPostprocessor : IPostprocessBuildWithReport
    {
        private const string UsageText = "CallibriBird connects to the Callibri muscle sensor over Bluetooth to record forehead muscle activity during the session.";

        public int callbackOrder => 100;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.StandaloneOSX)
            {
                return;
            }

            string appPath = report.summary.outputPath;
            string plistPath = Path.Combine(appPath, "Contents", "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogError("Bluetooth plist step: Info.plist not found at " + plistPath + ". The built app will not be allowed to use Bluetooth.");
                return;
            }

            bool ok = Run("/usr/bin/plutil", "-replace NSBluetoothAlwaysUsageDescription -string " + Quote(UsageText) + " " + Quote(plistPath))
                && Run("/usr/bin/plutil", "-replace NSBluetoothPeripheralUsageDescription -string " + Quote(UsageText) + " " + Quote(plistPath))
                && Run("/usr/bin/plutil", "-replace NSHumanReadableCopyright -string " + Quote(CallibriEmg.Credits.Copyright) + " " + Quote(plistPath))
                && Run("/usr/bin/plutil", "-lint " + Quote(plistPath));
            if (!ok)
            {
                return;
            }
            Debug.Log("Bluetooth plist step: added Bluetooth usage descriptions and the copyright line to " + plistPath);

            if (Run("/usr/bin/codesign", "--force --sign - " + Quote(appPath)))
            {
                Debug.Log("Bluetooth plist step: re-signed " + appPath + " for this Mac (ad-hoc).");
            }
        }

        private static bool Run(string tool, string arguments)
        {
            ProcessStartInfo start = new ProcessStartInfo
            {
                FileName = tool,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using (Process process = Process.Start(start))
            {
                string output = process.StandardOutput.ReadToEnd();
                string errors = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    Debug.LogError("Bluetooth plist step: " + Path.GetFileName(tool) + " failed (exit " + process.ExitCode + "): " + errors + output);
                    return false;
                }
            }
            return true;
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }
    }
}
