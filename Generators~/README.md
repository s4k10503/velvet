# Velvet.SourceGenerators

Roslyn Incremental Source Generators bundled with `com.velvet.core`.

## Build

Run one of the following inside the `Generators~/` directory to regenerate `../Runtime/Plugins/Generators/Velvet.SourceGenerators.dll`:

```bash
./build.sh    # macOS / Linux
./build.ps1   # Windows
```

Commit the rebuilt DLL. The distribution model assumes Unity users do not need to install `dotnet`.

## Test

```bash
dotnet test Velvet.SourceGenerators.sln
```

- `SourceBuilderTests` — unit tests for the indent / block helpers in `Shared/SourceBuilder.cs`
- `MemoOverloadGeneratorTests` — snapshot comparison that verifies the generated `V.Memoized<T1..T8>` output
- `MemoizeMethodGeneratorTests` — verifies `[Memoize]`-driven `V.Memoized(...)` wrapper expansion and the VEL001–011 diagnostics

## Directory layout

```
Generators~/
├── README.md                                 (this file)
├── .gitignore                                (bin/, obj/)
├── Velvet.SourceGenerators.sln
├── build.sh / build.ps1                      (build the DLL and stage it)
├── src/Velvet.SourceGenerators/
│   ├── Velvet.SourceGenerators.csproj
│   ├── MemoOverloadGenerator.cs              (auto-generates Memoized<T1..T8>)
│   ├── MemoizeMethodGenerator.cs             ([Memoize] → V.Memoized wrapper expansion)
│   ├── Diagnostics/MemoizeDiagnostics.cs     (VEL001–011 diagnostic descriptors)
│   ├── AnalyzerReleases.*.md                 (Roslyn analyzer release tracking)
│   └── Shared/SourceBuilder.cs               (shared helpers)
└── tests/Velvet.SourceGenerators.Tests/
    ├── Velvet.SourceGenerators.Tests.csproj
    ├── SourceBuilderTests.cs
    ├── MemoOverloadGeneratorTests.cs
    ├── MemoizeMethodGeneratorTests.cs
    ├── GeneratorTestHelper.cs
    └── Snapshots/                            (verified golden files)
        ├── Memoized_Arity*/MemoizedWithKey_Arity*    (MemoOverloadGenerator)
        └── Memoize/                          (MemoizeMethodGenerator)
```

The `~` suffix is the Unity Asset DB convention for "ignore this directory". Generator sources are not visible to Unity.

## Using `[Memoize]`

End-user guidance — usage, constraints, diagnostic IDs, and examples — has moved to [Documentation~/memoization.md](../Documentation~/memoization.md).

This README is now scoped to **contributor concerns** (build / test / DLL shipping / CI).

## CI

`.github/workflows/validate-velvet-generators.yml` runs:

1. `dotnet restore` / `dotnet build -c Release` / `dotnet test`
2. `git diff --exit-code Runtime/Plugins/Generators/` to confirm the committed DLL matches the rebuilt output

Always rebuild and commit the DLL after updating the generator sources.
