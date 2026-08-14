# Velvet.SourceGenerators

Compile-time tooling bundled with `com.velvet.core`: the Roslyn analyzers and incremental source generators that ship as assemblies, and `Velvet.StyleTable`, which derives committed source from the bundled stylesheets. The directory keeps its name from the former; both live here because they share one solution, one build script, one test project and one license-free CI job.

## Build

Run the following inside the `Generators~/` directory:

```bash
./build.py
```

Needs `python3` on PATH alongside the .NET SDK. On Windows, close Unity and any IDE first: Windows cannot overwrite an analyzer assembly a Roslyn host still holds open.

It regenerates all three committed artifacts:

- `../Runtime/Styling/StyleUtilityProperties.g.cs`
- `../Runtime/Plugins/Generators/Velvet.SourceGenerators.dll`
- `../Runtime/Plugins/Analyzers/Velvet.SourceGenerators.CodeFixes.dll`

Commit all three. The distribution model assumes Unity users do not need to install `dotnet`.

A run that follows no source change leaves the working tree clean, so `git status` after one answers whether the committed artifacts correspond to the sources — run it to check something and there is nothing to restore afterwards. The builds behind that were measured on macOS and on Linux; CI runs the Linux one. Windows carries no reading either way.

## Test

```bash
dotnet test Velvet.SourceGenerators.sln
```

- `SourceBuilderTests` — unit tests for the indent / block helpers in `Shared/SourceBuilder.cs`
- `MemoOverloadGeneratorTests` — snapshot comparison that verifies the generated `V.Memoized<T1..T8>` output, plus the file header the generator wraps around it, which no snapshot covers because the extraction window starts at a method's doc comment
- `MemoizeMethodGeneratorTests` — verifies `[MemoizeMethod]`-driven `V.Memoized(...)` wrapper expansion and its diagnostics (see [Documentation~/memoization.md](../Documentation~/memoization.md) for what they mean and the complete list)
- `HookSurfaceDriftTests` — pins the analyzer's hook-name and type-name strings to the runtime surface by parsing `../Runtime/` with Roslyn (syntax only, no Unity assemblies). Nothing else notices a hook rename or a newly added deps-comparing hook on this side of the compile boundary, so the guard turns both into a red test instead of silently narrowed exhaustive-deps coverage
- `StubSurfaceDriftTests` — pins the Velvet stub in `GeneratorTestHelper` (the surface every analyzer and generator test compiles its sample user code against) to the runtime signatures, using the same syntax-only parse. It fails on a divergent parameter type, return type, generic constraint, modifier or optionality, and on a runtime overload the stub names but does not model; without it the suite can verify a diagnostic for a call shape no user can write
- `StyleUtilityTableTests` — one case per USS shape the bundled stylesheets contain, plus the problem each shape they do not produces. Compiles and calls the emitted table rather than pattern-matching its source, which is what puts the bit packing under assertion and is also the only place the emitted C# is proved to compile
- `CodeShapeBacklogDriftTests` — re-measures the three code-shape limits over the package's own sources and fails on any violation. This is the only job that verifies the marked assemblies pass: `unity-tests` is skipped unless a `UNITY_LICENSE` secret is set, so on a fork or an outside contributor's PR nothing else compiles them. It parses each file both with and without `UNITY_EDITOR` defined, because a body inside an `#if UNITY_EDITOR` is real code in one assembly and disabled text in another, and a scan that saw only one of those would miss several hundred members
- `SolutionProjectMembershipDriftTests` — compares the `*.csproj` files on disk against what `Velvet.SourceGenerators.sln` names and maps to a build configuration. A project the solution omits, and that no member reaches by `ProjectReference`, is built by nothing: its analyzers never run while the guards that read its sources off disk keep reporting on them — the same silent exemption the opt-in guard closes, one level further out where it cannot see it. Reachability rather than membership is what decides whether a compiler ever sees the file, measured by referencing an omitted project from a member and watching a syntax error in it fail the build; membership is the convention this repository holds to on top of that
- `DocumentationDiagnosticTableTests` — reads this file's diagnostic IDs, failure-code range endpoints and analyzer category names, and [Documentation~/memoization.md](../Documentation~/memoization.md)'s diagnostic table, back against the descriptors and the derivation. The category half is what makes a rename in the descriptors red here rather than leaving this file describing one that is gone
- `BundledStyleSheetCensusTests` — re-derives the table from `../Runtime/Styles/*.uss` and compares it against the committed `../Runtime/Styling/StyleUtilityProperties.g.cs`, and pins the selector-shape and property census the derivation is designed around. This is what makes committing the table safe: a stylesheet edit not accompanied by a regenerated table, or one that introduces an unsurveyed shape, fails here rather than downstream where a class missing from the table is indistinguishable from a class that conflicts with nothing

## Mutation testing

```bash
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec   # Apple-silicon Homebrew; /usr/local/opt/dotnet/libexec on Intel
dotnet tool update --global dotnet-stryker --version 4.16.0
dotnet-stryker    # reads stryker-config.json
```

The tool is pinned for the same reason the SDK and the Roslyn host are: how many mutants it creates, and which, is its version's answer, and the counts below are 4.16.0's. It ships a `net8.0` asset with major roll-forward, so it runs on whatever runtime is installed and does not constrain `global.json`. Without `DOTNET_ROOT` a Homebrew install fails to launch it — the apphost probes only `/usr/local/share/dotnet`.

Stryker mutates the source projects one behaviour change at a time, rebuilds, and reruns **only the tests its coverage pass attributes to that mutant** — not the suite. The report lands in `StrykerOutput/<timestamp>/reports/`. It answers what line coverage does not: whether the tests that run a line would notice that line being wrong.

The score is `(killed + timeout) / (killed + timeout + survived + no-coverage)`, counting a mutant no test reaches as not rejected. Stryker's own "block already covered" filter and the mutants that do not compile are both excluded from the denominator, and that exclusion is deterministic. How the remainder divides between killed and survived is not — two runs over an unchanged tree moved 29 of them — so no score is quoted here, and neither are the mutant counts, which were taken against a tree four of the mutated files have since been rewritten in. Nothing re-derives them. Run the tool to learn the current figures.

`Velvet.SourceGenerators.CodeFixes` is worth pulling out of any aggregate, because an aggregate hides it. Measured at `f0ba4b6` and **not re-measured since**: 2 mutants killed, 4 survived, 17 reached by no test at all, and 32 of its 61 failing to compile under mutation. That is not a bad score, it is the absence of one — and it ships as a committed DLL under `../Runtime/Plugins/Analyzers/`. Treat the figures as the shape of the problem rather than as its current size; the claim they support is that this project is covered by almost nothing, which no edit since has set out to change.

Read survivors rather than the score, and read each one's `coveredBy` in the JSON report first — it is the set of tests Stryker actually ran against that mutant. A small set usually just means a narrow code path, which is what one-scenario-per-test fixtures are supposed to produce; it is the fixture that tells you whether the attribution is wrong, never the count.

- `coveredBy` empty means the whole suite ran, so a survivor there is a real hole. Every mutant in `MemoizeDiagnostics.cs` is in that state — the descriptors are static, so Stryker cannot attribute them to a test and runs all of them — and the survivors among them are messages and titles no test reads, only identifiers.
- Anything a fixture computes once per process rather than once per test gets attributed to whichever test loaded the class first, and Stryker then runs that single test per mutant, which makes most of the file look unkillable. `MemoOverloadGeneratorTests` regenerates per test for exactly this reason.

**No CI job runs this, and none should gate on it.** A full run is around half an hour against 13 seconds for `dotnet test`, and a threshold would sit on the run-to-run movement described above, so it would be slow and flaky both. A scheduled run reporting the score was rejected for the reason the coverage report already demonstrates: a number nobody has to act on is a number nobody reads.

### The Unity assemblies

Stryker cannot reach them: it mutates source and rebuilds through an MSBuild project graph, and Unity compiles asmdefs inside the editor with this package's ILPP in the pipeline. `../../../scripts/test_quality/mutation_check.py` asks the same question of them without it, scoped to the lines a branch changed rather than to the package:

```bash
python3 scripts/test_quality/mutation_check.py --base main --list    # the mutants it would run, without running any
python3 scripts/test_quality/mutation_check.py --base main
python3 scripts/test_quality/mutation_check.py --files Packages/com.velvet.core/Runtime/Store/Store.cs
python3 scripts/test_quality/mutation_check.py --files <source> --filter Velvet.Tests.SomeFixture
```

Every mutant is one batchmode launch, since the mutated source has to be compiled before the runner starts and there is no coverage pass to attribute it to fewer fixtures. Launching the editor, not running the tests, is the larger half of that: the whole EditMode suite is under half of a mutant's wall clock, so narrowing the run buys little and would let a mutant read as surviving because the fixture that would have killed it was out of scope. A branch touching a few methods is minutes; the package is not, which is why the diff is the unit.

The last form narrows anyway, because it asks a different question — the one to reach for when a fixture is under suspicion rather than a change. A whole-suite run answers whether anything notices, so a fixture that asks nothing stays invisible behind every other test that does; narrowed to one fixture, a surviving mutant is that fixture not noticing.

Only changed lines are mutated, and only in the shapes it knows: a spaced binary operator, a boolean literal, a statement whose entire value is a call, a clause of a logical chain, and a single-line guard — the last two removed rather than rewritten, because the operators before them keep the clause they land in participating in the condition, so a clause no test reaches survives all of them. None of the three removals is offered where the text it would carry off could declare a name — a deconstruction, a pattern variable, an `out` argument other than a discard or an assignment to a member — since the name outlives the text and a mutant stranding a read of it compiles nowhere. A line counts as reached when any operator emits on it, so only a line the refusal leaves with no mutant at all is named in the unreached list a run prints; one still carrying an `equality`, a `logic` or a `literal` mutant reads as reached with the removal's question gone and nothing saying so. Measured when the refusal landed: of the 156 lines that lost a removal, 71 fell out of the reached set and 85 stayed in it. A change can therefore yield no mutants at all, and what happens then is decided by the lines rather than by the kind of change. A change touching no code line — documentation comments alone — passes quietly, because there was never anything to ask. A change touching code lines none of the operators reach refuses, since a verdict there would be about no line at all: measured, an added `using` and a `protected` turned `internal` both refuse, while two renames of private fields in the same file generated mutants, because their lines carried operators too. `--list` takes the same reading, so it costs one command to learn which of the three a change is before spending an editor launch per mutant on it.

**Most of a diff is in none of those shapes, so a survivor count is a statement about the lines an operator reached rather than about the change.** Measured over the twenty commits ending at `48057c8`, with the generator as it stands after the parse fixes below: 478 changed production code lines, 213 mutants, and 149 lines reached — 31%. Two things move this number, so re-take it against both rather than quoting it bare: the window slides as main moves, and a change to what the operators generate moves it without the window moving at all. A method written as a run of assignments generates nothing at all: a branch adding state-transition methods to the mutation hook came back with six mutants over its 27 changed code lines, and all 22 lines of the file holding those transitions were reached by none of them. So every verdict is printed against that denominator and the unreached lines are named, and a change whose code lines are reached by nothing at all refuses rather than reporting a clean run over no line.

Widening the operator set is the obvious answer and is not free: a statement-deletion operator over the same twenty commits would add 113 mutants to the 213 — around half as much wall clock again — to reach 113 of the 329 unreached lines. It also has to emit compilable C#, which deleting a declaration the rest of the method reads does not.

Six verdicts, and five of them need reading rather than counting:

- **survived** — no test failed. Either a test that never asked about the mutated behaviour, or a mutation the behaviour does not depend on. Which of the two it was has to be written down: the run fails until the line either stops surviving or carries the declaration [CONTRIBUTING.md ▸ Checking that the tests can fail](../../../CONTRIBUTING.md#checking-that-the-tests-can-fail) shows.
- **not rebuilt** — the assembly came out byte-identical to the baseline, so the suite ran the unmutated binary and answered nothing. This is what an edit the editor never compiled looks like, and it is the only case the check catches: a mutation the compiler did see but discarded — one inside an `#if` the editor does not define — still comes out as a different assembly and reads as **survived**, which was measured on this package rather than assumed.
- **not measured (timed out)** — the editor was killed at `--timeout`. A mutation that leaves a loop unbounded and a timeout shorter than the suite arrive here identically, and a killed editor writes no verdict either way, so this is a mutant nobody asked about and it fails the run: raise `--timeout`, or read the log.
- **uncompilable** — the build stopped, so no assembly existed for any test to run against. It is not a survivor, because nothing could have failed; it fails the run for the same reason the two above do. Twenty-five clause cuts in shapes that compile nowhere existed in this package before the probe learned to stop at an enclosing comma or ternary.
- **killed** — the failing tests are named, because a mutant killed only by a test that also fails on an unmutated tree was killed by nothing, and because whether the fixture that caught it is the one named for the behaviour is the second question worth asking of a kill.

A run is killed at `--timeout` rather than waited on for as long as the machine is left alone; what that costs is the verdict above, which cannot tell an unbounded loop from a timeout set too low.

It computes no score, for the reason the paragraph above gives about this solution's: the denominator is a different set of changed lines on every branch, so the ratio is not comparable with itself and only the survivors are worth reading. A red or inconclusive baseline stops the run before any mutant is applied — a suite that fails on its own would report mutants as killed by its own flakiness, which is worse than no run at all. Two more states stop it before it starts, for the same reason: a second editor on the machine, whose failures are a second explanation for every one the mutant gets blamed for, and a target file whose comment-and-string mask blanks a span running past the line that opened it, since a blanked offset yields no mutant and nothing downstream can tell that from a line with nothing to ask. Two others let the run finish and then fail it, because what they cost is a mutant nobody asked about rather than a reading taken wrongly: a `--max` cap with mutants left behind it, and an editor killed at `--timeout` — a mutation that leaves a loop unbounded and a timeout shorter than the suite reach that point identically, and a killed editor writes no verdict either way.

## Directory layout

```
Generators~/
├── README.md                                 (this file)
├── .gitignore                                (bin/, obj/, StrykerOutput/)
├── Velvet.SourceGenerators.sln
├── stryker-config.json                       (mutation-testing run — report only, never a gate)
├── build.py                                  (derive the style table, build the assemblies, stage them)
├── src/Velvet.SourceGenerators/              (abridged — generators, analyzers, shared helpers)
│   ├── Velvet.SourceGenerators.csproj
│   ├── MemoOverloadGenerator.cs              (auto-generates Memoized<T1..T8>)
│   ├── MemoizeMethodGenerator.cs             ([MemoizeMethod] → V.Memoized wrapper expansion)
│   ├── AutoDeps/                             (VEL100 exhaustive-deps analyzer + its hook descriptor table)
│   ├── RulesOfHooks/                         (VEL101 rules-of-hooks analyzer)
│   ├── CodeShape/                            (VEL500 depth + VEL501 branch-count + VEL502 parameter-count
│   │                                          + VEL503 tolerance on a tuple comparison)
│   ├── Diagnostics/MemoizeDiagnostics.cs     (diagnostic descriptors — see Documentation~/memoization.md)
│   ├── Diagnostics/CodeShapeDiagnostics.cs   (diagnostic descriptors — see "The code-shape rules")
│   ├── AnalyzerReleases.*.md                 (Roslyn analyzer release tracking)
│   └── Shared/                               (SourceBuilder, VelvetWellKnownNames, …)
├── src/Velvet.SourceGenerators.Bootstrap/    (second compile of the sibling's sources — see "How this solution opts in")
│   └── Velvet.SourceGenerators.Bootstrap.csproj
├── src/Velvet.SourceGenerators.CodeFixes/    (ships to ../Runtime/Plugins/Analyzers/)
├── src/Velvet.StyleTable/                    (console tool — writes ../Runtime/Styling/StyleUtilityProperties.g.cs)
│   ├── Velvet.StyleTable.csproj              (CLI entry point below)
│   ├── Program.cs                            (CLI: --styles <dir> --output <file>)
│   ├── UssStyleSheetParser.cs                (rules and declarations, straight off the text)
│   ├── UssCascadeOrder.cs                    (the @import order the importer flattens the sheets to)
│   ├── UssSelector.cs                        (which selector shapes the table can model)
│   ├── UssPropertyVocabulary.cs              (UI Toolkit longhands + the shorthands that expand into them)
│   ├── UssProblem.cs                         (USS001-USS011 — why a derivation refused to write)
│   ├── StyleUtilityTableBuilder.cs           (stylesheets → class → property set)
│   └── StyleUtilityTableEmitter.cs           (property set → C# source)
└── tests/Velvet.SourceGenerators.Tests/      (abridged)
    ├── Velvet.SourceGenerators.Tests.csproj
    ├── SourceBuilderTests.cs
    ├── MemoOverloadGeneratorTests.cs
    ├── MemoizeMethodGeneratorTests.cs
    ├── StyleUtilityTableTests.cs              (one case per USS shape, one problem per unmodelled shape)
    ├── BundledStyleSheetCensusTests.cs        (pins the committed table + the census of ../Runtime/Styles/*.uss)
    ├── HookSurfaceDriftTests.cs               (pins the analyzer name lists to ../Runtime/)
    ├── StubSurfaceDriftTests.cs               (pins the Velvet stub's signatures to ../Runtime/)
    ├── RuntimeSourceIndex.cs                  (the shared syntax-only parse of ../Runtime/)
    ├── GeneratorTestHelper.cs                 (the Velvet stub the test sources compile against)
    └── Snapshots/                            (verified golden files)
        ├── Memoized_Arity*/MemoizedWithKey_Arity*    (MemoOverloadGenerator)
        └── Memoize/                          (MemoizeMethodGenerator)
```

The `~` suffix is the Unity Asset DB convention for "ignore this directory". Nothing under it is visible to Unity; the artifacts it produces are.

## The utility property table

`Velvet.StyleTable` reads `../Runtime/Styles/*.uss` and writes `../Runtime/Styling/StyleUtilityProperties.g.cs`: for each bundled utility class, the set of longhand properties its rule writes and the gate the rule carries. Class-payload variants (`dark:`, `md:`) apply a bare utility by adding it to the live class list, and bundled utilities are single-class rules, so a base class and a variant-applied class tie on specificity and the later stylesheet declaration wins regardless of intent. Resolving that by priority instead needs to know which properties are actually at stake per class.

It is a build-time tool rather than a source generator because the answer is a function of package content alone — a consumer never edits the bundled stylesheets, and nothing about their compilation can change it. Deriving it inside every consumer's build would re-parse two thousand rules on every incremental compile to reproduce a constant, and would additionally need plumbing that does not exist: Unity's `RoslynAdditionalFileImporter` keys strictly on the `.additionalfile` extension, so a `.uss` file cannot reach a generator as an additional file at all.

Committing generated source is only safe with a guard, and `BundledStyleSheetCensusTests` is it: it re-derives from the stylesheets and compares against the committed file. Unlike the two DLLs — which embed the git `HEAD` commit id and so can never be byte-compared against a rebuild — the emitted C# is deterministic, so the comparison is exact. `generators.yml` triggers on `Runtime/**`, so a stylesheet edit runs it whether or not any code changed.

Failures are reported as `USS001`-`USS011` and refuse to write the table. They are deliberately outside the `VEL###` space the analyzers use: nothing can suppress a build-script failure through an `.editorconfig` severity, and sharing the namespace would invite someone to try. Each code marks an assumption the table depends on — that `:root` declares only custom properties, that a utility declares no custom property, that every property name resolves to the pinned UI Toolkit vocabulary, that one class carries one gate, that the sheets arrive in the aggregator's `@import` order, that no gated rule declares `transition-property` — so the assumption cannot rot silently.

Four partials declare no rules and are expected to: `StyleUtilities.uss` is nothing but `@import`, and `_gap.uss`, `_presets.uss` and `_states.uss` describe utility families Velvet realises in C# rather than in USS (each says so in its own header). Their classes are therefore absent from the table, which from the table's side looks identical to a class that sets nothing — `BundledStyleSheetCensusTests` pins the list so the distinction stays visible.

## The code-shape rules

Three mechanical limits and one mechanical defect ship as analyzers under the `Velvet.Shape` category. Every
diagnostic this repository defines is listed in
[AnalyzerReleases.Unshipped.md](src/Velvet.SourceGenerators/AnalyzerReleases.Unshipped.md); what follows is
only the four definitions, which no table can carry, and the gate they share.

### The nesting-depth limit

`VEL500` is an error on any member body nesting control flow more than **4** levels deep.

Depth is the height of the nesting tree, not a count of indentation or of braces. A construct that opens a
level contributes one to everything beneath it, and a block is transparent — so `if (x) return;` and
`if (x) { return; }` measure the same, and dropping braces is not an escape.

Opens a level: `if`, `for`, `foreach`, `while`, `do`, `switch` (statement), `try`, `using` (statement form),
`lock`, and the body of a nested function — lambda, anonymous method or local function.

Transparent: the `if` of an `else if` (a chain is one level, so the flat multiway branch is not charged more
than the nested rewrite it replaces); `catch` and `finally` (siblings of their `try`, like the branches of an
`if`); the `using var` declaration form (no body); the expression-level branch forms `?:`, `switch`
expressions, `&&` and query expressions, which carry no statements and are themselves the flattening device;
and `unsafe` / `fixed` / `checked` blocks, which change compilation context rather than control flow.

Bodies measured: method, constructor, destructor, operator and accessor bodies, expression-bodied members,
and field and property initializers. Initializers count because they are the other place a deep lambda could
be parked without moving it.

A nested function is measured where it sits rather than resetting, so a block cannot satisfy the limit by
being wrapped in a lambda in place. Extraction to a sibling member is the remedy the rule pushes toward, and
that does reset — each member is measured from its own body.

### The branch-count limit

`VEL501` is an error on any member making more than **20** branching decisions. A branch is counted wherever
it appears in the body, including inside a lambda or local function the member declares — which does not
reset, for the reason depth does not.

Counted: `if`, and separately each `else if` of a chain; `for`, `foreach`, `while`, `do`; `&&`, `||`, `??`,
`??=`, `?:`; each `case` label; each arm of a `switch` expression; each `catch`; each `when` filter, beside
the `catch` or `case` it guards; and `and` / `or` between patterns.

Not counted: the closing `else`, `default:`, and the `_` or `var` arm of a `switch` expression — each runs
when no decision picked anything, and charging for them would price an exhaustive `switch` above one that
silently falls through; `try`, `finally`, `switch`, `lock` and `using`, which open no decision of their own;
`not`, which inverts one test rather than adding a second; `?.` and a bare `is`, which produce a value that
some other construct — already counted where it appears — turns into a decision; and a query expression,
whose clauses are calls, matching what the same query spelled in method syntax costs.

Where this disagrees with the depth definition it is deliberate. Depth is a property of the deepest path, so
it has to treat an `else if` chain and the expression-level branch forms as transparent: each is the
flattening device a depth limit pushes code toward, and charging for it would make the nested rewrite the
cheaper option. Count is a property of the whole body, and those same forms are what a flattened dense
parser is made of. A rule that let them through would be satisfied by turning nesting into width, and would
measure nothing.

Extraction to a sibling member is again the remedy: each member is counted from its own body.

### The parameter-count limit

`VEL502` is an error on any declaration demanding more than **6** arguments from every caller.

Counted: a parameter with no default value, including the `this` of an extension method — the receiver is
written at every call site in a fixed position, exactly as a first argument is.

Not counted: a parameter carrying a default value, and a trailing `params` array. Each can be left out of the
call entirely, so neither adds to what a caller has to line up or a reader has to decode. This is what exempts
the `V.*` factory surface without naming it: `V.Motion` declares 21 parameters, all optional and named, and
stands in for JSX props — while a helper sitting in the same file that demands eight positional arguments is
still reported. Naming the factories directly was the alternative — by file, by declaring type, or by return
type — and each of those exempts a helper that happens to sit among them, which is the population the rule
most needs to reach.

Declarations measured: methods, constructors, indexers, delegates, local functions, and primary constructors
on a class, struct or record. A local function does not reset, for the reason the sibling rules do not. The
operator forms are absent because the language caps them at two parameters.

The remedy is to group the parameters that travel together into a type the caller builds once — a `readonly
struct` with `init` properties, passed by `in`, costs no allocation — or to split the member along the axis
its parameters already divide it by.

One shipped surface is narrowed by this limit: `[MemoizeMethod]` supports 1-8 parameters (VEL002), so its top
two arities are unreachable inside an assembly that opts in. `V.Memoized<T1..T8>` is generated code and is
unaffected.

### The tolerance that cannot apply

`VEL503` is a warning on `Is.EqualTo(<tuple>)` carrying a `.Within(...)`.

NUnit's comparer chain has no entry for `ValueTuple`, so the pair falls through to the expected value's own
`IEquatable<T>`, which is not handed the `ref Tolerance` the numeric path receives. The assertion is bit-exact
equality, and its failure message prints the tolerance it did not use, so nothing at run time separates it
from one that applied. The remedy is to round each member before comparing, or to compare formatted strings.

Reported: any expected value whose type is a tuple, held in a local as readily as written as a parenthesised
literal — the type decides, not the syntax. The tolerance need not be the equality's immediate link, so a
constraint carrying another modifier between the two is still reported.

Not reported: a tuple inside an expected collection, where the tolerance descends into the collection and
then dies at the element — `Is.EqualTo(new[] { ("opacity", 150f) }).Within(1e-3f)` traps exactly as the bare
tuple does, while the expected type is an array and so does not match; a constraint built in one statement
and given its tolerance in another, since only a single chained expression is followed; an `EqualTo`/`Within`
pair declared outside NUnit, whose comparison this rule knows nothing about; and a tolerance dropped by
another expected type reaching the same `IEquatable<T>` fall-through — a tuple is the shape this repository
writes, not the only one that loses a tolerance.

The collection case is measured, not inferred: an array of scalars keeps its tolerance
(`Is.EqualTo(new[] { 1f }).Within(1e-4f)` passes against `0.99999f`) and an array of tuples does not, failing
with `Values differ at index [0]` and printing the tolerance beneath it.

It is a warning where its three siblings are errors because the package's own test assemblies still carry
sites it reports, and they opt into this category. An error would stop them compiling, and a test assembly
that does not compile runs no tests — including the ones that would have caught the next regression.

### Why the rules are opt-in per assembly

The analyzers ship in the package and the `RoslynAnalyzer` label propagates them to every assembly that
references Velvet, which in this project includes the default `Assembly-CSharp-Editor` and in a consumer's
project includes their game code. An error must have every site it fires on fixed by whoever ships it, and
this package cannot fix a consumer's game code. So the `Velvet.Shape` diagnostics fire only in an assembly
that declares

```csharp
[assembly: System.Reflection.AssemblyMetadata("Velvet.CodeShape", "enforce")]
```

Every asmdef under `Packages/com.velvet.core/` carries this in an `AssemblyInfo.cs`, and
`CodeShapeOptInDriftTests` fails when a new one does not — an assembly that silently escapes the rule looks
exactly like an assembly that satisfies it.

The marker is an assembly attribute rather than a check on the assembly name because a name cannot separate
the two populations in either direction: `Unity.Velvet.CodeGen` belongs to the package without carrying a
`Velvet.` prefix, and nothing stops a consumer from naming an assembly whatever pattern the check looked
for.

A consumer who wants the limits on their own assembly opts in with the same line.

### How this solution opts in

This solution is where the three analyzers are written, and for a long time it was the one place they never
ran: it references nothing from the package, so the `RoslynAnalyzer` label never reaches it. A project here
opts in with **two** MSBuild items, and needs both — the marker alone leaves the analyzers unloaded, and the
reference alone leaves the gate closed:

```xml
<AssemblyMetadata Include="Velvet.CodeShape" Value="enforce" />
<ProjectReference Include="..\Velvet.SourceGenerators.Bootstrap\Velvet.SourceGenerators.Bootstrap.csproj"
                  OutputItemType="Analyzer" ReferenceOutputAssembly="false" />
```

`GeneratorProjectOptInDriftTests` fails when a project under `Generators~` carries neither, for the reason
its Unity counterpart exists: a project that escapes looks exactly like one that complies. Neither half is
decided from the project XML — the marker goes through `CodeShapeMembers.OptsIntoCodeShapeRules`, the
analyzers' own gate, against the built assembly, and the reference through MSBuild's evaluation. That
fixture owns why each half needs the instrument it uses, and what the pair of them still does not reach.

The analyzers arrive from a **second compile** of the same sources rather than from the project that declares
them, because a project cannot reference itself as an analyzer — MSBuild rejects the cycle with `MSB4006`
during restore, before any compile starts. The alternative, pointing at the already-committed
`../Runtime/Plugins/Generators/Velvet.SourceGenerators.dll`, was rejected: `build.py` deploys that artifact
after this compile, so an edit to a rule would go on being enforced by the previous rule throughout the very
build that introduced it — the failure mode this wiring exists to remove, and one a check run afterwards
cannot reach.

The bootstrap therefore compiles the sibling's `**/*.cs` through a glob and is not itself opted in; its
content is measured by the sibling's own compile. A glob that stopped matching would produce an analyzer
assembly holding no analyzers, and all four projects would then still build — measured with a violation of
each rule planted in every one of them, and no error anywhere — which is why one case compares the two
assemblies' declared type names rather than trusting the build to notice.

## Using `[MemoizeMethod]`

End-user guidance — usage, constraints, diagnostic IDs, and examples — has moved to [Documentation~/memoization.md](../Documentation~/memoization.md).

This README is now scoped to **contributor concerns** (build / test / artifact shipping / CI).

## CI

`.github/workflows/generators.yml` builds and tests this solution in its `source-generators` job: check out, install the .NET SDK pinned by `global.json`, then `dotnet test Velvet.SourceGenerators.sln -c Release --nologo` (which restores and builds as part of the run), then the repository guards that run without a Unity license.

Which paths trigger it is stated in the repository's `CLAUDE.md`; the reason `Runtime/**` is among them is that the drift guards read the runtime sources, so a PR that only renames a hook, reshapes its signature or edits a stylesheet must still run this job.

Each committed artifact is compared against a fresh derivation, so forgetting to regenerate one fails rather than reaching Unity:

- `../Runtime/Styling/StyleUtilityProperties.g.cs` — `BundledStyleSheetCensusTests`, which re-derives the table from the bundled stylesheets
- both DLLs under `../Runtime/Plugins/` — `scripts/generators/deployed_dll_check.py`, which rebuilds each deployed project and compares byte for byte

The byte comparison is possible because the build reproduces across machines as well as across commits. Two inputs had to be removed for that, and each is set where it is because of the measurement below.

**The commit.** The SDK queries git and writes the commit `HEAD` was at into the assembly's informational version (`0.1.0+<sha>`), so identical sources built at two commits differed by 143 bytes — that string, the Win32 version resource repeating it, and the identity fields computed over the result: the MVID, the PE timestamp, the debug directory timestamp, the PDB GUID and the PDB checksum. Two clean builds in one working tree, and two builds of one commit from two different paths, were already byte-identical, so neither repetition nor the path was varying the output while the commit was. `build.py` runs before the commit that carries its output, so a deployed assembly named a commit older than the one it shipped in; the pair this replaced named one three commits back. `Directory.Build.props` turns those queries off.

**The runtime the compiler ran on.** With the commit gone, a macOS build and the Linux CI build still differed by 72 bytes, in five runs: the PE timestamp, the MVID, the debug directory timestamp, the PDB GUID and the PDB checksum. Every other byte of both assemblies was identical across the two operating systems and the two architectures. A portable PDB records the compilation options, and comparing them named the difference: `runtime-version`, because `global.json` pins the SDK rather than the runtime the SDK's compiler executes on. The assembly carries the PDB's checksum, so a PDB that differs moves the assembly. Each deployed project drops its Release PDB, which removes the carrier — measured byte-identical between macOS on arm64 and Linux on x86-64 afterwards. The cost is real: a contributor debugging a Release build of a generator gets no line numbers. Debug builds keep their PDB.

Pinning the runtime instead was rejected. It is set through the .NET host's DOTNET_ROLL_FORWARD variable rather than through a project property, so it would have to be right in CI, in every contributor's shell and in every IDE that builds this solution — and a build that did not set it would produce different bytes with nothing to say so. A guard that holds only where an environment variable was remembered is the failure mode this exists to remove.

The check refuses rather than passes when it cannot make the comparison — an absent binary, a build that fails, an SDK other than the one `global.json` pins. `test_deployed_dll_check.py` runs each of those states, because a guard that exits 0 having compared nothing is indistinguishable from one that compared and was satisfied.
