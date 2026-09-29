using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Velvet.Editor.Preview
{
    /// <summary>
    /// Runs <see cref="VelvetPreviewSmokeTest"/> over every story from the menu, or from a batch-mode editor:
    /// <c>-batchmode -executeMethod Velvet.Editor.Preview.VelvetPreviewSmokeTestCommand.RunAndExit</c> exits 1
    /// when a story fails or discovery is refused, and 0 when every story passes.
    /// </summary>
    public static class VelvetPreviewSmokeTestCommand
    {
        [MenuItem("Window/Velvet/Run Preview Smoke Test")]
        private static void RunFromMenu() => Report(VelvetPreviewSmokeTest.Run());

        /// <summary>The batch-mode entry point; exits the editor with the run's outcome.</summary>
        public static void RunAndExit() => EditorApplication.Exit(ExitCode(VelvetPreviewSmokeTest.Run));

        // A refused story index is reported as a failed run rather than thrown out of -executeMethod.
        internal static int ExitCode(Func<IReadOnlyList<VelvetPreviewSmokeResult>> run)
        {
            try
            {
                return Report(run()) == 0 ? 0 : 1;
            }
            catch (InvalidOperationException ex)
            {
                Debug.LogException(ex);
                return 1;
            }
        }

        // Each failure is its own error line, so a CI log names every failing story rather than the first.
        internal static int Report(IReadOnlyList<VelvetPreviewSmokeResult> results)
        {
            var failed = 0;
            foreach (var result in results)
            {
                if (result.Failure == null) continue;
                failed++;
                Debug.LogError($"[VelvetPreview] smoke test: '{result.Story.Id}' failed: {result.Failure}");
            }

            Debug.Log($"[VelvetPreview] smoke test: {results.Count - failed} of {results.Count} stories passed.");
            return failed;
        }
    }
}
