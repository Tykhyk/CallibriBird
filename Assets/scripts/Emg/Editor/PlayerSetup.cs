using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CallibriEmg.Editor
{
    // The window and icon settings for both desktop builds, in code. Unity's
    // ProjectSettings file is binary and can't be reviewed in git, so the
    // intended values live here, and every build applies them first.
    //
    // Window: a normal resizable window with the system title bar (close,
    // minimize, maximize), 1280 x 800 to start (Tykhyk, 2026-10-23).
    // WindowMode (Assets/scripts) switches existing installs out of full
    // screen once and shrinks the window to fit small screens.
    //
    // Icon: Assets/Icons/CallibriBird_Icon.png, an original minimal bird
    // drawn for this project (not the Flappy Bird art). Its editable source
    // is CallibriBird_Icon_source.svg.html next to it.
    public static class PlayerSetup
    {
        public const string IconPath = "Assets/Icons/CallibriBird_Icon.png";
        public const string MacOutput = "Build/CallibriBird.app";
        private const string ArchitectureSetting = "Architecture";
        // Both Apple-chip and Intel Macs. Built on an Apple-chip Mac, Unity
        // would otherwise default to Apple-chip only.
        private const string MacUniversal = "x64arm64";
        // CPU types in a Mac executable's header.
        private const uint CpuX64 = 0x01000007;
        private const uint CpuArm64 = 0x0100000C;

        [MenuItem("CallibriBird/Apply Window and Icon Settings")]
        public static void Apply()
        {
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 800;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.allowFullscreenSwitch = true;

            // Icons are shown from 16 to 1024 pixels: keep the source sharp
            // and its transparent corners clean.
            TextureImporter importer = AssetImporter.GetAtPath(IconPath) as TextureImporter;
            if (importer != null)
            {
                importer.textureType = TextureImporterType.Default;
                importer.mipmapEnabled = false;
                importer.alphaIsTransparency = true;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.maxTextureSize = 1024;
                importer.SaveAndReimport();
            }

            Texture2D icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            if (icon == null)
            {
                Debug.LogError("[PlayerSetup] Icon not found at " + IconPath);
            }
            else
            {
                PlayerSettings.SetIcons(NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
            }

            // Settings changed from code stay in memory until saved
            // (project rule 15).
            AssetDatabase.SaveAssets();
            Debug.Log("[PlayerSetup] Window and icon settings applied.");
        }

        [MenuItem("CallibriBird/Build macOS (universal)")]
        public static void BuildMacFromMenu()
        {
            BuildMac(false);
        }

        // Unity -batchmode -quit -projectPath <project> -executeMethod CallibriEmg.Editor.PlayerSetup.BuildMacFromCommandLine
        public static void BuildMacFromCommandLine()
        {
            BuildMac(true);
        }

        private static void BuildMac(bool exitOnFailure)
        {
            Apply();
            BuildTarget previous = EditorUserBuildSettings.activeBuildTarget;

            foreach (string platform in new[] { BuildPipeline.GetBuildTargetName(BuildTarget.StandaloneOSX), "Standalone" })
            {
                EditorUserBuildSettings.SetPlatformSettings(platform, ArchitectureSetting, MacUniversal);
            }

            if (Directory.Exists(MacOutput))
            {
                Directory.Delete(MacOutput, true);
            }
            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = EditorBuildSettings.scenes.Where(scene => scene.enabled).Select(scene => scene.path).ToArray(),
                locationPathName = MacOutput,
                target = BuildTarget.StandaloneOSX,
                targetGroup = BuildTargetGroup.Standalone,
                options = BuildOptions.None,
            };
            // BluetoothPlistPostprocessor runs inside this call, after the
            // player is written.
            BuildReport report = BuildPipeline.BuildPlayer(options);
            string problem = CheckMac(report, options.scenes.Length);

            if (previous != BuildTarget.StandaloneOSX)
            {
                EditorUserBuildSettings.SwitchActiveBuildTarget(BuildPipeline.GetBuildTargetGroup(previous), previous);
            }

            if (problem.Length > 0)
            {
                Debug.LogError("[MacBuild] FAILED: " + problem);
                if (exitOnFailure)
                {
                    EditorApplication.Exit(1);
                }
                return;
            }
            Debug.Log(string.Format("[MacBuild] OK: universal (Apple chip + Intel) build with {0} scenes in {1} ({2:0} MB).", options.scenes.Length, MacOutput, report.summary.totalSize / 1048576.0));
        }

        // Returns an empty string when the build is good, otherwise what's wrong.
        private static string CheckMac(BuildReport report, int sceneCount)
        {
            if (report.summary.result != BuildResult.Succeeded)
            {
                return "Unity reported " + report.summary.result + " with " + report.summary.totalErrors + " errors.";
            }

            string contents = Path.Combine(MacOutput, "Contents");
            string executable = Path.Combine(contents, "MacOS", PlayerSettings.productName);
            try
            {
                uint[] cpus = ReadMacCpuTypes(executable);
                if (!cpus.Contains(CpuX64) || !cpus.Contains(CpuArm64))
                {
                    return "The app isn't universal: it contains " + string.Join(", ", cpus.Select(c => "0x" + c.ToString("X8"))) + ", so it won't run on both Apple-chip and Intel Macs.";
                }
            }
            catch (Exception e)
            {
                return "Could not read " + executable + ": " + e.Message;
            }

            string data = Path.Combine(contents, "Resources", "Data");
            for (int i = 0; i < sceneCount; i++)
            {
                if (!File.Exists(Path.Combine(data, "level" + i)))
                {
                    return "Scene " + i + " is missing from the build.";
                }
            }

            string plist = Path.Combine(contents, "Info.plist");
            if (!File.Exists(plist) || !File.ReadAllText(plist).Contains("NSBluetoothAlwaysUsageDescription"))
            {
                return "Info.plist has no Bluetooth permission text, so macOS would stop the app when it uses Bluetooth.";
            }
            return "";
        }

        // A universal Mac executable starts with the "fat" magic number,
        // followed by one entry per processor type it contains. A
        // single-processor executable starts with its own magic number
        // and its processor type.
        private static uint[] ReadMacCpuTypes(string path)
        {
            using (BinaryReader reader = new BinaryReader(File.OpenRead(path)))
            {
                uint magic = ReadBigEndian(reader);
                if (magic == 0xCAFEBABE)
                {
                    uint count = ReadBigEndian(reader);
                    uint[] cpus = new uint[count];
                    for (int i = 0; i < count; i++)
                    {
                        cpus[i] = ReadBigEndian(reader);
                        // Skip subtype, offset, size and alignment.
                        reader.ReadBytes(16);
                    }
                    return cpus;
                }
                if (magic == 0xCFFAEDFE)
                {
                    // Single 64-bit executable, stored little-endian.
                    return new[] { reader.ReadUInt32() };
                }
                throw new InvalidDataException("not a Mac executable");
            }
        }

        private static uint ReadBigEndian(BinaryReader reader)
        {
            byte[] bytes = reader.ReadBytes(4);
            return (uint)(bytes[0] << 24 | bytes[1] << 16 | bytes[2] << 8 | bytes[3]);
        }
    }
}
