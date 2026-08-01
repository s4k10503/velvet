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
over-darken). Only negative amounts are rejected, as CSS disallows them. Both shaders are put in front of the
build by the step [player-builds.md](player-builds.md) describes, which needs nothing from you.

Stacked filters compose in the canonical CSS order (blur, brightness, contrast, grayscale,
hue-rotate, invert, saturate, sepia) regardless of class order, matching how browsers apply a
multi-function `filter` value.

Filter utilities work everywhere other utilities do: under variants
(`hover:blur-sm`, `dark:grayscale`), with the important modifier, and inside recipes. A filter change
animates like any other property: `transition-all`, a bare `duration-*`, and the dedicated
`transition-filter` class all tween it — see [Transitions](#transitions) for which of the two
animators runs and why it matters.

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

A filter change animates under any transition utility whose resolved `transition-property` covers
`filter` — `transition-all`, or a bare `duration-*` (whose `transition-property` stays at its
initial whole-property value). No opt-in class is required for that, matching CSS. The
`transition-filter` class exists because **which animator runs** is decided by that same resolved
value, and the two animators do not have the same capabilities:

| Resolved `transition-property` | Set by | Who animates |
|---|---|---|
| covers every property (`all`) | `transition-all`, a bare `duration-*` | UI Toolkit's own transition system |
| contains `filter` | `transition-filter` | Velvet's scheduler-driven tween (`StyleFilterTransitionDriver`) |
| names neither | `transition-colors`, `transition-transform`, `transition-none` | nobody — the change is instant |
| names `background-size` or `-unity-background-scale-mode` | hand-authored USS only | UI Toolkit's, even with no filter named — see the warning below |

The split is forced by the engine: writing an inline `filter` under a whole-property
`transition-property` makes the setter run the write as its own animation, and there is no API to
cancel one, so a Velvet tween writing a frame per tick would only be fighting it. A value that names
`filter` leaves that setter on its plain direct-write path, which is what lets the tween paint its
own frames. `transition-filter` sets exactly that, plus a default duration and curve; `duration-*` /
`ease-*` still override those.

> **`transition-filter` does not combine with another `transition-*` utility.** They all set the
> same `transition-property`, and at equal specificity the one declared later in the bundled sheet
> wins outright rather than merging — `transition-filter transition-colors` resolves to the colors
> list, which silently takes filter changes back to instant. Note also that pinning the property
> means `transition-filter` transitions *only* `filter`: the element's colours, transforms and
> geometry stop transitioning, which is what CSS does too. Put the other properties' transitions on a
> different element.
>
> Hand-authoring a `transition-property` that names `filter` alongside other properties does work —
> the tween accepts any list containing `filter` — but three things have to line up. The element must
> still carry `transition-filter`, because that class is what registers the tween binding; the
> stylesheet has to load after the bundled utilities to win the cascade; and the list must name
> neither `background-size` nor `-unity-background-scale-mode`.
>
> That last one is not a typo. The engine's inline-filter setter decides whether to animate by
> matching the transition list against **`background-size`** — never against `filter` — and it accepts
> any shorthand covering it, which `-unity-background-scale-mode` is. So a list naming `filter` and
> either of those two puts Velvet's tween and a native animation on the same property at once, and a
> list naming either of them *without* `filter` animates your filters with nothing in the declaration
> mentioning filters. Neither case is diagnosed. Everything above is measured on Unity 6000.3; a
> future engine fix would invert it, which is what the Group D tests in
> `FilterTransitionPanelTests` exist to catch.

Under `transition-filter`, Velvet's tween interpolates every native filter type (`blur`, `contrast`,
`grayscale`, `hue-rotate`, `invert`, `sepia`) and the two first-party built-in customs
(`brightness`, `saturate`), so `transition-filter duration-300` tweens `blur-0` → `blur-md` (or
`brightness-100` → `brightness-150`) smoothly. Under a whole-property value the engine interpolates
the inline filter list itself instead, on its own terms — which are not always Velvet's. `contrast`
is the visible case: Velvet fades an added or removed `contrast-*` from CSS's identity of `1`, while
the engine pads it from the `0` its own declaration states, so the same class change ramps from
neutral under `transition-filter` and from fully flat under `transition-all`. Reach for `transition-filter` when you need
the behavior Velvet's tween defines:

- **User custom filters interpolate** when both sides are the *same registered definition* with the
  same number of arguments and matching argument types per slot — `filter-[glow:#f00:2]` →
  `hover:filter-[glow:#00f:6]` cross-fades the color and lerps the amount. Another filter may be
  added or removed alongside one that pairs: `filter-[glow:1]` → `hover:blur-4 filter-[glow:2]`
  fades the blur in while the glow lerps.
- **A filter on only one side** of the change fades in/out from its neutral value, matching CSS's
  implicit list padding. For a user custom that neutral is the one *your definition declares* — each
  `FilterParameterDeclaration.interpolationDefaultValue`, the same value the engine pads its own
  filter-list transitions with — so `filter-[glow:2]` appearing on hover fades up from the glow's
  declared default rather than from zero. Declare those defaults deliberately; a slot left at the
  struct default fades from `0`.
- A user custom **snaps** when it cannot be paired: a different definition on each side (different
  shaders have no correspondence to interpolate along), a differing argument count, a slot that is a
  color on one side and a float on the other, or two or more *distinct* user customs when a filter is
  added or removed (every user custom composes last, so there is no order to place two of them in).
- A repeat of the same channel within one list is an ambiguous pairing and falls back to an instant
  write, like CSS.
- A definition **destroyed mid-tween** drops out of the frames the tween paints instead of throwing;
  the remaining filters keep animating. The value the tween settles on is the one composed when it
  started, so a dead definition is cleared from the element by the next compose rather than at settle.
- One cosmetic edge: clearing an inline filter list leaves the old value readable (an engine bug the
  reconciler already works around when pooling), so the change immediately after a tween that cleared
  its filters can compose one pass at a filter's declared neutral before settling. A neutral that is a
  visual no-op — which is what a neutral should be — makes this invisible.

`duration-0` (or any zero-duration resolution) and an off-panel change both write the target
value instantly, matching CSS's zero-duration behavior.

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
