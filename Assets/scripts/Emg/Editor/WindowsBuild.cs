using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CallibriEmg.Editor
{
    // Builds the Windows version for ordinary 64-bit Intel/AMD PCs (x64).
    //
    // On an Apple Silicon Mac, Unity defaults Windows builds to the Mac's own
    // processor family, ARM64. The first Windows build (2026-10-21) came out
    // ARM64 and wouldn't start on Tykhyk's x64 Windows 10 laptop ("This app
    // can't run on your PC"). The Callibri SDK only ships an x64 Windows
    // library, so an ARM64 build couldn't load it on any PC anyway.
    //
    // So the architecture is set explicitly, and the finished .exe is read
    // back. A build for the wrong processor fails loudly instead of being
    // zipped and shipped.
    public static class WindowsBuild
    {
        public const string OutputFolder = "Build/Windows/CallibriBird";
        private const string ExeName = "CallibriBird.exe";
        private const string ArchitectureSetting = "Architecture";
        private const string X64 = "x64";
        // The "machine" field of a Windows executable's header.
        private const ushort MachineX64 = 0x8664;
        private const ushort MachineArm64 = 0xAA64;

        [MenuItem("CallibriBird/Build Windows (64-bit x64)")]
        public static void BuildFromMenu()
        {
            Build(false);
        }

        // For command-line builds with the Editor closed:
        // Unity -batchmode -quit -projectPath <project> -executeMethod CallibriEmg.Editor.WindowsBuild.BuildFromCommandLine
        public static void BuildFromCommandLine()
        {
            Build(true);
        }

        private static void Build(bool exitOnFailure)
        {
            PlayerSetup.Apply();
            BuildTarget previousTarget = EditorUserBuildSettings.activeBuildTarget;

            // Unity has stored this setting under both names over versions,
            // so both are set.
            string windowsName = BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneWindows64);
            foreach (string platform in new[] { windowsName, "Standalone" })
            {
                EditorUserBuildSettings.SetPlatformSettings(platform, ArchitectureSetting, X64);
            }

            // Start from an empty folder, so no file from an earlier build for
            // another processor is left mixed in.
            if (Directory.Exists(OutputFolder))
            {
                Directory.Delete(OutputFolder, true);
            }
            Directory.CreateDirectory(OutputFolder);

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = Path.Combine(OutputFolder, ExeName),
                target = BuildTarget.StandaloneWindows64,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);

            string problem = Check(report, options.scenes.Length);

            // Building switched the Editor to Windows; put it back so Play
            // mode and Mac builds behave as before.
            if (previousTarget != BuildTarget.StandaloneWindows64)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(previousTarget), previousTarget);
            }

            if (problem.Length > 0)
            {
                Debug.LogError("[WindowsBuild] FAILED: " + problem);
                if (exitOnFailure)
                {
                    EditorApplication.Exit(1);
                }
                return;
            }
            Debug.Log(string.Format("[WindowsBuild] OK: x64 build with {0} scenes in {1} ({2:0} MB).", options.scenes.Length, OutputFolder, report.summary.totalSize / 1048576.0));
        }

        // Returns an empty string when the build is good, otherwise what's wrong.
        private static string Check(BuildReport report, int sceneCount)
        {
            if (report.summary.result != BuildResult.Succeeded)
            {
                return "Unity reported " + report.summary.result + " with " + report.summary.totalErrors + " errors.";
            }

            string exe = Path.Combine(OutputFolder, ExeName);
            ushort machine;
            try
            {
                machine = ReadMachine(exe);
            }
            catch (Exception e)
            {
                return "Could not read " + exe + ": " + e.Message;
            }
            if (machine != MachineX64)
            {
                string found = machine == MachineArm64 ? "ARM64" : "0x" + machine.ToString("X4");
                return ExeName + " was built for " + found + ", not x64. It would show \"This app can't run on your PC\" on Intel/AMD laptops.";
            }

            string data = Path.Combine(OutputFolder, "CallibriBird_Data");
            for (int i = 0; i < sceneCount; i++)
            {
                if (!File.Exists(Path.Combine(data, "level" + i)))
                {
                    return "Scene " + i + " is missing from the build.";
                }
            }
            if (!File.Exists(Path.Combine(data, "Plugins", "x86_64", "neurosdk2-x64.dll")))
            {
                return "The Callibri SDK's Windows library (neurosdk2-x64.dll) is missing from the build.";
            }
            return "";
        }

        // A Windows .exe starts with "MZ"; at offset 0x3C it stores where the
        // "PE" header begins, and the 2 bytes after "PE\0\0" name the processor.
        private static ushort ReadMachine(string path)
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
            {
                if (reader.ReadUInt16() != 0x5A4D)
                {
                    throw new InvalidDataException("not a Windows executable");
                }
                reader.BaseStream.Seek(0x3C, SeekOrigin.Begin);
                int peOffset = reader.ReadInt32();
                reader.BaseStream.Seek(peOffset, SeekOrigin.Begin);
                if (reader.ReadUInt32() != 0x00004550)
                {
                    throw new InvalidDataException("missing PE header");
                }
                return reader.ReadUInt16();
            }
        }
    }
}
