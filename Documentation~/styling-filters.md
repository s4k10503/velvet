# Styling notes: Filters & the custom filter registry

UI Toolkit 6.3 renders the USS `filter` property — a list of filter functions applied to an
element's rendered output, the CSS `filter` equivalent. Velvet exposes it two ways:

- **Built-in utilities** (`blur-*`, `contrast-*`, `grayscale-*`, …) mirroring Tailwind's filter
  scale, resolved to inline filter functions.
- **The custom filter registry** (`VelvetFilters`), which exposes user-authored
  `FilterFunctionDefinition` assets (custom filter shaders) to class strings as
  `filter-[name:args]` — the CSS `filter: url(#name)` parity point.

Every filter utility on an element — built-in or custom — composes into the **one** inline
`filter` list, so they merge rather than overwrite: `blur-sm grayscale-[0.5]` produces
`filter: blur(4px) grayscale(0.5)`.

## Built-in filter utilities

| Utility | Values | Notes |
|---|---|---|
| `blur` / `blur-<k>` / `blur-[Npx]` | bare = 8px; `none`/`sm`/`md`/`lg`/`xl`/`2xl`/`3xl` = 0/4/12/16/24/40/64px | px only |
| `contrast-<n>` / `contrast-[N]` | presets 0–200 (× 0.01); bracket ≥ 0 | |
| `grayscale` / `grayscale-0` / `grayscale-[N]` | bare = 100% | N in 0..1 |
| `invert` / `invert-0` / `invert-[N]` | bare = 100% | N in 0..1 |
| `sepia` / `sepia-0` / `sepia-[N]` | bare = 100% | N in 0..1 |
| `hue-rotate-<deg>` / `hue-rotate-[Ndeg]` | presets 0/15/30/60/90/180; the only filter with a negative form (`-hue-rotate-90`) | degrees |
| `brightness-<n>` / `brightness-[N]` | presets 0/50/75/90/95/100/105/110/125/150/200 (× 0.01); bracket ≥ 0 | full CSS range, see below |
| `saturate-<n>` / `saturate-[N]` | presets 0/50/100/150/200 (× 0.01); bracket ≥ 0 | full CSS range, see below |

`brightness` and `saturate` are the only two utilities UI Toolkit has no native filter type
for. Rather than approximate them through a built-in (which clamps to the darken/desaturate
range), Velvet renders each through its own custom-filter shader — `Velvet/FilterBrightness`
and `Velvet/FilterSaturate`, registered internally as `FilterFunctionType.Custom` definitions.
The shaders apply CSS `brightness()`'s uniform multiply and `saturate()`'s lerp-toward-luminance
directly, unclamped, so the **full CSS range** applies: over-brightening (`brightness-150`) and
over-saturation (`saturate-150`) work, and both match the browser exactly (the arithmetic runs on
the encoded pixel before the engine's Linear-colorspace conversion, so a Linear project does not
over-darken). Only negative amounts are rejected, as CSS disallows them.

Stacked filters compose in the canonical CSS order (blur, brightness, contrast, grayscale,
hue-rotate, invert, saturate, sepia) regardless of class order, matching how browsers apply a
multi-function `filter` value.

Filter utilities work everywhere other utilities do: under variants
(`hover:blur-sm`, `dark:grayscale`), with the important modifier, and inside recipes. Because
`filter` is an interpolable USS property, a `transition-all` / `duration-*` element tweens
filter changes like any other property change.

## Custom filters: `VelvetFilters` + `filter-[name:args]`

Unity 6.3 lets you author your own filter as a `FilterFunctionDefinition` — a ScriptableObject
that names the filter, declares its parameters, and lists the post-processing passes (your
shader) it runs. Velvet exposes such a definition to class strings through a registry, keeping
assets out of the render path the same way `VelvetFonts` keeps font assets out of class names:

```csharp
// Startup (before the consuming tree mounts):
VelvetFilters.Register("dissolve", dissolveDefinition);
VelvetFilters.Register("glow", glowDefinition);

// Anywhere in a component:
V.Div(className: "filter-[dissolve:0.4]");
V.Div(className: "filter-[glow:#ff0000:2] hover:filter-[glow:#ff0000:4]");
```

### Token grammar

`filter-[name]` or `filter-[name:arg(:arg)*]`. Arguments fill the definition's **declared
parameters** in order, and each one is parsed by its slot's declared type: a float slot takes
a signed float (`filter-[wave:-0.5]`), a color slot takes Velvet's color grammar (`#rgb` /
`#rrggbb` / `rgb(…)` / a named color). A missing tail is padded from the declaration's
defaults — the same values the USS parser pads with — so a bare `filter-[name]` applies the
declared defaults outright. Supplying more arguments than the declaration, or an argument that
fails its slot's grammar, rejects the whole token. (A filter function carries at most 4
parameters, so a definition declaring more is rejected at registration.)

A token that cannot resolve — an unregistered name (warned once), an extra argument, or an
argument that fails its slot's grammar — is not claimed and stays an inert class, like any
unrecognized utility.

### Composition and layering

- Custom functions compose **after** the built-in utilities, in the order their classes first
  applied: `blur-sm filter-[dissolve:0.4]` → `blur(4px) dissolve(0.4)`.
- Each registered name is its own layer stack, so `filter-[dissolve:0.4] filter-[glitch:0.1]`
  are independent — and a variant over one name (`hover:filter-[dissolve:0.9]`) restores that
  name's base arguments on hover-off without touching the others.
- Repeating a name in one class string replaces its arguments (last wins) instead of stacking
  a duplicate function.
- A name keeps its compose slot for the element's lifetime: changing a filter's arguments
  (which the class diff performs as a clear-then-apply) does not re-slot it behind its
  neighbors.

### Transitions

UI Toolkit interpolates a `filter` change when the from/to lists match function-for-function —
for a custom function that means the **same definition** on both sides, which the registry
guarantees for a same-name argument change. So `transition-all duration-300` tweens
`filter-[dissolve:0]` → `filter-[dissolve:1]` numerically; adding or removing a function
(list shapes differ) snaps, matching CSS.

Drive this through the `transition-*` utilities. Pinning an inline `transition-property` to
`filter` alone makes 6000.3 stop applying further filter changes to the rendered output at all
(engine quirk, observed on 6000.3.11f1) — `transition-all` does not have the problem.

### Contract

- **Register before mount.** Resolution happens when a class is applied; a class resolved
  before its name was registered stays inert until the element's class list changes again.
  Registration is not reactive.
- The built-in family names (`blur`, `brightness`, `contrast`, `grayscale`, `hue-rotate`,
  `invert`, `saturate`, `sepia`) are **reserved** and cannot be registered.
- A name must be free of whitespace, `:`, `[` and `]` (they would break the token grammar).
- Re-registering a name warns and overwrites; `Unregister` removes it. Removing a class (or a
  variant turning off) still clears its layer after an unregister — the clear resolves the
  name syntactically, not through the registry — but an element that keeps the class keeps its
  already-resolved filter, so unregister after the consuming trees unmount.
- A definition destroyed after registration stops rendering: the compose skips dead
  definitions instead of throwing.

### Authoring the definition

The definition asset and its shader contract are Unity's:
[FilterFunctionDefinition](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.FilterFunctionDefinition.html)
(`filterName`, `parameters`, `passes`). Note that as of 6000.3 Unity's custom-filter shader
examples target the Built-in Render Pipeline include (`UnityUIEFilter.cginc`); on URP projects
verify your filter shader against a URP panel before shipping — the filter executes inside UI
Toolkit's own renderer, but the include-level utilities are documented Built-in-first.
