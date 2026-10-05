using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace Velvet.Tests
{
    internal sealed class ImportedStyleSheetPlayerProbe : MonoBehaviour
    {
        [SerializeField] internal ImportedStyleSheetPlayerAssets Assets;

        [Serializable]
        private sealed class Result
        {
            public string Phase;
            public bool Passed;
            public string Failure;
            public string Platform;
            public bool Il2Cpp;
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
            var result = new Result { Phase = "started", Platform = Application.platform.ToString() };
#if ENABLE_IL2CPP
            result.Il2Cpp = true;
#endif
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
            var fixture = new ImportedStyleSheetPlayerTests();
            fixture.SetUp();
            var test = fixture.Given_TheBundledAssets_When_AStrippedIl2CppPlayerUsesThem_Then_ShadersAndCopiedSheetsRemainUsable();
            while (true)
            {
                bool next;
                try { next = test.MoveNext(); }
                catch (Exception exception) { result.Failure = exception.ToString(); break; }
                if (!next) { result.Passed = true; break; }
                yield return test.Current;
            }
            var cleanup = fixture.TearDown();
            while (cleanup.MoveNext()) yield return cleanup.Current;
            result.Observation = fixture.Snapshot;
            result.Phase = "completed";
            File.WriteAllText(path, JsonUtility.ToJson(result, true));
            Application.Quit(result.Passed ? 0 : 1);
        }
    }
}
