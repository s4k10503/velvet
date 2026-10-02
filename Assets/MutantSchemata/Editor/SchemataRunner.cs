#nullable enable
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
    // Runs a mutation campaign's session: the mutants scripts/test_quality/mutation_check.py placed in
    // one guarded tree, each armed by the variable its switch reads, in one editor. The domain is
    // reloaded before every stage so the switch reads the armed id in its static initializer, which the
    // `armed` each stage records shows. Everything the runner needs across a reload is in a file under
    // the plan's output directory, written before anything that can reload is asked for.
    [InitializeOnLoad]
    internal static class SchemataRunner
    {
        private const string PlanVariable = "VELVET_SCHEMATA_PLAN";
        private const int Opening = 0;

        [Serializable]
        internal sealed class Stage
        {
            public string name = "";
            public string[] assemblyNames = Array.Empty<string>();
            public string[] groupNames = Array.Empty<string>();
            public string[] testNames = Array.Empty<string>();
            public bool stopAtFirstFailure;
            // The opening stage that has to have come out green for this one to run.
            public string gate = "";
        }

        [Serializable]
        internal sealed class Item
        {
            public int id;
            public Stage[] stages = Array.Empty<Stage>();
            // Runs the first case each kill since the previous confirmation failed on.
            public bool confirm;
        }

        [Serializable]
        internal sealed class Plan
        {
            public string output = "";
            public string platform = "EditMode";
            public string env = "";
            public Item[] items = Array.Empty<Item>();
            public int start;
            public int end = -1;
            // Files the package's sources name directly under Library, where git reports nothing; read with
            // what git does report after every stage.
            public string[] watch = Array.Empty<string>();
        }

        [Serializable]
        private sealed class Leak
        {
            public int position;
            // Whether this stage ended the item -- its last stage, or one that killed it -- so that what the
            // item recorded is the reading it would have had without the leak.
            public bool complete;
            public string[] changed = Array.Empty<string>();
        }

        // `arm`: the next item's variable is not set yet. `start`: the domain the stage needs has been
        // asked for, and whichever domain reads this next is it. `running`: a job is in flight.
        // `cancelling`: a job was cancelled and has not stopped. `draining`: a job reported its result and
        // the next stage waits until the framework has released it and play mode is left.
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
            public bool omitted;
            public List<string> failures = new List<string>();
            public double seconds;
            public double reloadSeconds;
            public string[] armed = Array.Empty<string>();
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
            public string phase = "";
            public double since;
        }

        private static Plan plan = null!;
        private static Callbacks? callbacks;

        static SchemataRunner()
        {
            var planPath = Environment.GetEnvironmentVariable(PlanVariable) ?? "";
            if (planPath.Length == 0) return;
            plan = JsonUtility.FromJson<Plan>(File.ReadAllText(planPath));
            var state = ReadState();
            if (state.phase == "running")
            {
                // A reload inside a run: the job goes on, and its callbacks are registered again.
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

        private static int End => plan.end < 0 ? plan.items.Length : Math.Min(plan.end, plan.items.Length);

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
                phase = state.phase,
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
                // Read once a launch, before its first stage, after the harness put the tree back.
                if (!File.Exists(ReferencePath))
                {
                    var reading = Reading();
                    if (reading == null) return;
                    File.WriteAllText(ReferencePath, reading);
                }
                if (state.position >= End)
                {
                    Heartbeat(state);
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
            var testNames = stage.testNames;
            var omitted = stage.gate.Length > 0 && !Clean(Result(Opening).stages.FirstOrDefault(s => s.name == stage.gate));
            if (item.confirm)
            {
                testNames = KillingCases(state.position);
                omitted = testNames.Length == 0;
            }
            var result = Result(item.id);
            var recorded = new StageResult
            {
                name = stage.name,
                reloadSeconds = state.requested > 0 ? Now - state.requested : 0,
                armed = Armed(),
            };
            result.stages.Add(recorded);
            if (omitted)
            {
                recorded.omitted = true;
                recorded.finished = true;
                WriteResult(result);
                Advance(state);
                return;
            }
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
                testNames = testNames.Length > 0 ? testNames : null,
            });
            var guid = ScriptableObject.CreateInstance<TestRunnerApi>().Execute(settings);
            var now = ReadState();
            if (now.phase == "running" && now.position == state.position && now.stage == state.stage)
            {
                now.guid = guid;
                WriteState(now);
            }
        }

        private static bool Clean(StageResult? stage) =>
            stage != null && stage.finished && !stage.omitted && stage.failed == 0 && stage.inconclusive == 0 &&
            stage.failures.Count == 0 && stage.passed > 0;

        // The first case each item since the previous confirmation was killed by, in this launch: an item an
        // earlier launch carried is not confirmed by this one.
        private static string[] KillingCases(int position)
        {
            var found = new List<string>();
            for (var earlier = position - 1; earlier >= plan.start && !plan.items[earlier].confirm; earlier--)
            {
                if (plan.items[earlier].id == Opening) continue;
                var killed = Result(plan.items[earlier].id).stages.FirstOrDefault(s => s.failures.Count > 0);
                if (killed != null && !found.Contains(killed.failures[0])) found.Add(killed.failures[0]);
            }
            return found.ToArray();
        }

        private static void Register()
        {
            if (callbacks != null) TestRunnerApi.UnregisterTestCallback(callbacks);
            callbacks = new Callbacks();
            TestRunnerApi.RegisterTestCallback(callbacks);
        }

        // Each switch's assembly and the id it read, which is how a stage shows the tree it ran was the
        // guarded one and the mutant armed was the one asked for.
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

        private static string Name(int id) => id.ToString("000");

        private static string ResultPath(int id) => Path.Combine(plan.output, "item-" + Name(id) + ".json");

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
                    // Closed from AwaitCancel once the job is released, rather than from RunFinished.
                    state.phase = "cancelling";
                    WriteState(state);
                    EditorApplication.update += AwaitCancel;
                }
            }
            WriteResult(result);
        }

        private static readonly Type? JobHolder =
            typeof(TestRunnerApi).Assembly.GetType("UnityEditor.TestTools.TestRunner.TestRun.TestJobDataHolder");

        // Whether the framework has let go of the job. Not `TestRunnerApi.IsRunning`, which in the
        // prototype let the next stage's reload restart a finished job beside the next one.
        // Fails closed: without the holder there is no telling when a stage may reload, so the session ends
        // and every mutant it had not finished takes its own launch.
        private static bool Released(string guid)
        {
            if (guid.Length == 0) return Fail("a stage's job id was never recorded");
            var holder = JobHolder?.GetProperty("instance",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy)?.GetValue(null);
            var runner = JobHolder?.GetMethod("GetRunner", new[] { typeof(string) });
            if (holder == null || runner == null) return Fail("TestJobDataHolder.instance.GetRunner(string) did not resolve");
            return runner.Invoke(holder, new object[] { guid }) == null;
        }

        private static bool Fail(string reason)
        {
            File.WriteAllText(Path.Combine(plan.output, "runner-failed"), reason);
            EditorApplication.Exit(3);
            return false;
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
                var xml = Path.Combine(plan.output, "mutant-" + Name(item.id) + "-" + stage.name.Replace(':', '-') + ".xml");
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
            // A stage that left the tree other than this launch found it ends the launch: the next stage
            // would read what it left, and only a new editor over a tree the harness puts back drops what
            // the stage left in memory as well as on disk.
            var reading = Reading();
            if (reading == null) return;
            var reference = File.ReadAllText(ReferencePath);
            if (reading != reference)
            {
                Heartbeat(state);
                File.WriteAllText(Path.Combine(plan.output, "runner-leaked"), JsonUtility.ToJson(new Leak
                {
                    position = state.position,
                    complete = killed || state.stage + 1 >= item.stages.Length,
                    changed = reading.Split('\n').Except(reference.Split('\n'))
                        .Union(reference.Split('\n').Except(reading.Split('\n')))
                        .Where(line => line.Length > 0)
                        .Select(line => line.Substring(0, line.LastIndexOf(' ')))
                        .Distinct().ToArray(),
                }));
                EditorApplication.Exit(0);
                return;
            }
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

        private static string ReferencePath => Path.Combine(plan.output, "runner-reference");

        // Each file git reports and each watched one, with its content's hash, outside the plan's output.
        // Null once the runner has failed for want of git, since without it no stage can be shown to
        // have started from the tree the launch found.
        private static string? Reading()
        {
            var root = Directory.GetCurrentDirectory();
            var output = Path.GetFullPath(plan.output);
            var paths = new SortedSet<string>(plan.watch, StringComparer.Ordinal);
            string listed;
            try
            {
                using var git = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                    "git", "status --porcelain=v1 -z --untracked-files=all --no-renames")
                {
                    WorkingDirectory = root,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                });
                listed = git!.StandardOutput.ReadToEnd();
                git.WaitForExit();
                if (git.ExitCode != 0)
                {
                    Fail("git status exited " + git.ExitCode);
                    return null;
                }
            }
            catch (Exception failure)
            {
                Fail("git status did not run: " + failure.Message);
                return null;
            }
            foreach (var entry in listed.Split('\0'))
            {
                if (entry.Length > 3) paths.Add(entry.Substring(3));
            }
            var lines = new List<string>();
            using var sha = System.Security.Cryptography.SHA256.Create();
            foreach (var relative in paths)
            {
                var full = Path.GetFullPath(Path.Combine(root, relative));
                if (full == output || full.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.Ordinal)) continue;
                lines.Add(relative + " " + (File.Exists(full)
                    ? BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(full))).Replace("-", "")
                    : "-"));
            }
            return string.Join("\n", lines);
        }

        private sealed class Callbacks : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }

            public void RunFinished(ITestResultAdaptor result) => Finished(result);

            public void TestStarted(ITestAdaptor test) { }

            public void TestFinished(ITestResultAdaptor result)
            {
                if (!result.HasChildren && result.TestStatus == TestStatus.Failed) Failed(result.FullName);
            }
        }
    }
}
