# Styling notes: Flexbox direction, gap & divide

Velvet's utility classes are Tailwind-inspired, but they run on Unity UI Toolkit's layout engine
(Yoga), where every element is laid out as a flex container.

## 1. Without `.flex`, children stack vertically, as they do in a CSS block

UI Toolkit has no block layout, and its flex containers default to `flex-direction: column`, so an
element carrying no display utility stacks its children vertically, the way CSS stacks the children
of a `display: block` element. Velvet's `.flex` utility sets `flex-direction: row` in addition to
`display: flex`, so `V.Div(className: "flex", ...)` alone lays out children horizontally, matching
Tailwind. Code that styles an element through the engine's own API — a `refCallback` or a custom
manipulator — gets the engine's column default unless it sets `flex-direction` itself.

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
  `"flex-row !flex-col"` lays out as a column. Neither Tailwind nor Velvet has a bracket form for the
  direction: Tailwind's `flex-[…]` is the `flex` shorthand, so its `flex-[column]` writes a declaration
  the browser drops, and Velvet's changes nothing either.
- **`.flex` and `.grid` set `flex-direction: row` alongside `display`**, so a direction utility never
  displaces them — it holds only part of what they write — and takes the direction from them by
  declaration order instead, both being declared before all four.

## 2. `gap-*` is a framework-level CSS-`gap` polyfill (no USS rules)

No USS rule realises `gap-*`: a per-container `StyleGapManipulator` writes the inter-child
**leading** margin — `margin-left` for a row, `margin-top` for a column — on every child **except the
first**. The result is spacing strictly **between** children, like CSS `gap`: no leading, trailing, or
outer-edge margin. `gap-x-*` is CSS's `column-gap` and `gap-y-*` its `row-gap`, and `gap-*` sets both. A
single-line row is spaced by the column gap and a single-line column by the row gap; the other one only
separates wrapped lines (see "`flex-wrap` and `grid`" below). A `display: none` child (`hidden`) has no box,
so it takes no gap and does not count as the first. The `:first-child` / `:last-child` selectors are the
`first:` / `last:` variants — see [styling-variants.md](styling-variants.md).

| Utility | CSS | Effect without wrap |
|---|---|---|
| `gap-*`   | `gap` | the leading edge of the main axis (`margin-left` in a row, `margin-top` in a column) — or the trailing edge (`margin-right` / `margin-bottom`) on a reversed container, see "Reversed containers" below |
| `gap-x-*` | `column-gap` | `margin-left` between the items of a row, or `margin-right` on a `flex-row-reverse` container; nothing in a column |
| `gap-y-*` | `row-gap` | `margin-top` between the items of a column, or `margin-bottom` on a `flex-col-reverse` container; nothing in a row |

```csharp
// Horizontal spacing between columns, no trailing gap after the last item.
V.Div(className: "flex flex-row gap-x-4", children: ...);

// Vertical spacing between rows.
V.Div(className: "flex flex-col gap-4", children: ...);
```

The numeric scale (`gap-0-5`, `gap-1`, `gap-1-5`, `gap-2`, … mapping to the `--space-*` tokens,
1 unit = 4px) is Tailwind's; the classes are recognized in C#, not by USS selectors — see
`Runtime/Styling/StyleGapManipulator.cs` and `Runtime/Styling/StyleGapClass.cs`.

Tailwind's `space-x-*` / `space-y-*` (and their negative forms, `-space-x-4`) take the same scale and the
same manipulator, but they are Tailwind v4's margin rule rather than CSS `gap`: `margin-right`
(`margin-inline-end`) / `margin-bottom` (`margin-block-end`) on every child **except the last** — an
absolutely positioned child counts as the last, as it does for `:last-child` — whatever the container's
direction, and on a wrapping container too rather than the wrap strategy below. A gap and
a space on one element both apply, as they do in Tailwind: on an edge both write, the two add up.

Tailwind writes `space-*` and `divide-*` at zero specificity (`:where()`), so a class of the child's own
that sets the same edge wins there: `mr-2` or `mr-[5px]` on a child of a `space-x-4` row keeps its own
right margin, and `border-r-4` or a `border-red-500` on a divided child keeps its own width or color. An
important divide width or color (`!divide-x-4`, `divide-gray-200!`) is drawn over the child's own instead,
as the `!important` Tailwind emits for it does, unless the child's own is important too
(`!border-r-[3px]`), which wins as its higher specificity does in CSS. A gap is not a margin in CSS and
adds to one instead; see the residual edge cases below.

### Reversed containers (`flex-row-reverse` / `flex-col-reverse`) and `space-*-reverse`

A `flex-row-reverse` / `flex-col-reverse` container moves a gap's margin to the axis's **trailing**
physical edge (`margin-right` / `margin-bottom`) instead of the leading one (`margin-left` /
`margin-top`), which keeps it between the visually adjacent pair. The flip is per axis: a `gap-x-*`
never reacts to `flex-col-reverse`, and a `gap-y-*` never reacts to `flex-row-reverse`. A gap never
reads a `space-*-reverse` marker.

A `space-*` margin never reads the direction. It stays on the end edge until its own axis's marker moves
it to the start edge (`margin-left` / `margin-top`): on a reversed row Tailwind writes
`flex-row-reverse space-x-4 space-x-reverse`, and so does Velvet.

### `divide-x-*` / `divide-y-*` follow the space rule

`divide-x-*` / `divide-y-*` draw a **border** between adjacent children — Tailwind v4's
`:where(& > :not(:last-child))` divider — and UI Toolkit has no `:last-child` and no `> *` child
combinator either, so Velvet realizes them through the same kind of per-container manipulator
(`StyleDivideManipulator`): the border goes on every child **except the last**, on the end edge, as a
`space-*` margin does. `divide-x-reverse` / `divide-y-reverse` move it to the start edge, and nothing
else does: the container's direction is never read. The axis is always fixed by the class (`divide-x`
is horizontal, `divide-y` is vertical).

| Utility | Axis | Effect |
|---|---|---|
| `divide-x-*` | always horizontal | `border-right` (`border-inline-end`), or `border-left` with `divide-x-reverse` |
| `divide-y-*` | always vertical | `border-bottom`, or `border-top` with `divide-y-reverse` |

A lone `divide-x-reverse` does nothing on its own — like `divide-{color}`, it needs a `divide-x` /
`divide-y` to give it a width to move. Because a divider is a real border, the edge it lands on carries
its share of the box model, and a `divide-{color}` colors all four edges of a divided child, as
Tailwind's `border-color` does. Widths on the other three edges are left alone, so a child's own `border-b`
under a `divide-x` row is preserved and takes the divide color. Unlike gap, divide has no
wrap-specific strategy, and CSS has none either: the rule selects by sibling position, so under
`flex-wrap` every child but the last gets its one divider edge whichever line it sits on. The last child
of a wrapped line therefore keeps its end edge, and no rule is drawn along the break between two lines.

`divide-dashed` / `divide-dotted` have no UI Toolkit border-style, so Velvet paints those rules
itself, which costs them on a child that also carries `overflow-hidden` — see the painted-utility
table in [styling-variants.md](styling-variants.md).

### How re-spacing stays correct

Everything below is written for `gap-*`. The divider manipulator re-applies on the same three events,
though it has no direction to resolve.

The spacing depends on the child set and, for a gap, on the resolved direction — which every gap axis
needs, not just plain `gap-*`'s axis choice, since a `gap-x-*` / `gap-y-*` has a reversed-edge flip too.
Both can change outside the manipulator's own events, so it is re-applied from three sources:

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

**A child that leaves keeps whatever its new container wrote.** A container also clears what it wrote
on a child that is no longer in it, so a child *reparented* elsewhere carries no leftover gap. Because
the three sources above land on their own schedules, the container a child left can re-apply *after*
the container it joined has already spaced it — and the element pool makes that ordinary, handing a
child pooled out of one container straight to another. So the clear only fires while the value on the
child is still the clearing container's own: two `gap-4` rows exchanging a child leave it spaced by
the row it is in. `grid-cols-*` shares that answer with `gap-*` — both write a child's margins, and
the grid its width as well; `divide-*` keeps its own for the border edge it owns.

A child the reconciler *removes* gives back every margin, width and divider edge a gap, grid or divide
container wrote on it, and every `[&>*]:` payload its container applied, while its cleanup runs, so an
element your own code kept a reference to carries none of them when it is re-parented.

**What a container stops spacing goes back to the child's own layers, not to nothing.** Gap, grid and
divide write their value straight onto the child, while an arbitrary value — the child's own `ml-[2px]`
or a container's `[&>*]:ml-[2px]` — reaches the same slot through a layer. While a gap or grid spaces a
child its value holds, even when such a layer changes afterwards, whereas a space margin or a divider gives
way to such a layer for as long as one is there; where a container stops — the gap is dropped,
the child leaves, an edge is abandoned, or the child is the first of a gap row or the last of a space or
divide row and takes nothing there — the slot is given back and the child shows what its layers say there. A grid holds its
first column's and first row's zero margins rather than giving them back. So `flex flex-row gap-x-4 [&>*]:ml-[2px]` gives its
first child `2px` and the rest the gap, and every child `2px` once `gap-x-4` goes; and a child moving
between a `[&>*]:` row and a `gap-*`, `grid-cols-*` or `divide-*` row keeps what the row it is in gave
it, whichever of the two re-applies last.

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

A class string only reaches the element it is written on, so no direction class can land on an inner box:
unless an inline value is set on that box, its verdict comes from the `resolvedStyle` fallback below,
over whatever the widget's own built-in USS gives it. Off-panel an inner box falls back to the
**engine's** default (column) rather than to `.flex`'s row, which for most widgets makes the off-panel
answer equal the on-panel one.

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

**Direction source: an inline `flex-direction`, else a single resolved verdict from the class list by
USS precedence, and `resolvedStyle` only after both — on a panel included.** `flex` / `flex-row` / `flex-col` / `flex-row-reverse` /
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
element's gap and space through the separate grid manipulator instead — see "`flex-wrap` and `grid`"
below.

After an inline value, classes are consulted before `resolvedStyle`, even on a panel: the direction
classes are USS-only rules with no equivalent C# inline `flex-direction` write, so
`resolvedStyle.flexDirection` only catches up after the panel's *next* style pass, and a same-rect
direction toggle (children reorder; the container itself never resizes) fires no `GeometryChangedEvent`
to trigger a re-derive. A manipulator that trusted `resolvedStyle` here could converge on the *first*
toggle and then never converge on a later toggle back, leaving a gap margin wrong indefinitely.

An inline `flex-direction` is read before the classes, and a direction class outranks `flex-direction`
set by a custom stylesheet rule on the SAME element rather than composing with it. `resolvedStyle` is
read only as the fallback for the case neither can cover: a stylesheet-set `flex-direction` with *none*
of the five direction/display classes present on the element at all. That fallback case still needs a
live panel (`AttachToPanelEvent` above) and still cannot self-correct on a same-rect toggle with no
intervening reconcile pass, since nothing would tell the manipulator to look again. The direction is read
whenever the manipulator applies — at the container's reconcile or patch, on attach, or at a geometry
change, among others — and writing an inline `flex-direction` does not itself re-apply the spacing. Once
every direction/display class leaves the element, the verdict until the next `GeometryChangedEvent` is
the inline `flex-direction`, or the engine's column when none is set, rather than a `resolvedStyle` that
can still hold the removed class — the same rule the wrap verdict below follows. With no direction class
AND no panel to resolve against (EditMode, pre-attach), the default is **row** — the one place this
deliberately disagrees with the raw engine, whose own unstyled default is column (see "Without `.flex`,
children stack vertically" above).

## `flex-wrap` and `grid`: both axes are spaced (half-margin hybrid)

CSS `gap` under `flex-wrap` spaces **both** axes — between items in a line *and* between wrapped
lines. A single leading-edge margin can only space the main axis, so for a `gap-*` the manipulator
switches strategy when the container wraps (a `space-*` keeps its single margin, as in Tailwind):

| Container | Strategy | Children | Container |
|---|---|---|---|
| non-wrap (common) | leading margin | `gap` on the leading edge of all-but-first child | none |
| `flex-wrap` | half-margin | half the column gap on the left and right of **every** child, half the row gap on its top and bottom | the same halves, negated |

Under wrap, any two adjacent items (either axis, including across wrapped lines) are separated by two
halves of that axis's gap, and the container's negative margin cancels the children's outer-edge
half-margins so content stays flush to the container edge. A reversed container (e.g.
`flex-row-reverse flex-wrap`) still uses this same symmetric half-margin polyfill — direction never
changes which edges wrap spaces, only non-wrap's single leading/trailing edge choice.

Wrap is read from the same element the direction is (see "Which element the verdict is read from" above),
and in the same shape, for the same staleness reason: an inline `flex-wrap` first, then the `flex-wrap` /
`flex-nowrap` / `flex-wrap-reverse` class markers (by `_layout.uss` declaration order —
`flex-wrap-reverse` beats `flex-nowrap` beats `flex-wrap` when more than one is present), then
`resolvedStyle.flexWrap` whenever none of those three is present. Unlike the direction scan there is no
further "a direction class implies a default" tier: `flex` / `flex-row(-reverse)` / `flex-col(-reverse)`
say nothing about `flex-wrap`, and since nearly every real container carries one, counting them as
evidence of no-wrap would misread a genuinely wrapping inline-styled container.

Removing a wrap class needs no `flex-nowrap` to take its place: until the next
`GeometryChangedEvent`, a container whose marker has just gone answers from its inline `flex-wrap`, or
no-wrap when none is set, rather than a `resolvedStyle.flexWrap` that can still hold the removed class,
so a fixed-size container stops writing its own negative margin on the patch that drops `flex-wrap`.
After that event the fallback reads `resolvedStyle` again, which is how a wrap a stylesheet rule sets
comes back.

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
child margins it wrote before the arriving one writes its own. A `space-*` on a grid is still the space
rule rather than a column gap: the grid writes it on its children beside the gap, and a horizontal one
comes out of the child's column width, as a CSS grid item stretched to its cell gives up its margins.

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
  is untouched. (Under the wrap half-margin path every side a gap spaces belongs to it, so an explicit child
  margin there is overwritten.)
- **Wrap path overwrites the container's own margin.** The wrap half-margin path writes the
  container's own margins to the negated halves, so an explicit container margin (e.g. `m-4` on the same
  element that carries `flex-wrap gap-4`) is **overwritten** while a wrapping gap is active, and comes
  back when it stops. Non-wrap containers never touch the container's own margin. Workaround: put the
  margin on an **outer wrapper** around the wrapping gap container.
- **Wrap outer bleed.** The wrap path's container negative margin (half of each gap) bleeds that half
  **outward**, overlapping the container's own siblings or its parent's padding by it. The
  half-margin trick has no way to cancel only the *inner* outer-edge halves; only native UITK `gap`
  avoids it. Non-wrap containers never bleed — they write no container margin. Add `gap/2` of padding
  on the parent, or wrap the grid, if the overlap matters.

## Proportional splits: `grow-N` / `shrink-N`

`grow` / `shrink` set a factor of 1, `grow-0` / `shrink-0` a factor of 0, and `grow-<N>` / `shrink-<N>`
any other whole number, as Tailwind's bare values do: a sidebar carrying `grow` beside a content pane
carrying `grow-3` divides the leftover space one to three. The bracket form takes a fraction too —
`grow-[2.5]` — and both land as inline style.

The value is a plain number. `grow-1.5`, `grow-02`, `grow-[50%]`, `grow-[2rem]` and `-grow-[2]` are not
recognized — a factor has no unit and no sign, the bare form is a whole number spelled as Tailwind
spells it, and the percent form especially would otherwise read as a factor of fifty.

`basis-[..]` and `w-[..]` are a different thing and do not substitute: they fix a size, where these
two divide what is left over after every sibling's basis is taken.

## A text item's margin, padding, wrapping and minimum size

**Margin, padding, wrapping and shrinking.** Tailwind v4's preflight zeroes every element's margin and padding,
and CSS starts every element at `flex-shrink: 1` and, `white-space` being inherited, at its parent's. Velvet's
`_preflight.uss` does it for the `Label` that a `V.Label`, a `V.Text` or `V.Custom<Label>` creates, by the
class `velvet-label` those labels carry, and gives them all four values (`white-space: unset`, which takes
the parent's, so a `whitespace-nowrap` on an ancestor still reaches the label). The sheet is imported ahead of every
utility, so a `p-*`, `m-*`, `whitespace-*` or `shrink-*` on the label ties with it on specificity and wins on
order. A label therefore wraps (unless an ancestor says otherwise) and shrinks by default, where a theme may
have given it `nowrap` and `flex-shrink: 0`. A `Button` Velvet creates gets the white-space rule alone (class
`velvet-button`), so its text wraps like a button's in CSS and keeps the theme's spacing. A label centred in a box therefore sits on the
box's centre. A `Label` UI Toolkit builds inside its own control (a `V.TextField`'s label, a `V.Toggle`'s)
and a `Button` keep the spacing the theme gives them, as an `<input>`'s label does on the web, where it is
author markup.

**Minimum size.** A CSS flex item cannot shrink below its content-based minimum size along the
container's main axis (CSS Flexbox §4.5): its min-content size, capped by its definite preferred size on
that axis, then by its maximum size. Velvet writes that value inline, as `min-width` in a row and
`min-height` in a column, on every `Label` and `Button` it creates:

- a row's min-content width is the widest run of text with no break opportunity in it: spaces
  separate runs (a no-break space does not), a hyphen between letters ends one, and each ideograph,
  kana or Hangul syllable is its own, bar closing punctuation sticking to the character before it. Under
  `whitespace-nowrap` and `whitespace-pre` it is the whole text;
- a column's min-content height is the height the text takes at the width the item was given;
- the item's own padding and border on that axis is added to either.

A declared `w-*` / `h-*` / `size-*` (class, bracket or inline) caps the minimum at that size, and a
`max-w-*` / `max-h-*` caps it at the maximum; `w-fit`, `w-min`, `w-max` and `w-auto` declare no definite
size and cap nothing. The `flex-shrink: 0` a reset rule for centred labels used to carry is replaced by this: a label in a row
still wraps at its widest run instead of refusing to shrink, and one in a column cannot be squeezed below its
text.

The item keeps whatever its cascade gives it when it is clipped (`overflow-hidden`, `truncate`), declares
its own `min-w-*` / `min-h-*` on that axis, sits in a `grid` column, has children, or is
absolutely positioned or hidden. A clipped item and a grid item have an automatic minimum of zero in CSS.

Not covered: a minimum the theme or a stylesheet of your own declares on a label is not seen, and the
automatic minimum is the inline value, so an element carrying a `transition-all` animates it. The widest
run is searched among the 64 longest distinct runs of a text, and a variant that lights `min-w-*`, `h-*`
or `overflow-hidden` outside a patch is read at the label's next patch.
