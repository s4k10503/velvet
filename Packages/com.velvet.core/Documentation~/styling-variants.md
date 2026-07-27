# Styling notes: Variants & container queries

Velvet's utility classes are Tailwind-inspired, and so is its **variant** syntax — the
`hover:`, `dark:`, `sm:`, `group-hover:`, … prefixes that apply a utility only in a given
state, theme, breakpoint, or relation. This page is the reference for the full variant set
and for **container queries** (`@container`), the CSS `container-type: inline-size`
equivalent that re-points responsive breakpoints at a specific element's width.

A variant token has the shape `<variant>:<payload>`, where the payload is an ordinary
utility — a USS class (`hover:bg-blue-500`) or an arbitrary value (`active:w-[200px]`). USS
class selectors cannot contain `:`, so these tokens are never written to the element's class
list; the reconciler routes each one to a **manipulator** that toggles the payload on and off
as the matching signal changes.

A payload may also be one of the utilities Velvet realises itself rather than through a USS rule.
Those need re-deriving when the variant toggles, and nearly all of them get it — see
[Payloads Velvet realises itself](#payloads-velvet-realises-itself) for the one family that does not
and for the class channels that are not variants at all.

A payload occupies one slot per `(priority, token)` pair. Declaring the same token literally and
behind a variant is therefore safe — in `gap-4 md:gap-4` the `md:` payload turning off leaves the
literal `gap-4` alone — and so is declaring it behind two variants of different precedence
(`dark:gap-4 md:gap-4`). What still shares a slot is the same token behind two payloads of the **same**
precedence. Declaring the base value once and letting
the variant override it (`gap-4 md:gap-8`) remains the idiomatic form.

> **Two shapes to watch for, both when several variants name the same utility from
> [Payloads Velvet realises itself](#payloads-velvet-realises-itself).** Ordinary USS utilities are
> unaffected by either, since the ranking above governs them.
>
> *Same token, no literal base* — `"dark:gap-4 md:gap-4"`, `"dark:shadow-lg has-[.sel]:shadow-lg"`.
> When the first of the two turns off, the class stays on the element exactly as described above, but
> stops being *driven*: the spacing disappears and the shadow stops painting while `gap-4` /
> `shadow-lg` is still sitting in the class list. A literal base is immune — `"gap-4 md:gap-4"` keeps
> its gap.
>
> *Same family, different values* — `"bg-white md:shadow-sm dark:shadow-lg"`. Which one wins is decided
> by the order the two signals happened to fire, not by the precedence table: go dark and then widen
> past `md` and you get `shadow-sm`; widen first and then go dark and you get `shadow-lg`. The end
> state is identical either way, so the same window can render differently depending on how the user
> got there.
>
> Both come from the same gap — these utilities have no USS property set for the ranking to work from —
> and the idiomatic `"gap-4 md:gap-8"` shape, one base plus one variant per element, avoids both.

## The variant set

| Family | Prefixes | Driven by |
|---|---|---|
| **State** | `hover:` · `focus:` · `focus-visible:` · `active:` · `checked:` | The element's own pointer / focus state (and `ChangeEvent<bool>` for `checked:`) |
| **Theme** | `dark:` | `VelvetTheme.IsDark` |
| **Responsive** | `sm:` · `md:` · `lg:` · `xl:` · `2xl:` | The resolved responsive-scope width (the panel root by default — see below) |
| **Relational (group)** | `group-hover:` · `group-focus:` · `group-focus-within:` · `group-active:` | A marked ancestor's (`group`) state |
| **Relational (peer)** | `peer-hover:` · `peer-focus:` · `peer-focus-within:` · `peer-active:` · `peer-checked:` | A marked previous-sibling's (`peer`) state |

```csharp
// State: a hover background and an active scale, layered over the base utilities.
V.Button(className: "bg-primary hover:bg-primary-600 active:scale-95", text: "Save");

// Theme: a dark-mode surface color.
V.Div(className: "bg-neutral-50 dark:bg-neutral-900", ...);

// Responsive: full-width below md, a fixed column from md up.
V.Div(className: "w-full md:w-[320px]", ...);
```

### Variants and the USS cascade

**A variant payload beats a base utility that writes the same USS properties, in either direction,
whatever order the bundled stylesheet declares them in.** `bg-white dark:bg-neutral-900`,
`w-full md:w-64`, `items-center md:items-start` and `flex flex-row md:flex-col` all do what they
read like.

Velvet cannot lean on the cascade for that. A payload is realized by adding the bare utility to the
element's live class list, and both it and the base are single-class selectors, so specificity ties
and USS breaks the tie by declaration order — which says nothing about which one the author meant to
win. So Velvet decides instead. Each element keeps a model of which class each priority layer wants,
where the priority is the one the variant's own precedence already defines (see **Precedence order**);
for every USS property, only the highest-priority class holding it stays on the element. The losers
come off, and go back on the moment they stop losing.

Four consequences worth knowing:

- **Same-priority classes still tie by declaration order.** Two base utilities on one property, or two
  payloads of the same variant family, are ranked by nothing, so the stylesheet decides. Marking one
  important (below) breaks the tie.
- **A payload only displaces a class whose properties it wholly covers.** A base utility writing
  something the payload does not keeps its place, and the two then settle their shared properties by
  declaration order. That is reliable where one set contains the other — `size-8 md:w-4` resolves the
  width correctly, because every utility is declared before the narrower ones its set contains — and
  unreliable where the sets merely overlap. `rounded-l` and `rounded-t` share one corner and neither
  contains the other, so `rounded-l md:rounded-t` can be a silent no-op. The important modifier does
  not rescue it: the base is still uncontained, so it still applies. Write one class, or use the
  arbitrary-value form.
- **A class Velvet does not ship is never ranked.** The property table is derived from the bundled
  stylesheets, so a class of your own carries no known properties and can neither displace another
  class nor be displaced by one. `my-card dark:my-card-dark` ties exactly as before, and marking
  either one important changes nothing — there is no property set to rank them by. Use the
  arbitrary-value form where the family has one, or compute the class string in C# and render exactly
  one member.
- **`has-[.foo]:` matches what is on the element, not what was written — a deviation from CSS.** On
  the web `:has(.foo)` tests class-attribute membership, and the cascade never removes a class from
  the DOM, so a `.foo` whose declarations all lost still matches there. Velvet suppresses the class
  itself, so the condition stops matching.

An **arbitrary-value payload** (`md:w-[320px]`, `hover:bg-[#fff]`) is applied as an inline style
rather than a class, and the two mechanisms agree: an inline layer outranked by a higher-priority
class stands down so the class shows through, and a class outranked by a higher-priority inline layer
comes off. `bg-[#fff] dark:bg-neutral-900` and `bg-white dark:bg-[#171717]` both work. The filter
family is the exception — filters compose rather than override, so a `filter` class and a
`blur-[6px]` layer both apply.

### Precedence order

Lowest first. Families are ordered by how strong and how deliberate the condition that activates them
is: a position among siblings is the weakest signal, the element's own interaction state the
strongest. Where members of one row also rank against each other, `<` shows that order; where they do
not, each still occupies a layer of its own, so turning one off never disturbs another.

| | Layer |
|---|---|
| 1 | The base utility |
| 2 | `[&>*]:` — a rule the container imposes on its children |
| 3 | Structural — `first:` · `last:` · `odd:` · `even:` · `[&:nth-child(N)]:` |
| 4 | Responsive — `sm:` < `md:` < `lg:` < `xl:` < `2xl:` < `supports-[…]:` |
| 5 | Theme — `dark:` |
| 6 | `has-[…]:` < `data-[…]:` / `aria-[…]:` |
| 7 | Relational — the `group-*` and `peer-*` states |
| 8 | Element state — `checked:` < `hover:` < `focus:` < `focus-visible:` < `active:` |
| 9 | The important band — rows 1–8 again, one level each, for anything carrying `!` |

A **stacked** variant (`dark:hover:bg-red`) layers at the higher of its two parts, so it sits above
either one alone.

### The important modifier

Prefix or suffix any utility with `!` (`!bg-red-500`, `bg-red-500!`) to raise it into the **important
band**, the class equivalent of CSS `!important`. It applies to a variant payload too
(`dark:!bg-red-500`, `hover:bg-red-500!`).

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
`lg` 1024, `xl` 1280, `2xl` 1536 (reference px). They are evaluated against a single resolved
**width source**: by default the panel root, so an unscoped tree behaves exactly like a
panel-width media query. The `@container` marker below changes which element supplies that
width.

### Relational variants (`group-` / `peer-`)

`group-*` reacts to a **marked ancestor**: add the `group` class to a container, and a
descendant's `group-hover:` payload toggles when that container is hovered. `peer-*` reacts to
a **marked previous sibling**: add `peer` to one element, and a later sibling's `peer-checked:`
payload toggles with that peer's checked state. Tailwind's **named** forms are supported, so
multiple groups / peers can coexist without cross-talk:

```csharp
V.Div(className: "group ...",
    children: new[]
    {
        // Tints only when THIS card (the group) is hovered.
        V.Label(className: "text-muted group-hover:text-foreground", text: "Title"),
    });

// Named group: scope the relation to "sidebar" so a nested group does not trigger it.
V.Div(className: "group/sidebar ...",
    children: new[] { V.Label(className: "group-hover/sidebar:text-on", text: "Item") });
```

> Note — there is no `disabled:` variant. UI Toolkit has no reliable "enabled changed" event
> to drive a manipulator, so disabled-state styling stays on the USS `:disabled` pseudo-class
> (the curated `disabled-*` utilities).

### Stacked variants

Variants **stack** like Tailwind's, and the order does not matter — `dark:hover:bg-red`
applies `bg-red` only when the theme is dark **and** the element is hovered, identical to
`hover:dark:bg-red`. A stacked leaf may itself still be a variant (`dark:hover:focus:…`),
nesting another gate. Stacking composes any of the families above (state / theme / responsive
/ relational), so `md:hover:`, `group-hover:dark:`, and similar combinations are all valid.

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
utilities — `gap-*` / `space-*`, `grid` / `grid-cols-*`, `divide-*`, `text-balance` — and the
wrapper-less paints — `skew-*`, `shadow-*` / `drop-shadow-*`, gradients (`bg-gradient-*` and its
`from-` / `via-` / `to-` stops), `animate-*`, and `border-dashed` / `border-dotted`. Each resolves at
mount and on every toggle in both directions, and the order they compose in is preserved on a toggle
just as on a render — so `className="gap-4 md:grid md:grid-cols-3"` is a gapped flex row below `md`
and a three-column grid (spaced by the grid, which owns its gap) from `md` up, and
`className="bg-white shadow-sm md:shadow-lg"` deepens its shadow from `md` up.

**`ring-*` is the exception: a ring variant is inert.** `focus:ring-2` toggles a class that draws
nothing. A ring is not a paint — UI Toolkit has neither `box-shadow` nor `outline`, so Velvet draws
the band on a wrapper element placed around the target, and only a reconcile pass may add or remove a
wrapper. Doing it from the pointer callback or breakpoint notification a variant fires in is not
available, and the alternative — wrapping every element that so much as mentions a ring variant, for
its whole life — is worse than the missing feature: the wrapper is layout-transparent for `flex-grow`
and `flex-shrink` only, so a percentage-width or stretch-sized inner would begin measuring against the
wrapper instead of the real parent. Declare `ring-*` literally, or compute the class in C# from your
own state so it reaches the element through the reconciler.

**Class channels that are not variants drive none of this.** `whileHoverClass`, `whileTapClass` and
`whileFocusClass`, the transient enter / exit classes an `AnimatePresence` play applies for the
duration of an animation, and the drag-and-drop channels (`whileDraggingClass`, `whileOverClass`,
`whileDragActiveClass`) all write their utilities straight onto the live class list without telling
the reconciler. So `V.Div(whileHoverClass: "shadow-lg")` toggles a class nothing paints — use
`hover:shadow-lg`. What these channels *do* carry is any utility backed by a plain USS rule —
`bg-red-500`, `opacity-50`, `px-4`, `border-red-500`, `scale-105`, `rotate-3`. Read that as the list in
the first paragraph above versus everything else, not as whole categories: `gap-4` is spacing and
`skew-x-6` is a transform, yet both are in that list and neither works here. A `V.Motion`'s resting
`variants` classes go through the reconciler and are unaffected.

**`[&>*]:` reaches the paints late, and inconsistently.** It is the only family whose payload is
spelled on the *container* rather than on the element it lands on, and a child is fully built before
the container applies it. The layout utilities still re-derive at mount, so `[&>*]:gap-2` spaces
immediately. The paints depend on something the child cannot be relied on to have: if the child
declares a variant-gated payload **of its own** — any one, `hover:gap-4` is enough — it was already
recorded at create and `[&>*]:shadow-lg` paints at mount; if it declares none, the same class paints
only from that child's next render. Do not lean on either outcome. Put the utility on the child, or
behind a variant the child declares itself.

## Container queries — `@container`

By default every responsive breakpoint (`sm:`/`md:`/…) is measured against the **panel root**
width. A container query re-points that measurement at a specific element, so the same
breakpoints respond to **that element's** width instead — the CSS `container-type:
inline-size` equivalent. This lets a component be responsive to the space it is *given* rather
than to the whole window, so the same component can sit in a narrow sidebar and a wide main
column and lay out correctly in each.

Mark an element as a responsive scope with the `@container` class. Its descendants' responsive
breakpoints then resolve against its width. Resolution walks up from each descendant to the
nearest `@container` ancestor; with none marked it falls back to the panel root, so adding
`@container` is purely additive — unscoped subtrees keep the original panel-width behavior
exactly.

```csharp
// This card is a responsive container. Inside it, md: means "the CARD is >= 768px wide",
// not "the window is >= 768px wide".
V.Div(className: "@container w-full",
    children: new[]
    {
        V.Div(className: "flex flex-col md:flex-row gap-4", children: ...),
    });
```

Reference the marker from code via `VelvetResponsive.ContainerClass` (its value is the literal
`"@container"`) rather than hardcoding the string — tooling such as the preview viewport
switcher applies it this way.

### When breakpoints resolve — attach-time binding

A container query is **structural**, like a real CSS container, so the binding has one caveat
worth knowing:

- A descendant binds its responsive **width source once, at the moment it attaches to the
  panel** — it resolves the nearest `@container` ancestor (or the panel root) then, and watches
  that element's width from then on.
- Adding or removing `@container` on an **already-attached** ancestor at runtime does **not**
  re-point descendants that are already attached. They keep the source they bound at attach
  until they re-attach.

The supported usage follows from this: put `@container` on the scope element **before its
subtree mounts**, or **re-mount the subtree** after toggling the marker. (The preview window's
viewport switcher does exactly the latter — it re-mounts the story after changing the canvas's
scope so the simulated width drives the breakpoints; see
[preview-tooling.md](preview-tooling.md).)

### `@container` vs. the panel-width default

| | Default (no marker) | `@container` scope |
|---|---|---|
| What `sm:`/`md:`/… measure | The panel root's width | The nearest `@container` ancestor's width |
| Analogy | A CSS media query | A CSS container query (`container-type: inline-size`) |
| When it binds | At descendant attach | At descendant attach |
| Effect of toggling at runtime | n/a | Needs a re-mount to re-point already-attached descendants |

See also [styling-flexbox-and-gap.md](styling-flexbox-and-gap.md) for the layout utilities the
examples above compose with.
