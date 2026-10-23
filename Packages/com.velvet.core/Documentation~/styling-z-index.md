# Styling notes: z-index and paint order

Velvet paints an element's children in the order CSS paints siblings: unpositioned children first, in flex order;
then positioned children and those at `z-0`, in flex order; then positive `z-*`, by value. Negative `z-*` paints
first of all, behind every other child, by value. Unity UI Toolkit has no `z-index` or flex `order`: paint order,
pointer-pick order and layout order all follow one child list, so Velvet reorders that list and keeps each element's
layout and declared position where CSS has them.

```csharp
V.Div(className: "relative w-64 h-64", children: new VNode[]
{
    V.Div(className: "absolute inset-0 bg-blue-500"),
    V.Div(className: "absolute inset-0 z-10 bg-red-500"),   // paints in front
    V.Div(className: "absolute inset-0 -z-10 bg-gray-200"), // paints behind the other two
});
```

## Which children paint where

| Child | Paints |
|---|---|
| `-z-<n>` and the other negative forms | first, lowest z first |
| unpositioned (no `absolute`, `relative` or `V.Anchored`, z auto) | next, in flex order |
| positioned with z auto (`absolute`, `relative`, `V.Anchored`), and `z-0` | next, in flex order |
| `z-<n>` above 0 | last, lowest z first |

Equal places keep flex order, which is declared order unless [`order-*`](styling-flexbox-and-gap.md#flex-order)
says otherwise. `z-*` takes effect on any child, in flow or not, as it does on a flex item in CSS: an in-flow `z-10`
paints over the siblings declared after it and still lays out where it is declared. So does a `relative` card
followed by an unpositioned sibling, as in CSS, where an earlier positioned sibling paints over a later unpositioned
one.

| Utility | Resolved z |
|---|---|
| `z-auto` | none: the element is left unstacked, so `z-10 z-auto` resets the earlier `z-10` |
| `z-<n>` | a whole number spelled without a sign or a leading zero (`z-0`, `z-15`, `z-999`) |
| `z-[N]` / `z-[-N]` | arbitrary integer (the bracket carries its own sign) |
| `-z-<n>` / `-z-[N]` | the negation of either form above (`-z-15`, `-z-[5]`) |
| `z-[var(--z)]` / `z-(--z)` | the custom property's integer, restacking the element when it changes ([styling-custom-properties.md](styling-custom-properties.md)); unstacked, as `z-auto` leaves it, while it resolves to nothing, to `auto` or to no integer |

`z-05`, `z-1.5` and `z-+5` are not whole numbers as Tailwind spells them and leave the class unresolved. A value
beyond the 32-bit integer range stacks at that end of it, as CSS clamps it (`z-2147483648` stacks as
`z-2147483647`). Each form also accepts the important modifier (`!z-10`, `z-10!`, `!z-[5]`, `z-[5]!`): an
important `z-*` wins over every plain one on the same element wherever it sits in the class list, and within
either group the one Tailwind emits last wins (`z-20 z-10` gives 20).

A `z-*` or `order-*` behind a variant (`hover:z-10`, `md:z-10`, `group-hover:z-20`, `dark:order-first`) restacks the
element while the variant holds and puts it back when it stops, ranked against the base class as every
[variant payload](styling-variants.md) is. So does a position utility behind a variant (`md:absolute`).

## How it works

An element that paints away from its declared slot is **relocated within its own parent**: a hidden,
zero-footprint **placeholder** — a real, displayed, zero-size element (not `display: none`, which would drop it from
the focus ring) — keeps the declared slot, so the reconciler, `first:`/`last:`/`odd:`/`even:`/`nth-child`
structural variants, `peer-*:` searches and Tab order all still see it there, while the element itself is sorted to
where it paints:

- the **back band** — negative z — ahead of the declared slots;
- the **front band** — positive z, and the positioned children and `z-0` where an unpositioned sibling would
  otherwise paint over one of them — after them.

A relocated element that takes part in flex layout keeps its place in the row or column: Velvet lays it out at its
declared slot (or at the slot its flex order gives it) whatever position it paints from. That takes an engine member
([player-builds.md](player-builds.md)); where it does not resolve, an in-flow `z-*` element stays at its declared
slot, unstacked. An `absolute` element lays out against its parent as it does unrelocated, so moving it never moves
it on screen.

The element never leaves its parent: its events bubble through the parent as they would from its slot, it inherits
the parent's styles, and the parent's clipping applies. A reordering never detaches it from the panel either, so
gaining, changing or losing a `z-*` keeps its running transitions, its pointer capture and its focus.

A ring band (`ring-*`) stays directly after the element it rings, wherever that paints.

## Tab order

Tab order follows the declared position, not the band. The placeholder is a real Tab stop at the element's declared
slot; Tab reaching it forwards focus into the relocated element, and Tab leaving the relocated element continues
from its declared slot. A Tab move that the engine would land inside a relocated element where it paints goes on
past it instead, so an overlay declared before the content it covers is reached before the content, as in CSS. The
ring a [`V.FocusScope`](focus.md) contains or makes a single tab stop is the engine's own, and follows the bands. A
relocated element belongs to the scope that declares it, wherever it paints.

## Deviations from CSS

- **Comparison is sibling-scope only.** Velvet does not form CSS stacking contexts: every comparison is among the
  children of one parent, and a parent's subtree paints as one block at the parent's own place. In CSS a `z-10`
  inside a parent that forms no stacking context competes with that parent's siblings and their descendants.
- **Negative z never paints behind its own parent.** A `-z-*` child paints after its parent's background and border,
  ahead of the parent's other children. In CSS it paints behind a parent that forms no stacking context.
- **Absolutely positioned children take no flex order.** As in CSS, `order-*` on an `absolute` child changes
  nothing; its place among the positioned children is its declared one.
- **A parent that is neither `flex` nor `grid` gives `order-*` nothing to move.** As in CSS, the order of a child of
  such a parent is its declared one.
