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
| `blur` / `blur-<k>` / `blur-[N]` | bare = 8px; `none`/`sm`/`md`/`lg`/`xl`/`2xl`/`3xl` = 0/4/12/16/24/40/64px | bracket: `px`, `rem` (at 16px), an absolute unit (`in`, `cm`, `mm`, `pt`, `pc`, `Q`) or a bare number (px); no `%`, as CSS `blur()` takes none. A math function that comes to pixels is read ([styling-arbitrary-lengths.md](styling-arbitrary-lengths.md)). Other CSS lengths (`em`, `vw`, …) are not recognized |
| `contrast-<n>` / `contrast-[N]` | presets 0–200 (× 0.01); bracket ≥ 0, or a percentage | |
| `grayscale` / `grayscale-0` / `grayscale-[N]` | bare = 100% | N ≥ 0, or a percentage; above 1 is clamped to 1, as CSS clamps it |
| `invert` / `invert-0` / `invert-[N]` | bare = 100% | N ≥ 0, or a percentage; above 1 is clamped to 1, as CSS clamps it |
| `sepia` / `sepia-0` / `sepia-[N]` | bare = 100% | N ≥ 0, or a percentage; above 1 is clamped to 1, as CSS clamps it |
| `hue-rotate-<deg>` / `hue-rotate-[Ndeg]` | presets 0/15/30/60/90/180 (degrees); the only filter with a negative form (`-hue-rotate-90`) | bracket: a CSS angle in `deg`, `rad`, `grad` or `turn`; a bare number only as `0`, as CSS takes it |
| `brightness-<n>` / `brightness-[N]` | presets 0/50/75/90/95/100/105/110/125/150/200 (× 0.01); bracket ≥ 0, or a percentage | full CSS range, see below |
| `saturate-<n>` / `saturate-[N]` | presets 0/50/100/150/200 (× 0.01); bracket ≥ 0, or a percentage | full CSS range, see below |

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

Stacked filters compose in a fixed order (blur, brightness, contrast, grayscale, hue-rotate, invert,
saturate, sepia) regardless of class order, as Tailwind composes its filter utilities into one `filter`
value. A browser applies a `filter` list in the order it is written, and the list Tailwind writes is in this
order.

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
a signed float (`filter-[wave:-0.5]`), a color slot takes Velvet's color grammar
([styling-colors.md](styling-colors.md)). A missing tail is padded from the declaration's
defaults — the same values the USS parser pads with — so a bare `filter-[name]` applies the
declared defaults outright. Supplying more arguments than the declaration, or an argument that
fails its slot's grammar, rejects the whole token. (UI Toolkit's `FilterFunction` holds at most 4
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

A filter change transitions, by the interpolation rule below, wherever the resolved `transition-property` runs
an entry for `filter` — `transition-filter`, `transition-all`, a bare `duration-*` (whose `transition-property`
stays at its initial whole-property value), or a hand-authored list naming `filter`. No opt-in class is required, matching
CSS. An entry counts only where a transition runs for it, as UI Toolkit reads the lists: its own duration
(floored at `0`) plus its own delay, each list wrapping, has to be positive. The last such entry naming `filter`,
or `all`, gives the change its duration, delay and curve, and the old value holds through the delay. Where no
entry runs for `filter` (`duration-0` with no delay, `transition-colors`, say), and on an off-panel change, the
target value is written instantly, matching CSS's zero-duration behavior.

Two animators run these changes:

| The entry that runs for `filter` | Set by | Who animates |
|---|---|---|
| has the duration, delay and curve of the entry that runs for `background-size` or `-unity-background-scale-mode` | `transition-all`, a bare `duration-*` | UI Toolkit's own transition system |
| any other | `transition-filter`, a hand-authored list | Velvet's scheduler-driven tween (`StyleFilterTransitionDriver`) |

UI Toolkit's inline-filter setter animates a filter list write by the entry for `background-size`, whatever the
list says about `filter`. Where that entry's timing is the one `filter`'s entry gives (an `all` covering both, say),
Velvet leaves the engine's animation in place, which keeps UI Toolkit's shortening of a reversed transition.
Anywhere else Velvet's tween runs the change, and a filter write the setter would animate — a tween frame or an
instant write — is made with transitions suspended, so a list naming `background-size` never animates a filter
utility's change on its behalf. The tween eases by the same curve a USS transition takes for each `ease-*` value.

While `animate-hue` drives an element's filter, the motion shows: a filter utility's change under it starts no
transition and is not painted, and a filter transition already running keeps its clock unseen. When the motion
ends, the filter its layers compose, variant layers included, is written at once, or the transition still running
shows again until it ends. This is CSS's order: a transition applies only while no animation runs on the property,
and an animation's start or end starts no transition.

> **`transition-filter` does not combine with another `transition-*` utility.** They all set the
> same `transition-property`, and at equal specificity the one declared later in the bundled sheet
> wins outright rather than merging — `transition-filter transition-colors` resolves to the colors
> list, which silently takes filter changes back to instant. Note also that pinning the property
> means `transition-filter` transitions *only* `filter`: the element's colours, transforms and
> geometry stop transitioning, which is what CSS does too. Put the other properties' transitions on a
> different element, or hand-author a `transition-property` naming `filter` alongside them, in a stylesheet
> that loads after the bundled utilities so it wins the cascade.

Both animators interpolate the filter list by the CSS rule: functions pair by position, and a filter added or
removed at the **end** of the list fades in from, or out to, its neutral value — `contrast` from and to `1` under
either animator (Group E of `FilterTransitionPanelTests`). Any other change is discrete and applies at once, as in
CSS: a filter added *before* an existing one (`grayscale` → `blur grayscale`), or a position whose function
changes. The native filter types the utilities compose (`blur`, `contrast`, `grayscale`, `hue-rotate`, `invert`,
`sepia`) and the two first-party built-in customs (`brightness`, `saturate`) interpolate, so
`transition-filter duration-300` tweens `blur-0` → `blur-md` (or `brightness-100` → `brightness-150`) smoothly.
Under Velvet's tween:

- **User custom filters interpolate** when both sides hold the *same registered definition* at a position with
  the same number of arguments and matching argument types per slot — `filter-[glow:#f00:2]` →
  `hover:filter-[glow:#00f:6]` cross-fades the color and lerps the amount. CSS applies a `url()` filter change at
  once; this is a Velvet addition.
- A user custom **added or removed at the end** fades from or to the neutral *your definition declares* — each
  `FilterParameterDeclaration.interpolationDefaultValue`, the same value the engine pads its own filter-list
  transitions with — so `filter-[glow:2]` appearing on hover fades up from the glow's declared default rather
  than from zero. Declare those defaults deliberately; a slot left at the struct default fades from `0`.
- A user custom **snaps** when its position cannot be paired: a different definition on each side, a differing
  argument count, or a slot that is a color on one side and a float on the other. Every user custom composes
  after the built-ins, so adding a built-in filter to an element carrying a custom one is a change before its end,
  and snaps too.
- A definition **destroyed mid-tween** drops out of the frames the tween paints instead of throwing;
  the remaining filters keep animating. The value the tween settles on is the one composed when it
  started, so a dead definition is cleared from the element by the next compose rather than at settle.

### Contract

- **Register before mount.** Resolution happens when a class is applied, and registration is not
  reactive: `Register` re-resolves no element and raises no event. A class applied before its name
  was registered does not resolve at that point.
- The built-in family names (`blur`, `brightness`, `contrast`, `grayscale`, `hue-rotate`,
  `invert`, `saturate`, `sepia`) are **reserved** and cannot be registered.
- A name must be free of whitespace, `:`, `[` and `]` (they would break the token grammar).
- Re-registering a name warns and overwrites; `Unregister` removes it. Removing a class (or a
  variant turning off) still clears its layer after an unregister — the clear resolves the
  name syntactically, not through the registry — but an element that keeps the class keeps its
  already-resolved filter, so unregister after the consuming trees unmount.
- A definition destroyed after registration stops rendering: the compose skips dead
  definitions instead of throwing.

### What a late registration does today

Characterization, not contract. This is what the current implementation does; a release that made a
registry change reach mounted trees would change it, so register before mount rather than building
on any of it.

A class applied before its name was registered stays on the USS class list instead of resolving to an
inline value, and paints nothing. It resolves the next time some pass re-applies that element's
inline values from the classes it still carries — a motion on the element settling, being cancelled
or detaching does that, and so does a class change that removes another filter-family token.

### Authoring the definition

The definition asset and its shader contract are Unity's:
[FilterFunctionDefinition](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/UIElements.FilterFunctionDefinition.html)
(`filterName`, `parameters`, `passes`). Velvet's own `Velvet/FilterBrightness` and
`Velvet/FilterSaturate` shaders are written against `UnityUIEFilter.cginc`, and
`BuiltInFilterShaderPlaybackTests` renders them on this project's URP pipeline.
