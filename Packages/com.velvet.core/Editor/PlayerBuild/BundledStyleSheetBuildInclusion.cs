using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Velvet.Editor
{
    /// <summary>
    /// Puts <see cref="VelvetRuntimeAssets"/> into PlayerSettings' preloaded assets for the duration of a
    /// build, and takes it out again afterwards.
    /// </summary>
    /// <remarks>
    /// Same shape and the same reasons as <see cref="BundledShaderBuildInclusion"/>, which owns the
    /// explanation of why the record lives on disk, why the revert must save, and why an entry the consumer
    /// already had is left alone.
    /// </remarks>
    public sealed class BundledStyleSheetBuildInclusion : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string RecordFile = "Library/VelvetBundledStyleSheetInclusion.txt";
        private const string LiveSessionKey = "Velvet.BundledStyleSheetBuildInclusion.Live";
        private const string SettingsAsset = "ProjectSettings/ProjectSettings.asset";

        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            RequireWritableSettings();
            Revert();
            Inject();
        }

        public void OnPostprocessBuild(BuildReport report) => Revert();

        [InitializeOnLoadMethod]
        internal static void RevertWhatAnEndedSessionLeft()
        {
            if (SessionState.GetString(LiveSessionKey, string.Empty).Length == 0)
            {
                Revert();
            }
        }

        private static bool CanWriteSettings()
        {
            var file = new FileInfo(SettingsAsset);
            if (!file.Exists)
            {
                return true;
            }
            try
            {
                using var probe = file.Open(FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                return true;
            }
            catch (UnauthorizedAccessException) { return false; }
            catch (IOException) { return false; }
        }

        private static void RequireWritableSettings()
        {
            if (!CanWriteSettings())
            {
                throw new BuildFailedException(
                    $"{SettingsAsset} cannot be opened for writing, so Velvet cannot add its stylesheet to "
                    + "the preloaded assets for this build and could not take it out again afterwards. Make "
                    + "the file writable — check it out of version control if that is what holds it — and "
                    + "build again.");
            }
        }

        internal static UnityEngine.Object Holder()
            => AssetDatabase.LoadAssetAtPath<VelvetRuntimeAssets>(VelvetStyleUtilities.RuntimeAssetsPath);

        internal static void Inject()
        {
            var holder = Holder();
            if (holder == null)
            {
                throw new BuildFailedException(
                    $"{VelvetStyleUtilities.RuntimeAssetsPath} did not load, so the bundled utility "
                    + "stylesheet would not reach the player and every plain utility class would resolve to "
                    + "nothing.");
            }

            var preloaded = PlayerSettings.GetPreloadedAssets().Where(asset => asset != null).ToList();
            if (preloaded.Contains(holder))
            {
                // Already the consumer's own entry. Recording it would have the revert remove something this
                // build did not add.
                return;
            }
            preloaded.Add(holder);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());

            // Recorded before the apply, not after: a record naming something that never landed is removed on
            // the next pass, while an entry applied with no record is the permanent diff.
            File.WriteAllText(RecordFile, VelvetStyleUtilities.RuntimeAssetsPath);
            AssetDatabase.SaveAssets();
            SessionState.SetString(LiveSessionKey, "1");
        }

        internal static void Revert()
        {
            if (!File.Exists(RecordFile))
            {
                return;
            }
            if (!CanWriteSettings())
            {
                return;
            }

            var holder = Holder();
            if (holder == null)
            {
                // The asset moved or was removed since the build recorded it. Keeping the record is what
                // lets a later pass finish; deleting it is what would make the leftover entry permanent.
                return;
            }

            var preloaded = PlayerSettings.GetPreloadedAssets()
                .Where(asset => asset != null && asset != holder)
                .ToArray();
            PlayerSettings.SetPreloadedAssets(preloaded);
            AssetDatabase.SaveAssets();
            File.Delete(RecordFile);
            SessionState.EraseString(LiveSessionKey);
        }

        /// <summary>Whether the holder is currently absent from the preloaded assets.</summary>
        internal static bool Unreached()
        {
            var holder = Holder();
            return holder == null
                   || !PlayerSettings.GetPreloadedAssets().Any(asset => asset == holder);
        }
    }
}
