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
    internal sealed class BundledStyleSheetBuildInclusion : IPreprocessBuildWithReport, IPostprocessBuildWithReport
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

        internal static UnityEngine.Object Holder() => HolderAt(VelvetStyleUtilities.RuntimeAssetsPath);

        private static UnityEngine.Object HolderAt(string path)
            => AssetDatabase.LoadAssetAtPath<VelvetRuntimeAssets>(path);

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

            var preloaded = PlayerSettings.GetPreloadedAssets().ToList();
            if (preloaded.Contains(holder))
            {
                // Already the consumer's own entry. Recording it would have the revert remove something this
                // build did not add.
                return;
            }

            // Written before the list is mutated: a record naming something that never landed is removed on
            // the next pass, while an entry added with no record is the permanent diff.
            File.WriteAllLines(RecordFile, new[] { VelvetStyleUtilities.RuntimeAssetsPath });
            preloaded.Add(holder);

            // The list goes back carrying whatever else it held, nulls included. A consumer's empty slot is
            // theirs — dropping it here is a diff they did not ask for and cannot undo.
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
            SessionState.SetString(LiveSessionKey, "1");

            if (Unreached())
            {
                throw new BuildFailedException(
                    $"{VelvetStyleUtilities.RuntimeAssetsPath} did not reach PlayerSettings' preloaded "
                    + "assets, so the bundled utility stylesheet would not reach the player and every plain "
                    + "utility class would resolve to nothing.");
            }
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

            // What the build recorded, not what the constant names today: a package update that moves the
            // holder must still be able to remove the entry an older build added.
            var preloaded = PlayerSettings.GetPreloadedAssets().ToList();
            var unresolved = new List<string>();
            foreach (var path in File.ReadAllLines(RecordFile))
            {
                var holder = HolderAt(path);
                if (holder == null || !preloaded.Remove(holder))
                {
                    unresolved.Add(path);
                }
            }

            // Everything else goes back untouched, for the reason the injection leaves it alone.
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());

            // The build writes project settings to disk while it runs, so undoing the injection in the
            // loaded object alone would leave the entry in the file the consumer sees.
            AssetDatabase.SaveAssets();

            // Same reason the sibling keeps its record: an entry this pass could not resolve is still in the
            // consumer's file, and deleting the record is the only thing that could make it unremovable.
            if (unresolved.Count > 0)
            {
                File.WriteAllLines(RecordFile, unresolved);
                return;
            }
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
