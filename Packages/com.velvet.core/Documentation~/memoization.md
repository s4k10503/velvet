# Memoization

This guide covers two things: the reactive scopes the build-time compiler transform caches a component's children in, and the `[MemoizeMethod]` attribute — Velvet's partial-method-level memoization driven by the Source Generator. For component-level memoization (`React.memo` equivalent), use `[Component(Memoize = true)]`.

## Automatic reactive scopes

The compiler transform (`[Component(Compiler = true)]`, the default) caches a component twice. Its gate caches the whole tree on the component's props and hook values, and a render whose inputs all compare equal returns that tree. Below the gate, an expression is cached on the values it reads, so a render that changes one input builds again what reads it and hands the reconciler the same `VNode` instances for the rest, which the reconciler skips. That is React Compiler's reactive scope.

```csharp
[Component]
static VNode Dashboard()
{
    var (count, setCount) = Hooks.UseState(0);
    var (name, setName) = Hooks.UseState("");
    return V.Div("root",
        V.Label(text: $"Clicks: {count}"),   // built again when count changes
        V.Label(text: name),                 // built again when name changes
        V.Label(text: "Static"));            // built once
}
```

A scope is one of:

- a call or constructor returning a `VNode`, wherever it stands: an element of a children list, a single child, a member of a props record, an arm of a conditional, the returned root;
- a lambda that captures variables, cached under the captured variables it reads when it runs;
- a value the body derives and stores once into a local (`var visible = items.Where(...)`), when nothing can change it afterwards: a value type, a `string`, a delegate, or an `IEnumerable<T>` / `IReadOnlyList<T>`-style interface. A `List<T>` or an array is not one, because the body may write to it later.

A scope sits at any depth: one inside a scope that is built again keeps its own cache. One reading nothing is cached once it calls nothing but `V` factories. A scope the render did not reach, because an early `return` came first, keeps what it last built. A scope starting after the component's last hook call is woven; in a body that calls no hook, any scope is.

### What a scope is compared on

Dependencies are compared as [react-migration.md §1-4](react-migration.md#1-4-what-a-dependency-list-means) states for the gate. A scope is compared on:

- the parameters it reads;
- the hook values it reads (`UseState`, `UseReducer`, `UseMemo`, `UseCallback`, `UseContext`, `UseStore`, …), where the body does not store to the local after the last hook call; the setter of a state pair is one object for the life of the component and is never a change;
- the locals it reads of a value type or `string`, by value, and the output of another scope stored once before it;
- the variables a lambda it creates captures and reads, provided nothing stores to them once the scope began.

A local only the scope itself uses is not compared.

A scope compares the references it reads, as React Compiler's does. A list, dictionary or object held in state, context or props and changed in place (`items.Add(x)` followed by a state update, a property written on a context object) counts as unchanged, so the scope reading it keeps what it built before. Replace the value (a new list, a new object) as in React, or opt the component out with `[Component(Compiler = false)]`. An instance property accessor of a type in `System.Diagnostics` or `System.Threading` (`Stopwatch.Elapsed`) is the exception: a scope calling one is not cached. Any other instance property accessor is read like the value it belongs to, so a getter that returns something other than a function of its object's state is not seen to change.

The weaver reads the calls an expression itself makes and does not follow them: the target of a delegate the expression hands on, the override a virtual call reaches, the body of a lambda beyond the variables it captures. As in React, render code must be pure; an expression that reads something no dependency stands for through one of those shows what it read when it last ran.

### What is built on every render the gate misses

A scope is not cached, and is built as it was before scopes, where it:

- calls a member of a `Ref<T>` or `MutableRef<T>`, or a method taking one, reads a `UseService` value, or reads a reference a custom hook returns;
- reads a `static` field, other than a `readonly` one of a value type or `string`, or calls anything but a `V` factory, a constructor, an instance property accessor outside `System.Diagnostics` and `System.Threading`, or a member of `System`, `System.Collections`, `System.Collections.Generic`, `System.Linq`, `System.Text` or `System.Runtime.CompilerServices`. Of those, calls on `Random` and `Environment` are left out, and the static members of `DateTime`, `DateTimeOffset`, `Guid` and `TimeZoneInfo`. A method of your own that builds a child is not cached; make it a component or cache its result with `Hooks.UseMemo`;
- reads a local of another kind, such as a list, a `VNode` or a delegate built in the body outside a scope of its own;
- reads a list, dictionary or object that a call outside every scope is given: the call may write through it, so the scope would hold what it built from the object as it was. A `Grow(items)` statement in the body keeps every scope reading `items` uncached;
- writes a local that code outside it also uses, such as a variable a later child reads, or passes the address of such a local to a call;
- sits in a loop, or in a method with `try`, `catch` or `finally`.

A body whose weave fails the stack-discipline check is woven with its gate alone.

### Hooks and calls ahead of the gate

The gate sits after the component's last hook call, and what stands ahead of it runs on every render.

- A call ahead of it that may run a hook the weaver cannot see (an interface or virtual dispatch outside the BCL and Unity, or a method whose assembly the post-processor cannot read) is bracketed by `Hooks.BeginOpaqueCall` / `Hooks.EndOpaqueCall`. The gate of a render in which a hook ran inside one reports a miss, since that hook may have fed the body a value the gate does not compare; a call that runs none costs nothing else. A custom hook that composes such a call is not bracketed: its value is a dependency.
- `Use` and `UseFallback` are on the allow-lists: `Use` returns a value that changes with its resource, and `UseFallback` returns nothing.
- `UseMutation`, `UseBlocker` and any hook the weaver has not classified return an object changed in place, which no comparison of the reference sees. The component is woven, its gate never hits (it carries a fresh `object` as a dependency), and a scope reading what such a hook returns is not cached, while the scopes that do not read it are.
- A conditional or repeated hook, a hook inside a `try`, a discarded hook result and a result read by a shape the weaver does not recognise still leave the component unwoven.

### Inspecting what was woven

Define `VELVET_REPORT_AUTOMEMO` in an assembly's scripting define symbols to have the post-processor emit one warning per `[Component]` of it: the reason the component is left unwoven, or what was woven into it (the number of scopes, a gate that never hits, the calls bracketed). Without the define it says nothing about a component left unwoven.

### Where it stays short of React Compiler

- A scope is one of the three kinds above. A derived `List<T>` is computed again on every render the gate misses, and a scope reading it is built again; `Hooks.UseMemo` caches such a value.
- A scope stands after the component's last hook call, since the gate does; what stands ahead of it is built on every render.
- A render the gate misses allocates a dependency array for each scope it reaches, whether the scope then hits or misses.
- A child component keeps its own gate, and `[Component(Memoize = true)]` its props bail.

`[Component(Compiler = false)]` opts a component out of the gate and every scope.

## `[MemoizeMethod]`

### Overview

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

### What the declaration may carry

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

### Use inside the Runtime asmdef

`[MemoizeMethod]` works in any partial class inside `Velvet.asmdef`. The Generator DLL is placed at `Runtime/Plugins/Generators/Velvet.SourceGenerators.dll` and Unity applies it automatically via the `RoslynAnalyzer` label.

### Diagnostic IDs

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
