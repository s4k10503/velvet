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

## Nine ways a test here has passed while checking nothing

Every one was found by mutating the implementation and confirming a test died — none by reading the test, because each reads as reasonable. The common shape is that **the input's form never reaches the code under test**.

- **Siblings do not nest.** Five `from` clauses, or five `using var` declarations, are siblings at depth 1. A test for "does this construct open a nesting level" must put it *inside* several levels.
- **The depth is the same either way.** A `try` at depth 4 reports nothing whether or not `try` opens a level, so `Assert.Empty` passes on both.
- **The attribute never bound.** A `namespace` written above `[assembly: …]` is invalid C#; the attribute does not bind, so a gate keyed on it returns false regardless of what the gate does.
- **N configurations, one test.** A guard evaluating both `UNITY_EDITOR` and no-symbols needs a case per configuration: each conditional spelling is visible to exactly one, so one case leaves the other deletable with nothing going red.
- **A static field collapses the covering set.** A fixture caching generated output in `static readonly` gets the whole class attributed to whichever test touched it first, so mutation testing runs one test per mutant and whole regions go unasserted.
- **An `Assume` gates the behaviour under test.** The regression reports Inconclusive, which the runner does not count. Fold it into the assertion as one tuple comparison.
- **An arbitrary value skips the path.** `rounded-tl-[12px]` is not in the scale, so a fallback under test never fires; `rounded-tl-lg` reaches it.
- **The class under test was inert.** See the stylesheet trap below — the rest of the fixture works, so it looks built.
- **The arranged condition has no term depending on it.** A case named for a clipped opacity group, or for an inline filter, sets that condition up and then asserts only what is true without it — so it passes with the clip or the filter removed, and pins its context in name only. Two instances in one fixture, found in consecutive review rounds — the second was next door to the first and survived the round that fixed it.

Three traps in the folding itself, each of which passes a count check:

- **`Is.EqualTo(tuple)` matches a nested collection by reference.** Join to strings instead.
- **`.Within()` does not reach the members of a tuple** (decompiled from `com.unity.ext.nunit` 2.0.5 and run under Mono; `VEL503` reports it, so a regression here fails a build rather than this note). Unity's NUnit ships no `ValueTupleComparer`, so `NUnitEqualityComparer.AreEqual` falls through to `FirstImplementsIEquatableOfSecond`, which never sees the tolerance — `Assert.That((0.99999f, 0.00001f), Is.EqualTo((1f, 0f)).Within(1e-4f))` **fails**. Its message still prints `+/- 9.99999975E-05f`, so a passing tuple assertion looks like proof the tolerance applied — two people read it that way before anyone ran the failing case. A fold that moves a scalar comparison into a tuple silently converts it to bit-exact equality wearing a tolerance suffix. A collection of tuples traps the same way: NUnit does walk an expected array element-wise carrying the tolerance, but each element is then a tuple, so `new[] { ("a", 0.99999f) }` against `new[] { ("a", 1f) }` fails where `new[] { 0.99999f }` against `new[] { 1f }` passes. Rounding each term to the tolerance's own decimal place does not restore it, because two values inside the tolerance can still land in different buckets — `83.33334` and `83.33383` are 4.9e-4 apart and round to `83.333` and `83.334` — and comparing values formatted to that many places is the same operation. What does restore it: keep the compared value a scalar (or an array of scalars) and fold the control term in as a gate that substitutes `float.NaN`, which satisfies no tolerance.
- **The logically sharpest gate is not always the discriminating one.** A `ReferenceEquals` precondition on a LIFO pool holds even when the mechanism is neutered, and a count term next to it is what goes red.

## Neuter at every layer, not at one

A test whose discriminating term is a **side effect of a different layer than the one under test** survives a neuter of the layer it is aimed at. An expected log line, a suppression flag, a gate read one level up: kill the applier and the parser still says "wants clip", so the warning still fires and the assertion still passes with the feature dead.

Every vacuous test found in three sweeps of one fixture pair was cut-dependent in this direction — green at the applier cut, red at the parser cut — and each sweep at a single cut undercounted. A count of vacuous tests is meaningless without the cut beside it, and the answer changed on every recount taken by reading: three, then four, then five for one half; five, then six for another.

Build the harness instead. Applying each cut, running the fixtures, reverting in a `finally` and diffing the per-test JSON is about three minutes for four cuts, and it reproduces independently where a careful read does not. Two traps inside the harness itself, both of which inflate:

- **Strip comments before deciding which fixture a cut applies to.** A ring test whose comment mentions `clip-path-*` matched as a clip test.
- **Ask a cut only of a fixture that exercises that layer.** A parser unit test is not vacuous because the applier died; asking it anyway reported 18 phantom holes.

## A test dying under some mutation does not mean it tests what it claims

A mutation that reddens a test proves the test is sensitive to *that mutation*, not that it measures the fact in its name. An assertion whose value is independent of its stated fact is broken even when an unrelated perturbation happens to move it — measured here on a case that survived deleting the very transform it was named for, while a different mutation to the same code path did redden it. Check what the assertion is a function of, not only that something can break it.

## Sweep for the shape, not the instance

Consecutive review rounds on one branch kept finding one more instance of a defect already fixed, each fix right, the class never moving — because each round fixed what it was handed. Two sweeps end that, and both cost minutes:

- **Every assertion**: remove the condition the case arranges and check that something goes red. If nothing does, the arrangement is decoration.
- **Every sentence containing `every`, `no other`, `always` or `none`**: check it against the set it quantifies over. Universals written from memory have been false here more often than they have held, including inside the rule that forbids writing an unverified claim, and including in a comment written after that rule landed.

Report what a sweep found, zero included. One nobody hears about is indistinguishable from one nobody ran.

## A pixel fixture on `RenderTexturePanelHost` mounts without the bundled stylesheet

A class that gets its whole effect from the sheet does nothing there, and what hides that is the classes that do not. Arbitrary values resolve to inline style; `gap-*`, `space-*`, `divide-*`, `ring-*` and the filter families have no rule in any bundled sheet and are realised from C#. `grid` is both — declared at `_layout.uss:13` and driven by `StyleGridClass` — so it half-works, which is the worst case to debug from. Check the class you are relying on rather than reasoning from a category. So a fixture written from `w-[60px] bg-[#0000ff] flex flex-row` looks correctly constructed and is not: the sizes and colours land, the `flex-row` does not, and the container stays UI Toolkit's default `column`.

This has produced a wrong conclusion (a paint was reported as surviving `overflow-hidden` when the clip had never applied) and, separately, two reds that looked like evidence about paint order and were actually a fixture measuring non-overlapping elements. It costs more than either trap above.

- **Attach the sheet** (`VelvetStyleUtilities.AttachTo`) or use inline style deliberately — not a mix you have not checked.
- **Assert the measured geometry before reading a pixel**, and derive every sample coordinate from the measured `layout`/`resolvedStyle` values rather than from expected ones. If the layout assertion fails, that is the finding.
- **Put a control in the frame.** "The paint was clipped" and "the clip never applied" are indistinguishable without one — an overflowing child on the opposite side answers it in the same capture.
- Log more than one axis. An x-only diagnostic read a 20px difference that was really a column layout with a 60px y-shift.

## Reading a result you intend to report

- EditMode batchmode does not run layout. A test that reads `resolvedStyle` must force it through the panel's `ApplyStyles`/`UpdateForRepaint`, or it silently measures nothing. Assertions on measured values belong in PlayMode.
- A threshold derived from font metrics or layout lands differently on the CI runner than locally. Assert against declared values where possible.
- Before reporting "no allocations", confirm the instrument works (measured on 6000.3.11f1): `GC.GetAllocatedBytesForCurrentThread()` and `GC.GetTotalMemory` both report 0 under Unity's Mono regardless of what happens. Only `Unity.PerformanceTesting`'s `.GC()` recorder measures it.
- A benchmark measures only the path its fixture drives. Confirm the code under test actually runs in it before citing it as evidence.

## The generator suite

Separate dotnet solution, no Unity licence needed:

```bash
cd Packages/com.velvet.core/Generators~ && dotnet test Velvet.SourceGenerators.sln -c Release
```

`./build.sh` rebuilds and redeploys the committed DLLs and regenerates the derived stylesheet table. Run it after changing generator or stylesheet sources, and commit what it produces.
