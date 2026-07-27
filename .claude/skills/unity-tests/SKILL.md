---
name: unity-tests
description: Run Velvet's EditMode or PlayMode suites headlessly and read the results correctly. Use whenever a change needs verifying against the test suite, or when a reported pass/fail count needs to be trusted.
---

# Running the Unity suites

The editor must be closed — it holds the project lock.

```bash
UNITY=/Applications/Unity/Hub/Editor/6000.3.11f1/Unity.app/Contents/MacOS/Unity
"$UNITY" -runTests -batchmode -projectPath "$PWD" -testPlatform EditMode \
  -testResults /tmp/results.xml -logFile /tmp/run.log
```

`-testPlatform PlayMode` for the other suite. `-testFilter "Velvet.Tests.SomeFixture"` narrows it; semicolons separate several, and it matches fully-qualified class or method names.

## Traps that produce wrong answers

**Never pass `-nographics`.** Any test that mounts a real panel — an `EditorWindow.rootVisualElement`, or anything reading `resolvedStyle` or firing pointer/focus events — fails with "No graphic device is available". Graphics-free tests pass with graphics on, so there is no reason to use the flag.

**Check for a running instance with a pattern that cannot match its own command line.** `pgrep -f "MacOS/Unity -runTests"` matches the waiting shell itself and deadlocks against a machine that is actually idle. Use:

```bash
ps -Ao command= | grep -c '^/Applications/.*/MacOS/Uni[t]y -runTests'
```

Concurrent Unity instances make unrelated tests fail. A failure measured while another run was in flight is not evidence; re-measure on a quiet machine before concluding anything.

**Compile errors appear only in the log**, never in the XML: `grep "error CS" /tmp/run.log`. A run that failed to compile still writes an XML, so a missing failure count is not proof of success.

**Counts come only from the XML**: `grep -o 'passed="[0-9]*"\|failed="[0-9]*"\|inconclusive="[0-9]*"' /tmp/results.xml | head -3`.

**`inconclusive` is not counted as a failure by the runner.** A non-zero count means a test skipped rather than reported — usually an `Assume` gating the behaviour under test. Treat it as a failure and find the test.

## Reading a result you intend to report

- EditMode batchmode does not run layout. A test that reads `resolvedStyle` must force it through the panel's `ApplyStyles`/`UpdateForRepaint`, or it silently measures nothing. Assertions on measured values belong in PlayMode.
- A threshold derived from font metrics or layout lands differently on the CI runner than locally. Assert against declared values where possible.
- Before reporting "no allocations", confirm the instrument works: `GC.GetAllocatedBytesForCurrentThread()` and `GC.GetTotalMemory` both report 0 under Unity's Mono regardless of what happens. Only `Unity.PerformanceTesting`'s `.GC()` recorder measures it.
- A benchmark measures only the path its fixture drives. Confirm the code under test actually runs in it before citing it as evidence.

## The generator suite

Separate dotnet solution, no Unity licence needed:

```bash
cd Packages/com.velvet.core/Generators~ && dotnet test Velvet.SourceGenerators.sln -c Release
```

`./build.sh` rebuilds and redeploys the committed DLLs and regenerates the derived stylesheet table. Run it after changing generator or stylesheet sources, and commit what it produces.
