# Memoization with `[MemoizeMethod]`

This guide covers the `[MemoizeMethod]` attribute — Velvet's partial-method-level memoization driven by the Source Generator — and the memoization the build-time compiler transform gives every `[Component]` body ([jump to it](#automatic-memoization-of-a-component-body)). For component-level memoization (`React.memo` equivalent), use `[Component(Memoize = true)]`.

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

The generator emits a wrapper that calls `V.Memoized` with a deps array holding each parameter, each type argument of a generic method and, where `_Impl` is an instance method, the instance the method is called on, so a render reuses the result cached at the method's position only while those values are unchanged — [react-migration.md](react-migration.md#what-a-position-is) states what a position is. A method with none of these to key on — no parameter, not generic, and a static `_Impl` — passes an explicitly empty deps array — see [react-migration.md §1-4](react-migration.md#1-4-what-a-dependency-list-means) for what each spelling of a dependency list means.

## What the declaration may carry

The generated wrapper repeats the declaration's signature, so any number of parameters, type parameters with their constraints, `in` and `params` parameters, an extension method's `this`, and the `new`, `virtual`, `override`, `sealed`, `readonly` and `unsafe` modifiers all carry over, and so does an interface as the containing type. How each parameter is keyed:

- An `in` parameter is copied, and the copy is what the factory reads and what the memo keys on.
- A `params` array is keyed by its length and its elements rather than by the array, which the compiler builds afresh at every call; a null array is told apart from an empty one.
- Any other array is one dependency, compared by instance like any other reference, including when it is the only parameter.
- A generic method's type arguments are dependencies too, so calling it at one position with another type argument rebuilds.
- The receiver of an instance member whose `_Impl` is an instance method is a dependency, compared by instance for a class and by value for a struct, so calling it at one position on another instance rebuilds. It is one more element of the array, which is built per call as any non-empty one is. What a class instance holds is not a dependency: VEL012 reports an `_Impl` reading, while it runs, a field or property of `this` that can change while the instance stays the same — any but a `static`, `const` or `readonly` field and a `static`, init-only or get-only auto-property. A lambda `_Impl` passes as an argument counts as running when it returns a value, as `V.When`'s and `V.List`'s do, and not when it returns nothing, as an event handler does; a local function counts where `_Impl` calls it. Pass the value as a parameter instead.

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

## Automatic memoization of a `[Component]` body

Every `[Component]` is processed at build time unless it sets `Compiler = false`. Hooks still run on every render; only the construction of the `VNode` tree is cached, at two levels:

- **The body as a whole** is keyed on the component's parameters and the values its hooks return. When every key compares equal, the cached tree is returned and the rest of the body does not run.
- **A scope** is a call that builds a `VNode` — `V.Label(...)`, `V.Div(...)`, a call to a helper returning a node — together with the code that builds its arguments. Each scope is keyed on only the values it reads, so when the body as a whole misses, a scope whose own keys did not change returns the node it built last time and the reconciler skips that subtree by instance.

```csharp
[Component]
public static VNode Render()
{
    var (count, setCount) = Hooks.UseState(0);
    var (text, setText) = Hooks.UseState("");
    return V.Div("column",
        V.Label(text: $"Count: {count}"),   // runs again when `count` changes
        V.Label(text: $"Typed: {text}"),    // runs again when `text` changes
        V.Label(text: "static"));           // keyed on nothing: built once
}
```

Typing in the field builds the second label again and nothing else.

### What a scope reads

Keys are compared the way hook dependencies are ([react-migration.md §1-4](react-migration.md#1-4-what-a-dependency-list-means)): by instance for a class, by content for a string, a number or a struct.

- A parameter, a hook value or a local computed outside the scope is one key.
- A class value that the scope reads only through its fields or its automatic properties — a props record's `Name` — is keyed on those members, so a new props record that differs only in `Count` leaves the scope reading `Name` cached. A value handed on whole, or read through any other member, is keyed on the value itself.
- A lambda in a scope captures variables of the enclosing method; the scope is keyed on every variable its closure holds.

### What keeps a scope from being cached

A scope is built on every render when it:

- takes the result of a call whose target the build cannot see — through an interface or a virtual method, or into an assembly the build cannot load;
- takes the result of a hook outside the weaver's allow-lists (`Hooks.Use`, `Hooks.UseMutation`, `Hooks.UseBlocker`, `Hooks.UseFallback`), or of a custom hook that reaches one;
- calls something that reads state it was not handed: a static member with nothing to read from (`DateTime.Now`, `Time.time`), a framework method outside a short list of pure ones (`List<T>.Add`), or a static field that is not `readonly`;
- reads a stable object that a call may have changed — the result of `Hooks.UseRef`, `Hooks.UseMutableRef` or `Hooks.UseService` handed as an argument to anything but a `V.*` factory, a property getter or a pure framework member — so it is rebuilt after the call rather than served from before it;
- calls `V.Portal` or `V.WorldSpace`, or builds a child of one, directly or through a local;
- sits in a loop, calls a hook, writes a variable or object it did not create, or is entered by a branch from outside it.

Only the scope that does one of these is affected: its siblings, and the scopes around it that do not read what it reads, stay cached. A body-level gate is not placed when a call the build cannot see runs ahead of the last hook, when a hook result is used in a shape the weaver cannot key on, when the body reaches a hook outside the allow-lists, or when a return or the last hook sits inside a `try`; the scopes that read none of those are still cached.

A body that breaks the Rules of Hooks — a return before a hook call, a hook that a branch can skip or repeat, or a hook inside a `try`, `catch` or `finally` — is left exactly as written. So is a body with a hook whose value it never reads, such as `var (_, setTick) = Hooks.UseState(0)` kept to force a render: the render stands for something the body reads some other way, and nothing in it says what.

A computation that does not end in a `VNode` — a LINQ query, a string — is not a scope of its own: it runs whenever the scope reading it does, or on a miss of the body as a whole. To keep other work cached across a change, split it out by hand with `Hooks.UseMemo`, `V.Memoized(factory, deps)` or a `[MemoizeMethod]`.

### Reading what was left out

What the transform did and did not cache is written onto each method as a `MemoizationReportAttribute`: whether the body as a whole is keyed (`BodyGate`), how many scopes it has (`Scopes`), and one line per thing left uncached with the reason (`Bailouts`).

```csharp
var report = typeof(HomePage).GetMethod(nameof(HomePage.Render))
    .GetCustomAttribute<MemoizationReportAttribute>();
Debug.Log($"{report.Scopes} scopes, gate: {report.BodyGate}\n{string.Join("\n", report.Bailouts)}");
```

An assembly that carries the `AssemblyMetadata` below also has every component with something left out reported as a build warning naming the method. A body the transform fails on is left as written and reported as a warning whether or not the assembly carries it.

```csharp
[assembly: System.Reflection.AssemblyMetadata(MemoizationReportAttribute.MetadataKey, MemoizationReportAttribute.WarnValue)]
```

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
| VEL012 | `_Impl` reads a field or property of `this` the memo does not key on |

For every diagnostic the package's analyzers report, see `Generators~/src/Velvet.SourceGenerators/AnalyzerReleases.Unshipped.md`.

## See also

- [`Generators~/README.md`](https://github.com/s4k10503/velvet/blob/main/Packages/com.velvet.core/Generators~/README.md) — contributor guide for building, testing, and shipping the generator and code-fix assemblies
- `[Component(Memoize = true)]` — component-level memoization (`React.memo` equivalent)
