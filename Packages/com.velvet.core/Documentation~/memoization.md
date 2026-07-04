# Memoization with `[Memoize]`

This guide covers the `[Memoize]` attribute — Velvet's partial-method-level memoization driven by the Source Generator. For component-level memoization (`React.memo` equivalent), use `[Component(Memoize = true)]`.

## Overview

Annotate a partial method declaration with `[Memoize]` and the SG generates a `V.Memoized(...)` wrapper body whose deps are auto-extracted from the method parameters. Write the actual implementation in a sibling method with the `_Impl` suffix.

```csharp
public static partial class HomePage
{
    [Memoize]
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
- Only arity 1–8 is supported (arity 0 emits VEL003, 9+ emits VEL004)
- The return type must derive from `Velvet.VNode` (VEL010)
- Generic methods, `async`, and `ref`/`out` parameters are unsupported (VEL005/006/007)
- The implementation lives in `<MethodName>_Impl` (writing the body directly on the partial declaration emits VEL011)

## Use inside the Runtime asmdef

`[Memoize]` works in any partial class inside `Velvet.asmdef`. The Generator DLL is placed at `Runtime/Plugins/Generators/Velvet.SourceGenerators.dll` and Unity applies it automatically via the `RoslynAnalyzer` label.

## Diagnostic IDs

| ID | Trigger |
|----|---------|
| VEL003 | arity 0 |
| VEL004 | arity 9+ |
| VEL005 | generic method |
| VEL006 | async / Task / ValueTask |
| VEL007 | ref / out / in / ref readonly parameter |
| VEL008 | accessibility modifier missing |
| VEL009 | containing class is not partial |
| VEL010 | return type does not derive from VNode |
| VEL011 | partial method already has a body |

For a complete list including the `ReactiveScopeAnalyzer` and `PurityAnalyzer` diagnostics, see `Generators~/src/Velvet.SourceGenerators/AnalyzerReleases.Shipped.md`.

## See also

- [`Generators~/README.md`](../Generators~/README.md) — contributor guide for building, testing, and shipping the Source Generator DLL
- `[Component(Memoize = true)]` — component-level memoization (`React.memo` equivalent)
