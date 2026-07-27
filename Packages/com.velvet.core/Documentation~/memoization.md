# Memoization with `[MemoizeMethod]`

This guide covers the `[MemoizeMethod]` attribute — Velvet's partial-method-level memoization driven by the Source Generator. For component-level memoization (`React.memo` equivalent), use `[Component(Memoize = true)]`.

## Overview

Annotate a partial method declaration with `[MemoizeMethod]` and the SG generates a `V.Memoized(...)` wrapper body whose deps are auto-extracted from the method parameters. Write the actual implementation in a sibling method with the `_Impl` suffix.

```csharp
public static partial class HomePage
{
    [MemoizeMethod]
    private static partial VNode BuildHeader(string title, int count);

    private static VNode BuildHeader_Impl(string title, int count)
        => V.Div(/* ... */);

    [Component]
    public static VNode Render()
        => BuildHeader(title: "...", count: 0);
}
```

The generator emits a wrapper that calls `V.Memoized` with the parameters as the deps array, so the result is cached unless any of the parameter values change between renders.

## Constraints

- The partial method declaration must carry an accessibility modifier (C# 9.0 extended partial methods spec)
- The containing class must be declared `partial`
- Arity 0 still generates but warns (VEL001) unless the `_Impl` method is provably pure; arity 9+ is rejected outright (VEL002)
- The return type must derive from `Velvet.VNode` (VEL008)
- Generic methods, `async`, and `ref`/`out` parameters are unsupported (VEL003/004/005)
- The implementation lives in `<MethodName>_Impl` (writing the body directly on the partial declaration emits VEL009)

## Use inside the Runtime asmdef

`[MemoizeMethod]` works in any partial class inside `Velvet.asmdef`. The Generator DLL is placed at `Runtime/Plugins/Generators/Velvet.SourceGenerators.dll` and Unity applies it automatically via the `RoslynAnalyzer` label.

## Diagnostic IDs

| ID | Trigger |
|----|---------|
| VEL001 | arity 0 cannot prove purity |
| VEL002 | arity 9+ |
| VEL003 | generic method |
| VEL004 | async / Task / ValueTask |
| VEL005 | ref / out / in / ref readonly parameter |
| VEL006 | accessibility modifier missing |
| VEL007 | containing class is not partial |
| VEL008 | return type does not derive from VNode |
| VEL009 | partial method already has a body |

For a complete list including the `ReactiveScopeAnalyzer` and `PurityAnalyzer` diagnostics, see `Generators~/src/Velvet.SourceGenerators/AnalyzerReleases.Unshipped.md`.

## See also

- [`Generators~/README.md`](https://github.com/s4k10503/velvet/blob/main/Packages/com.velvet.core/Generators~/README.md) — contributor guide for building, testing, and shipping the generator and code-fix assemblies
- `[Component(Memoize = true)]` — component-level memoization (`React.memo` equivalent)
