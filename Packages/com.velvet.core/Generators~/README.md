# Velvet.SourceGenerators

Roslyn Incremental Source Generators bundled with `com.velvet.core`.

## Build

Run the following inside the `Generators~/` directory:

```bash
./build.sh    # macOS / Linux
./build.ps1   # Windows
```

On Windows, run `build.ps1` from PowerShell 7 (`pwsh`) and close Unity and any IDE first: Windows PowerShell 5.1's default execution policy refuses unsigned scripts, and Windows cannot overwrite an analyzer assembly a Roslyn host still holds open.

Either script rebuilds and deploys both shipped assemblies, leaving the tree in the same state:

- `../Runtime/Plugins/Generators/Velvet.SourceGenerators.dll`
- `../Runtime/Plugins/Analyzers/Velvet.SourceGenerators.CodeFixes.dll`

Commit both rebuilt DLLs. The distribution model assumes Unity users do not need to install `dotnet`.

## Test

```bash
dotnet test Velvet.SourceGenerators.sln
```

- `SourceBuilderTests` — unit tests for the indent / block helpers in `Shared/SourceBuilder.cs`
- `MemoOverloadGeneratorTests` — snapshot comparison that verifies the generated `V.Memoized<T1..T8>` output
- `MemoizeMethodGeneratorTests` — verifies `[MemoizeMethod]`-driven `V.Memoized(...)` wrapper expansion and its diagnostics (see [Documentation~/memoization.md](../Documentation~/memoization.md) for what they mean and the complete list)
- `HookSurfaceDriftTests` — pins the analyzer's hook-name and type-name strings to the runtime surface by parsing `../Runtime/` with Roslyn (syntax only, no Unity assemblies). Nothing else notices a hook rename or a newly added deps-comparing hook on this side of the compile boundary, so the guard turns both into a red test instead of silently narrowed exhaustive-deps coverage
- `StubSurfaceDriftTests` — pins the Velvet stub in `GeneratorTestHelper` (the surface every analyzer and generator test compiles its sample user code against) to the runtime signatures, using the same syntax-only parse. It fails on a divergent parameter type, return type, generic constraint, modifier or optionality, and on a runtime overload the stub names but does not model; without it the suite can verify a diagnostic for a call shape no user can write

## Directory layout

```
Generators~/
├── README.md                                 (this file)
├── .gitignore                                (bin/, obj/)
├── Velvet.SourceGenerators.sln
├── build.sh / build.ps1                      (build the assemblies and stage them)
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
└── tests/Velvet.SourceGenerators.Tests/      (abridged)
    ├── Velvet.SourceGenerators.Tests.csproj
    ├── SourceBuilderTests.cs
    ├── MemoOverloadGeneratorTests.cs
    ├── MemoizeMethodGeneratorTests.cs
    ├── HookSurfaceDriftTests.cs               (pins the analyzer name lists to ../Runtime/)
    ├── StubSurfaceDriftTests.cs               (pins the Velvet stub's signatures to ../Runtime/)
    ├── RuntimeSourceIndex.cs                  (the shared syntax-only parse of ../Runtime/)
    ├── GeneratorTestHelper.cs                 (the Velvet stub the test sources compile against)
    └── Snapshots/                            (verified golden files)
        ├── Memoized_Arity*/MemoizedWithKey_Arity*    (MemoOverloadGenerator)
        └── Memoize/                          (MemoizeMethodGenerator)
```

The `~` suffix is the Unity Asset DB convention for "ignore this directory". Generator sources are not visible to Unity.

## Using `[MemoizeMethod]`

End-user guidance — usage, constraints, diagnostic IDs, and examples — has moved to [Documentation~/memoization.md](../Documentation~/memoization.md).

This README is now scoped to **contributor concerns** (build / test / DLL shipping / CI).

## CI

`.github/workflows/generators.yml` has a single job with one build/test step: check out, install the .NET SDK pinned by `global.json`, then `dotnet test Velvet.SourceGenerators.sln -c Release --nologo` (which restores and builds as part of the run). No Unity license is required.

Which paths trigger it is stated in the repository's `CLAUDE.md`; the reason `Runtime/**` is among them is that the two drift guards read the runtime sources, so a PR that only renames a hook or reshapes its signature must still run this job.

**CI does not check the committed DLLs under `../Runtime/Plugins/` at all.** It tests the sources; it never compares the deployed assemblies against a rebuild, so a PR that edits generator sources and forgets to rerun the build script goes green while Unity keeps consuming the stale binaries. Rebuilding and committing them is the contributor's responsibility.

A plain `git diff --exit-code` on the deployed DLLs would not close that gap either: the build embeds the git `HEAD` commit id in the assembly, so rebuilding at commit *N* never reproduces the DLL committed *in* commit *N* (it was necessarily built at *N-1*). The build is otherwise deterministic — repeated rebuilds of unchanged sources at the same `HEAD` are byte-identical.
