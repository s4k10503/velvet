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

The generator emits a wrapper that calls `V.Memoized` with a deps array holding each parameter and, for a generic method, each type argument, so a render reuses the result cached at the method's position only while those values are unchanged — [react-migration.md](react-migration.md#what-a-position-is) states what a position is. An arity-0 method has no parameters to key on, so its wrapper passes an explicitly empty deps array — see [react-migration.md §1-4](react-migration.md#1-4-what-a-dependency-list-means) for what each spelling of a dependency list means.

## What the declaration may carry

The generated wrapper repeats the declaration's signature, so any number of parameters, type parameters with their constraints, `in` and `params` parameters, an extension method's `this`, and the `new`, `virtual`, `override`, `sealed`, `readonly` and `unsafe` modifiers all carry over, and so does an interface as the containing type. How each parameter is keyed:

- An `in` parameter is copied, and the copy is what the factory reads and what the memo keys on.
- A `params` array is keyed by its length and its elements rather than by the array, which the compiler builds afresh at every call; a null array is told apart from an empty one.
- Any other array is one dependency, compared by instance like any other reference, including when it is the only parameter.
- A generic method's type arguments are dependencies too, so calling it at one position with another type argument rebuilds.

Two rules come from C# itself, which compiles the declaration and the generated implementation as the two halves of one extended partial method:

- The declaration carries an accessibility modifier. Without one it is the older partial-method form, which must return `void` (CS8796); the generator reports VEL006 instead of writing an implementation the compiler would reject.
- Every declaration of the containing type, and of each type enclosing it, is `partial`. C# reports CS0260 where some are not, and the generator reports VEL007 where none is.

The rest follow from what the wrapper is: a `V.Memoized(...)` call that returns a `MemoNode` in place of the method's result.

- The declared return type is `VNode` or `MemoNode`, returned by value (VEL008). To memoize a value of any other type, call `Hooks.UseMemo`.
- The method is not `async` and does not return a `Task`, a ValueTask or a `VelvetTask` (VEL004): the node is placed synchronously, and memoizing the task itself is `Hooks.UseMemo`'s job.
- No parameter is `ref` or `out` (VEL005): the factory that calls `_Impl` runs later, during reconcile, after the wrapper has returned, so no write through the parameter could reach the caller; a lambda cannot capture one either (CS1628).
- No parameter is a ref struct such as `Span<T>`, a pointer, or an array of pointers (VEL010): each parameter is stored in the dependency array and read by the factory lambda. A ref struct can be neither, a pointer cannot be stored, and an array of pointers needs an unsafe context the generated code does not open.
- An instance member of a struct is `readonly`, and the struct is not a `ref struct` (VEL011): a lambda in a struct cannot capture `this`, so the factory calls `_Impl` on a copy, and a write to that copy would be lost; a ref struct cannot be copied anywhere the factory can reach.
- The body lives in `<MethodName>_Impl` (VEL009 when the partial declaration carries one): the declaration with a body is the implementing half, and C# accepts only one.

## Use inside the Runtime asmdef

`[MemoizeMethod]` works in any partial class inside `Velvet.asmdef`. The Generator DLL is placed at `Runtime/Plugins/Generators/Velvet.SourceGenerators.dll` and Unity applies it automatically via the `RoslynAnalyzer` label.

## Diagnostic IDs

| ID | Trigger |
|----|---------|
| VEL004 | async / Task / ValueTask / VelvetTask |
| VEL005 | ref / out parameter |
| VEL006 | accessibility modifier missing |
| VEL007 | containing class is not partial |
| VEL008 | return type is neither VNode nor MemoNode |
| VEL009 | partial method already has a body |
| VEL010 | ref struct or pointer parameter |
| VEL011 | struct instance member that is not readonly, or of a ref struct |

For every diagnostic the package's analyzers report, see `Generators~/src/Velvet.SourceGenerators/AnalyzerReleases.Unshipped.md`.

## See also

- [`Generators~/README.md`](https://github.com/s4k10503/velvet/blob/main/Packages/com.velvet.core/Generators~/README.md) — contributor guide for building, testing, and shipping the generator and code-fix assemblies
- `[Component(Memoize = true)]` — component-level memoization (`React.memo` equivalent)
