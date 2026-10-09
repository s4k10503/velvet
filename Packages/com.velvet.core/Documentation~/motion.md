# Motion & AnimatePresence: the Framer Motion parity guide

`V.Motion` and `V.AnimatePresence` model [Framer Motion](https://www.framer.com/motion/)'s
declarative animation API on Unity UI Toolkit. This guide covers the variant-driven feature set:
labels and inheritance, mount enters, exits and `PopLayout`, orchestration
(`staggerChildren` / `delayChildren` / `when`), per-property transition overrides, spring physics,
exact cubic-bezier easing and repeating a play — plus the transition semantics that tie them together: **the node's
own config as the default a pose overrides** (and the instant opt-out; see the last section).

The `StyleTransition` presets (`Fade`, `SlideUp`, `ScaleIn`, `FadeSlideUp`, …) and
`whileHoverClass` / `whileTapClass` gestures are covered in the README; everything below uses
**variants**. One limit applies to those gesture channels and to the classes a play applies while it
runs: they carry ordinary USS utilities only, not the few Velvet realises itself — see
[styling-variants.md](styling-variants.md#payloads-velvet-realises-itself).

## Variants & labels

A variant map names poses; `initial` / `animate` / `exit` select them by label, exactly like Framer's
`variants` / `initial` / `animate` / `exit`. Each entry is a `MotionVariant` — a utility-class string
plus an optional transition of its own — and a bare `string` converts implicitly, so a pose that
takes the Motion's `transition:` is written as the class string alone:

```csharp
static readonly Dictionary<string, MotionVariant> s_fade = new()
{
    ["hidden"]  = "opacity-0 translate-y-8",
    ["visible"] = "opacity-100",
};

V.Motion(key: "card", className: "w-24 h-24 rounded-xl bg-sky-500",
    variants: s_fade, initial: "hidden", animate: "visible",
    transition: new StyleTransitionConfig { DurationSec = 0.4f });
```

A pose is a *class delta*: classes present in the resting variant and absent from another are
removed/added on swap, and anything not mentioned falls back to the element's base `className`.

**Label inheritance (Framer's variant propagation):** a Motion naming none of `animate`, `initial` and
`exit` follows the nearest ancestor Motion's active label and takes that ancestor's `initial` label with
it. A Motion naming any of the three takes neither, as Framer treats a Motion naming any variant label
as controlling its own; one naming only an `initial` rests at that pose. A coordinator can therefore
flip one label and drive a whole subtree of inheriting children — that is also what orchestration
staggers (below).

## Enter on mount (`initial` → `animate`)

Any Motion that declares its own `animate` plus a resolvable `initial` label plays a mount
enter — **standalone, no `AnimatePresence` required** (Framer parity: `initial` / `animate` work
on any `motion.*` element). The element mounts showing `variants[initial]`, then transitions to
`variants[animate]` and rests there.

- An inherited label drives the enter too, a presence's keyed child included: an inheriting Motion mounts
  at its own pose for the inherited `initial` label and enters to its pose for the inherited `animate` one. One
  mounting with an entering parent enters in the slot the parent's transition orchestrates for it (see
  *Orchestration*); one mounting under a parent already mounted enters on its own.
- An `initial` pose applying no class starts the enter from the Motion's own classes, the way a
  Framer `initial` naming no value starts each value from the one it already has.
- Inside `AnimatePresence`, the presence can withhold enters: `V.AnimatePresence(initial: false, …)`
  suppresses them for the children its first render mounts, like Framer's `<AnimatePresence initial={false}>`
  (see *Exits* below).

## Exits (`AnimatePresence`)

`V.AnimatePresence` keeps a removed keyed child mounted as a *ghost* until its exits finish,
then removes it and fires `onExitComplete` (once, cancelled exits excluded):

```csharp
V.Div(name: "row", className: "flex flex-row gap-x-2", children: new VNode[]
{
    V.AnimatePresence(key: "presence", onExitComplete: OnRowSettled, children: items),
});
```

- **DOM-less:** the presence emits no wrapper element — children expand directly into the
  parent, so put `flex` / `gap-*` / wrapping on the parent.
- **Framer's splice semantics:** while a ghost exits, surviving siblings keep their positions;
  the ghost holds its slot until the exit completes (the default `Sync` mode).
- Re-adding a key mid-exit cancels the exit and returns the element to its resting variant —
  including inline geometry the pose had overwritten.
- A key whose Motion is created again while the key stays, as an `elementType` change does, enters
  again, unless `initial: false` still withholds that key's enter (below).
- **What an exit animates:** under the default Tween driver, any USS-transitionable property the
  pose swap changed animates — `transition-property: all` picks up the whole class delta, not a
  fixed channel set. Spring and cubic-bezier exits drive the channels they can resolve a number
  for out of the class strings; *Driven channels* below is the single list of what that covers and
  what it deliberately leaves out. A `skew-*` exit never animates under any driver, because skew
  is a silhouette paint rather than a transform.
- **A removed child's exits play however deep their Motion sits** — behind a component,
  `V.Memoized` or `V.Suspense`, or inside another element or Motion: a Motion plays the `exit` it
  declares, or one it inherits from the Motion above it the way an `animate` label is inherited, and
  the child stays mounted until the last of those exits completes. A coordinator's exit pose staggers
  its inheriting children's exits with its `StaggerChildrenSec`, `DelayChildrenSec` and `When`,
  numbered as *Orchestration* below numbers a label change. The children of an inner
  `V.AnimatePresence` are that presence's, as in Framer without `propagate`; see *Propagating a removal
  to an inner presence* below for `propagate: true`. `initial: false`
  suppresses the mount enter of every Motion that mounts under a child the presence's first render
  created, a `V.Portal`'s and a later render's included, for as long as that child stays, as Framer's
  `PresenceChild` keeps the `initial` it was created with; an inner presence's children answer to that
  presence.
- The presence's own enter, and the classic exit a Motion with no `exit` label plays from its
  `transition:`, belong to the child's *anchor*: the child itself when it is a Motion, else the first
  Motion found through the `V.Provider`s, `V.Fragment`s and z-managed elements it wraps. A keyed
  `V.Fragment` child holds every element it places until its Motions' exits finish.
- A classless `exit` pose is still a variant exit: the removal takes the resting pose's classes
  off, on the timing that pose resolves; see *Transition semantics* below. An `exit` label naming no
  pose plays the classic exit instead.
- An `exit` on a Motion outside every `V.AnimatePresence` plays nothing, as in Framer.
- A `mode:` naming no `AnimatePresenceMode` member is refused at construction: `V.AnimatePresence`
  throws `ArgumentOutOfRangeException`, naming the parameter.

### Propagating a removal to an inner presence

`propagate: true` on an inner `V.AnimatePresence` follows Framer's `propagate`. While the keyed child of the
enclosing presence that holds it is leaving, the inner presence treats every one of its own children as not
present and exits them through its own exit path, and the enclosing child stays mounted until those exits have
completed.

- Each presence owns its own exits. The inner presence's `onExitComplete` runs once, when its own exits have
  finished, whatever else the enclosing child still waits on. When those were the last thing the enclosing child
  waited for, the enclosing presence's `onExitComplete` runs first and the inner presence's second, as in Framer,
  where the inner presence's `safeToRemove()` completes the enclosing child before its own callback runs.
- A child the inner presence was already exiting is waited for and not exited again. One it removes in the same
  render keeps the one exit.
- The nearest enclosing presence decides: a presence between the two that does not propagate stops it, and the
  enclosing child leaves without waiting. Without `propagate` on the inner presence its children are left as they
  are when the enclosing child is removed.
- If the enclosing key returns before the exits end, the inner presence's present children come back, as they do
  for a cancelled exit. A child the inner presence removed itself stays exiting.
- An inner presence that stops propagating, or is no longer rendered, while the enclosing child waits on it stops
  holding that child.
- A key added to the inner presence while the enclosing child is leaving mounts already leaving, as Framer's
  `PresenceChild` mounts it with `isPresent=false`: its Motions start at their `initial` pose, with no enter, and
  play their `exit` from there. The key counts in the enclosing child's wait while the inner presence's exits
  are running, and the inner presence's `onExitComplete` runs again when it finishes, once the others have.
- The inner presence is found wherever it sits under the enclosing child: written inline under elements, rendered
  by a component, at the top of the child, or inside a `V.Portal`, and whether it mounted with the child or in a
  later render of its own component. When it mounts that way, the nearest enclosing child is the one that
  counts: the child of the nearest presence its host element or its component sits inside.

```csharp
V.AnimatePresence(key: "pages", children: new VNode[]
{
    V.Div(key: "settings", children: new VNode[]
    {
        V.AnimatePresence(key: "tabs", propagate: true, onExitComplete: OnTabsGone, children: tabs),
    }),
}),
```

### `PopLayout` mode

```csharp
V.AnimatePresence(mode: AnimatePresenceMode.PopLayout, children: items);
```

Framer's `mode="popLayout"`: the exiting child is pinned **out of flow** (absolute, at its last
laid-out rect, margins accounted for) so surviving siblings reflow *immediately* while the ghost
plays its exit in place. The `gap-*` / `grid-cols-*` / `divide-*` emulations skip pinned ghosts
in their index math, so spacing recomputes as if the child were already gone. A keyed `V.Fragment` is not
pinned and exits in flow, as under Framer's `popLayout`. Note the ghost
keeps its original paint order: a survivor that reflows into the ghost's rect draws over it.

A negative `StyleTransitionConfig.DelaySec` starts the animation partway through its run. For
example, `DurationSec = 1f` and `DelaySec = -0.5f` start a linear tween halfway to its target.
Spring and bezier plays advance before their first scheduled tick and complete immediately if that
offset reaches their end. A property's `StylePropertyTransition.DelaySec` overrides the tween's
delay for that property. Additional orchestration delay combines with the configured delay: a
negative total starts partway through the animation; a positive total postpones its start.

## Orchestration (`staggerChildren` / `delayChildren` / `when`)

A parent Motion whose transition declares orchestration knobs staggers its **inheriting**
children (children naming none of `animate`, `initial` and `exit`) whenever its propagated label changes or its
mount enter plays — no `AnimatePresence` boundary required. "Its transition" is the one resolved for the pose it is
swapping into (see *Transition semantics* below):

```csharp
V.Motion(key: "list", animate: label, className: "flex flex-col gap-2",
    transition: new StyleTransitionConfig
    {
        DurationSec = 0.2f,          // the parent's own swap
        StaggerChildrenSec = 0.25f,  // +0.25s per child, in tree order
        DelayChildrenSec = 0.1f,     // base offset for every child
        When = TransitionWhen.BeforeChildren,
    },
    children: cards);
```

- `When = Together` (default): children start at `DelayChildrenSec` + their stagger slot.
- `When = BeforeChildren`: children additionally wait out the parent's own delay and play: its
  `DurationSec` for a tween or a bezier, the time its slowest channel takes to rest for a spring
  (measured as *Repeating a play* gives), and every repeat of either (see *Repeating a play*).
- `When = AfterChildren` is not orchestratable under label propagation; it warns once and falls
  back to `Together`.
- Each Motion numbers its own inheriting children, as Framer numbers each variant parent's: a child
  with variants of its own starts its children with it and numbers them from zero, while a Motion
  with neither variants nor a label of its own passes its parent's numbering through to the
  children below it. Every inheriting child with variants takes a slot, whether or not the label
  changes its pose, and one naming a label of its own takes none. A `V.VirtualList` row, which mounts
  after the pass that rendered the list, takes no slot.
- `V.AnimatePresence(staggerSec: …, delayChildrenSec: …, staggerDirection: …)` provides the
  presence-side equivalent for enter/exit plays — the same stagger/delay knobs, scoped to a
  list of children entering or exiting under one presence boundary.
- A **staggered animated list** is that composition and nothing more: a keyed `V.List` inside
  `V.AnimatePresence(staggerSec: …)`, with each item rendered as a `V.Motion` carrying its own
  `variants` / `initial` / `animate` / `exit`. Velvet ships no single `AnimatedList` factory
  wrapping the three, matching Framer, whose canonical animated-list recipe is likewise
  `AnimatePresence` + a mapped list + `motion.li`. Because `V.AnimatePresence` renders no element
  of its own, the `flex` / `flex-col` / `gap-*` classes belong on the surrounding parent, not on
  the presence.

Orchestration delays each child's **swap itself**: once a child's slot elapses, the swap fires
and tweens (or springs) on the config resolved for the child's own destination pose.

## Per-property transition overrides

`PropertyOverrides` gives individual USS properties their own timing inside one variant
transition, like Framer's per-value `transition` maps:

```csharp
new StyleTransitionConfig
{
    DurationSec = 0.3f,   // the default for every animated property
    PropertyOverrides = new[]
    {
        new StylePropertyTransition("opacity",   durationSec: 0.15f),
        new StylePropertyTransition("translate", durationSec: 0.5f, easing: EasingMode.EaseOutBounce),
    },
}
```

Property names are UI Toolkit `transition-property` spellings (`"opacity"`, `"translate"`,
`"scale"`, `"rotate"`, `"background-color"`, …). Null fields fall back to the enclosing config, and a
property no override names animates on the enclosing config's timing, as a value missing from Framer's
per-value map takes the default transition.
Completion is sized off the **slowest** of those, so a long override finishes instead
of being snapped when the top-level duration elapses. A `Tween` reads them on every variant swap:
mount enters, label changes and exits.

## Springs

```csharp
new StyleTransitionConfig
{
    Type = TransitionType.Spring,
    Stiffness = 170f,   // default 100
    Damping   = 10f,    // default 10 — this pair overshoots visibly
    Mass      = 1f,     // default 1
}
```

- Springs drive the channels of a variant delta (see *Driven channels* below) with a
  velocity-preserving integrator: **interrupting a spring retargets from the current value *and
  velocity***, Framer's signature interruptible feel. An exit whose delta resolves no channel at
  all completes immediately.
- `DurationSec` is ignored for springs — settling time comes from the physics. A play samples each
  channel's spring by the time since it started and ends when its slowest channel has rested,
  measured as Framer Motion measures a spring's duration (see *Repeating a play* for the
  thresholds), so a frame that arrives late moves the spring as far as the time it covers. A
  spring that has not rested by 20 s never ends, as Framer's does not. An interruption hands the
  channel to the integrator, which rests it by the same thresholds over the travel left.
- Springs drive mount enters, presence exits, and runtime `animate` label swaps alike — flipping
  a label mid-spring retargets from the current value and velocity.
- Non-finite / non-positive `Stiffness` / `Damping` / `Mass` log a warning and complete
  immediately rather than freezing the element mid-pose.

## Driven channels (Spring and Bezier)

Both non-CSS drivers write inline styles every tick. A play reads its targets from the swapped-in
classes and the element's classes left in place. An explicit swapped-out value takes priority as the
start. Otherwise, on a mounted element, the start is the current inline value, including a running
Spring or Bezier frame, or the resolved value when no inline value is set. If a native transition is
running on that same element and longhand, the play samples its displayed value before cancellation
or the class swap. A transition on another property does not change this slot's starting value.
If the native activity lookup is missing or cannot be invoked, the play uses the ordinary inline or
resolved start described above.

- **The transform quartet.** `opacity` and the `translate` / `scale` / `rotate` trio the transform
  utilities write. Naming one on one side of the delta is enough. When only the from-side names
  it and no resting class supplies a target, the target is its identity value (opacity 1, scale 1,
  translate 0, rotate 0deg). When only the to-side names it, the play samples the current value;
  on an off-panel element it starts at identity.
- **Colors.** `background-color`, `color` and `border-color`, from palette utilities
  (`bg-red-500`), the alpha modifier (`bg-red-500/50`) and the bracket forms (`bg-[#1e293b]`).
  Interpolation is straight RGBA, the same path UI Toolkit's own color transition takes, so
  switching a config between `Tween` and `Spring` never changes which colors the element passes
  through. `border-color` fans out to all four sides.
- **`border-radius`,** from the `rounded-*` scale and the bracket forms, including the per-side and
  per-corner spellings.
- **Spacing-scale lengths:** width / height / min / max / `size-*`, padding, margin, inset (`top-*`
  … `inset-y-*`) and `basis-*`, from the `--space-*` scale (`p-4`, `w-64`, `-mt-2`), the sizing
  and position fractions (`w-1/2`, `left-1/2`), and the bracket forms. These dirty Yoga layout on
  every tick — the whole subtree relayouts each frame for the length of the play — so reach for a
  transform channel first and animate a length only where the reflow is the point.
- **Border widths,** from the `border-*` width utilities (`border`, `border-2`, `border-t-4`) and
  the bracket forms. These read a **separate literal scale mirroring the stylesheet's own
  declarations**, not the spacing scale — `border-2` is 2px, not `--space-2`. Same per-tick layout
  cost as the lengths above.
- **Font size and letter spacing, bracket forms only:** `text-[20px]` and `tracking-[2px]` are
  driven. The named presets are not — `text-lg` resolves through a `--text-*` token and
  `tracking-wide` through a fixed named step, neither of which has a C# mirror to read a magnitude
  from — so a `text-sm` → `text-2xl` delta lands instantly while `text-[14px]` → `text-[24px]`
  animates. Reach for the bracket form when you want the size to move.
- **The to-side must name a color or length.** A swapped-out value in the target's unit supplies
  the start; otherwise a mounted element supplies its current inline or displayed value. Width and
  height, including their min/max forms, can convert between pixels and percentages through the
  parent's content box. Other mixed-unit pairs, or an off-panel target with no matching-unit start,
  land instantly. A color or length with no compatible effective to-side target lands instantly;
  classes left in place can provide that target when the swap removes a from-side class.
- **Classes on one side that write the same slot resolve as the cascade resolves them.** A shorthand
  is read slot by slot — `p-8` as four edges, `size-*` as a width and a height, `rounded-*` as four
  corners, `border-*` as four widths — and each slot animates toward whichever class holds it at
  rest. Important declarations take priority for each longhand they write, including across a shorthand
  and its longhand. Among inline-resolved tokens in the same importance band (bracket forms, `-mt-2`,
  `translate-x-4`), the later write holds the slot, including across a shorthand and its longhand.
  Thus `pt-[2px] p-[8px]` holds the top at 8px; reversing those tokens holds it at 2px,
  including when an update reorders existing tokens.
  Classes left in place participate in the cascade of the slots named by the delta. A resting inline
  value keeps its slot over a plain stylesheet utility, so `pt-[2px]` beside a `p-0` → `p-8` swap keeps its top edge. Running
  custom frames and native targets supply current values instead of ordinary resting holders;
  inactive inline border edges still block a uniform stylesheet border-color channel. Resting
  important holders keep their priority, while an important inline holder the swap replaces can change.
  Inline values hold their slots over surviving stylesheet utilities; between two stylesheet utilities
  the one the stylesheet declares later holds it, wherever the two sit in the class string. So `p-8 pt-2` animates the top edge toward `pt-2` and the
  other three toward `p-8`, and `opacity-50 opacity-20` animates toward `opacity-50`. A slot held by a
  class no number is read from (`rounded-tl-full` beside `rounded-3xl`, `scale-x-[.5]` beside
  `scale-[1.4]`) lands with the swap, and the classes it outranks do not drive it. Per-axis scale
  composition remains undriven beside an important uniform inline scale declaration.
- **Not driven,** each because the class alone yields no number to interpolate or because another
  subsystem owns the slot: semantic theme tokens (`bg-primary`, `text-current`) resolve through
  `--color-*` with no C# mirror; the preset font-size (`text-lg`) and letter-spacing
  (`tracking-wide`) names likewise, per the bullet above; keyword lengths (`w-auto`, `w-full`) are
  modes, not magnitudes; `rounded-full` is a saturating radius sentinel; `shadow-*`, `skew-*` and
  gradients are baked silhouette paints; `filter-*` transitions by its own path
  ([styling-filters.md](styling-filters.md#transitions)); `z-*` is a physical reparent; `aspect-[…]` is claimed by neither motion
  parser, so a ratio change snaps.
- A target-only uniform scale or border color lands instantly when the sampled x/y scale values
  or border edge colors differ.
- **Percentage-based translate** (`translate-x-1/2`, `translate-x-full`) **and per-axis `scale-x-` /
  `scale-y-` are not channels either,** for all that the quartet above names `translate` and `scale`.
  Both families resolve as ordinary utilities; they just apply as plain classes, so the swap lands
  them instantly.
- **A play suspends the element's own USS transitions when they cover what it drives.** A driver
  writes the exact value the curve or the physics calls for on that frame, so a transition utility
  naming that same property restarts a native transition on every one of those writes and leaves
  the painted value trailing for the whole play, easing in at the end instead of landing. Whether
  that applies is decided from the element's **class list**, resolved the way the cascade resolves
  it: `transition-property` holds one value, so the utilities that set it do not combine — the
  **last-declared** one present on the element wins outright and the rest contribute nothing. The
  bundled order is `transition-transform`, `transition-filter`, `transition-all`,
  `transition-none`, `transition-opacity`, `transition-colors`, `transition-colors-scale`,
  `transition-colors-scale-opacity`, then the scheduler-applied `anim-*` presets. So
  `transition-all transition-colors` transitions the colours only, and
  `transition-all transition-none` transitions nothing, whichever order the class strings appear
  in. `transition-filter` names `filter`, which no driver writes, so it never triggers a
  suspension. With none of those utilities present, a `duration-*` class — or the inline
  `duration-[…]` form — leaves `transition-property` at UI Toolkit's default of `all`, so those
  elements transition *everything* natively and a play on them always suspends; with no duration
  either, the element transitions nothing and nothing suspends. A play suspends only where its own
  channels intersect the resolved set — a `transition-colors` element running a fade/slide keeps
  its hover fade, while the same play on a `transition-transform` or `transition-all` element
  suspends. One blind spot: the check runs once at play start, so a variant turning on
  `transition-all` mid-play is not picked up until the next play. A play starting while a variant
  tween holds the inline `transition-property` takes what it drives out of that list rather than
  writing over it. At play start, spring and bezier length channels remove only their own longhands
  from a held inline list on each style owner; a width-only play keeps that list's height entry,
  including on a clip wrapper.
  When it does suspend, the suspension is **element-wide**: the element's *other* transitions
  land instantly too, across the play's `DelaySec` and stagger slot as well as its motion — the
  element is already parked at its from-pose over that window. This is **narrower than Framer
  Motion**, which takes over only the values it animates and leaves the element's other CSS
  transitions running. Two overlapping plays each hold
  their own claim, so the first to finish cannot un-suspend the second, and the suspension lifts as
  soon as the last one settles or is cancelled. Reach for `Tween` when you want the class's own
  transition to do the work instead.

## Cubic-bezier easing

`TransitionType.Bezier` is Spring's other non-CSS sibling: instead of `EasingMode`'s five keyword
curves, it samples an exact numeric CSS `cubic-bezier(x1,y1,x2,y2)` curve every tick — the same
algorithm every browser's `cubic-bezier()` runs — via `BezierX1` / `BezierY1` / `BezierX2` /
`BezierY2`. Unlike a spring it keeps a fixed `DurationSec`, exactly like a plain tween; only the
shape of the easing differs. It shares Spring's channel scope (*Driven channels* above) and its
one-curve-drives-both-directions contract —
there is no separate exit curve, and `PropertyOverrides` is not read. Defaults to Tailwind's own
default curve, `cubic-bezier(0.4, 0, 0.2, 1)`, the exact curve the bundled USS only approximates
with the `ease-in-out` keyword. `BezierX1` / `BezierX2` must stay in `[0,1]`, since a CSS timing
function is a function of time and so must be monotone; a value outside that range is invalid and
falls back to the default curve with a one-shot console warning instead of being silently clamped.
`BezierY1` / `BezierY2` are left unclamped, so an overshoot/anticipate curve genuinely passes its
target mid-tween.

## Repeating a play (`Repeat` / `RepeatType` / `RepeatDelaySec`)

Framer Motion's `repeat`, `repeatType` and `repeatDelay`, on `StyleTransitionConfig`, played by
`TransitionType.Bezier` and `TransitionType.Spring`:

```csharp
// A down-arrow that bobs for as long as it is on screen.
var bob = new StyleTransitionConfig
{
    Type = TransitionType.Bezier, DurationSec = 0.6f,
    Repeat = float.PositiveInfinity, RepeatType = TransitionRepeatType.Reverse,
};
V.Motion(variants: arrow, initial: "up", animate: "down", transition: bob);
```

- `Repeat` counts the passes after the first, as Framer's `repeat` does: `Repeat = 2` plays three.
  `float.PositiveInfinity` repeats until a later play, an exit or an unmount replaces the play. A
  negative, NaN or fractional value throws `ArgumentOutOfRangeException`; Framer accepts a fraction.
- `RepeatType = Loop` (the default) starts every pass at the from-pose. `Reverse` plays every second
  pass backwards in time: a bezier's easing comes back reversed, so an ease-out pass returns as an
  ease-in one, as CSS's `animation-direction: alternate` does, and a spring retraces its own path,
  overshoot included. `Mirror` plays every second pass from the to-pose back to the from-pose: a bezier
  on the same easing, a spring released from the to-pose.
- `RepeatDelaySec` waits that many seconds after each pass but the last before the next one starts.
  A bezier holds the value its pass ended on; a spring keeps being sampled past its pass, as Framer's
  is, so it shows whatever small motion its spring still has. A negative or non-finite value throws
  `ArgumentOutOfRangeException`.
- A spring's pass lasts as long as its spring takes to rest, measured as Framer measures it (a play
  that does not repeat ends there too): sampled
  every 50 ms, resting within 0.005 of its target and moving at no more than 0.01 per second for a
  travel under 5, or within 0.5 and at 2 per second for a longer one, and a spring that has not rested
  by 20 s has no pass to repeat and plays its first pass on. Framer animates each value on its own, so
  each channel repeats on its own pass: an opacity travelling 1 and a color, which Framer springs over
  a travel of 100, fall out of step. The play ends when its slowest channel does.
- A play of an odd `Repeat` under `Reverse` or `Mirror` ends on its from-pose, as Framer's does, and
  holds it there although the element's classes are the to-pose's, until a later play, an exit or a
  teardown cancels it; a pose that lands at once takes over only the properties it names. Every other
  finished play hands its values back to the to-pose's classes, as a play that does not repeat does.
- The repeat covers enters, label changes and exits alike. An exit repeating without end never
  completes, so its `AnimatePresence` keeps the element.
- A cancelled exit's reversal plays once, whatever the exit repeated.
- `When = BeforeChildren` waits for every pass and every wait between them. A parent repeating without
  end never finishes, so its children's plays never start, as Framer's never do: they hold the pose
  they were at.
- A `Hooks.UseAnimationSequence` `To` step deriving its hold counts a repeat below 20. Framer's
  sequence ignores a segment's repeat of 20 or more, an endless one included, and so does a `To`
  step: it hands out its transition without the repeat, holds for one pass and logs a warning, once
  per transition.

A `Tween` does not play a repeat yet: a play on one with `Repeat` above zero plays once, and the first
such play in a mounted tree logs a warning. A `layoutId` move plays once whatever its transition
repeats.

## Shared-element layout animation (`layoutId`)

```csharp
V.Motion(layoutId: "card-3", className: expanded ? "absolute left-[0px] top-[0px] w-[600px] h-[400px]"
                                                  : "absolute left-[40px] top-[120px] w-[120px] h-[80px]");
```

- Framer's `layoutId` parity. When a Motion carrying this same string patches at a resolved
  layout box (position and/or size) different from the box the SAME id stood at, it
  tweens from the old box to the new one — FLIP: the old box is captured, layout settles at the
  new one, an inverse inline transform is applied immediately, then its transition carries it back to zero —
  instead of jump-cutting. A move between two parents compares the boxes in panel space, so parents
  placed apart tween across the distance between them; within one parent, the rect relative to it is
  compared, so a Motion nested in a moving one tweens only its own move inside it. A layoutId Motion
  inside one that grows or shrinks keeps its own size and its offset from its parent's drawn corner on
  every frame of the outer tween, whether or not it moved itself — Framer's scale correction. One rotated
  by its own class inside an outer one stretching by different factors on its two axes is not kept
  exactly: no scale and translate undo a stretch at an angle to the element's axes. A move that
  lands while a tween is still running starts from where the element is drawn, not from its last layout.
  A box in a rotated or sheared frame (a rotated element inside a non-uniformly scaled one) starts
  unrotated over the same centre, at the drawn lengths of its sides.
- The element's own `translate-*` and `scale-*` compose with the tween — the tween's translate adds to
  the element's own, its scale multiplies it — and when the tween ends each slot it wrote holds what it
  held before the tween, or whatever something else wrote there while the tween ran, which the tween
  composes with from its next frame on. A Motion that leaves its panel without being unmounted has
  its tween ended on its panel's next frame.
- Works across a same-key type flip or a move to a different parent, not just an in-place resize:
  the id, not the physical element, is what's tracked. An element that leaves the tree hands its box on
  within one batch — the updates one scheduler drain commits together, such as the ordinary updates
  queued for a frame or the ones a discrete event flushes, however many components they re-render — and a
  Motion that mounts under its id in a later batch appears in place.
- Several Motions may hold one `layoutId` at once. The one that took the id last — by mounting under it
  or by changing to it — leads, and the others are hidden by an inline `visibility: hidden`; a render
  that moves one of them does not make it the lead. While a lead moves from a box it took from another
  holder, each other holder on its panel is drawn over the lead's box. Where more than one holds the id
  and no ancestor is crossfading already, the lead also crossfades with them, as Framer's default does:
  it fades in on circOut over the first half of its move while they fade out linearly between halfway
  and 95% of it. The fade is written as the element's inline opacity over the opacity its classes,
  variants and drivers give it. While it is written, an element whose transitions cover opacity has them
  suspended as a play's are (see [Driven channels](#driven-channels-spring-and-bezier)), element-wide
  unless a variant tween holds the list, and Velvet carries a change of that opacity itself — a class
  change, an `opacity-[x]` class, a variant swap such as an `AnimatePresence` exit — from where the
  opacity stands, with the duration, delay and easing the element declares for it, running one that
  outlasts the move on to its end. The others fade from
  the opacity the holder the lead took its box from was drawn at, if that holder was itself moving from
  another's box, or else from its own opacity as it is now, and take no pointer while they are drawn. A
  lead alone under its id instead mixes its opacity from that holder's to its own over the move, written
  the same way. A move of
  the lead's own that interrupts the crossfade holds the opacities it had reached until that move lands.
  The others are hidden again once the lead lands. When the lead leaves the tree, the holder of those
  left that took the id last leads in its place. When a holder inside a `V.AnimatePresence` child starts
  its exit, the latest holder that took the id before it and is not exiting takes the lead, as Framer's
  relegate hands it on, and the child is removed once both its exit has played and that lead has landed;
  a holder whose key comes back mid-exit takes the lead again. A holder that takes the lead from another, in
  any of these ways or by taking the id, tweens from the box of the lead before it whether or not that box
  differs from its own. A Motion
  whose `layoutId` becomes null stops holding the id and is shown.
- Independent of `Variants`/`Animate`: the tween runs from the ACTUAL rect delta captured off
  `element.layout`, not a class-defined from/to pair, so it fires whether or not the same patch
  also changed variants. It takes the Motion's own `transition:` rather than an active pose's — a
  rect delta is not a swap into a pose — or that transition's `Layout` in its place when set
  (Framer's `transition.layout`). Its `Type` decides the curve as for a variant swap: a spring by
  `Stiffness` / `Damping` / `Mass`, a tween by `DurationSec` / `Easing` on the curve UI Toolkit eases a
  USS transition by for that `EasingMode`, a bezier by its control points, each after `DelaySec`, and a
  zero duration lands the move at once. A Motion whose caller names no `transition`, `duration`,
  `easing` or `delay` moves on Framer's default layout transition, a 0.45 s tween eased by
  `cubic-bezier(0.4, 0, 0.1, 1)`. A transition the scheduler rejects lands the move at once, its warning
  logged where the move settles rather than on every render.
- **Each axis scales by its own factor.** A box whose width and height change by different factors
  starts stretched over the old box, as Framer's does, and a layoutId Motion inside it is corrected for
  the stretch as for any change of size. The scale holds the element's transform origin still (its
  centre unless an `origin-*` class or style moves it), and the translate places the element so that it
  starts over the old box.
- Position is captured synchronously before the patch (mirroring `PopLayout`'s own "read
  `.layout` before the mutation that invalidates it" pattern); the new rect is captured on the
  element's own next `GeometryChangedEvent`, since a reparented/freshly-created element's
  `.layout` stays stale until the following layout pass.

## Looping utilities (`animate-*`)

Seven class-driven loops, each infinite, each driven from a panel-root tick. `animate-pulse`, `animate-spin`,
`animate-ping` and `animate-bounce` take Tailwind's durations, keyframes and timing functions: each keyframe
interval is eased by the timing function its keyframe names, as a CSS animation applies it.

| class | what moves | default loop |
|---|---|---|
| `animate-gradient` | pans a baked gradient back and forth along its axis | 3s |
| `animate-shimmer` | sweeps the gradient one way across the box | 1.5s |
| `animate-hue` | rotates the hue-rotate filter angle a full turn | 4s |
| `animate-pulse` | oscillates opacity from the element's own to half and back, each half on `cubic-bezier(0.4, 0, 0.6, 1)` | 2s |
| `animate-spin` | rotates a full turn on from the element's own rotation, linearly | 1s |
| `animate-ping` | scales to twice the element's own scale while fading to nothing by three quarters of the loop, on `cubic-bezier(0, 0, 0.2, 1)`, then holds | 1s |
| `animate-bounce` | lifts the element a quarter of its height and drops it back, the lift easing in on `cubic-bezier(0.8, 0, 1, 1)` and out on `cubic-bezier(0, 0, 0.2, 1)` | 1s |

`animate-none` cancels, and the last *recognised* `animate-*` in the class list wins — an unclaimed
name leaves the one before it standing. A bracketed time overrides the loop: `animate-spin-[2500ms]`,
`animate-hue-[5s]`. The two gradient modes are inert without a gradient
([styling-gradients.md](styling-gradients.md)) to pan.

Tailwind's keyframes leave some frames unnamed, and a CSS animation fills those from the element's own value.
The loops do the same, so `opacity-75 animate-pulse` runs between 0.75 and 0.5, `rotate-45 animate-spin` turns on
from 45 degrees, `scale-50 animate-ping` starts at half size and grows to full size, and
`translate-y-[10px] animate-bounce` bounces about that offset. The element's own value is what its classes give
the slot, a named class or an arbitrary one, or a value written into the slot by anything other than the loop or a
Motion play driving that slot. Class values are re-read whenever the element's class list changes, which is how
`hover:` and `dark:` variants reach them, and when something else that decides which rules match the element
changes: a pointer, focus or press event on it or beneath it, a theme switch, a change to its `enabled` prop or an
ancestor's, or a class change on an ancestor made through a render. A class added to an ancestor imperatively, and
an enabled state set with `SetEnabled` outside the prop, are not seen until the next of those. A bounce is measured in the element's own pixels, so it moves nothing until the element has
a laid-out height. The keyframes' `transform` applies beneath the `scale` and `rotate` properties, so a bounce on a
scaled or turned element is scaled and turned with it. `animate-spin` on an element with an uneven `scale` does
not carry it: CSS turns the content beneath the scale and leaves the squash axes where they are,
while a single `rotate` and `scale` turn the squash axes with the element. An even scale is exact.

A layoutId move on an element with a running `animate-bounce` or `animate-ping` composes with it as CSS composes a
layout animation with a keyframe one: the move writes the element's translate or scale, and the bounce's lift or
the ping's growth is added to its frame, so the element keeps bouncing or pinging while it moves.

Each mode owns its style slot while it runs, as a CSS animation outranks an element's ordinary
declarations: the gradient pair owns background position, size and repeat, `animate-hue` owns the
filter, `animate-pulse` owns opacity, `animate-spin` owns rotate, `animate-ping` owns opacity and scale, and
`animate-bounce` owns translate. A static utility writing that
slot supplies the element's own value rather than showing, and a `transition-filter` tween and a `Spring` or
`Bezier` Motion channel on a rotate, scale or translate slot driving it are shadowed — the mode's frame is written
over each of their writes and the channel's value is not taken for the element's own. A `Spring` or `Bezier`
Motion `opacity` channel is the exception: it shows over `animate-pulse` for as long as its play drives
it, and the pulse takes the slot back when the play lets go, as Framer Motion runs opacity on the
browser's own animation engine, whose animations outrank a CSS animation. Detaching restores the slot
and the reconciler re-asserts whatever class was under it.

A native transition covering a mode's slot would animate each of the mode's writes, so every mode
suspends one for the length of its run, the way Motion's own drivers do. The pan modes also set
background size and repeat once at attach, before the suspension, so a transition covering either
still takes that one write. Note a transition needs no `transition-*` utility — a bare `duration-*`
leaves UI Toolkit's initial `all` standing, which covers every slot.

The suspension is element-wide, so while a suspended mode runs, the element's *other*
transitions land instantly too. It is taken only when the element's own utility CLASSES name the slot
the mode writes — `animate-pulse transition-colors` keeps its colour fade — and handed back as soon
as a re-render leaves nothing transitioning that slot. Reading the classes means anything that never
reaches the class list is invisible to it — the bracket duration `duration-[400ms]` lands as an
inline value, and so does a `V.Motion` variant swap's own transition, which belongs to the swap. A
swap driving the same slot as the mode is shadowed too. While such a swap is running the element's
inline `transition-property` is the swap's: the suspension is neither taken nor handed back for the
swap's length, and the swap's own completion puts back whichever of the two the element still needs.

## Timelines (`Hooks.UseAnimationSequence`)

Framer Motion's `useAnimate` parity target: `UseAnimationSequence` owns the clock (it is itself built
on `UseFrame`) and walks an ordered `AnimationSequenceStep[]`, so a multi-stage animation ("float, then
emit, then fly to target one at a time, then fire an arrival event") never needs to be hand-rolled with
`UseEffect` + a timer + `UseState`.

A step is exactly one of:

- **`AnimationSequenceStep.To(label, transition?, holdSec?)`** -- activates `label` on the sequence's
  coordinator Motion. Its effect commits the moment the walker *arrives* at the step (not once its hold
  elapses) -- "holds on this step" means the label is already active and the cursor is waiting before
  moving to the next one. `transition` reuses the most recent non-null transition earlier in the
  sequence when omitted (falling back to `StyleTransition.Fade` if none has been set yet); `holdSec`
  defaults to that transition's `DurationSec + DelaySec` for a tween or a bezier, lengthened by a
  `Bezier` or `Spring` repeat (see *Repeating a play*). A `Spring`-typed step holds for its
  `DelaySec` plus the duration Framer Motion's sequence gives the same spring (clamped to zero when
  the negative delay consumes the hold): a travel of 100,
  sampled every 50ms until it is within 0.5 of its target and moving at no more than 2 per second, and
  at most 20 seconds. A label does not tell the sequence how far anything moves, and 100 is the travel
  Framer takes when it cannot read the distance.
- **`AnimationSequenceStep.Wait(seconds)`** -- holds the current label for `seconds` with no effect of
  its own.
- **`AnimationSequenceStep.Call(callback)`** -- fires `callback` synchronously on arrival, then advances
  immediately (never holds the cursor).
- **`AnimationSequenceStep.Await(taskFactory)`** -- calls `taskFactory` on arrival and holds the cursor
  until the `VelvetTask` it returns settles: the `await` a `useAnimate` caller writes between two
  `animate()` calls, for a wait no clock produces -- a server response, a tap, a dialogue advance. A task
  that settles while the sequence is paused is read once it resumes, so no step after the await commits
  in between. A task that has already settled when the factory returns is crossed in the same frame, as a
  `Call` step is; otherwise the step after it commits on the first frame after the settle, and its hold
  counts that frame's time and none from before it, the wait's included. A task that faults, or cancels on its
  own, throws out of that frame as a throwing `Call` callback does, reaching the nearest error boundary,
  and the cursor moves on from the next frame. The factory receives a `CancellationToken`, which a
  restart, a `deps` change or unmount cancels when it leaves the step with the task still pending; a
  restart or `deps` change cancels it only after reseeding the sequence, so a token callback that throws
  does so out of the restart with the sequence already reseeded. A task the sequence has left
  advances nothing, and a fault it ends in is logged, as `Forget()` logs one. Under the Editor's StrictMode
  mount double-invoke an `Await` at step 0 calls its factory twice, the first call's token cancelled when
  its task is still pending.

**"One at a time" needs no separate multi-target API.** Descendant Motions naming no label of their own inherit
the coordinator's label exactly as they already do for any hand-toggled label change (see "Label
inheritance" above); a `To` step's own `transition` declaring `StaggerChildrenSec` fans that swap out
across those descendants in document order, the same mechanism `V.Motion`'s orchestration knobs already
provide.

`autoplay` (default `true`) starts the sequence on mount; pass `false` and call `controls.Play()` (e.g.
from an `onClick`) to start it on demand. `loop: true` wraps the cursor back to step 0 once the last
step's hold elapses instead of latching `AnimationSequenceState.IsComplete`. `deps` is required and
decides when the sequence restarts from step 0, read as
[§1-4 of the React migration guide](react-migration.md#1-4-what-a-dependency-list-means) describes: null
restarts it on every render, so a sequence that plays once per mount passes `Array.Empty<object>()`, as
`useEffect(fn, [])` would. `controls.Restart()` returns to step 0 and re-commits its effect (including
firing a `Call` step 0's callback again) without implicitly resuming a paused sequence.

`controls` drive the sequence's own timeline -- its step cursor and its clock -- and not a Motion play a
step's label has already started: `controls.Pause()` freezes the cursor, and the transition the current
step handed out runs on to its end. The handle also carries `controls.TimeSec`, the Web Animations API's
`currentTime` and Framer Motion's `time`, read-only: seconds into the timeline, counting each hold at its
authored length. As `currentTime` does, it keeps growing across a loop's passes rather than starting from 0
on each; a completed sequence reads its full length, and a reseed reads 0. Under `iterations` it counts a
`repeatDelaySec` gap as it passes, as Framer Motion's `time` counts `repeatDelay`, so the timeline of `n` passes
of length `L` ends at `n * L + (n - 1) * repeatDelaySec`, with no gap after the last pass. It is read live from
the handle, where `state` is a per-render snapshot.

A cancel, a playback rate (`playbackRate`, Framer Motion's `speed`), seek (a settable `time`) and reverse
(`reverse()`, a negative `playbackRate`) are not offered. Each acts on the animation already running, and the
sequence only hands a label's Motion its transition: it holds no handle on the play that starts, so none of
them could reach it, as `Pause` cannot. A timeline of labels also cannot sample the interpolated motion
between two of them, and running it backwards across a `Call` step has no settled answer to whether the
callback fires again. To reverse a transition a label started, flip that Motion's `animate` label back: see
"Springs" for what an interrupted spring keeps.

Not attempted: an arbitrary-selector scope ref (`useAnimate`'s `[scopeRef, animate]`) reaching elements
outside the declarative Motion/variant tree, and overlapping/parallel tracks (Framer's `"<"` / `"+0.2"`
relative-offset DSL) -- steps are a strict FIFO queue; two independently-timed tracks need two separate
`UseAnimationSequence` coordinators.

**A fixed number of passes** is the overload taking `iterations` in place of `loop` -- the Web Animations
API's `iterations` and CSS's `animation-iteration-count`, so the count includes the first pass and Framer
Motion's `repeat: 2` is `iterations: 3`. Each pass after the first starts again at step 0, re-committing
its effect as `loop` does, and `IsComplete` latches once the last pass's last hold elapses, leaving the
cursor on the last step. `iterations: 0` plays no pass: no step commits, no `Call` fires, and the sequence
reads complete from its mount render on. A negative count throws `ArgumentOutOfRangeException`. A restart
plays every pass again.

A count changed by a re-render applies to the sequence as it plays, as the Web Animations API's
`updateTiming` does: a count no higher than the passes already finished completes the sequence at the next
frame with the end state a normal completion holds, as `animation-fill-mode: forwards` shows the end keyframe: the
last step current and its label and transition adopted, with no skipped `Call` callback run. A count of zero commits
nothing: a mount at zero commits no step, and a count lowered to zero leaves the cursor and label as they are. A count above the finished passes resumes a completed sequence at the
next pass's step 0, after the `repeatDelaySec` gap that follows any pass already played.

`repeatDelaySec` is Framer Motion's `repeatDelay`: seconds the cursor waits on the last step between one
pass and the next, never after the last, so it does not delay completion. It throws
`ArgumentOutOfRangeException` when negative or not finite. Under `loop`, a trailing `Wait` step is the same
gap, since no completion waits behind it.

A finished sequence keeps its last step current, as `animation-fill-mode: forwards` would, while `iterations: 0`
commits nothing, as the default `animation-fill-mode: none` would. Where this differs from the Web Animations API
and CSS: the count is a whole number, where both accept a fraction such as `2.5`.

An alternate direction for the sequence as a whole (CSS's `animation-direction: alternate`, Framer
Motion's `repeatType: "reverse"` on a sequence) is not offered: playing a `Call` step backwards has no
settled answer to whether its callback fires again, and a `To` step played backwards would need the label
before it rather than its own. A single label swap that goes there and back is a `To` step whose
`Bezier` or `Spring` transition repeats (see *Repeating a play*).

## Transition semantics: a node default a pose overrides

Every variant update on a `V.Motion` rides a `StyleTransitionConfig` — mount enters (`initial` → `animate`),
presence enters and exits, and runtime `animate` label changes, whether the label is the Motion's
own or inherited from an ancestor (orchestrated stagger children included). Tweens write the
config's timing as an inline transition for the swap and release it on completion; spring configs
integrate physically instead.

**Which config a swap takes is decided by the pose it is animating INTO.** A `MotionVariant` naming
its own `transition` supplies it; a pose that names none takes the Motion's `transition:`, which is
itself the `Fade` preset when the call site left it out. That is the direction each of the three
plays reads: a mount enter takes `variants[animate]`, a runtime label change takes the new label's
pose, and an `AnimatePresence` exit takes `variants[exit]` — the pose swapped INTO, not the one left
behind. Framer expresses the same thing as a `transition` key inside a variant object and inside
`exit`.

A destination pose applying no class still supplies its own timing, at a mount enter, a label change
and a removal alike, and an enter whose `variants[initial]` applies no class starts from the Motion's
own classes.

So slow in against fast out is one declaration:

```csharp
static readonly Dictionary<string, MotionVariant> s_menu = new()
{
    ["closed"] = new MotionVariant("opacity-0 scale-95",
        new StyleTransitionConfig { DurationSec = 0.05f }),   // …and out in 0.05s
    ["open"]   = new MotionVariant("opacity-100 scale-100",
        new StyleTransitionConfig { DurationSec = 0.5f }),    // in over 0.5s…
};

V.AnimatePresence(children: new VNode[]
{
    V.Motion(key: "menu", className: "w-40 bg-slate-800",
        variants: s_menu, initial: "closed", animate: "open", exit: "closed"),
});
```

Three consequences worth knowing:

- **Every Motion animates by default.** `V.Motion` without an explicit `transition` falls back
  to the `Fade` preset's timing, so even a bare Motion tweens its label swaps. Pass
  `transition: StyleTransitionConfig.None` for an instant, non-animated swap — and because the
  node's config is only a default, `transition: StyleTransitionConfig.None` beside a timed exit pose
  means exactly that pose animates and the rest are instant. Whether a removal is held as an
  exiting ghost at all is decided by the same resolved config, so such an exit is not dropped for
  the node's `None`. A label change into a `None` pose, or one whose bezier transition has zero
  duration, while an earlier swap is still moving the Motion lands the properties that pose names
  within two frames, and leaves the rest moving. One exception: a value the pose repeats from a moving
  tween's target in another kind of spelling — a stylesheet class for an arbitrary value, such as
  `opacity-100` against `opacity-[1]` — finishes that tween.
- **A pose's transition carries the child-orchestration knobs too.** `StaggerChildrenSec`,
  `DelayChildrenSec` and `When` are read off whichever config drives the swap, so a coordinator's
  pose can orchestrate its inheriting descendants — and a `When = BeforeChildren` wait is measured
  from that pose's own delay and play (see *Orchestration*).
- **`transition-*` utilities are optional for variant swaps.** They still govern property
  changes outside the variant system (a `hover:` state flipping, an arbitrary class toggle); a
  variant swap's inline transition simply takes precedence while it plays and is released
  afterwards.
