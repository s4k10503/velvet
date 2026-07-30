# Styling notes: Flexbox direction, gap & divide

Velvet's utility classes are Tailwind-inspired, but they run on Unity UI Toolkit's layout engine
(Yoga), which implements a **subset of Flexbox** and differs from CSS in two places that trip up
people coming from Tailwind.

## 1. The engine's raw flex default is a **column**, not a row — `.flex` corrects it

| | CSS / Tailwind | Raw UI Toolkit / Yoga default | Velvet's `.flex` utility |
|---|---|---|---|
| Default `flex-direction` of a flex container | `row` | `column` | `row` |

Velvet's `.flex` utility sets `flex-direction: row` in addition to `display: flex`, so
`V.Div(className: "flex", ...)` alone lays out children horizontally, matching Tailwind. The raw
engine default (column) still surfaces on a flex container built **without** the `.flex` utility
class — a bare `VisualElement` styled entirely through `refCallback`, a manual inline
`display: flex`, or a custom manipulator — where children stack **vertically**.

```csharp
// Horizontal row — the `.flex` default, matching Tailwind (`flex-row` is redundant but harmless).
V.Div(className: "flex items-center gap-x-2", ...);

// Vertical column — flex-col overrides the row default.
V.Div(className: "flex flex-col gap-2", ...);
```

### Overriding the direction from a variant

Every combination works, in both directions — `flex flex-col md:flex-row` and
`flex flex-row md:flex-col`, `flex-col sm:flex-col-reverse` and `flex-col-reverse sm:flex-col`. A
variant payload outranks the base utility on the properties they share, whatever order `_layout.uss`
declares them in; see
[styling-variants.md](styling-variants.md#variants-and-the-uss-cascade) for the rule and its limits.

Two family-specific facts survive that:

- **Two direction utilities at the same priority still tie**, and `_layout.uss` declares them
  `.flex-col` → `.flex-col-reverse` → `.flex-row` → `.flex-row-reverse`, so a literal
  `"flex-col flex-row"` lays out as a row. Write one, or mark the winner important —
  `"flex-row !flex-col"` lays out as a column. There is no bracket form to fall back on:
  `flex-direction` has no arbitrary-value parse path, so `md:flex-[column]` is not recognized and
  silently adds a class matching no rule.
- **`.flex` and `.grid` set `flex-direction: row` alongside `display`**, so a direction utility never
  displaces them — it holds only part of what they write — and takes the direction from them by
  declaration order instead, both being declared before all four.

## 2. `gap-*` is a framework-level CSS-`gap` polyfill (no USS rules)

Unity UI Toolkit (6000.3) has **no** native flex `gap` / `row-gap` / `column-gap` and **no**
`:first-child` / `:last-child` USS selectors. A per-container `StyleGapManipulator` writes the
inter-child **leading** margin instead — `margin-left` for a row, `margin-top` for a column — on
every child **except the first**. The result is spacing strictly **between** children, exactly like
CSS `gap`: no leading, trailing, or outer-edge margin.

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
```

The numeric scale (`gap-0-5`, `gap-1`, `gap-1-5`, `gap-2`, … mapping to the `--space-*` tokens,
1 unit = 4px) is Tailwind's; the classes are recognized in C#, not by USS selectors — see
`Runtime/Styling/StyleGapManipulator.cs` and `Runtime/Styling/StyleGapClass.cs`.

Tailwind's `space-x-*` / `space-y-*` are accepted as aliases of `gap-x-*` / `gap-y-*` (same scale,
same manipulator), and `space-x-reverse` / `space-y-reverse` are accepted too — see "Reversed
containers" below.

### Reversed containers (`flex-row-reverse` / `flex-col-reverse`) and `space-*-reverse`

A `flex-row-reverse` / `flex-col-reverse` container moves a gap's margin to the axis's **trailing**
physical edge (`margin-right` / `margin-bottom`) instead of the leading one (`margin-left` /
`margin-top`). This applies to **every** gap on that axis, including a plain `gap-4` with no
`space-*-reverse` marker at all. `space-x-reverse` / `space-y-reverse` are Tailwind's own,
direction-independent way to ask for that trailing edge — an **absolute** per-axis marker that
Tailwind itself never conditions on `flex-direction` — so a marker and a detected reverse
direction on the same axis combine with **OR**, not XOR: `flex-row-reverse space-x-4
space-x-reverse` still lands trailing. The flip is per axis: a `gap-x-*` / `space-x-*` never
reacts to `flex-col-reverse`, and a `gap-y-*` / `space-y-*` never reacts to `flex-row-reverse`.

Because `space-*` runs through this same direction-aware polyfill, `space-x-4` **alone** — with no
`space-x-reverse` — is already direction-correct on a `flex-row-reverse` container, which makes
`space-x-reverse` a **compatibility no-op** there. That is a deviation from plain CSS, where
Tailwind's own `> * + *` margin rule needs the marker to ever move off the leading edge.

### `divide-x-*` / `divide-y-*` follow the same rule

`divide-x-*` / `divide-y-*` draw a **border** between adjacent children — Tailwind's `> * + *`
divider — and UI Toolkit has no `:first-child` and no `> *` child combinator either, so Velvet
realizes them through the same kind of per-container manipulator (`StyleDivideManipulator`): the
border goes on every child **except the first**, never on the container's outer edges.

Which physical edge carries that border follows **exactly** the rule "Reversed containers" above
states for gap — the same trailing-edge move, the same OR-not-XOR combination with the reverse
marker, the same per-axis independence, and the same class-list-before-`resolvedStyle` direction
source. Read that section for the rule; only the two differences are stated here. The axis is
always fixed by the class (`divide-x` is horizontal, `divide-y` is vertical — there is no
direction-following `Auto` form that a plain `gap-*` has), and the marker is spelled
`divide-x-reverse` / `divide-y-reverse`.

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

`divide-dashed` / `divide-dotted` have no UI Toolkit border-style, so Velvet paints those rules
itself, which costs them on a child that also carries `overflow-hidden` — see the painted-utility
table in [styling-variants.md](styling-variants.md).

### How re-spacing stays correct

Everything below is written for `gap-*`, but the divider manipulator resolves its direction the same
way and re-applies on the same three events, so it holds for `divide-*` too.

The spacing depends on the child set and on the resolved direction — which every axis needs, not
just plain `gap-*`'s axis choice, since a `gap-x-*` / `gap-y-*` has a reversed-edge flip too. Both
can change outside the manipulator's own events, so it is re-applied from three sources:

1. **Reconcile.** The reconciler calls the manipulator right after it reconciles the container's
   children, so an add / remove / reorder during a reconcile pass immediately re-spaces. This is also
   the path that makes it correct in EditMode, where layout never ticks.
2. **`GeometryChangedEvent`.** Catches child mutations driven by an *unrelated* reconcile pass at
   runtime (e.g. a nested component re-render that adds a child under this container). Registered on
   the attached element **and**, when it differs, on the inner box the verdict is read from (below),
   because `GeometryChangedEvent` neither bubbles nor trickles.
3. **`AttachToPanelEvent`.** Re-resolves plain `gap-*`'s axis, and every axis's reversed-edge flip,
   once `resolvedStyle.flexDirection` is valid — needed for the one case no class marker can cover
   (below). Registered on the attached element only, since the inner box attaches in its subtree pass.

**Which element the verdict is read from: the container the children are actually in.** Both
manipulators are attached to the element the class string is written on, but they resolve, iterate and
read from the element that element's children are *reconciled into*. For a plain element those are the
same. A **composite widget** redirects its children into an inner box, so a direction (or `flex-wrap`)
class on one lays out the **widget's own** box — a `ScrollView`'s viewport and scrollers, a `Foldout`'s
toggle above its content — and leaves the content untouched.

The spacing follows the content: in `V.ScrollView("flex flex-row-reverse gap-x-4", …)` the gap stays
on the leading edge, and in `V.ScrollView("flex flex-row gap-4", …)` a plain `gap-4` spaces
**vertically**, the axis its content actually stacks on. The rule is keyed on the redirect itself,
not on a list of widgets — `V.Custom<T>` mounts any `VisualElement` subclass with children. The
engine's redirecting widgets include `ScrollView`,
`Foldout`, `Tab`, `TabView`, `TwoPaneSplitView`, `RadioButtonGroup`, `ToggleButtonGroup` and
`PopupWindow`; the collection views (`ListView`, `TreeView`, `MultiColumnListView`) parent nothing and
build their rows themselves, so nothing here spaces them.

A class string only reaches the element it is written on, so no direction class can land on an inner
box: its verdict comes from the `resolvedStyle` fallback below, over whatever the widget's own built-in
USS gives it. Off-panel an inner box falls back to the **engine's** default (column) rather than to
`.flex`'s row, which for most widgets makes the off-panel answer equal the on-panel one.

**The residue, where the two disagree:** a widget whose own USS overrides the engine default, so its
inner box is a **row** — a horizontally scrolling `ScrollView`, a `TwoPaneSplitView`, a
`ToggleButtonGroup`, and any other whose built-in USS does the same. Only a live panel has the answer
there; off-panel they still resolve as a column. That costs a frame: the first application runs before
the widget attaches, writes the column edge, and moves to the row edge on the first geometry event. The
same residue applies to `flex-wrap` on an inner box whose built-in USS wraps, and is worse there — wrap
is the only mode that writes the container's own margin.

No **direction or wrap** utility reaches inside a composite widget to lay its content out, though the
spacing utilities do reach the inner box's children. Nest a plain container inside the widget and put
the direction class there when the content needs one.

**Direction source: a single resolved verdict from the class list, by USS precedence, not
`resolvedStyle` — on a panel included.** `flex` / `flex-row` / `flex-col` / `flex-row-reverse` /
`flex-col-reverse` are all direction-bearing (`flex` sets `flex-direction: row`, same as a bare
`flex-row`), and an element routinely carries more than one at once — the bare `flex` beside whichever
direction utility holds the direction, and two direction utilities written at one priority.
`StyleFlexDirectionResolver` reproduces the `_layout.uss` precedence given under "Overriding the
direction from a variant" above — checking `flex-row-reverse`, then `flex-row`, then
`flex-col-reverse`, then `flex-col`, then `flex` — and resolves it into ONE mutually-exclusive verdict
(axis + reversed-or-not together) rather than an axis check and a reversed check answered
independently: a `gap-x-4` container patched straight from `flex-row-reverse` to `flex-col-reverse`
(no row-family class survives the patch) needs the reversed bit to come from a fresh read of whichever
family the CURRENT verdict is, not a stale answer cached from the row family that is no longer even
present.

`grid` is excluded from this scan: a `grid`, literal or from a variant such as `md:grid`, routes the
element's gap through the separate grid manipulator instead — see "`flex-wrap` and `grid`" below.

Classes are consulted before `resolvedStyle`, even on a panel: the direction classes are USS-only
rules with no equivalent C# inline `flex-direction` write, so `resolvedStyle.flexDirection` only
catches up after the panel's *next* style pass, and a same-rect direction toggle (children reorder;
the container itself never resizes) fires no `GeometryChangedEvent` to trigger a re-derive. A
manipulator that trusted `resolvedStyle` here could converge on the *first* toggle and then never
converge on a later toggle back, leaving a gap margin wrong indefinitely.

One consequence: a Velvet direction class, when present, **outranks** `flex-direction` set some
other way (a custom stylesheet rule, an inline style) on the SAME element rather than composing with
it. `resolvedStyle` is read only as the fallback for the case no class can cover: `flex-direction`
set that other way with *none* of the five direction/display classes present on the element at all.
That fallback case still needs a live panel (`AttachToPanelEvent` above) and still cannot
self-correct on a same-rect toggle with no intervening reconcile pass, since nothing would tell the
manipulator to look again. With no direction class AND no panel to resolve against (EditMode,
pre-attach), the default is **row** — the one place this deliberately disagrees with the raw engine,
whose own unstyled default is column (see "The engine's raw flex default" above).

One more asymmetry: a plain `gap-*` (the Auto axis) DOES react to a `space-*-reverse` marker on
whichever axis it resolves to, even though real Tailwind `gap` has no reverse-marker concept at all.
`flex flex-col gap-4 space-y-reverse` demonstrates it: `gap-4` resolves to the vertical axis (via
`flex-col`), and `space-y-reverse` is on that SAME axis, so the plain gap lands on `margin-bottom`
instead of `margin-top` — an (irrelevant) `space-x-reverse` on the same element would not apply, since
it targets the other axis.

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
changes which edges wrap spaces, only non-wrap's single leading/trailing edge choice.

Wrap is read from the same element the direction is (see "Which element the verdict is read from"
above), and in the same shape, for the same staleness reason: the `flex-wrap` / `flex-nowrap` /
`flex-wrap-reverse` class markers first (by `_layout.uss` declaration order — `flex-wrap-reverse`
beats `flex-nowrap` beats `flex-wrap` when more than one is present), then `resolvedStyle.flexWrap`
whenever none of those three is present. Unlike the direction scan there is no further "a direction
class implies a default" tier: `flex` / `flex-row(-reverse)` / `flex-col(-reverse)` say nothing about
`flex-wrap`, and since nearly every real container carries one, counting them as evidence of no-wrap
would misread a genuinely wrapping inline-styled container.

One consequence: removing a `flex-wrap` class does not converge on its own, because the verdict
drops to `resolvedStyle.flexWrap`, which can read one pass stale. Spell the OFF state `flex-nowrap`
— `wrapped ? "flex flex-wrap gap-4" : "flex flex-nowrap gap-4"` — and the marker tier answers
immediately. Getting this wrong is worse than getting direction wrong: wrap is the only mode that
writes the CONTAINER's own margin, so a stuck stale "wrap" verdict leaves a fixed-size container
bleeding its negative margin outward over its siblings until something unrelated forces a re-apply.

A corollary on a **composite widget**: a `flex-wrap` class wraps the widget, never its content, so the
half-margin path is **unreachable from class strings** there. It is still reachable by anything setting
the inner box's own `flex-wrap` (a custom stylesheet rule, a `refCallback` reaching in) and by a widget
whose built-in USS wraps. Nest a plain container inside the widget for a wrapping gap from a class.

`grid` also sets `flex-direction: row` (and `flex-wrap: wrap`) in `_layout.uss`, but a `grid` /
`grid-cols-*` class routes an element's gap through the separate grid manipulator entirely —
`StyleGapManipulator` is suppressed and removed for that element rather than ever computing a
direction or wrap verdict for it. The suppression is decided once per patch, from the same class
source the two manipulators are configured from, and handed to the gap configuration as a verdict —
which is also what orders the handoff, since whichever of the two is departing has to release the
child margins it wrote before the arriving one writes its own.

```csharp
// Wrapping grid: gap-4 now spaces BOTH the row direction and between wrapped rows.
V.Div(className: "flex flex-row flex-wrap gap-4", children: ...);
```

## Residual edge cases (where the polyfill is approximate)

The common non-wrap row/column layout is **exact**; the remaining gaps are called out here and in
`StyleGapManipulator.cs`:

- **Explicit per-child margin on the gap edge.** A child with `ml-2` (or an inline `margin-left`)
  under a `gap-x-4` row has that margin **overwritten** — the manipulator owns the margin edge(s) it
  spaces along and rewrites the gap value there on every pass. The same property cannot
  simultaneously *be* the gap and carry an independent child margin: composing the two would mean
  capturing each child's pre-gap base margin once and re-deriving it on every re-apply (reconcile /
  geometry / attach), and a re-apply reads back the already-gap-modified inline value with no way to
  tell base from gap. Only native UITK `gap` composes the two. Workaround: use padding, an inner
  wrapper, or a different axis when a child needs its own margin on the gap edge. Margins on a
  **different** edge than the gap are preserved, so `mt-2` on a child under a non-wrap `gap-x-4` row
  is untouched. (Under the wrap half-margin path every side belongs to the gap, so any explicit child
  margin is overwritten on all four sides.)
- **First child's spacing-edge margin is erased.** The non-wrap path forces the **first** child's
  spacing-edge margin to `Null` on every pass — `margin-left` / `margin-top` normally, or `margin-right` /
  `margin-bottom` on a reversed container (the SAME edge `gap` writes on every other child). The first
  child must carry no gap on that edge to match CSS `gap`. So an explicit margin on the first child's
  gap edge (e.g. `ml-2` on the first child of a `gap-x-4` row) is **erased**: the manipulator cannot
  distinguish an intentional first-child margin from a stale gap value it wrote on a previous pass. The
  first child's *other* edges, and all edges of non-first children's *cross* axis, are untouched.
  Workaround: use container padding for a leading inset, or an inner wrapper.
- **Wrap path overwrites (and loses) the container's own margin.** The wrap half-margin path writes the
  container's own four margins to `-gap/2`, so an explicit container margin (e.g. `m-4` on the same
  element that carries `flex-wrap gap-4`) is **overwritten** while gap is active — and `Clear` resets the
  container margin to `Null`, so the user's container margin is **lost** (not restored) for as long as a
  wrapping gap is applied. Non-wrap containers never touch the container's own margin. Workaround: put
  the margin on an **outer wrapper** around the wrapping gap container.
- **Wrap outer bleed.** The wrap path's container negative margin (`-gap/2` on all four sides) bleeds
  `gap/2` **outward**, overlapping the container's own siblings or its parent's padding by `gap/2`. The
  half-margin trick has no way to cancel only the *inner* outer-edge halves; only native UITK `gap`
  avoids it. Non-wrap containers never bleed — they write no container margin. Add `gap/2` of padding
  on the parent, or wrap the grid, if the overlap matters.

## Roadmap

A native `gap` depends on a UI Toolkit feature that is not yet available (USS `gap`, and broader
Flexbox parity, are on Unity's roadmap beyond 6.7 LTS). When native `gap` lands, the polyfill can
be replaced and the residual edge cases above go away.
