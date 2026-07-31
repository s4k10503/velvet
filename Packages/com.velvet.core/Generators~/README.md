# Velvet.SourceGenerators

Compile-time tooling bundled with `com.velvet.core`: the Roslyn analyzers and incremental source generators that ship as assemblies, and `Velvet.StyleTable`, which derives committed source from the bundled stylesheets. The directory keeps its name from the former; both live here because they share one solution, one build script, one test project and one license-free CI job.

## Build

Run the following inside the `Generators~/` directory:

```bash
./build.sh    # macOS / Linux
./build.ps1   # Windows
```

On Windows, run `build.ps1` from PowerShell 7 (`pwsh`) and close Unity and any IDE first: Windows PowerShell 5.1's default execution policy refuses unsigned scripts, and Windows cannot overwrite an analyzer assembly a Roslyn host still holds open.

Either script regenerates all three committed artifacts, leaving the tree in the same state:

- `../Runtime/Styling/StyleUtilityProperties.g.cs`
- `../Runtime/Plugins/Generators/Velvet.SourceGenerators.dll`
- `../Runtime/Plugins/Analyzers/Velvet.SourceGenerators.CodeFixes.dll`

Commit all three. The distribution model assumes Unity users do not need to install `dotnet`.

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
- `BundledStyleSheetCensusTests` — re-derives the table from `../Runtime/Styles/*.uss` and compares it against the committed `../Runtime/Styling/StyleUtilityProperties.g.cs`, and pins the selector-shape and property census the derivation is designed around. This is what makes committing the table safe: a stylesheet edit not accompanied by a regenerated table, or one that introduces an unsurveyed shape, fails here rather than downstream where a class missing from the table is indistinguishable from a class that conflicts with nothing

## Mutation testing

```bash
export DOTNET_ROOT=/opt/homebrew/opt/dotnet/libexec   # Apple-silicon Homebrew; /usr/local/opt/dotnet/libexec on Intel
dotnet tool update --global dotnet-stryker --version 4.16.0
dotnet-stryker    # reads stryker-config.json
```

The tool is pinned for the same reason the SDK and the Roslyn host are: how many mutants it creates, and which, is its version's answer, and the counts below are 4.16.0's. It ships a `net8.0` asset with major roll-forward, so it runs on whatever runtime is installed and does not constrain `global.json`. Without `DOTNET_ROOT` a Homebrew install fails to launch it — the apphost probes only `/usr/local/share/dotnet`.

Stryker mutates the three source projects one behaviour change at a time, rebuilds, and reruns **only the tests its coverage pass attributes to that mutant** — not the suite. The report lands in `StrykerOutput/<timestamp>/reports/`. It answers what line coverage does not: whether the tests that run a line would notice that line being wrong.

The score is `(killed + timeout) / (killed + timeout + survived + no-coverage)`, counting a mutant no test reaches as not rejected. Of the 2586 mutants created here, 377 fall to Stryker's own "block already covered" filter and 314 do not compile; neither is scored, so the denominator is 1895. That split is deterministic. How the 1895 divide between killed and survived is not — two runs over an unchanged tree moved 29 of them — so no percentage is quoted here: one would be stale by the next commit and nothing checks it.

`Velvet.SourceGenerators.CodeFixes` is worth pulling out of any aggregate, because an aggregate hides it: 2 mutants killed, 4 survived, 17 reached by no test at all, and 32 of its 61 failing to compile under mutation. That is not a bad score, it is the absence of one — and it ships as a committed DLL under `../Runtime/Plugins/Analyzers/`.

Read survivors rather than the score, and read each one's `coveredBy` in the JSON report first — it is the set of tests Stryker actually ran against that mutant. A small set usually just means a narrow code path, which is what one-scenario-per-test fixtures are supposed to produce; it is the fixture that tells you whether the attribution is wrong, never the count.

- `coveredBy` empty means the whole suite ran, so a survivor there is a real hole. Every mutant in `MemoizeDiagnostics.cs` is in that state — the descriptors are static, so Stryker cannot attribute them to a test and runs all of them — and the survivors among them are messages and titles no test reads, only identifiers.
- Anything a fixture computes once per process rather than once per test gets attributed to whichever test loaded the class first, and Stryker then runs that single test per mutant, which makes most of the file look unkillable. `MemoOverloadGeneratorTests` regenerates per test for exactly this reason.

**No CI job runs this, and none should gate on it.** A full run is around half an hour against 13 seconds for `dotnet test`, and a threshold would sit on the run-to-run movement described above, so it would be slow and flaky both. A scheduled run reporting the score was rejected for the reason the coverage report already demonstrates: a number nobody has to act on is a number nobody reads.

### The Unity assemblies

Stryker cannot reach them: it mutates source and rebuilds through an MSBuild project graph, and Unity compiles asmdefs inside the editor with this package's ILPP in the pipeline. `../../../scripts/mutation-check.py` asks the same question of them without it, scoped to the lines a branch changed rather than to the package:

```bash
python3 scripts/mutation-check.py --base main --list    # the mutants it would run, without running any
python3 scripts/mutation-check.py --base main
python3 scripts/mutation-check.py --files Packages/com.velvet.core/Runtime/Store/Store.cs
python3 scripts/mutation-check.py --files <source> --filter Velvet.Tests.SomeFixture
```

Every mutant is one batchmode launch, since the mutated source has to be compiled before the runner starts and there is no coverage pass to attribute it to fewer fixtures. Launching the editor, not running the tests, is the larger half of that: the whole EditMode suite is under half of a mutant's wall clock, so narrowing the run buys little and would let a mutant read as surviving because the fixture that would have killed it was out of scope. A branch touching a few methods is minutes; the package is not, which is why the diff is the unit.

The last form narrows anyway, because it asks a different question — the one to reach for when a fixture is under suspicion rather than a change. A whole-suite run answers whether anything notices, so a fixture that asks nothing stays invisible behind every other test that does; narrowed to one fixture, a surviving mutant is that fixture not noticing.

Only changed lines are mutated, and only in the shapes it knows: a spaced binary operator, a boolean literal, and a statement whose entire value is a call, which it deletes. A change can therefore yield no mutants at all — a rename, a move, a signature — which `--list` shows for free and which means the run asked nothing, not that everything survived scrutiny.

Three of the verdicts need reading rather than counting:

- **survived** — no test failed. Either a test that never asked about the mutated behaviour, or a mutation the behaviour does not depend on.
- **not built** — the assembly came out byte-identical to the baseline, so the suite ran against unmutated code and answered nothing. A mutation inside an `#if` the editor does not define lands here.
- **killed** — the failing tests are named, because a mutant killed only by a test that also fails on an unmutated tree was killed by nothing, and because whether the fixture that caught it is the one named for the behaviour is the second question worth asking of a kill.

A run that does not finish inside `--timeout` is counted as killed and says so: a mutation that leaves the suite running forever — an inverted loop bound, most often — did change the behaviour, and the alternative is a harness that waits on it for as long as the machine is left alone.

It computes no score, for the reason the paragraph above gives about this solution's: the denominator is a different set of changed lines on every branch, so the ratio is not comparable with itself and only the survivors are worth reading. A red or inconclusive baseline stops the run before any mutant is applied — a suite that fails on its own would report mutants as killed by its own flakiness, which is worse than no run at all.

## Directory layout

```
Generators~/
├── README.md                                 (this file)
├── .gitignore                                (bin/, obj/, StrykerOutput/)
├── Velvet.SourceGenerators.sln
├── stryker-config.json                       (mutation-testing run — report only, never a gate)
├── build.sh / build.ps1                      (derive the style table, build the assemblies, stage them)
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
├── src/Velvet.SourceGenerators.CodeFixes/    (ships to ../Runtime/Plugins/Analyzers/)
├── src/Velvet.StyleTable/                    (console tool — writes ../Runtime/Styling/StyleUtilityProperties.g.cs)
│   ├── Velvet.StyleTable.csproj              (force-added — the root .gitignore ignores *.csproj)
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

## Using `[MemoizeMethod]`

End-user guidance — usage, constraints, diagnostic IDs, and examples — has moved to [Documentation~/memoization.md](../Documentation~/memoization.md).

This README is now scoped to **contributor concerns** (build / test / artifact shipping / CI).

## CI

`.github/workflows/generators.yml` has a single job with one build/test step: check out, install the .NET SDK pinned by `global.json`, then `dotnet test Velvet.SourceGenerators.sln -c Release --nologo` (which restores and builds as part of the run). No Unity license is required.

Which paths trigger it is stated in the repository's `CLAUDE.md`; the reason `Runtime/**` is among them is that the drift guards read the runtime sources, so a PR that only renames a hook, reshapes its signature or edits a stylesheet must still run this job.

**CI does not check the committed DLLs under `../Runtime/Plugins/` at all.** It tests the sources; it never compares the deployed assemblies against a rebuild, so a PR that edits generator sources and forgets to rerun the build script goes green while Unity keeps consuming the stale binaries. Rebuilding and committing them is the contributor's responsibility. The third committed artifact, `../Runtime/Styling/StyleUtilityProperties.g.cs`, is the exception — `BundledStyleSheetCensusTests` compares it against a fresh derivation, so forgetting to regenerate that one is caught.

A plain `git diff --exit-code` on the deployed DLLs would not close that gap either: the build embeds the git `HEAD` commit id in the assembly, so rebuilding at commit *N* never reproduces the DLL committed *in* commit *N* (it was necessarily built at *N-1*). The build is otherwise deterministic — repeated rebuilds of unchanged sources at the same `HEAD` are byte-identical.
