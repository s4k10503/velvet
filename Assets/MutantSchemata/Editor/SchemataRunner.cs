using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace Velvet.MutantSchemata.Editor
{
    // Prototype. Runs a campaign's mutants in one editor session over a tree the rewriter compiled once:
    // each mutant is armed by the environment variable its switch reads, the domain is reloaded so every
    // static starts over as it would in a fresh editor, and its stages run through TestRunnerApi.
    // Everything it needs across a reload is in files under the plan's output directory, written before
    // anything that can reload the domain is asked for, since a reload drops every pending delayCall.
    [InitializeOnLoad]
    internal static class SchemataRunner
    {
        private const string PlanVariable = "VELVET_SCHEMATA_PLAN";

        [Serializable]
        internal sealed class Stage
        {
            public string name = "";
            public string[] assemblyNames = Array.Empty<string>();
            public string[] groupNames = Array.Empty<string>();
            public string[] testNames = Array.Empty<string>();
            public bool stopAtFirstFailure;
        }

        [Serializable]
        internal sealed class Item
        {
            public int id;
            public Stage[] stages = Array.Empty<Stage>();
        }

        [Serializable]
        internal sealed class Plan
        {
            public string output = "";
            public string platform = "EditMode";
            public string env = "";
            public Item[] items = Array.Empty<Item>();
            public int start;
            public int hang = int.MinValue;
            public bool scrub;
        }

        // `arm`: the next item's variable is not set yet. `start`: the domain the stage needs has been
        // asked for, and whichever domain reads this next is it. `running`: a job is in flight.
        // `cancelling`: a job was cancelled and has not stopped. `draining`: a job reported its result and
        // is still running the tasks after that -- leaving play mode, restoring the scene setup -- which a
        // reload asked for now would cut off, and the next job then stalls on the scene they left.
        [Serializable]
        internal sealed class State
        {
            public int position;
            public int stage;
            public string phase = "arm";
            public string guid = "";
            public double requested;
            public double started;
        }

        [Serializable]
        internal sealed class StageResult
        {
            public string name = "";
            public string xml = "";
            public int passed;
            public int failed;
            public int inconclusive;
            public int skipped;
            public bool cancelled;
            public bool finished;
            public List<string> failures = new List<string>();
            public double seconds;
            public double reloadSeconds;
            public string[] armed = Array.Empty<string>();
            public double setupSeconds;
            public int windows;
            public int logEntries;
            public int objects;
        }

        [Serializable]
        internal sealed class ItemResult
        {
            public int id;
            public List<StageResult> stages = new List<StageResult>();
        }

        [Serializable]
        private sealed class Progress
        {
            public int position;
            public int id;
            public int stage;
            public double since;
        }

        private static Plan plan = null!;
        private static Callbacks? callbacks;

        static SchemataRunner()
        {
            var armedPath = Environment.GetEnvironmentVariable("VELVET_SCHEMATA_ARMED") ?? "";
            if (armedPath.Length > 0) File.WriteAllText(armedPath, string.Join(",", Armed()));
            var planPath = Environment.GetEnvironmentVariable(PlanVariable) ?? "";
            if (planPath.Length == 0) return;
            plan = JsonUtility.FromJson<Plan>(File.ReadAllText(planPath));
            var state = ReadState();
            if (state.phase == "running")
            {
                // A reload inside a run, as entering and leaving play mode make: the job survives it,
                // the callbacks do not.
                Register();
                return;
            }
            if (state.phase == "cancelling")
            {
                EditorApplication.update += AwaitCancel;
                return;
            }
            if (state.phase == "draining")
            {
                EditorApplication.update += AwaitDrain;
                return;
            }
            EditorApplication.delayCall += Step;
        }

        private static string StatePath => Path.Combine(plan.output, "runner-state.json");

        private static State ReadState() =>
            File.Exists(StatePath) ? JsonUtility.FromJson<State>(File.ReadAllText(StatePath)) : new State { position = plan.start };

        private static void WriteState(State state) => File.WriteAllText(StatePath, JsonUtility.ToJson(state));

        private static double Now => (DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;

        private static void Heartbeat(State state)
        {
            File.WriteAllText(Path.Combine(plan.output, "runner-progress.json"), JsonUtility.ToJson(new Progress
            {
                position = state.position,
                id = state.position < plan.items.Length ? plan.items[state.position].id : int.MinValue,
                stage = state.stage,
                since = Now,
            }));
        }

        private static void Step()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.delayCall += Step;
                return;
            }
            var state = ReadState();
            if (state.phase == "arm")
            {
                if (state.position >= plan.items.Length)
                {
                    File.WriteAllText(Path.Combine(plan.output, "runner-done"), "");
                    EditorApplication.Exit(0);
                    return;
                }
                Environment.SetEnvironmentVariable(plan.env, plan.items[state.position].id.ToString());
                state.phase = "start";
                state.stage = 0;
                state.requested = Now;
                WriteState(state);
                Heartbeat(state);
                EditorUtility.RequestScriptReload();
                return;
            }
            if (state.phase == "start") Start(state);
        }

        private static void Start(State state)
        {
            var item = plan.items[state.position];
            var stage = item.stages[state.stage];
            if (item.id == plan.hang)
            {
                // The watchdog's own check: a main thread that never returns, as a mutant's loop would.
                Heartbeat(state);
                while (true) { }
            }
            var result = Result(item.id);
            result.stages.Add(new StageResult
            {
                name = stage.name,
                reloadSeconds = state.requested > 0 ? Now - state.requested : 0,
                armed = Armed(),
                windows = Resources.FindObjectsOfTypeAll<EditorWindow>().Length,
                logEntries = LogEntryCount(),
                objects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length,
            });
            if (plan.scrub) Scrub();
            WriteResult(result);
            state.phase = "running";
            state.requested = 0;
            state.started = Now;
            WriteState(state);
            Heartbeat(state);
            Register();
            var settings = new ExecutionSettings(new Filter
            {
                testMode = plan.platform == "PlayMode" ? TestMode.PlayMode : TestMode.EditMode,
                assemblyNames = stage.assemblyNames.Length > 0 ? stage.assemblyNames : null,
                groupNames = stage.groupNames.Length > 0 ? stage.groupNames : null,
                testNames = stage.testNames.Length > 0 ? stage.testNames : null,
            });
            var guid = ScriptableObject.CreateInstance<TestRunnerApi>().Execute(settings);
            var now = ReadState();
            if (now.phase == "running" && now.position == state.position && now.stage == state.stage)
            {
                now.guid = guid;
                WriteState(now);
            }
        }

        private static readonly Type? LogEntries = typeof(EditorWindow).Assembly.GetType("UnityEditor.LogEntries");

        private static int LogEntryCount() =>
            LogEntries?.GetMethod("GetCount", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null) is int count ? count : -1;

        // Prototype diagnostic: what a fresh editor would not have -- the windows earlier stages left open
        // and the console entries they logged.
        private static void Scrub()
        {
            LogEntries?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public)?.Invoke(null, null);
            var keep = new HashSet<string> { "UnityEditor.ConsoleWindow", "UnityEditor.InspectorWindow",
                "UnityEditor.SceneView", "UnityEditor.GameView", "UnityEditor.SceneHierarchyWindow",
                "UnityEditor.ProjectBrowser" };
            foreach (var window in Resources.FindObjectsOfTypeAll<EditorWindow>())
            {
                if (!keep.Contains(window.GetType().FullName ?? "")) window.Close();
            }
        }

        private static void Register()
        {
            if (callbacks != null) TestRunnerApi.UnregisterTestCallback(callbacks);
            callbacks = new Callbacks();
            TestRunnerApi.RegisterTestCallback(callbacks);
        }

        private static string[] Armed()
        {
            var found = new List<string>();
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException failure) { types = failure.Types.Where(t => t != null).ToArray(); }
                foreach (var type in types)
                {
                    if (!type.Name.StartsWith("__VelvetMutantSwitch_", StringComparison.Ordinal)) continue;
                    var field = type.GetField("Active", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                    found.Add(assembly.GetName().Name + ":" + field?.GetValue(null));
                }
            }
            return found.ToArray();
        }

        private static string ResultPath(int id) => Path.Combine(plan.output, "item-" + id.ToString("000") + ".json");

        private static ItemResult Result(int id)
        {
            var path = ResultPath(id);
            return File.Exists(path) ? JsonUtility.FromJson<ItemResult>(File.ReadAllText(path)) : new ItemResult { id = id };
        }

        private static void WriteResult(ItemResult result) => File.WriteAllText(ResultPath(result.id), JsonUtility.ToJson(result, true));

        private static void Failed(string name)
        {
            var state = ReadState();
            var item = plan.items[state.position];
            var result = Result(item.id);
            var recorded = result.stages[result.stages.Count - 1];
            recorded.failures.Add(name);
            if (item.stages[state.stage].stopAtFirstFailure && state.guid.Length > 0 && !recorded.cancelled)
            {
                recorded.cancelled = TestRunnerApi.CancelTestRun(state.guid);
                if (recorded.cancelled)
                {
                    // A cancelled job skips the task that invokes RunFinished, so the stage is closed here
                    // once the job reports it is no longer running.
                    state.phase = "cancelling";
                    WriteState(state);
                    EditorApplication.update += AwaitCancel;
                }
            }
            WriteResult(result);
        }

        private static readonly Type? JobHolder =
            typeof(TestRunnerApi).Assembly.GetType("UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder");

        // Whether the framework has let go of the job. Not `TestRunnerApi.IsRunning`: that turns false when
        // the job's last task returns, one editor update before the job is unregistered, and a reload in
        // that update leaves it registered as running -- `TestJobDataHolder.ResumeRunningJobs` then starts
        // it again from its first task beside the next job (measured: a narrowed stage re-ran beside the
        // whole-suite stage that followed it, and its result was taken as the whole suite's).
        private static bool Released(string guid)
        {
            if (JobHolder == null || guid.Length == 0) return true;
            var holder = JobHolder.GetProperty("instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null);
            return JobHolder.GetMethod("GetRunner")?.Invoke(holder, new object[] { guid }) == null;
        }

        private static void AwaitCancel()
        {
            var state = ReadState();
            if (state.phase != "cancelling")
            {
                EditorApplication.update -= AwaitCancel;
                return;
            }
            if (!Released(state.guid)) return;
            EditorApplication.update -= AwaitCancel;
            Close(state, null);
        }

        private static void Finished(ITestResultAdaptor run)
        {
            var state = ReadState();
            if (state.phase != "running") return;
            Close(state, run);
        }

        private static void Close(State state, ITestResultAdaptor? run)
        {
            var item = plan.items[state.position];
            var stage = item.stages[state.stage];
            var result = Result(item.id);
            var recorded = result.stages[result.stages.Count - 1];
            if (run != null)
            {
                var xml = Path.Combine(plan.output, "mutant-" + item.id.ToString("000") + "-" + stage.name + ".xml");
                TestRunnerApi.SaveResultToFile(run, xml);
                recorded.xml = xml;
                recorded.passed = run.PassCount;
                recorded.failed = run.FailCount;
                recorded.inconclusive = run.InconclusiveCount;
                recorded.skipped = run.SkipCount;
            }
            recorded.finished = true;
            recorded.seconds = Now - state.started;
            WriteResult(result);
            state.phase = "draining";
            WriteState(state);
            EditorApplication.update += AwaitDrain;
        }

        private static void AwaitDrain()
        {
            var state = ReadState();
            if (state.phase != "draining")
            {
                EditorApplication.update -= AwaitDrain;
                return;
            }
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (!Released(state.guid)) return;
            EditorApplication.update -= AwaitDrain;
            Advance(state);
        }

        private static void Advance(State state)
        {
            var item = plan.items[state.position];
            var stage = item.stages[state.stage];
            var recorded = Result(item.id).stages.Last();
            var killed = stage.stopAtFirstFailure && recorded.failures.Count > 0;
            state.guid = "";
            if (!killed && state.stage + 1 < item.stages.Length)
            {
                state.stage += 1;
                state.phase = "start";
                state.requested = Now;
                WriteState(state);
                Heartbeat(state);
                EditorUtility.RequestScriptReload();
                return;
            }
            state.position += 1;
            state.stage = 0;
            state.phase = "arm";
            WriteState(state);
            Heartbeat(state);
            EditorApplication.delayCall += Step;
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun)
            {
                var state = ReadState();
                if (state.phase != "running") return;
                var result = Result(plan.items[state.position].id);
                result.stages[result.stages.Count - 1].setupSeconds = Now - state.started;
                WriteResult(result);
            }

            public void RunFinished(ITestResultAdaptor result) => Finished(result);

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.TestStatus == TestStatus.Failed) Failed(result.FullName);
            }
        }
    }
}
