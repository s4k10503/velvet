# Velvet `V.*` DSL deep nest mitigation guide

## 1. Problem

Velvet's `V.*` DSL is a compromise for React + JSX under the constraints of the C# language, and when building deeply nested UI the `new VNode[] { ... }` block structure accounts for **15-30%** of the file's line count. Measurements:

| file | total lines | `new VNode[]` / `new[]` occurrences | structural-noise ratio |
|------|-------------|------------------------------|------------------|
| LoginModalComponent.cs | 422 | 12 | ~14% |
| SettingsPageComponent.cs | 416 | 23 | ~28% |
| RoomEditPageComponent.cs | 329 | 11 | ~17% |

The `V.Div(string, params VNode[])` overload approach reduces the character count, but **simplifying the structure itself** requires a further step of component splitting. This guide collects the DX patterns for the component-splitting approach.

## 2. Canonical pattern: extract each section into a `private static VNode RenderXxxSection`

For a component where nesting goes beyond three levels, extract each functionally independent section into a `private static VNode RenderXxxSection(...)` method. The entry `[Component]` static `Render()` then lists only the section calls.

### Reference: `SettingsPageComponent`

```csharp
// SettingsPageComponent.cs
public static class SettingsPageComponent
{
    private static readonly StyleSlotClasses Ss = S.Settings.Apply();

    [Component]
    public static VNode Render()
    {
        var ctx = SettingsPageContext.UseRequired();
        var pageStore = ctx.PageStore;
        // ... hook calls + view state construction ...

        return V.Div(className: StyleClassNames.Class(Layout.Overlay("flex-col"), "text-strong"),
            name: "settings-page-root", children: new VNode[]
            {
                V.Div(className: Layout.Overlay("flex-col bg-transparent overflow-hidden"),
                    name: "settings-content-panel", children: new[]
                    {
                        V.Custom<BlurredBackgroundElement>(...),
                        V.ScrollView(className: "bg-transparent grow p-4", ..., children: new[]
                        {
                            V.Label(className: "text-strong text-xl mb-4", text: "Settings"),
                            RenderSoundSection(state, v => pageStore.UpdateSoundVolume(v)),
                            RenderGraphicsSection(state, v => pageStore.UpdateGraphicsQuality(v)),
                            RenderCameraSection(state, v => pageStore.UpdateCameraSensitivity(v)),
                            RenderAccountSection(state, form, dispatch, accountStore),
                            RenderAppInfoSection(state, () => licenseStore.ShowLicenseModal()),
                        }),
                        RenderFooter(state, onBack: ..., onReset: ..., onSave: ...),
                    }),
            });
    }

    private static VNode RenderSoundSection(SettingsPageViewState state, Action<float> onSoundVolumeChanged)
    {
        return V.Div(className: Ss["section"], name: "sound-settings-section", children: new VNode[]
        {
            V.Label(className: Ss["title"], text: "Sound"),
            V.Div(className: Ss["row"], children: new VNode[]
            {
                V.Label(className: Ss["rowLabel"], text: "Volume"),
                V.Slider(className: "grow mx-3",
                    name: "sound-volume-slider",
                    value: state.SoundVolume,
                    lowValue: 0f, highValue: 1f,
                    onValueChanged: onSoundVolumeChanged,
                    onCreated: SliderStyleApplier.Apply),
                V.Label(className: Ss["rowValue"],
                    name: "sound-volume-label",
                    text: $"{(int)(state.SoundVolume * 100)}%"),
            }),
        });
    }

    // RenderGraphicsSection / RenderCameraSection / RenderAccountSection /
    // RenderAppInfoSection / RenderFooter follow the same shape (private static VNode taking arguments).
}
```

Key point: the entry `Render()` body becomes almost nothing but a list of section calls. Each section method can be reasoned about independently, confining the cognitive load of deep nesting within the section.

## 3. Criteria for extraction (rule of thumb)

| Signal | Response |
|---------|------|
| Nesting of `V.Div` / `V.ScrollView` / `V.Custom<T>` goes beyond three levels | Consider extracting the child section into a `private static VNode RenderXxxSection` |
| The `Render()` body exceeds 100 lines | Consider splitting into sections per functional unit |
| You want to reuse the same section from another page | Instead of `private static`, make it **a standalone file as a separate `[Component]` static method** and embed it from the parent via `V.Component(OtherComponent.Render, key: "...")` |
| A section internally needs hooks (UseState / UseEffect / UseStore, etc.) | **Always make it a separate `[Component]`**. Do not write hooks inside a `private static VNode RenderXxxSection` |

These are guidelines, not rules. As shown in the "Example where extraction is unnecessary" in section 6, for a single widget that has no section to split functionally, there are cases where extraction is unnecessary even when the level count is exceeded.

The last point is especially important: a `private static VNode RenderXxxSection` is **a non-isolated helper that runs on the parent `Render()`'s single ComponentFiber**, so calling a hook inside the section numbers it as part of the parent's hook order. A section that needs hooks must be extracted into an isolated fiber with `[Component]`.

## 4. Typo mitigation: look up typed constants via `StyleSlotClasses`

The `params VNode[]` overload passes the className in positional form, so a string typo cannot be detected as a compile error. To compensate, avoid writing utility class name string literals directly and look them up via **`StyleSlotClasses` (= the return value of `S.<Recipe>.Apply()`)**:

```csharp
// Acquire StyleSlotClasses at the top of each file
using S = Shared.Presentation.Utils.ViewRecipes;

private static readonly StyleSlotClasses Ss = S.Settings.Apply();

// Usage
V.Div(Ss["section"],
    V.Label(Ss["title"], text: "Sound"),
    V.Div(Ss["row"],
        V.Label(Ss["rowLabel"], text: "Volume"),
        V.Slider(...)));
```

The `StyleSlotClasses` indexer fails early at runtime for a key that does not exist. The static type does not catch it, but at the PoC level a typo will always be noticed during **the first render check**. In addition, because the visual tokens of a section are consolidated in `ViewRecipes.cs`, duplicate definitions of a utility class are prevented.

Example registration on the ViewRecipes side (`Shared/Presentation/Utils/ViewRecipes.cs`):

```csharp
public static readonly StyleSlotRecipe Settings = new(new Dictionary<string, string>
{
    ["section"] = "rounded-md mb-4 p-4 ...",
    ["title"]   = "text-lg font-bold mb-2",
    ["row"]     = "flex-row items-center mb-2",
    // ...
});
```

## 5. Combining with the params overload approach

Combined with the `V.Div(string className, params VNode[] children)` overload, the body of a section method becomes even more concise:

```csharp
// before (named-arg + new VNode[])
V.Div(className: Ss["row"], children: new VNode[]
{
    V.Label(className: Ss["rowLabel"], text: "Volume"),
    V.Slider(...),
});

// after (params overload)
V.Div(Ss["row"],
    V.Label(className: Ss["rowLabel"], text: "Volume"),
    V.Slider(...));
```

The builders targeted by the params overload are the container element factories: `V.Div` / `V.Custom<T>` / `V.ScrollView` / `V.Button`. A caller that uses any other prop — for `V.Button` notably `onClick:`, and for all of them `name:` / `key:` / `props:` / `styles:` / `refCallback:` / `whileHoverClass:` / `whileTapClass:` / `whileFocusClass:` — continues to use the long form (named arguments). The `V.Button` shorthand resolves a positional string second argument to the long-form `text` param (there is no implicit `string`→`VNode`), so `V.Button("btn", "Save")` is a text button, while `V.Button("btn", icon, label)` is the children form.

## 6. Example where extraction is unnecessary

A component whose deep nesting stays within three levels does not need section extraction. Reference: `RoomPageComponent` (164 lines) has a view tree in the entry `Render()` that is four levels deep (`slide-pad-touch-zone → instance → background → handle`), but this is localized to the single unit of the SlidePad widget (absolute positioning + four stages of ref forwarding) and has no section to split functionally. Most of the file is taken up by hooks + `UseEffect` bodies (PointerDown / SlidePad input wiring), so it stays readable without section extraction. Extraction is **a means to apply when a problem becomes apparent, not a requirement**; a structured nest in a single widget can be written plainly.

## 7. Experimental: initializer-style builders (`Velvet.Experimental`) — cold UI only

A PoC authoring surface (Issue #56) lets a subtree be written in a JSX/Compose-like nested-brace form using C# object/collection initializers, while keeping full IDE completion and requiring **no IDE plugin and no compiler swap** (it is C# 6/7 syntax, so it compiles on stock Unity's default C# version — unlike C# 12 collection expressions `[...]`, which would force every consumer of the package to update Roslyn):

```csharp
using Velvet.Experimental;

return new VDiv("flex flex-col items-center gap-4 p-4")
{
    new VLabel("text-2xl font-bold") { Text = $"Count: {count}" },
    new VButton("px-4 py-2 rounded bg-primary text-white")
    {
        Text = "Increment",
        OnClick = () => setCount(count + 1),
    },
};
```

Each builder is a thin wrapper that delegates to the existing `V.*` factory via an implicit `VBuilder -> VNode` conversion, so it produces the same `ElementNode` and inherits all parsing / pooling behaviour.

The available builders mirror the corresponding `V.*` factories: containers `VDiv` / `VScrollView` / `VCustom<T>` / `VButton` (children kept) and leaves `VLabel` / `VTextField` / `VSlider` / `VToggle` / `VImage` (children on the collection initializer are ignored). Input builders expose React-style props — `Value`, `OnChange`, `Label`, `Enabled` (plus `LowValue` / `HighValue` on `VSlider`, `IsPasswordField` on `VTextField`); `VImage` exposes `Styles` and the `WhileHover/Tap/FocusClass` gesture classes. Props absent on a builder (a less-common factory argument) are reached by dropping back to the `V.*` factory for that node.

### Use it only for cold (low-frequency) UI

The hot-path default remains the `params` / factory style. **Rationale (allocation):** Velvet is an immutable VDOM — one node object is allocated per element per render regardless of authoring style. The initializer form adds, on top of that, **one builder object per node that the `new VDiv { ... }` syntax makes irreducible** (the accumulator list and the child array are pooled — see §track 2 of Issue #56 — but the builder object itself cannot be pooled because the caller `new`s it). On a frequently re-rendered subtree that extra churn is wasted GC, so:

| Context | Recommended style |
|---------|-------------------|
| Hot path (re-renders often, lists, animated subtrees) | `params` / factory (`V.Div("cls", child, child)`), feeding pooled / `V.List` arrays — lowest allocation |
| Cold path (settings panels, modals, one-shot screens) | initializer style is fine when readability wins |

If allocation is a hard constraint for a given subtree, prefer the factory style and lean on `[Component(Memoize = true)]` so unchanged subtrees bail (and allocate nothing — see [memoization.md](memoization.md)). The builders live under the `Velvet.Experimental` namespace specifically to signal they are not the supported hot-path default.

## 8. Related

- [react-migration.md](react-migration.md) — naming alignment for those with React experience
- [memoization.md](memoization.md) — how to use `[Component(Memoize = true)]`
