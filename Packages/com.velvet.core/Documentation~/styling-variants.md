# Styling notes: Variants & container queries

Velvet's utility classes are Tailwind-inspired, and so is its **variant** syntax — the
`hover:`, `dark:`, `sm:`, `group-hover:`, … prefixes that apply a utility only in a given
state, theme, breakpoint, or relation. This page is the reference for the full variant set
and for **container queries** (`@container` with the `@sm:`…`@7xl:`, `@min-*:` and `@max-*:`
variants), Tailwind v4's `container-type: inline-size` equivalent: a variant that responds to
a specific element's width rather than to the viewport's.

A variant token has the shape `<variant>:<payload>`, where the payload is an ordinary
utility — a USS class (`hover:bg-blue-500`) or an arbitrary value (`active:w-[200px]`). USS
class selectors cannot contain `:`, so these tokens are never written to the element's class
list; the reconciler routes each one to a **manipulator** that toggles the payload on and off
as the matching signal changes.

A payload may also be one of the utilities Velvet realises itself rather than through a USS rule.
Those need re-deriving when the variant toggles — see
[Payloads Velvet realises itself](#payloads-velvet-realises-itself) for the ones that get it and for
the class channels that are not variants at all.

A payload occupies one slot per rule and token. Declaring the same token literally and
behind a variant is therefore safe — in `gap-4 md:gap-4` the `md:` payload turning off leaves the
literal `gap-4` alone — and so is declaring it behind two variants of different precedence
(`dark:gap-4 md:gap-4`), or behind two rules of one variant family (`nth-1:gap-4 nth-2:gap-4`). What
still shares a slot is the same token written twice behind the same variant. Declaring the base value once
and letting
the variant override it (`gap-4 md:gap-8`) remains the idiomatic form.

The utilities from [Payloads Velvet realises itself](#payloads-velvet-realises-itself) are ranked by
that same per-rule model directly rather than through the class list, since the class list
records only *whether* a class is present and these are read out of it as a family. Two consequences
worth knowing, both when several variants name one such utility:

- *Same token, no literal base* — `"dark:gap-4 md:gap-4"`, `"dark:shadow-lg has-[.sel]:shadow-lg"`.
  One of the two turning off leaves the other driving, so the spacing and the shadow stay.
- *Same family, different values* — `"bg-white md:shadow-sm dark:shadow-lg"` resolves by the
  precedence table, not by the order the two signals fired. `dark:` outranks `md:`, so both lit paint
  `shadow-lg` whichever way the window got there. Where the precedence table cannot separate them —
  two `data-[…]:` rules, two `has-[.class]:` rules — they order as Tailwind emits them, as the precedence
  order below describes, whatever order the className writes them in. Only rules that actually apply
  take part: a `lg:` rule below the breakpoint, a `peer-` rule with no peer, and a `[&>*]:` rule (which
  lands on the children) rank nothing on this element, so adding one never moves what it paints.
  A stacked variant is ranked by its own position too, which the precedence table below gives: so
  `"dark:hover:shadow-lg hover:shadow-sm"` resolves to `shadow-lg` while dark and hover both hold.

## The variant set

| Family | Prefixes | Driven by |
|---|---|---|
| **State** | `hover:` · `focus:` · `focus-visible:` · `active:` · `checked:` · `disabled:` | The element's own pointer / focus state (for `checked:`, its own value — whether the user changed it or a controlled `value:` prop did; for `disabled:`, whether it or any ancestor is disabled, which is what USS `:disabled` matches; `focus-visible:` also reads the last input to any panel, see [focus.md](focus.md#focus-visible-styling-and-state)) |
| **Theme** | `dark:` | `VelvetTheme.IsDark` |
| **Responsive** | `sm:` · `md:` · `lg:` · `xl:` · `2xl:` | The viewport's width, never a container's — see below |
| **Container** | `@sm:` … `@7xl:` · `@min-*:` · `@max-*:` · `@md/name:` | The width of the nearest `@container` ancestor, or of the nearest `@container/name` for a named variant — see [Container queries](#container-queries--container) |
| **Relational (group)** | `group-hover:` · `group-focus:` · `group-focus-within:` · `group-active:` · `group-disabled:` | A marked ancestor's (`group`) state; `group-disabled:` reads it on the same terms as `disabled:` above |
| **Relational (peer)** | `peer-hover:` · `peer-focus:` · `peer-focus-within:` · `peer-active:` · `peer-checked:` · `peer-disabled:` | A marked previous-sibling's (`peer`) state; `peer-checked:` and `peer-disabled:` read it on the same terms as `checked:` and `disabled:` above |
| **Structural** | `first:` · `last:` · `only:` · `odd:` · `even:` · `nth-N:` · `nth-last-N:` | The element's place among its parent's children. The element a tree is mounted into and a portal's target are parents too, and the children of theirs this tree did not render count as siblings |

```csharp
// State: a hover background and an active scale, layered over the base utilities.
V.Button(className: "bg-primary hover:bg-primary-600 active:scale-95", text: "Save");

// Theme: a dark-mode surface color.
V.Div(className: "bg-neutral-50 dark:bg-neutral-900", ...);

// Responsive: full-width below md, a fixed column from md up.
V.Div(className: "w-full md:w-[320px]", ...);

// Container: the same, answering the width of the @container around it rather than the window's.
V.Div(className: "w-full @md:w-[320px]", ...);
```

### Theme: the `dark:` variant, and the token set beside it

`dark:` is a class-level variant — it adds and removes its payload on the element carrying it. The
bundled stylesheet's semantic colours are not that. `_tokens.uss` declares them twice, a light set on
`:root` and a dark set on `.dark`, and a subtree resolves the dark set when it or an ancestor carries
that class.

Both answer to `VelvetTheme.IsDark`. `VelvetStyleUtilities.AttachTo` binds the element it attaches the
sheet to, which then carries `VelvetStyleUtilities.DarkThemeClass` (`dark`) exactly while the flag is
set, so one assignment moves the variants and the colours together:

```csharp
VelvetStyleUtilities.AttachTo(uiDocument.rootVisualElement);
VelvetTheme.IsDark = true;
```

A project that reaches the sheet from a scene reference rather than `AttachTo` (see
[setup.md](setup.md)) calls `VelvetStyleUtilities.BindThemeTo(root)` for the same binding, or drives
the class from its own code.

`bg-background` paints the page the rest of the semantic colours sit on, and those colours are opaque:
`bg-surface` inside `bg-surface` is one colour, not a brighter one. The tokens naming a strength
rather than a role — the `--color-white-*` ladder, `--color-overlay*`, `--color-shadow` — keep their
alpha and are declared once for both themes. A role colour that is a palette colour takes the palette's value:
`--color-primary` is `blue-600`, `--color-danger` `red-600`, `--color-text` `slate-900`, and the dark set's
`--color-background` is `slate-900` too.

To override a token, declare it in a stylesheet of your own attached after Velvet's: a `:root` rule
for the light set, a `.dark` rule for the dark one.

### Variants and the USS cascade

**A variant payload beats a base utility that writes the same USS properties, in either direction,
whatever order the bundled stylesheet declares them in.** `bg-white dark:bg-neutral-900`,
`w-full md:w-64`, `items-start md:items-center` and `flex flex-row md:flex-col` all do what they
read like.

Velvet cannot lean on the cascade for that. A payload is realized by adding the bare utility to the
element's live class list, and both it and the base are single-class selectors, so specificity ties
and USS breaks the tie by declaration order — which says nothing about which one the author meant to
win. So Velvet decides instead. Each element keeps a model of which class each priority layer wants,
where the priority is the one the variant's own precedence already defines (see **Precedence order**);
for every USS property, only the highest-priority class holding it stays on the element. The losers
come off, and go back on the moment they stop losing.

Four consequences worth knowing:

- **Two bundled base utilities that resolve through the stylesheet and write one property are ordered as Tailwind emits them.** The bundled stylesheets
  declare their utilities in that order — the property order Tailwind sorts by, then the candidate names
  compared with digits as numbers and ahead of letters — so the one Tailwind emits later wins whichever is
  written first: `w-px w-4` and `w-4 w-px` both give 1px, `p-1 p-px` gives 1px, `bg-red-500 bg-blue-500`
  gives red, and `flex hidden` hides. Marking one important (below) overrides the order.
  `StyleSheetTailwindOrderTests` fails when a rule's place in a sheet stops agreeing with it. The `anim-*`
  presets are the exception: the animation scheduler applies them to outrank the utilities they animate.
  One utility that resolves inline beside the bundled classes is ordered the same way ([An inline value and a stylesheet class](styling-arbitrary-lengths.md#an-inline-value-and-a-stylesheet-class)
  has the cases). Two utilities that both resolve inline are ordered as Tailwind emits them when they write one property
  ([Two inline utilities of one property](styling-arbitrary-lengths.md#two-inline-utilities-of-one-property)), when a
  shorthand meets its longhand in the spacing, size, inset, radius and border-width families
  ([styling-arbitrary-lengths.md](styling-arbitrary-lengths.md)), and among per-side border colours
  ([react-migration.md §4-1b](react-migration.md#4-1b-per-side-border-colors)); the families a C# resolver folds from
  the class list, the font facets (`font-light font-bold`, `not-italic italic`) among them, and the nine-slice
  utilities are put in Tailwind's order too ([the list](styling-arbitrary-lengths.md#utilities-a-c-resolver-folds-from-the-class-list)).
- **A payload displaces the properties of a class it covers, and only those.** A base utility writing something
  the payload does not keeps it: `size-8 md:w-4` resolves the width correctly, because Tailwind emits `size-8`
  ahead of `w-4`. A side radius that a higher layer takes one corner of keeps its other corner, which Velvet
  writes with the per-corner class of the same size: `rounded-l-lg md:rounded-t-lg` paints `rounded-t-lg` and
  `rounded-bl-lg`, whichever is written first and whether or not either is marked important.
- **A class of your own is ranked by the properties its rule sets.** The table of what a class sets is derived from
  the bundled stylesheets, so for a class it does not name Velvet reads the answer off a probe: an element carrying
  the class is set beside one carrying none, and a property whose computed value differs between them is one the class
  sets. `my-card dark:my-card-dark` then does what it reads like: while `dark:` holds, `my-card` comes off if
  `my-card-dark` sets every property it does. The probe is read once per class and panel, at the first panel update
  after a variant payload of a class of your own reaches an element, and the other classes of your own on that element
  are read then too, so until then the classes are ranked by nothing, as they were before. A class a play shows is
  not read. A declaration behind a pseudo-class does not count, since the probe is in no state, nor does one that
  matches the property's value without the class. A class starting `unity-` or `velvet-` is never probed, since
  the rules of UI Toolkit's own controls are theirs to apply.
- **`has-[.foo]:` matches what the className wrote.** A `.foo` written in a descendant's `className`
  matches while Velvet keeps it off that descendant's class list, as `:has(.foo)` matches on the web
  however the cascade ranks `.foo`'s declarations. A `.foo` that only a variant payload puts on the
  descendant (`hover:foo`) does not match, as `:has(.foo)` does not match a class the author never wrote.
  A `.foo` a play writes onto the descendant — a `V.Motion` pose's class, an enter or exit class — matches
  too, including while Velvet keeps it off the class list for a rule that outranks it, as `:has(.foo)`
  matches a class a script adds to the element.

An **arbitrary-value payload** (`md:w-[320px]`, `hover:bg-[#fff]`) is applied as an inline style rather
than a class, and the two mechanisms agree: an inline layer outranked by a higher-priority class stands
down on the properties the class sets, so the class shows through there (above `md`, `p-[12px] md:pt-6`
takes its top from `pt-6` and the rest from `p-[12px]`) — except on a margin `space-*` holds and a border
`divide-*` holds, where the arbitrary value keeps the property — and a class outranked by a
higher-priority inline layer comes off. `bg-[#fff] dark:bg-neutral-900` and `bg-white dark:bg-[#171717]`
both work. The filter family is the exception — filters compose rather than override, so a `filter` class
and a `blur-[6px]` layer both apply.

A per-axis scale keeps the uniform utility on its other axis: `scale-50 scale-x-75` resolves to
`(0.75, 0.5)`, and `scale-50 scale-y-75` to `(0.5, 0.75)`. A negated preset (`-scale-50`, `-scale-x-50`,
`-scale-y-50`) writes the negative factor on the same axes, mirroring the element, so `-scale-50 scale-x-75`
resolves to `(0.75, -0.5)`. A bare value is any whole number written without a leading zero, read as a percent
(`scale-33`, `scale-x-120`), as Tailwind reads it. Adding or removing a uniform preset
updates that fallback. Several uniform presets follow the bundled stylesheet's declaration order;
an inline uniform value supplies the fallback ahead of a plain preset. The priority rules above
still decide which classes and inline layers participate.

`origin-[…]` takes CSS `transform-origin`'s grammar, the underscore standing for a space as it does in
`shadow-[0px_2px_8px_#0004]` and `clip-path-[polygon(…)]`: `origin-[33%_75%]` is
`transform-origin: 33% 75%`, `origin-[left_20px]` and `origin-[bottom_left]` mix and reorder keywords the
way CSS allows, a single component leaves the other axis at 50% (`origin-[0px]` is the left edge's
middle), and a third component is the z length (`origin-[50%_50%_10px]`). A pair CSS declares invalid —
`origin-[top_20px]`, `origin-[left_right]`, a percentage z — is not recognised. There is no negative
form of the class, as there is none in Tailwind either; a minus goes inside the brackets.

### Precedence order

A tie on one property resolves the way Tailwind's generated CSS resolves it: the rule with the higher
specificity wins, and between rules of equal specificity the one Tailwind emits later does. A media or
feature query adds no specificity, so `md:w-[10px] hover:w-[20px]` on a hovered element wider than `md`
is 20 px wide; an attribute selector carries a pseudo-class's and is emitted after the states, so
`disabled:opacity-50 aria-[busy=true]:opacity-75` on a disabled, busy element resolves to 0.75.

Lowest first, each row in the order `<` shows. Two variant rules of one rank — `nth-1:` beside `nth-2:`,
two `data-[…]:` rules — keep their payloads apart, so turning one off leaves the other's standing. While
both hold, the one Tailwind emits later outranks the other, whatever order the className writes them in.
On every property an arbitrary value shares with another variant rule of its rank, the one emitted later
takes it (`hover:bg-red-500 hover:bg-[#f00]` paints `bg-red-500`, which sorts after the value) unless the
class is the later one on a margin `space-*` holds or a border `divide-*` holds; two classes settle it as
the second consequence above describes. The order is first by their variants' values (`data-[side=left]:`
before `data-[state=open]:`, `group-hover:` before `group-hover/card:`, `[&:first-child]:` before
`[&:nth-child(1)]:`), then by the first property they differ on in Tailwind's property order
(`hover:m-[4px]` before `hover:ms-[4px]` before `hover:mt-[8px]`), then by the candidate itself, digits read as numbers
(`hover:w-[10px]` before `hover:w-[20px]`).

| | Specificity | Layer |
|---|---|---|
| 1 | (0,1,0) | The base utility |
| 2 | (0,1,0) | `supports-[…]:` < responsive — `sm:` < `md:` < `lg:` < `xl:` < `2xl:` < container queries — every `@max-*:` < every `@sm:` … `@7xl:` and `@min-*:` |
| 3 | (0,1,0) | Theme — `dark:` |
| 4 | (0,1,0) | `[&>*]:` — the container's rule, on each child |
| 5 | (0,2,0) | Relational — the `group-*` states < the `peer-*` states |
| 6 | (0,2,0) | Structural — `first:` < `last:` < `only:` < `odd:` < `even:` |
| 7 | (0,2,0) | Element state — `checked:` < `hover:` < `focus:` < `focus-visible:` < `active:` < `disabled:` |
| 8 | (0,2,0) | `has-[…]:` < `aria-[…]:` < `data-[…]:` < `nth-N:` < `nth-last-N:` |
| 9 | (0,2,0) | Arbitrary selector — `[&:nth-child(N)]:`, `[&:first-child]:` and the other `[&:…]:` structural forms |
| 10 | | The important band — rows 1–9 again, one level each, for anything carrying `!` |

A **stacked** variant is one rule carrying every part: their specificities add, and it sorts by the
latest-emitted part, then the next, and so on. So `hover:focus:` is (0,3,0) and outranks every row
above; `dark:hover:` keeps hover's (0,2,0), outranks plain `hover:` and row 8 because `dark` is emitted
after all of them, ranks below row 9, and loses to `dark:focus:`, because `focus` is emitted after
`hover`; and `[&>*]:hover:` outranks the child's own `hover:`, the arbitrary variant being emitted last.

### The important modifier

Prefix or suffix any utility with `!` (`!bg-red-500`, `bg-red-500!`) to raise it into the **important
band**, the class equivalent of CSS `!important`. It applies to a variant payload too
(`dark:!bg-red-500`, `hover:bg-red-500!`). A `!` ahead of the variants (`!dark:bg-red-500`) leaves the class
inert, as Tailwind generates no rule for it.

Two rules:

- An important utility beats every non-important one on the same property, whatever their priorities.
  `!bg-red-500 dark:bg-blue-500` stays red in dark mode.
- Two important utilities fall back to the ordinary ladder. `!bg-blue-500 dark:!bg-red-500` is blue
  normally and red in dark mode, the same shape as the plain pair.

This is the escape hatch for a same-priority tie: `bg-white !bg-red-500` resolves red, where
`bg-white bg-red-500` resolves white. It cannot help where the ranking has nothing to work with: an
overlap that is not containment, and a class whose properties Velvet does not know, are decided by
declaration order with or without the bang.

### Responsive breakpoints

The responsive prefixes activate at Tailwind's default **min-widths** — `sm` 640, `md` 768,
`lg` 1024, `xl` 1280, `2xl` 1536 (reference px). They measure the **viewport**: the panel root, so a
tree behaves exactly like a panel-width media query, or for a layer or world-space portal's children
the panel the portal was declared on. Nothing a component writes changes that, an `@container` ancestor
included: a component that answers its own width writes the `@`-prefixed [container
variants](#container-queries--container) instead, and one element can carry both (`sm:p-2 @md:p-4`). Only tooling
that simulates a viewport re-points them, [as below](#simulating-a-viewport-velvet-viewport).

### Relational variants (`group-` / `peer-`)

`group-*` reacts to **marked ancestors**: add the `group` class to a container, and a
descendant's `group-hover:` payload toggles when that container is hovered. `peer-*` reacts to
**marked previous siblings**: add `peer` to one element, and a later sibling's `peer-checked:`
payload toggles with that peer's checked state. With several marked ancestors or previous siblings,
the payload holds while any one of them is in the state. A source already under the pointer, or already
holding focus, when a consumer mounts or moves lights the consumer at once; a source already pressed lights a
`group-active:` / `peer-active:` consumer only from its next press. A source that code outside a render adds,
removes or reorders is picked up at the panel's next scheduler update, while a `group` / `peer` class that code
adds to or removes from an element already in the tree is read at the next render. Tailwind's **named** forms are supported, so
multiple groups / peers can coexist without cross-talk:

```csharp
V.Div(className: "group ...",
    children: new[]
    {
        // Tints when this card, or any other unnamed group around it, is hovered.
        V.Label(className: "text-muted group-hover:text-foreground", text: "Title"),
    });

// Named group: scope the relation to "sidebar" so a nested group does not trigger it.
V.Div(className: "group/sidebar ...",
    children: new[] { V.Label(className: "group-hover/sidebar:text-on", text: "Item") });
```

### Stacked variants

Variants **stack** like Tailwind's, and the order does not matter — `dark:hover:bg-red`
applies `bg-red` only when the theme is dark **and** the element is hovered, identical to
`hover:dark:bg-red`. A stacked leaf may itself still be a variant (`dark:hover:focus:…`),
nesting another gate. Stacking composes any of the families above (state / theme / responsive /
container / relational), so `md:hover:`, `@md:hover:`, `group-hover:dark:`, and similar combinations
are all valid.

```csharp
// Underline on hover, but only in dark mode and only from md up.
V.Label(className: "md:dark:hover:underline", text: "Docs");
```

### Payloads Velvet realises itself

Most payloads are plain USS classes, and putting one on the element's live class list is the whole
job. A few utilities are not USS rules at all: UI Toolkit has no property for them, so Velvet builds
them from the class array the reconciler last reconciled — an array that never contains a variant's
payload, since `md:shadow-lg` is a variant token and `shadow-lg` is what it resolves to. Those
utilities have to be re-derived when the variant toggles.

**Re-derived, so the variant behaves exactly like a literal class.** The manipulator-backed layout
utilities — `gap-*` / `space-*`, `grid` / `grid-cols-*`, `divide-*`; the picking
Velvet writes down a subtree — `pointer-events-none` / `pointer-events-auto`
([styling-pointer-events.md](styling-pointer-events.md)); the wrapper-less paints — `skew-*`,
`shadow-*`, gradients (`bg-gradient-*` and its `from-` / `via-` / `to-` stops), `animate-*`, `border-dashed` / `border-dotted`, and `ring-*` /
`outline-*`; the inline font layer — `font-<family>`, `font-<weight>`, `italic` / `not-italic` and the
`font-[…]` forms; and the axes Velvet writes into the displayed string — `uppercase` / `lowercase` /
`capitalize` / `normal-case`, `underline` / `line-through` / `overline` / `no-underline`,
`whitespace-pre-line`, `leading-*`, with the wrap mode `text-balance` / `text-pretty` write onto the
text; and the sibling paint and flex order — `z-*` and `order-*` ([styling-z-index.md](styling-z-index.md)). Each
resolves at mount and on every toggle in both directions, and
the order they compose in is preserved on a toggle just as on a render — so
`className="gap-4 md:grid md:grid-cols-3"` is a
gapped flex row below `md` and a three-column grid (spaced by the grid, which owns its gap) from `md`
up, `className="bg-white shadow-sm md:shadow-lg"` deepens its shadow from `md` up,
`className="font-sans dark:font-mono"` swaps the family with the theme, and
`className="focus:ring-2"` shows a ring while focused and none otherwise.

**Where `ring-*` deviates from CSS.** UI Toolkit has neither `box-shadow` nor `outline`, so Velvet
draws the band on its own element, positioned over the ringed element and hosted as a hidden sibling
placed directly after it inside the same parent. The ringed element's own layout is untouched, and the
band takes that element's own paint position, so overlapping `-space-x-*` avatars each carrying
`ring-2 ring-white` occlude the previous one's band as they do on the web, and the order among several
bands on one parent is their elements' order rather than the order the bands happened to be attached —
two `focus:ring-2` siblings render the same whichever was focused first. A `ring-inset` band is not a
sibling: it is the element's own first child, stretched over its padding box, so it paints above the
element's background and under its children, as an inset box-shadow does. The reconciler and the child
variants skip it as they skip Velvet's other internal children.

A band is drawn by a class that names one, as in Tailwind v4: `ring` (1px), `ring-<n>`, `ring-[…]`, `outline`
(1px), `outline-<n>` and `outline-[…]`. `<n>` is any whole number of pixels written without a leading zero (`ring-3`),
as it is in `ring-offset-<n>` and `outline-offset-<n>`. `-outline-offset-<n>`, `-outline-offset-[…]` and a negative
`outline-offset-[…]` draw the outline that far inside the element's edge. A colour class alone (`ring-red-500`, `outline-white`) draws nothing, and
colours only its own family's band, so `outline ring-red-500` is an outline in the text colour. A band whose
family names no colour, or names `ring-current` / `outline-current`, is drawn in the element's text colour and
follows it as it changes. Velvet still
draws one band where CSS draws a ring and an outline together.

What the ringed element's own subtree would carry reaches the band as well. Its transform —
`translate-*`, `scale-*`, `rotate-*` about any `origin-*`, a transition or a `V.Motion` animating those,
a `layoutId` play — its opacity, its `invisible` / `visible` and its `hidden` are copied onto the band
at layout and once per frame, and an ancestor's carry element and band together. A ring on a
`V.Motion` renders like one on a `Div`.

A ring inside a `V.AnimatePresence` fades with its element's enter and exit: the band is the one paint
the scheduler samples the caster's opacity for each frame, because it is the only one hosted outside
the element the renderer applies that opacity to.

An ancestor's `overflow-hidden` clips the band, as CSS does. The ringed element's **own**
`overflow-hidden` does not — so `overflow-hidden rounded-full ring-2`, the avatar pattern, renders.

`skew-<n>`, `-skew-<n>` and `skew-[…]` shear both axes by one angle. They compose with `skew-x-*` and
`skew-y-*` as Tailwind's `skewX() skewY()` does, so x is also stretched by 1 + tan θx · tan θy. Each axis takes
the class Tailwind emits last: a per-axis class over `skew-<n>` whichever is written first and whatever their
signs (`skew-6 skew-x-12` and `skew-x-12 skew-6` both give 12° on x and 6° on y, `-skew-x-12 skew-6` gives −12°
on x), a class behind a variant over the base ones (`skew-x-12 hover:skew-6` gives 6° on both axes while
hovered), and an important one (`!skew-6`, `skew-6!`, `hover:!skew-6`) over every plain one (`!skew-6 skew-x-12`
gives 6° on both). A bare value is a whole number of degrees written without a leading zero, so
`skew-x-07` is declined, as Tailwind declines it.

**How `skew-*` is drawn.** A skewed element takes its skew as its own transform: Velvet writes the inline `rotate` and
`scale` slots with a scale and one 3D rotation whose projection onto the panel is the matrix CSS composes, the
individual `rotate` and `scale` properties and then `skewX() skewY()` (`R(θ) · diag(sx, sy) · K`, a negative scale
carried as a reflection in the scale's y). The rotate and scale come from whichever of these the element has, behind a
variant or not, and the slots are written again when any of them changes:

- a `rotate-*` or uniform `scale-*` class, read off the element's class list (`BundledClassTransform`) — a rule of your
  own stylesheet that sets `rotate` or `scale` on a skewed element is not read, since the inline composite outranks
  it;
- an inline rotate or scale, which is where `rotate-[…]`, `scale-[…]`, `scale-x-*` and `scale-y-*` land, and what an
  `animate-*` preset, a `V.Motion` channel or a `layoutId` move writes (`AuthoredTransform`).

UI Toolkit then draws the element sheared about the pivot `origin-*` names, with its text and descendants under the
same transform. The sheared face keeps its overhang under a hidden overflow (`SkewTransformPlaybackTests` measures it),
a `shadow-*`, a gradient and a dashed border are drawn under the transform with the rest of the face, and the angles a
`V.Motion` animates are written into the same slots.

**What a pointer picks of a skewed element.** The shape the transform draws, not the bounding rectangle of the
transformed box, so a click in a corner the shear leaves bare reaches what is behind the element. UI Toolkit
reads whether an element overrides `ContainsPoint` from its type, so Velvet creates the plain `VisualElement`, `Button`
and `Label` it builds for `V.Div`, `V.Button` and its labels as subclasses that do, and the shape each is picked by is
set when a `skew-*` class reaches it. An element of any other type, a `V.TextField` or a `V.Toggle`, is picked by its
rectangle. The shape is the box without its rounded corners.

**How a radius larger than its box fits.** A radius that does not fit its box, whether a `rounded-*` class, an
arbitrary value, a rule of your own or one your code writes, is scaled the way CSS scales it: once two adjacent
radii overlap on a side, every radius on the element shrinks by one factor. So `rounded-full` on a 330 × 34 box is a pill with 17 px ends, and
`rounded-r-[400px]` on a 75 px-tall tab ends in a half-disc. Velvet holds the fitted radii as inline style.
On an element that carries a `rounded-*` class or an arbitrary radius, the radius a corner is fitted from is the
one that wins the cascade there: a rule of your own that outranks the class on a corner is that corner's
declared radius, and a radius your code writes to `style` is scaled with the others and comes back when the box
no longer needs the fit. A keyword written there (`initial`) is left alone. A change of class under
`transition-all` animates between the fitted radii rather than the declared ones. An element with no such class and
no arbitrary radius is fitted all the same, from a radius your own stylesheet gives it or your code writes to `style`
(`border-radius: 400px` on a `.pill` rule). It is checked each time its size changes, first layout included, so a
radius that appears with no size change, a class swap or a write to `style` after layout, is fitted when the size next
changes.

**The wrapper-less paints under a hidden overflow.** UI Toolkit applies an element's own overflow clip to the
element's own painted content, and cuts it at the **padding** box, while CSS clips neither a box-shadow nor a border
that way. What matters is the resolved `overflow: hidden`, not the utility that set it: `truncate` sets it
alongside `white-space` and `text-overflow`. A paint that lies outside the padding box is therefore painted on a
sibling placed over the element and outside its clip (`FaceBorderOverlay`, `RingOverlay`), and the element's own
paint keeps what lies inside it:

| Utility | On an element whose overflow resolves to hidden (`overflow-hidden`, `truncate`, or an inline / USS `overflow: hidden`) |
|---|---|
| `shadow-*` | the outer layers are painted on a sibling over the element, and an `inset` layer (`shadow-inner`, `inset` in `shadow-[…]`) is drawn inside the padding box |
| `skew-*` (and a gradient on a skewed element) | the sheared face keeps its overhang |
| `border-dashed` / `border-dotted` | the outline lies in the border band, which the padding-box clip excludes, so it is painted on a sibling over the element |
| `divide-dashed` / `divide-dotted` | the rule on a clipped child is painted on a sibling over that child |
| `overline` | unaffected — the rule sits inside the content box |

A `shadow-*` takes ownership of the element's face: it suppresses the native background and border and repaints both
in its own generated content, which the padding-box clip would cut at the border. While the element clips, a
`shadow-*` leaves a solid border and its background to UI Toolkit and paints a dashed or dotted one, and its outer
layers, on a sibling placed over the element and outside its clip, so the border and the shadow are drawn whole, as
CSS draws them:

```csharp
// Border and the shadow intact.
V.Div(className: "shadow-lg overflow-hidden bg-white border-2 border-dashed border-black rounded-lg");
```

**Class channels that are not variants.** `whileHoverClass`, `whileTapClass`, `whileFocusClass` and a
draggable's `whileDraggingClass` are Framer Motion's gesture states, and apply their utilities as a payload
does, ranked above every rule of the element's own short of an important one — a utility the stylesheet
declares later, a bracket value such as `bg-[#00f]`, a divider's color, `StyleOverrides`, and a className
variant such as `hover:`, `focus:`, `disabled:` or `dark:hover:` — and above a play's classes but an exit's:
Framer's `variantPriorityOrder` protects a value a gesture state sets from the element's `animate` and variants
while the state is on, and not from its `exit`, so a play other than an exit leaves that property where the
gesture puts it, whatever its driver, and one starting during the state leaves it out. A Spring or Bezier
play still running as the state ends takes the property back from where it is shown, on the play's spring or
over its duration again, as Framer animates a value a gesture state lets go of back to its `animate`
target. Among themselves they rank as Framer's `variantPriorityOrder` does: `whileFocus`, then `whileHover`, then `whileTap`, the later outranking the earlier.
A droppable's `whileOverClass` and `whileDragActiveClass` apply theirs at `data-[…]:`'s rank, after every rule
the className declares with that variant alone, and a play outranks them. An important utility outranks every
one of these channels. The element's own value comes back when the state goes off. Bracket values apply through them, and so do `shadow-*`,
`ring-*`, `skew-*`, gradients, `animate-*`, `border-dashed`, `gap-*` and `uppercase`:
`whileHoverClass: "shadow-lg"` paints a shadow while hovered. The classes a play shows for the duration of
an animation — a `V.Motion` pose's, an `AnimatePresence` enter or exit class — apply the same set, and a
pose's class the play takes off for that duration stops applying until it is back: `EnterFromClass:
"gap-4 shadow-lg w-[40px]"` spaces, shadows and sizes the element until the enter swaps it out.

**`[&>*]:` on a UI Toolkit composite reaches no part of the control.** `& > *` selects the children a tree
placed, so the walk is over whatever the container redirects its children into. A `V.ScrollView` redirects, so
the payload reaches the children reconciled into it. A control that redirects nothing, such as `V.TextField`, holds
only the parts it built for itself (its label and `#unity-text-input`), and a literal `[&>*]:` token leaves
them alone. A field's input box takes the utilities the next paragraph lists from the field's own class list.

**A field control's own surface utilities paint its input box.** On an `<input>` or a `<select>` the class
lands on the box the value is shown in; UI Toolkit draws a field as an outer control around a child box (the
element carrying `unity-base-field__input`, `#unity-text-input` in a text field) that the theme dresses. So
the field factories — `V.TextField`, `V.IntegerField`, `V.DropdownField` and `V.Custom<T>` for a `T` that is
a text-input field (`FloatField`, `DoubleField`, `LongField`, …) or a popup field (`PopupField<T>`, …), and
`V.Motion` for such an `elementType` — send these to the box:

- backgrounds, including the gradient utilities (`bg-*`, `bg-linear-*`, `from-*` / `via-*` / `to-*`) and
  the nine-slice ones (`slice-*`, [styling-backgrounds.md](styling-backgrounds.md));
- borders, including the line style (`border`, `border-*`, `border-solid` / `-dashed` / `-dotted`);
- radius (`rounded`, `rounded-*`) and padding (`p-*`, `px-*`, `py-*`, `pt-*` … `pe-*`);
- `shadow-*`, `ring-*` and `outline-*`;
- the filter utilities, `drop-shadow-*` and `filter-[…]` among them
  ([styling-filters.md](styling-filters.md));
- the text colour (`text-<color>`, `text-[<color>]`) and `opacity-*`;
- the text styling: `font-*`, `italic`, the text size (`text-sm`, `text-[14px]`), the alignment
  (`text-left` / `-center` / `-right`), `tracking-*`, and the text effects (`uppercase`, `underline`,
  `leading-*`, `whitespace-*`, …), and `truncate`, `text-ellipsis` and `text-clip`, which also land on the
  element that draws the input's text, with `overflow-hidden` on it and on the box, as an `<input>` always clips;
  an unfocused field elides that text (`TruncatedFieldEllipsisPlaybackTests`);
- the transforms (`translate-*`, `rotate-*`, `scale-*`, `skew-*`, `origin-*`) and `animate-*`;
- the caret and selection colours, `caret-*`, `selection:bg-*` and `selection:text-*`.

The transition utilities (`transition-*`, `duration-*`, `ease-*`) go to both, so a change on either element
tweens.

**A text input's caret and selection colours follow CSS and Tailwind.**
- **Values:** each utility takes a palette colour, `white`, `black`, `transparent`, a bracketed value
  (`caret-[#f00]`) or an opacity modifier (`caret-red-500/50`). `caret-current` follows the input's text
  colour as it changes, and `caret-current/50` takes it at half its alpha.
- **Precedence:** where two utilities compete, the one Tailwind emits last wins, whatever order the
  classes were added in: `caret-red-500 caret-blue-500` and `caret-blue-500 caret-red-500` both give
  red, and `caret-inherit` beats `caret-blue-500` either way. A value that does not parse is skipped.
- **`caret-*` inherits:** a field with none takes the nearest ancestor's, as `caret-color` inherits, so
  only the classes of the nearest element carrying one compete. `caret-inherit` winning there passes
  the question to that element's parent.
- **`selection:*` reaches from every ancestor:** Tailwind writes it as `& *::selection, &::selection`,
  so the field's and every ancestor's compete at one specificity, and an ancestor's
  `selection:bg-red-500` beats the field's own `selection:bg-blue-500`. `selection:bg-inherit` winning
  takes the parent's selection colour, which the classes from the next element up decide.
- **Removal:** a colour a utility set goes back, when no utility asks for one any more, to the theme's
  colour if the theme declares one, or else to the colour the field was built with.

UI Toolkit paints the selection highlight over the selected glyphs. So while a selection utility applies
and the field holds focus and a non-empty selection, the selected text is drawn again above the
highlight, in the `selection:text-*` colour or the field's own at its own alpha. Over an opaque
`selection:bg-*` the highlight then reads as a background the way `::selection` does. Over a translucent
one, the field's own glyphs stay under the highlight and show through it beneath the redrawn text,
which a browser does not paint.

`V.TextField(className: "w-64 bg-slate-800 rounded-lg px-3")` sizes the outer control and paints the box;
layout, size and margin utilities, and every utility not listed, stay on the outer control. A declared
`label:` is left unpainted. `whileHoverClass` / `whileTapClass` / `whileFocusClass`
send their surface classes to the box the same way.

Where a variant's condition lives decides who evaluates it:

- `hover:`, `focus:`, `active:`, `focus-visible:`, `dark:` and the responsive variants are the
  box's own, so `focus:border-blue-500` follows focus on the box, as it does on an `<input>`.
- `group-*` and `peer-*` look for their source from the control: the peer is a sibling of the control, not of
  the box, and the group an ancestor of it.
- `first:`, `last:`, `odd:`, `even:`, `has-[…]:`, `data-[…]:`, `aria-[…]:` and `supports-[…]:` are
  conditions on the control, so the control evaluates them and the box takes the paint.

The same holds when a container's `[&>*]:` payload lands on a field. A variant stacked behind one of the
control's conditions (`first:hover:bg-x`) is not routed to the box.

The cases that pin this are `InputBoxSurfaceTests` (the class list each factory builds, where each
payload and paint binding lands, and what teardown releases) and `InputBoxSurfacePanelTests` (the
sheet-attached resolved colours, the peer source and focus). They resolve the utility's colour from a
reference element carrying the same class rather than from a literal.

**`[&>*]:` reaches every direct child at once.** It is the only family whose payload is spelled on the
*container* rather than on the element it lands on, and a child is fully built before the container applies
it. Velvet records the class array of each element made or patched under a container carrying the token, so
the layout utilities, the paints (`shadow-lg`, `ring-2`, `skew-x-6`, gradients), the font layer and the string
axes (`font-mono`, `uppercase`) all land at mount for a child rendered as an element, whether or not the child
declares a variant of its own, and the same holds for a `V.Text` child.

## Container queries — `@container`

Mark an element a **query container** with the `@container` class, and its descendants can respond to
*its* width through the `@`-prefixed variants, as in Tailwind v4. That lets a component be responsive to
the space it is *given* rather than to the whole window, so the same component can sit in a narrow
sidebar and a wide main column and lay out correctly in each. The unprefixed `sm:`…`2xl:` are a separate
family and always measure the viewport, with or without a container around them.

```csharp
// This card is a container. Inside it, @md: means "the CARD is >= 448px wide", while md: still
// means "the window is >= 768px wide".
V.Div(className: "@container w-full",
    children: new[]
    {
        V.Div(className: "flex flex-col @md:flex-row gap-4", children: ...),
    });
```

Reference the marker from code via `VelvetResponsive.ContainerClass` (its value is the literal
`"@container"`) rather than hardcoding the string.

### The variants

| Variant | Applies while the container's width is |
|---|---|
| `@3xs:` `@2xs:` `@xs:` `@sm:` `@md:` `@lg:` `@xl:` `@2xl:` `@3xl:` `@4xl:` `@5xl:` `@6xl:` `@7xl:` | at or above 256, 288, 320, 384, 448, 512, 576, 672, 768, 896, 1024, 1152 or 1280 px |
| `@min-md:` | the same as `@md:`, for every name above |
| `@max-md:` | below `@md:`'s width, for every name above |
| `@min-[475px]:` · `@[475px]:` · `@max-[600px]:` | at or above, at or above, or below an arbitrary length |

The names are Tailwind v4's `--container-*` scale, at 16 px to a rem. An arbitrary length reads in `px`, `rem`, the absolute
units or a `calc()` that comes to one length. A bare number, a percentage, an `em` and a negative length are
not recognised, and the token stays an ordinary class.

A container can be **named**, `@container/main`, and a variant can name the container it asks for with
the same suffix: `@md/main:`, `@max-lg/main:`, `@min-[475px]/main:`. A named variant reads the nearest
ancestor marked with that name; an unnamed one reads the nearest ancestor marked `@container` under any
name, so a named container still answers the unnamed variants inside it.

- A variant with no container to ask **matches nothing**, `@max-*:` included. Mark a container, or use `sm:`
  for the viewport.
- A container is never queried by its own variants: an element carrying `@container` and `@md:` asks the
  container above it.
- Marking an element a container does not contain its size, as `container-type: inline-size` does in CSS, so
  give the container a width that does not depend on the content its variants change.
- A container's width is its content width, padding and border excluded, as CSS measures it: a `p-[20px]`
  container 460 px across is 420 px for its variants.
- A floor holds from its width up and a ceiling below its width, so `@md:` and `@max-md:` are on
  complementary sides of 448 px.
- Variants stack with the rest like any other: `dark:@md:`, `@md:dark:` and `@md:hover:`, either way round.
- Where a viewport variant and a container variant set the same property and both hold, the container
  variant wins, however they are written. Between container variants a floor beats a ceiling, a larger
  floor beats a smaller one, and a smaller ceiling beats a larger one. [Precedence order](#precedence-order)
  has the ranks.
- Tailwind's `@container-normal` is not recognised.

### When container variants resolve

A descendant resolves the container each of its variants asks for when it attaches to the panel, and
watches that container's width from then on. It resolves again whenever a render adds `@container` to an
element's `className`, removes it, or renames it (`@container/a` to `@container/b`), so toggling the
marker re-points the descendants already attached. A marker written straight to the class list
(`element.AddToClassList("@container")` or `RemoveFromClassList`) re-points them without a render: each
attached descendant with a container variant checks, once per animation frame, that the container it
resolved is still the nearest one.

### Simulating a viewport: `velvet-viewport`

Tooling that shows a story at a chosen device size inside a larger panel has a marker of its own,
`VelvetResponsive.ViewportClass` (`"velvet-viewport"`). An element carrying it stands in for the panel root: the
`sm:`…`2xl:` variants of its descendants measure *its* width. The preview window needs none, since it renders a
story on a panel of its own, sized to the simulated screen or to the story's footprint
([preview-tooling.md](preview-tooling.md#viewport)); an application that wants a component to answer its own width marks it `@container` and writes container variants.

### Migrating from `@container` re-pointing `sm:`…`2xl:`

An earlier version of Velvet made `@container` re-point `sm:`…`2xl:` at the nearest marked ancestor, and had no
`@`-prefixed variants. `md:` inside an `@container` now measures the window, so a tree written for that
writes the container variants where it relied on the container's width. `@3xl:` (768 px), `@5xl:` (1024 px) and
`@7xl:` (1280 px) sit at the widths `md:`, `lg:` and `xl:` did; for `sm:` (640 px) and `2xl:` (1536 px), which the
scale has no name for, write `@min-[640px]:` or `@min-[1536px]:`. Or take the scale's own thresholds, which
start lower: `@md:` is 448 px.

An `@container` with no `@`-prefixed variant under it does nothing, and markup copied from Tailwind that writes
`@md:p-4`, which used to leave an inert class, is now a container variant.

See also [styling-flexbox-and-gap.md](styling-flexbox-and-gap.md) for the layout utilities the
examples above compose with.
