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
- `MemoOverloadGeneratorTests` — snapshot comparison that verifies the generated `V.Memoized<T1..T8>` output
- `MemoizeMethodGeneratorTests` — verifies `[MemoizeMethod]`-driven `V.Memoized(...)` wrapper expansion and its diagnostics (see [Documentation~/memoization.md](../Documentation~/memoization.md) for what they mean and the complete list)
- `HookSurfaceDriftTests` — pins the analyzer's hook-name and type-name strings to the runtime surface by parsing `../Runtime/` with Roslyn (syntax only, no Unity assemblies). Nothing else notices a hook rename or a newly added deps-comparing hook on this side of the compile boundary, so the guard turns both into a red test instead of silently narrowed exhaustive-deps coverage
- `StubSurfaceDriftTests` — pins the Velvet stub in `GeneratorTestHelper` (the surface every analyzer and generator test compiles its sample user code against) to the runtime signatures, using the same syntax-only parse. It fails on a divergent parameter type, return type, generic constraint, modifier or optionality, and on a runtime overload the stub names but does not model; without it the suite can verify a diagnostic for a call shape no user can write
- `StyleUtilityTableTests` — one case per USS shape the bundled stylesheets contain, plus the problem each shape they do not produces. Compiles and calls the emitted table rather than pattern-matching its source, which is what puts the bit packing under assertion and is also the only place the emitted C# is proved to compile
- `BundledStyleSheetCensusTests` — re-derives the table from `../Runtime/Styles/*.uss` and compares it against the committed `../Runtime/Styling/StyleUtilityProperties.g.cs`, and pins the selector-shape and property census the derivation is designed around. This is what makes committing the table safe: a stylesheet edit not accompanied by a regenerated table, or one that introduces an unsurveyed shape, fails here rather than downstream where a class missing from the table is indistinguishable from a class that conflicts with nothing

## Directory layout

```
Generators~/
├── README.md                                 (this file)
├── .gitignore                                (bin/, obj/)
├── Velvet.SourceGenerators.sln
├── build.sh / build.ps1                      (derive the style table, build the assemblies, stage them)
├── src/Velvet.SourceGenerators/              (abridged — generators, analyzers, shared helpers)
│   ├── Velvet.SourceGenerators.csproj
│   ├── MemoOverloadGenerator.cs              (auto-generates Memoized<T1..T8>)
│   ├── MemoizeMethodGenerator.cs             ([MemoizeMethod] → V.Memoized wrapper expansion)
│   ├── AutoDeps/                             (VEL100 exhaustive-deps analyzer + its hook descriptor table)
│   ├── RulesOfHooks/                         (VEL101 rules-of-hooks analyzer)
│   ├── Diagnostics/MemoizeDiagnostics.cs     (diagnostic descriptors — see Documentation~/memoization.md)
│   ├── AnalyzerReleases.*.md                 (Roslyn analyzer release tracking)
│   └── Shared/                               (SourceBuilder, VelvetWellKnownNames, …)
├── src/Velvet.SourceGenerators.CodeFixes/    (ships to ../Runtime/Plugins/Analyzers/)
├── src/Velvet.StyleTable/                    (console tool — writes ../Runtime/Styling/StyleUtilityProperties.g.cs)
│   ├── Program.cs                            (CLI: --styles <dir> --output <file>)
│   ├── UssStyleSheetParser.cs                (rules and declarations, straight off the text)
│   ├── UssSelector.cs                        (which selector shapes the table can model)
│   ├── UssPropertyVocabulary.cs              (UI Toolkit longhands + the shorthands that expand into them)
│   ├── UssProblem.cs                         (USS001-008 — why a derivation refused to write)
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

Failures are reported as `USS001`-`USS008` and refuse to write the table. They are deliberately outside the `VEL###` space the analyzers use: nothing can suppress a build-script failure through an `.editorconfig` severity, and sharing the namespace would invite someone to try. Each code marks an assumption the table depends on — that `:root` declares only custom properties, that a utility declares no custom property, that every property name resolves to the pinned UI Toolkit vocabulary, that one class carries one gate — so the assumption cannot rot silently.

Four partials declare no rules and are expected to: `StyleUtilities.uss` is nothing but `@import`, and `_gap.uss`, `_presets.uss` and `_states.uss` describe utility families Velvet realises in C# rather than in USS (each says so in its own header). Their classes are therefore absent from the table, which from the table's side looks identical to a class that sets nothing — `BundledStyleSheetCensusTests` pins the list so the distinction stays visible.

## Using `[MemoizeMethod]`

End-user guidance — usage, constraints, diagnostic IDs, and examples — has moved to [Documentation~/memoization.md](../Documentation~/memoization.md).

This README is now scoped to **contributor concerns** (build / test / artifact shipping / CI).

## CI

`.github/workflows/generators.yml` has a single job with one build/test step: check out, install the .NET SDK pinned by `global.json`, then `dotnet test Velvet.SourceGenerators.sln -c Release --nologo` (which restores and builds as part of the run). No Unity license is required.

Which paths trigger it is stated in the repository's `CLAUDE.md`; the reason `Runtime/**` is among them is that the drift guards read the runtime sources, so a PR that only renames a hook, reshapes its signature or edits a stylesheet must still run this job.

**CI does not check the committed DLLs under `../Runtime/Plugins/` at all.** It tests the sources; it never compares the deployed assemblies against a rebuild, so a PR that edits generator sources and forgets to rerun the build script goes green while Unity keeps consuming the stale binaries. Rebuilding and committing them is the contributor's responsibility. The third committed artifact, `../Runtime/Styling/StyleUtilityProperties.g.cs`, is the exception — `BundledStyleSheetCensusTests` compares it against a fresh derivation, so forgetting to regenerate that one is caught.

A plain `git diff --exit-code` on the deployed DLLs would not close that gap either: the build embeds the git `HEAD` commit id in the assembly, so rebuilding at commit *N* never reproduces the DLL committed *in* commit *N* (it was necessarily built at *N-1*). The build is otherwise deterministic — repeated rebuilds of unchanged sources at the same `HEAD` are byte-identical.
