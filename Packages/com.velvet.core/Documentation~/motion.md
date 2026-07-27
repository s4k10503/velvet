# Motion & AnimatePresence: the Framer Motion parity guide

`V.Motion` and `V.AnimatePresence` model [Framer Motion](https://www.framer.com/motion/)'s
declarative animation API on Unity UI Toolkit. This guide covers the variant-driven feature set:
labels and inheritance, mount enters, exits and `PopLayout`, orchestration
(`staggerChildren` / `delayChildren` / `when`), per-property transition overrides, spring physics,
and exact cubic-bezier easing — plus the transition semantics that tie them together: **one
config, every update** (and the instant opt-out; see the last section).

The `StyleTransition` presets (`Fade`, `SlideUp`, `ScaleIn`, `FadeSlideUp`, …) and
`whileHoverClass` / `whileTapClass` gestures are covered in the README; everything below uses
**variants**.

## Variants & labels

A variant map names poses as utility-class strings; `initial` / `animate` / `exit` select them by
label, exactly like Framer's `variants` / `initial` / `animate` / `exit`:

```csharp
static readonly Dictionary<string, string> s_fade = new()
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

**Label inheritance (Framer's variant propagation):** a Motion with no `animate` of its own
follows the nearest ancestor Motion's active label. A coordinator can therefore flip one label
and drive a whole subtree of inheriting children — that is also what orchestration staggers
(below).

## Enter on mount (`initial` → `animate`)

Any Motion that declares its own `animate` plus a resolvable `initial` label plays a mount
enter — **standalone, no `AnimatePresence` required** (Framer parity: `initial` / `animate` work
on any `motion.*` element). The element mounts showing `variants[initial]`, then transitions to
`variants[animate]` and rests there.

- An *inherited* label does not drive a standalone enter: the Motion needs its own `animate`
  (a warning explains this at mount time otherwise).
- Inside `AnimatePresence`, first-mount enters are controlled by the presence instead:
  `V.AnimatePresence(initial: false, …)` suppresses them on the initial mount, like Framer's
  `<AnimatePresence initial={false}>`.

## Exits (`AnimatePresence`)

`V.AnimatePresence` keeps a removed keyed child mounted as a *ghost* until its `exit` variant
finishes, then removes it and fires `onExitComplete` (once, cancelled exits excluded):

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
- **What an exit animates:** under the default Tween driver, any USS-transitionable property the
  pose swap changed animates — `transition-property: all` picks up the whole class delta, not a
  fixed channel set. Spring and cubic-bezier exits drive the channels they can resolve a number
  for out of the class strings; *Driven channels* below is the single list of what that covers and
  what it deliberately leaves out. A `skew-*` exit never animates under any driver, because skew
  is a silhouette paint rather than a transform.

### `PopLayout` mode

```csharp
V.AnimatePresence(mode: AnimatePresenceMode.PopLayout, children: items);
```

Framer's `mode="popLayout"`: the exiting child is pinned **out of flow** (absolute, at its last
laid-out rect, margins accounted for) so surviving siblings reflow *immediately* while the ghost
plays its exit in place. The `gap-*` / `grid-cols-*` / `divide-*` emulations skip pinned ghosts
in their index math, so spacing recomputes as if the child were already gone. Note the ghost
keeps its original paint order: a survivor that reflows into the ghost's rect draws over it.

## Orchestration (`staggerChildren` / `delayChildren` / `when`)

A parent Motion whose transition declares orchestration knobs staggers its **inheriting**
children (children with no `animate` of their own) whenever its propagated label changes — no
`AnimatePresence` boundary required:

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
- `When = BeforeChildren`: children additionally wait out the parent's own
  `DelaySec + DurationSec` span.
- `When = AfterChildren` is not orchestratable under label propagation; it warns once and falls
  back to `Together`.
- The stagger counter runs in **tree order across the orchestrator's whole subtree** (a
  documented deviation from Framer, which numbers each parent's children independently).
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
and tweens (or springs) on that child's own `StyleTransitionConfig`.

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
`"scale"`, `"rotate"`, `"background-color"`, …). Null fields fall back to the enclosing config.
Completion is sized off the **slowest** overridden property, so a long override finishes instead
of being snapped when the top-level duration elapses. Overrides apply on the scheduler-driven
paths (enters / exits); they are not read by plain label-swap patches.

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
- `DurationSec` is ignored for springs — settling time comes from the physics.
- Springs drive mount enters, presence exits, and runtime `animate` label swaps alike — flipping
  a label mid-spring retargets from the current value and velocity.
- Non-finite / non-positive `Stiffness` / `Damping` / `Mass` log a warning and complete
  immediately rather than freezing the element mid-pose.

## Driven channels (Spring and Bezier)

Both non-CSS drivers write inline styles every tick rather than handing the change to UI Toolkit's
own transition system, so what they can animate is exactly what they can read a number for **out
of the class strings themselves** — there is no resolved style to sample, since the class swap and
the plan are built in one synchronous call, off-panel, before any style resolution.

- **The transform quartet.** `opacity` and the `translate` / `scale` / `rotate` trio. UI Toolkit
  has no animatable `transform` shorthand, so movement is expressed with the three separate
  transform utilities. Each of these has an identity value (opacity 1, scale 1, translate 0,
  rotate 0deg), so naming it on **one** side of the delta is enough — the silent side falls back
  to identity, matching the usual "declare only what changes" authoring style.
- **Colors.** `background-color`, `color` and `border-color`, from palette utilities
  (`bg-red-500`), the alpha modifier (`bg-red-500/50`) and the bracket forms (`bg-[#1e293b]`).
  Interpolation is straight RGBA, the same path UI Toolkit's own color transition takes, so
  switching a config between `Tween` and `Spring` never changes which colors the element passes
  through. `border-color` fans out to all four sides.
- **`border-radius`,** from the `rounded-*` scale and the bracket forms, including the per-side and
  per-corner spellings.
- **Spacing-scale lengths:** width / height / min / max / `size-*`, padding, margin, inset (`top-*`
  … `inset-y-*`) and `basis-*`, from the `--space-*` scale (`p-4`, `w-64`, `-mt-2`), the sizing
  fractions (`w-1/2`), and the bracket forms. These dirty Yoga layout on every tick — the whole
  subtree relayouts each frame for the length of the play — so reach for a transform channel first
  and animate a length only where the reflow is the point.
- **Border widths,** from the `border-*` width utilities (`border`, `border-2`, `border-t-4`) and
  the bracket forms. These read a **separate literal scale mirroring the stylesheet's own
  declarations**, not the spacing scale — `border-2` is 2px, not `--space-2`. Same per-tick layout
  cost as the lengths above.
- **Font size and letter spacing, bracket forms only:** `text-[20px]` and `tracking-[2px]` are
  driven. The named presets are not — `text-lg` resolves through a `--text-*` token and
  `tracking-wide` through a fixed named step, neither of which has a C# mirror to read a magnitude
  from — so a `text-sm` → `text-2xl` delta lands instantly while `text-[14px]` → `text-[24px]`
  animates. Reach for the bracket form when you want the size to move.
- **Both sides must name the property.** Unlike the quartet, a color or a length has no identity
  value the silent side could stand in for ("no background color declared" is not the statement
  "transparent"), so a property only one side names is **not** animated: the swap lands it
  instantly. The same applies to a pair whose two sides carry different units (`w-1/2` →
  `w-[200px]`) — a percentage resolves against a laid-out parent this path cannot consult.
- **A shorthand beside its own longhand animates only when both can.** `p-8` with `pt-2` — or
  `size-*` with `w-*`, `inset-*` with `top-*`, `rounded-*` with `rounded-tl-*`, `border-*` with
  `border-t-*` — has two utilities claiming one slot. When **both** sides of the delta name both,
  both animate: the shorthand is written first and the longhand last on every tick, so the shared
  slot is held by the same utility that holds it at rest, matching CSS longhand-after-shorthand
  resolution. When one of them **cannot** animate (named on one side only, or a mixed-unit pair),
  the whole overlapping group drops out and lands with the class swap instead — the survivor would
  otherwise drive the shared slot toward a value the other was going to overrule, and pop at the
  end. One caveat: the rule only sees utilities whose value is resolvable, so an unreadable longhand
  beside a readable shorthand (`rounded-3xl rounded-tl-full`, `size-32 w-full`) is invisible to it
  and the shorthand still drives the slot the longhand owns at rest. Prefer one or the other on a
  given axis.
- **Not driven,** each because the class alone yields no number to interpolate or because another
  subsystem owns the slot: semantic theme tokens (`bg-primary`, `text-current`) resolve through
  `--color-*` with no C# mirror; the preset font-size (`text-lg`) and letter-spacing
  (`tracking-wide`) names likewise, per the bullet above; keyword lengths (`w-auto`, `w-full`) are
  modes, not magnitudes; `rounded-full` is a saturating pill sentinel; `shadow-*`, `skew-*` and
  gradients are baked silhouette paints; `filter-*` is driven by its own opt-in
  `transition-filter`; `z-*` is a physical reparent.
- **A play suspends the element's own USS transitions when they cover what it drives.** A driver
  writes the exact value the curve or the physics calls for on that frame, so a transition utility
  naming that same property restarts a native transition on every one of those writes and leaves
  the painted value trailing for the whole play, easing in at the end instead of landing. Whether
  that applies is decided from the element's **class list**: `transition-transform` names
  translate/scale/rotate, `transition-colors` names the three colours, `transition-colors-scale`
  and `transition-colors-scale-opacity` add those, `transition-all` names everything, and
  `transition-filter` declares no `transition-property` at all. A play suspends only where its own
  channels intersect that set — a `transition-colors` element running a fade/slide keeps its hover
  fade, while the same play on a `transition-transform` or `transition-all` element suspends.
  When it does suspend, the suspension is **element-wide**: UI Toolkit's `transition-property` is a
  positive list with no "everything except these" spelling, so the element's *other* transitions
  land instantly too, across the play's `DelaySec` and stagger slot as well as its motion — the
  element is already parked at its from-pose over that window. This is **narrower than Framer
  Motion**, which takes over only the values it animates and leaves the element's other CSS
  transitions running; UI Toolkit gives no way to express that. Two overlapping plays each hold
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

## Shared-element layout animation (`layoutId`)

```csharp
V.Motion(layoutId: "card-3", className: expanded ? "absolute left-[0px] top-[0px] w-[600px] h-[400px]"
                                                  : "absolute left-[40px] top-[120px] w-[120px] h-[80px]");
```

- Framer's `layoutId` parity. When a Motion carrying this same string patches at a resolved
  layout rect (position and/or size) different from the rect the SAME id last settled at, it
  tweens from the old rect to the new one — FLIP: the old rect is captured, layout settles at the
  new one, an inverse inline transform is applied immediately, then it springs back to zero —
  instead of jump-cutting.
- Works across a same-key type flip or a move to a different parent, not just an in-place resize:
  the id, not the physical element, is what's tracked. Two Motions in the same tree must never
  share a live `layoutId` simultaneously — the second one to patch silently steals the
  registration.
- Independent of `Variants`/`Animate`: the tween runs from the ACTUAL rect delta captured off
  `element.layout`, not a class-defined from/to pair, so it fires whether or not the same patch
  also changed variants. Falls back to `StyleTransitionConfig`'s own spring defaults (Stiffness
  100 / Damping 10 / Mass 1) when the Motion declares no `Transition`.
- **Uniform scale only.** A non-uniform rect change (width and height scale by different factors)
  averages the two axis scale factors rather than distorting the element on two independent axes
  — UI Toolkit's `scale` style is a single uniform factor, not independent X/Y.
- Position is captured synchronously before the patch (mirroring `PopLayout`'s own "read
  `.layout` before the mutation that invalidates it" pattern); the new rect is captured on the
  element's own next `GeometryChangedEvent`, since a reparented/freshly-created element's
  `.layout` stays stale until the following layout pass.

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
  defaults to that transition's `DurationSec + DelaySec` for a tween. A `Spring`-typed step needs an
  explicit `holdSec` -- a spring's settle time is physics-derived, not statically knowable -- an omitted
  one logs a warning and falls back to a fixed estimate rather than stalling the sequence.
- **`AnimationSequenceStep.Wait(seconds)`** -- holds the current label for `seconds` with no effect of
  its own.
- **`AnimationSequenceStep.Call(callback)`** -- fires `callback` synchronously on arrival, then advances
  immediately (never holds the cursor).

**"One at a time" needs no separate multi-target API.** Descendant Motions with no own `animate` inherit
the coordinator's label exactly as they already do for any hand-toggled label change (see "Label
inheritance" above); a `To` step's own `transition` declaring `StaggerChildrenSec` fans that swap out
across those descendants in document order, the same mechanism `V.Motion`'s orchestration knobs already
provide.

`autoplay` (default `true`) starts the sequence on mount; pass `false` and call `controls.Play()` (e.g.
from an `onClick`) to start it on demand. `loop: true` wraps the cursor back to step 0 once the last
step's hold elapses instead of latching `AnimationSequenceState.IsComplete`. `deps` follows the same
convention as every other deps-taking hook, but — unlike `UseEffect` — omitting it resets the walker on
**mount only**, not on every render: a freshly-built `steps` array literal in the component body (the
common case) must not restart an in-flight sequence every render. `controls.Restart()` returns to step 0
and re-commits its effect (including firing a `Call` step 0's callback again) without implicitly
resuming a paused sequence.

Not attempted: an arbitrary-selector scope ref (`useAnimate`'s `[scopeRef, animate]`) reaching elements
outside the declarative Motion/variant tree, and overlapping/parallel tracks (Framer's `"<"` / `"+0.2"`
relative-offset DSL) -- steps are a strict FIFO queue; two independently-timed tracks need two separate
`UseAnimationSequence` coordinators.

## Transition semantics: one config, every update

Every variant update rides the Motion's own `StyleTransitionConfig` — mount enters
(`initial` → `animate`), presence enters and exits, and runtime `animate` label changes, whether
the label is the Motion's own or inherited from an ancestor (orchestrated stagger children
included). Tweens write the config's timing as an inline transition for the swap and release it
on completion; spring configs integrate physically instead. This matches Framer, where
`transition` applies to every animate update:

```csharp
// Tweens on its own config — no transition-* utilities anywhere.
V.Motion(key: "card", className: "w-24 h-24 bg-sky-500",
    variants: s_fade, animate: isOpen ? "visible" : "hidden",
    transition: new StyleTransitionConfig { DurationSec = 0.3f });
```

Two consequences worth knowing:

- **Every Motion animates by default.** `V.Motion` without an explicit `transition` falls back
  to the `Fade` preset's timing, so even a bare Motion tweens its label swaps. Pass
  `transition: StyleTransitionConfig.None` for an instant, non-animated swap.
- **`transition-*` utilities are optional for variant swaps.** They still govern property
  changes outside the variant system (a `hover:` state flipping, an arbitrary class toggle); a
  variant swap's inline transition simply takes precedence while it plays and is released
  afterwards.
