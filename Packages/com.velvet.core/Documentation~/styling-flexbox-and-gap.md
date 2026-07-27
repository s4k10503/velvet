# Styling notes: Flexbox direction, gap & divide

Velvet's utility classes are Tailwind-inspired, but they run on Unity UI Toolkit's
layout engine (Yoga), which implements a **subset of Flexbox** and behaves differently
from CSS in two places that trip up people coming from Tailwind. Both are inherent to
UI Toolkit, not bugs in Velvet — this page documents the gotchas, what Velvet already
papers over, and the idioms that avoid what is left.

## 1. The engine's raw flex default is a **column**, not a row — `.flex` corrects it

| | CSS / Tailwind | Raw UI Toolkit / Yoga default | Velvet's `.flex` utility |
|---|---|---|---|
| Default `flex-direction` of a flex container | `row` | `column` | `row` |

In CSS, `display: flex` lays children out horizontally by default. UI Toolkit's underlying flex
container (Yoga) defaults to `column` instead, so a raw `display: flex` written outside Velvet's
utilities (a manual inline style, a `refCallback`, …) stacks children **vertically**. Velvet's
`.flex` utility class closes that gap explicitly: it sets `flex-direction: row` in addition to
`display: flex`, so `V.Div(className: "flex", ...)` alone already lays out children
**horizontally**, matching Tailwind's default.

```csharp
// Horizontal row — the `.flex` default, matching Tailwind (`flex-row` is redundant but harmless).
V.Div(className: "flex items-center gap-x-2", ...);

// Vertical column — flex-col overrides the row default.
V.Div(className: "flex flex-col gap-2", ...);
```

`flex-col` still forces a column when you need one. **`flex-row` is not decorative**: it resolves
to the same declaration as the `.flex` default, but writing it out forecloses every column
override, so `"flex md:flex-col"` lays out as a column above the breakpoint while
`"flex flex-row md:flex-col"` stays a row at every width — see "Overriding the direction from a
variant" below. Spell `flex-row` only when nothing may ever override the direction; otherwise leave
the row implicit in `.flex`. The only place the raw engine default (column) still surfaces is a
flex container built **without** the `.flex` utility class — e.g. a bare `VisualElement` styled
entirely through `refCallback` or a custom manipulator.

### Overriding the direction from a variant

A variant is realized by adding the **bare** utility to the live class list, so
`"flex flex-col md:flex-row"` carries both `flex-col` and `flex-row` above the breakpoint. Every
direction utility is a single-class selector, so specificity ties and **the later-declared rule
wins**. `_layout.uss` therefore declares the column family before the row family:

| Declaration order in `_layout.uss` | Precedence |
|---|---|
| `.flex-col` → `.flex-col-reverse` → `.flex-row` → `.flex-row-reverse` | `flex-row-reverse` > `flex-row` > `flex-col-reverse` > `flex-col` > `flex` / `grid` |

That is chosen for the mobile-first idiom: a variant can turn a **column into a row**
(`flex flex-col md:flex-row` — a narrow-screen stack that becomes a wide-screen row), and within
an axis it can turn a **plain direction into its reversed form**
(`flex-col sm:flex-col-reverse`, `flex-row md:flex-row-reverse`).

What does **not** work, in either case, is the opposite override:

| Works | Does not work |
|---|---|
| `flex flex-col md:flex-row` | `flex flex-row md:flex-col` |
| `flex flex-col-reverse md:flex-row` | `flex flex-row md:flex-col-reverse` |
| `flex flex-col md:flex-col-reverse` | `flex flex-col-reverse md:flex-col` |
| `flex flex-row md:flex-row-reverse` | `flex flex-row-reverse md:flex-row` |

A right-hand entry is a silent no-op: the variant class lands on the element and loses the cascade,
so the base direction holds at every width. This is a limit of **the current class-toggle
mechanism**, not of CSS: upstream Tailwind emits variant utilities into a later layer, so both
columns would work there. Velvet toggles the bare utility onto the live class list instead, which
leaves the cascade with no way to tell a variant-applied class from a base one.

Two things that work elsewhere do **not** rescue this family:

- **Swapping base and variant is not always the same design.** Velvet's responsive variants are
  min-width only, so `flex-col md:flex-row` means "column below the breakpoint, row above" — the
  mirror image of "row below, column above", not a rewrite of it. That second layout is currently
  not expressible with the direction utilities.
- **There is no bracket escape hatch.** `flex-direction` has no arbitrary-value parse path, so
  `md:flex-[column]` is not recognized and silently adds a class matching no rule. The inline-layer
  trick that makes `md:w-[320px]` order-independent is unavailable here.

When you need an override this table does not offer, compute the class string in C# from a width
the component observes itself (a `refCallback` registering `GeometryChangedEvent`, feeding a
`UseState`) and render `flex-col` or `flex-row` — never both.

`.flex` and `.grid` set `flex-direction: row` too, and are declared before all four, so any
explicit direction utility outranks them.

## 2. `gap-*` is a framework-level CSS-`gap` polyfill (no USS rules)

Unity UI Toolkit (6000.3) has **no** native flex `gap` / `row-gap` / `column-gap` and **no**
`:first-child` / `:last-child` USS selectors. The old emulation used a child-margin USS rule
(`.gap-* > *`), which had two parity defects: it could only target one fixed axis (it ignored
`flex-direction`), and `> *` also margined the **last** child (a trailing gap that USS could not
cancel).

Velvet now implements gap at the **framework level**. Because Velvet owns the ordered child list,
a per-container `StyleGapManipulator` writes the inter-child **leading** margin — `margin-left`
for a row, `margin-top` for a column — on every child **except the first**. The result is spacing
strictly **between** children, exactly like CSS `gap`: no leading, trailing, or outer-edge margin.

| Utility | Axis | Effect |
|---|---|---|
| `gap-*`   | follows `flex-direction` | row → horizontal, column → vertical; the leading edge of that axis (`margin-left` / `margin-top`) — or the trailing edge (`margin-right` / `margin-bottom`) on a reversed container, see "Reversed containers" below |
| `gap-x-*` | always horizontal | `margin-left` between columns, or `margin-right` on a `flex-row-reverse` container |
| `gap-y-*` | always vertical | `margin-top` between rows, or `margin-bottom` on a `flex-col-reverse` container |

```csharp
// Horizontal spacing between columns, no trailing gap after the last item.
V.Div(className: "flex flex-row gap-x-4", children: ...);

// Vertical spacing between rows.
V.Div(className: "flex flex-col gap-4", children: ...);

// Plain gap follows the direction — this row is spaced horizontally.
V.Div(className: "flex flex-row gap-4", children: ...);
```

The class names and the numeric scale (`gap-0-5`, `gap-1`, `gap-1-5`, `gap-2`, … mapping to the
`--space-*` tokens, 1 unit = 4px) are unchanged from the old emulation, so existing call sites are
unaffected — the classes are recognized in C# now instead of by USS selectors. `_gap.uss` no longer
emits any rules; see `Runtime/Styling/StyleGapManipulator.cs` and `Runtime/Styling/StyleGapClass.cs`.

Tailwind's `space-x-*` / `space-y-*` are accepted as aliases of `gap-x-*` / `gap-y-*` (same scale,
same manipulator) — they realize the same "leading margin on every child but the first" spacing a
`space-* > * + *` margin rule would inside a flex container. `space-x-reverse` / `space-y-reverse`
are accepted too — see "Reversed containers" below for what they do and the one case where they are
a no-op.

### Reversed containers (`flex-row-reverse` / `flex-col-reverse`) and `space-*-reverse`

A `flex-row-reverse` / `flex-col-reverse` container moves a gap's margin to the axis's **trailing**
physical edge (`margin-right` / `margin-bottom`) instead of the leading one (`margin-left` /
`margin-top`) — this applies to **every** gap on that axis, including a plain `gap-4` with no
`space-*-reverse` marker at all, because native CSS `gap` has no leading/trailing distinction to
begin with: it spaces consecutive children the same way regardless of direction, so matching it
here means the polyfill's margin has to move to whichever physical edge is the "between children"
edge for the resolved direction. `space-x-reverse` / `space-y-reverse` are Tailwind's own,
direction-independent way to ask for the same trailing edge — they are an **absolute** per-axis
marker ("put the margin on the trailing edge") that Tailwind itself never conditions on
`flex-direction`, so a marker and a detected reverse direction on the same axis combine with **OR**,
not XOR: `flex-row-reverse space-x-4 space-x-reverse` still lands trailing rather than the marker
cancelling the direction back to leading. The flip is per axis — a `gap-x-*` / `space-x-*` never
reacts to `flex-col-reverse`, and a `gap-y-*` / `space-y-*` never reacts to `flex-row-reverse`.

One deliberate consequence: because `space-x-*` / `space-y-*` are realized through this same
CSS-`gap` polyfill, and `gap` is already direction-correct on its own (per the paragraph above),
`space-x-4` **alone** — with no `space-x-reverse` — is already direction-correct on a
`flex-row-reverse` container. That makes `space-x-reverse` a **compatibility no-op** there: adding
it changes nothing, because the direction already produced the trailing edge without it. This is a
deviation from plain CSS (where `space-x-*` has no native realization at all, so Tailwind's own
`> * + *` margin rule needs the marker to ever move off the leading edge); it exists here only
because Velvet's `space-*` and `gap-*` share one direction-aware implementation.

### `divide-x-*` / `divide-y-*` follow the same rule

`divide-x-*` / `divide-y-*` draw a **border** between adjacent children — Tailwind's `> * + *`
divider — and UI Toolkit has no `:first-child` and no `> *` child combinator either, so Velvet
realizes them through the same kind of per-container manipulator (`StyleDivideManipulator`): the
border goes on every child **except the first**, never on the container's outer edges.

Which physical edge carries that border follows **exactly** the rule "Reversed containers" above
states for gap — the same trailing-edge move on `flex-row-reverse` / `flex-col-reverse`, the same
OR-not-XOR combination with the reverse marker, the same per-axis independence, and the same
class-list-before-`resolvedStyle` direction source. Read that section for the rule; only the two
differences are restated here. The axis is always fixed by the class (`divide-x` is horizontal,
`divide-y` is vertical — there is no direction-following `Auto` form that a plain `gap-*` has), and
the marker is spelled `divide-x-reverse` / `divide-y-reverse` rather than `space-x-reverse` /
`space-y-reverse`.

| Utility | Axis | Effect |
|---|---|---|
| `divide-x-*` | always horizontal | `border-left` between columns, or `border-right` on a `flex-row-reverse` container / with `divide-x-reverse` |
| `divide-y-*` | always vertical | `border-top` between rows, or `border-bottom` on a `flex-col-reverse` container / with `divide-y-reverse` |

A lone `divide-x-reverse` does nothing on its own — like `divide-{color}`, it needs a `divide-x` /
`divide-y` to give it a width to move. Because a divider is a real border, the edge it lands on also
carries its **color** and its share of the box model: the manipulator owns the width *and* color
channel of that one edge and releases both when the edge changes. The other three edges are left
alone, so a child's own `border-b` under a `divide-x` row is preserved. Unlike gap, divide has no
wrap-specific strategy: a wrapping container still gets a single per-child divider edge, not a
symmetric one, so dividers between wrapped *lines* are not drawn.

### How re-spacing stays correct

Everything below is written for `gap-*`, but the divider manipulator resolves its direction the same
way and re-applies on the same three events, so it holds for `divide-*` too.

The spacing depends on the child set and, for every axis (not just plain `gap-*`'s axis choice — a
`gap-x-*` / `gap-y-*`'s reversed-edge flip too), the resolved direction. Both can change outside the
manipulator's own events. It is re-applied from three sources:

1. **Reconcile.** The reconciler calls the manipulator right after it reconciles the container's
   children, so an add / remove / reorder during a reconcile pass immediately re-spaces. This is also
   the path that makes it correct in EditMode, where layout never ticks.
2. **`GeometryChangedEvent`.** Catches child mutations driven by an *unrelated* reconcile pass at
   runtime (e.g. a nested component re-render that adds a child under this container).
3. **`AttachToPanelEvent`.** Re-resolves plain `gap-*`'s axis, and every axis's reversed-edge flip,
   once `resolvedStyle.flexDirection` is valid — needed for the one case no class marker can cover
   (below).

**Direction source: a single resolved verdict from the class list, by USS precedence, not
`resolvedStyle` — on a panel included.** `flex` / `flex-row` / `flex-col` / `flex-row-reverse` /
`flex-col-reverse` are all direction-bearing (`flex` sets `flex-direction: row`, same as a bare
`flex-row`), and `_layout.uss` declares them in the order given under "Overriding the direction
from a variant" above — so at equal specificity (one class selector each) a
*later*-declared rule always overrides an earlier one when an element carries more than one of them at
once, which is routine once a responsive/state variant is involved (`flex flex-col md:flex-row`
leaves BOTH `flex-col` and `flex-row` on the live class list above the breakpoint).
`StyleFlexDirectionResolver` reproduces that exact precedence — checking
`flex-row-reverse`, then `flex-row`, then `flex-col-reverse`, then `flex-col`, then `flex`, in that
order —
and resolves it into ONE mutually-exclusive verdict (axis + reversed-or-not together), rather than an
axis check and a reversed check answered independently: a `gap-x-4` container patched straight from
`flex-row-reverse` to `flex-col-reverse` (no row-family class survives the patch) needs the reversed
bit to come from a fresh read of whichever family the CURRENT verdict is, not a stale answer cached
from the row family that is no longer even present. (`grid` sets `flex-direction: row` too, but is
deliberately excluded from this scan: a `grid` present in the RECONCILED class array routes the
element's gap through the separate grid manipulator instead, suppressing this one entirely — see
"`flex-wrap` and `grid`" below. A variant-gated class such as `md:grid` can still land on the LIVE
class list outside that reconciled array, a pre-existing gap in the suppression check rather than
something this change introduces, but it does not change the answer here either way: `grid` implies
Row, which coincides with this scan's own fallback default, so omitting it never produces a different
result.)

Classes are consulted before `resolvedStyle`, even on a panel: the direction classes are USS-only
rules with no equivalent C# inline `flex-direction` write, so `resolvedStyle.flexDirection` only
catches up after the panel's *next* style pass, and a same-rect direction toggle (children reorder;
the container itself never resizes) fires no `GeometryChangedEvent` to trigger a re-derive. A
manipulator that trusted `resolvedStyle` here could converge on the *first* toggle (its initial layout
pass happens to touch the rect) and then never converge on a later toggle back — leaving a gap margin
wrong indefinitely. The class list, by contrast, is already the *final* one at the moment a
class-driven patch reaches the manipulator, so reading it directly is both correct and immediate, with
no dependency on a style pass or a geometry event. One consequence: a Velvet direction class, when
present, now **outranks** `flex-direction` set some other way (a custom stylesheet rule, an inline
style) on the SAME element, rather than composing with it — `resolvedStyle` is read only as the
fallback for the case no class can cover: `flex-direction` set that other way with *none* of the five
direction/display classes present on the element at all. That fallback case still needs a live panel
(`AttachToPanelEvent` above) and still cannot self-correct on a same-rect toggle with no intervening
reconcile pass, since nothing would tell the manipulator to look again. With no direction class AND no
panel to resolve against (EditMode, pre-attach), the default is **row** — the one place this
deliberately disagrees with the raw engine, whose own unstyled default is column (see "The engine's
raw flex default" above); mirroring `.flex`'s intent here matters more than mirroring Yoga's raw
default, since `.flex` alone is the common case this default exists for.

One more asymmetry worth knowing: a plain `gap-*` (the Auto axis) DOES react to a `space-*-reverse`
marker on whichever axis it resolves to, even though real Tailwind `gap` has no reverse-marker concept
at all (only its `space-*` polyfill does, since native `gap` never needed one) — this is a consequence
of Velvet routing `gap-*` and `space-*` through one shared, direction-aware implementation rather than
a divergence you would predict from Tailwind's own model. `flex flex-col gap-4 space-y-reverse`
demonstrates it: `gap-4` resolves to the vertical axis (via `flex-col`), and `space-y-reverse` is on
that SAME axis, so the plain gap lands on `margin-bottom` instead of `margin-top` — an (irrelevant)
`space-x-reverse` on the same element would not apply, since it targets the other axis.

## `flex-wrap` and `grid`: both axes are spaced (half-margin hybrid)

CSS `gap` under `flex-wrap` spaces **both** axes — between items in a line *and* between wrapped
lines. A single leading-edge margin can only space the main axis, so the manipulator switches
strategy when the container wraps:

| Container | Strategy | Children | Container |
|---|---|---|---|
| non-wrap (common) | leading margin | `gap` on the leading edge of all-but-first child | none |
| `flex-wrap` | half-margin | `gap/2` on **all four sides** of **every** child | `-gap/2` on all four sides |

Under wrap, any two adjacent items (either axis, including across wrapped lines) are separated by
`gap/2 + gap/2 == gap`, and the container's negative margin cancels the children's outer-edge
half-margins so content stays flush to the container edge. A reversed container (e.g.
`flex-row-reverse flex-wrap`) still uses this same symmetric half-margin polyfill — direction never
changes which edges wrap spaces, only non-wrap's single leading/trailing edge choice. Wrap is detected
the same way direction is, and for the same staleness reason: the `flex-wrap` / `flex-nowrap` /
`flex-wrap-reverse` class markers first (by `_layout.uss` declaration order — `flex-wrap-reverse`
beats `flex-nowrap` beats `flex-wrap` when more than one is present); when none of those three are
present, `flex` / `flex-row` / `flex-col` / `flex-row-reverse` / `flex-col-reverse` — none of which
touch `flex-wrap` themselves — mean Yoga's own default (no wrap) applies, so an OFF-state toggle like
`wrapped ? "flex flex-wrap gap-4" : "flex gap-4"` still converges without a wrap class in its OFF
state; `resolvedStyle.flexWrap` is the fallback only when none of those eight classes are on the
element at all. Getting this wrong is worse than getting direction wrong: wrap is the only mode that
writes the CONTAINER's own margin, so a stuck stale "wrap" verdict leaves a fixed-size container
bleeding its negative margin outward over its siblings until something unrelated forces a re-apply.

`grid` also sets `flex-direction: row` (and `flex-wrap: wrap`) in `_layout.uss`, but a `grid` /
`grid-cols-*` class routes an element's gap through the separate grid manipulator entirely —
`StyleGapManipulator` is suppressed and removed for that element rather than ever computing a
direction or wrap verdict for it — the suppression itself lives in
`FiberNodePatcher.ApplyGapManipulator`, which checks for the grid class before the manipulator is
ever created or updated — so neither class scan needs to (or does) recognize `grid` at all.

```csharp
// Wrapping grid: gap-4 now spaces BOTH the row direction and between wrapped rows.
V.Div(className: "flex flex-row flex-wrap gap-4", children: ...);
```

## Residual edge cases (where the polyfill is approximate)

A margin polyfill cannot be 100% identical to native `gap` in every case. The common non-wrap
row/column layout is **exact**; the remaining gaps are called out here and in
`StyleGapManipulator.cs`:

- **Explicit per-child margin on the gap edge.** A child with `ml-2` (or an inline `margin-left`)
  under a `gap-x-4` row has that margin **overwritten** — the manipulator owns the margin edge(s) it
  spaces along and rewrites the gap value there on every pass. This is an *inherent* limitation of a
  margin-based polyfill: the same property can't simultaneously *be* the gap and carry an independent
  child margin. Composing the two would require capturing each child's pre-gap base margin once and
  re-deriving it on every re-apply (reconcile / geometry / attach), which is fragile — a re-apply
  reads back the already-gap-modified inline value and can't tell base from gap — so Velvet does
  **not** attempt it. Only native UITK `gap` composes the two. Workaround: use padding, an inner
  wrapper, or a different axis when a child needs its own margin on the gap edge. Margins on a
  **different** edge than the gap are preserved, so `mt-2` on a child under a non-wrap `gap-x-4` row
  is untouched. (Under the wrap half-margin path every side belongs to the gap, so any explicit child
  margin is overwritten on all four sides.)
- **First child's spacing-edge margin is erased.** The non-wrap path forces the **first** child's
  spacing-edge margin to `Null` on every pass — `margin-left` / `margin-top` normally, or `margin-right` /
  `margin-bottom` on a reversed container (the SAME edge `gap` writes on every other child — see
  "Reversed containers" above). The first child must carry no gap on that edge to match CSS `gap` (no
  outer-edge spacing). So an explicit margin on the first child's gap edge (e.g. `ml-2` on the first
  child of a `gap-x-4` row) is **erased**: the manipulator cannot distinguish an intentional first-child
  margin from a stale gap value it wrote on a previous pass. The first child's *other* edges, and all
  edges of non-first children's *cross* axis, are untouched. Workaround: use container padding for a
  leading inset, or an inner wrapper.
- **Wrap path overwrites (and loses) the container's own margin.** The wrap half-margin path writes the
  container's own four margins to `-gap/2`, so an explicit container margin (e.g. `m-4` on the same
  element that carries `flex-wrap gap-4`) is **overwritten** while gap is active — and `Clear` resets the
  container margin to `Null`, so the user's container margin is **lost** (not restored) for as long as a
  wrapping gap is applied. This is the wrap polyfill's price for both-axis spacing; non-wrap containers
  never touch the container's own margin. Workaround: put the margin on an **outer wrapper** around the
  wrapping gap container.
- **Wrap outer bleed.** The wrap path's container negative margin (`-gap/2` on all four sides) bleeds
  `gap/2` **outward**, overlapping the container's own siblings or its parent's padding by `gap/2`.
  This is inherent to every pre-native-gap wrap polyfill (the half-margin trick has no way to cancel
  only the *inner* outer-edge halves); only native UITK `gap` avoids it. Non-wrap containers never
  bleed — they write no container margin. Add `gap/2` of padding on the parent, or wrap the grid, if
  the overlap matters.

## Roadmap

A native `gap` depends on a UI Toolkit feature that is not yet available (USS `gap`, and broader
Flexbox parity, are on Unity's roadmap beyond 6.7 LTS). When native `gap` lands, the polyfill can
be replaced and the residual edge cases above go away. Until then, the framework-level manipulator
is the supported approach and matches CSS `gap` for the common cases.
