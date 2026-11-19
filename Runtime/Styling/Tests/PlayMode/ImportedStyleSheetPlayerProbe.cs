using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Velvet.Tests
{
    internal sealed class ImportedStyleSheetPlayerProbe : MonoBehaviour
    {
        [SerializeField] internal ImportedStyleSheetPlayerAssets Assets;
        [SerializeField] internal bool NativeStartOnly;

        [Serializable]
        private sealed class Result
        {
            public string Phase;
            public bool Passed;
            public string Failure;
            public string Platform;
            public bool Il2Cpp;
            public bool CleanupSucceeded;
            public bool NativeStartOnly;
            public ImportedStyleSheetPlayerTests.NativeObservation NativeObservation;
            public ImportedStyleSheetPlayerTests.Observation Observation;
        }

        private IEnumerator Start()
        {
            var arguments = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(arguments, "--velvet-import-result");
            if (index < 0 || index + 1 == arguments.Length)
            {
                Debug.LogError("The imported-sheet probe needs an output path.");
                Application.Quit(1);
                yield break;
            }
            var path = arguments[index + 1];
            var result = new Result { Phase = "started", Platform = Application.platform.ToString(), NativeStartOnly = NativeStartOnly };
#if ENABLE_IL2CPP
            result.Il2Cpp = true;
#endif
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
            var fixture = new ImportedStyleSheetPlayerTests();
            IEnumerator test = null;
            try
            {
                if (!NativeStartOnly) fixture.SetUp();
                test = NativeStartOnly
                    ? fixture.Given_ANativeTransition_When_AStrippedIl2CppPlayerTakesOver_Then_TheCustomStartMatchesTheDisplayedValue()
                    : fixture.Given_TheBundledAssets_When_AStrippedIl2CppPlayerUsesThem_Then_ShadersAndCopiedSheetsRemainUsable();
            }
            catch (Exception exception) { result.Failure = exception.ToString(); }
            while (test != null)
            {
                bool next;
                try { next = test.MoveNext(); }
                catch (Exception exception) { result.Failure = exception.ToString(); break; }
                if (!next) { result.Passed = true; break; }
                yield return test.Current;
            }
            var cleanup = fixture.TearDown();
            while (true)
            {
                bool next;
                try { next = cleanup.MoveNext(); }
                catch (Exception exception)
                {
                    result.Failure += "\nCleanup: " + exception;
                    result.Passed = false;
                    break;
                }
                if (!next) { result.CleanupSucceeded = true; break; }
                yield return cleanup.Current;
            }
            result.NativeObservation = fixture.NativeSnapshot;
            result.Observation = fixture.Snapshot;
            result.Phase = "completed";
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
            Application.Quit(result.Passed ? 0 : 1);
        }
    }
}
