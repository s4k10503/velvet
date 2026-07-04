# Styling notes: Flexbox direction & gap

Velvet's utility classes are Tailwind-inspired, but they run on Unity UI Toolkit's
layout engine (Yoga), which implements a **subset of Flexbox** and behaves differently
from CSS in two places that trip up people coming from Tailwind. Both are inherent to
UI Toolkit, not bugs in Velvet — this page documents the gotchas and the idioms that
avoid them.

## 1. `flex` defaults to a **column**, not a row

| | CSS / Tailwind | Velvet (UI Toolkit / Yoga) |
|---|---|---|
| Default `flex-direction` of a flex container | `row` | `column` |

In CSS, `display: flex` lays children out horizontally by default. In UI Toolkit a flex
container's default direction is `column`, so `.flex` alone stacks children **vertically**.

> A `VisualElement` is already a flex container by default, so `.flex` is mostly about
> intent/readability — the direction is what actually matters.

**Always state the direction explicitly:**

```csharp
// Horizontal row (Tailwind's `flex` default)
V.Div(className: "flex flex-row items-center gap-x-2", ...);

// Vertical column
V.Div(className: "flex flex-col gap-2", ...);
```

`.flex` intentionally does **not** force `flex-direction: row`. Doing so would override the
engine default and silently re-flow every existing `flex`-without-direction element. Direction
is therefore opt-in via `flex-row` / `flex-col`.

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
| `gap-*`   | follows `flex-direction` | row → horizontal (`margin-left`), column → vertical (`margin-top`) |
| `gap-x-*` | always horizontal | `margin-left` between columns |
| `gap-y-*` | always vertical | `margin-top` between rows |

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

### How re-spacing stays correct

The spacing depends on the child set and (for plain `gap-*`) the resolved direction, both of which
can change outside the manipulator's own events. It is re-applied from three sources:

1. **Reconcile.** The reconciler calls the manipulator right after it reconciles the container's
   children, so an add / remove / reorder during a reconcile pass immediately re-spaces. This is also
   the path that makes it correct in EditMode, where layout never ticks.
2. **`GeometryChangedEvent`.** Catches child mutations driven by an *unrelated* reconcile pass at
   runtime (e.g. a nested component re-render that adds a child under this container).
3. **`AttachToPanelEvent`.** Re-resolves plain `gap-*`'s axis once `resolvedStyle.flexDirection` is
   valid. Off-panel (EditMode, pre-attach) it falls back to the `flex-row` / `flex-col` class marker,
   defaulting to **row** (matching Tailwind's `flex` and the framework's `.flex` intent) when neither is
   present; `flex-col` still forces a column. On a panel the resolved direction is authoritative either
   way, so this off-panel default only affects pre-layout assertions (and EditMode).

## `flex-wrap`: both axes are spaced (half-margin hybrid)

CSS `gap` under `flex-wrap` spaces **both** axes — between items in a line *and* between wrapped
lines. A single leading-edge margin can only space the main axis, so the manipulator switches
strategy when the container wraps:

| Container | Strategy | Children | Container |
|---|---|---|---|
| non-wrap (common) | leading margin | `gap` on the leading edge of all-but-first child | none |
| `flex-wrap` | half-margin | `gap/2` on **all four sides** of **every** child | `-gap/2` on all four sides |

Under wrap, any two adjacent items (either axis, including across wrapped lines) are separated by
`gap/2 + gap/2 == gap`, and the container's negative margin cancels the children's outer-edge
half-margins so content stays flush to the container edge. Wrap is detected from
`resolvedStyle.flexWrap` on a panel and from the `flex-wrap` class off-panel (EditMode); the
half-margin values are layout-independent, so they resolve without a layout tick.

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
- **First child's leading-edge margin is erased.** The non-wrap path forces the **first** child's
  leading-edge margin (`margin-left` for a row, `margin-top` for a column) to `Null` on every pass — the
  first child must have no leading gap to match CSS `gap` (no outer-edge spacing). So an explicit margin
  on the first child's gap edge (e.g. `ml-2` on the first child of a `gap-x-4` row) is **erased**: the
  manipulator cannot distinguish an intentional first-child margin from a stale gap value it wrote on a
  previous pass. The first child's *other* edges, and all edges of non-first children's *cross* axis, are
  untouched. Workaround: use container padding for a leading inset, or an inner wrapper.
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

A native `gap` and a configurable default direction depend on UI Toolkit features that are not yet
available (USS `gap`, and broader Flexbox parity, are on Unity's roadmap beyond 6.7 LTS). When native
`gap` lands, the polyfill can be replaced and the residual edge cases above go away. Until then, the
framework-level manipulator is the supported approach and matches CSS `gap` for the common cases.

See also: the related discussion in issues #7 (gap implementation) and #9 (flex default direction).
