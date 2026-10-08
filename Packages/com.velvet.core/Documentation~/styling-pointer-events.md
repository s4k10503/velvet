# Styling notes: pointer-events

`pointer-events-none` makes an element and everything inside it transparent to the pointer — CSS's
`pointer-events: none`, and uGUI's CanvasGroup with `blocksRaycasts` off. The element keeps painting, stays
enabled and keeps its place in the focus order; a pointer over it reaches whatever is behind it.
`pointer-events-auto` on a descendant takes that descendant, and what is inside it, back.

```csharp
// A full-screen vignette that never swallows a click meant for the screen below it.
V.Div(className: "absolute inset-0 pointer-events-none bg-black/40");

// A toast layer that lets clicks through everywhere except on the toast itself.
V.Div(className: "absolute inset-0 pointer-events-none", children: new VNode[]
{
    V.Div(className: "pointer-events-auto rounded bg-white p-4", children: new VNode[] { V.Text("Saved") }),
});
```

`FiberElementProps.Enabled = false` is the other half of a CanvasGroup, its `interactable`: it blocks
input too, but it also matches the `:disabled` rules (`disabled-opacity-50`), so the subtree looks
disabled. Neither utility touches it.

## What the two utilities do

UI Toolkit has no `pointer-events` property. Its counterpart, `PickingMode.Ignore`, takes only the element
it is set on out of hit testing — a child of an ignored element is still picked — so Velvet writes it down
the subtree itself, the way CSS inheritance would carry the property down.

- **`pointer-events-none`** sets `PickingMode.Ignore` on the element and on the elements below it in the
  element tree, down to any `pointer-events-auto`, a control's own internal parts included (a
  `ScrollView`'s scrollers, a `Toggle`'s checkmark). No hit test lands on any of them, so none lights its
  own `hover:` or `active:` payload while the pointer is over the element itself (an ancestor of a
  `pointer-events-auto` descendant still does while the pointer is over that descendant, below).
- **`pointer-events-auto`** stops the walk of an enclosing `pointer-events-none`: the element and its subtree
  keep the modes they had, until a `pointer-events-none` further down starts a scope of its own. The
  element is a target again, and what it receives bubbles up through the ignored ancestors as usual — a
  handler registered on one of them still runs, and each ancestor's `hover:` payload lights while the
  pointer is over the `auto` descendant, as CSS's `:hover` matches every ancestor of the hovered element.
  On an element with no `pointer-events-none` above it, `pointer-events-auto` changes nothing.
- **Both on one element:** an important one (`!pointer-events-none`) outranks an unmarked one wherever it
  sits in the class list; otherwise the later one wins.

Keyboard focus is untouched: a focusable element inside an ignored subtree is still reached by Tab and by
`Focus()`, as in CSS.

When the utility goes away — the class is removed, a variant's gate shuts, the element unmounts — every
element gets back the picking mode it had before. Some elements are `Ignore` before the utility arrives —
Velvet's component wrappers and z-layer containers, a `Slider`'s root — and those stay `Ignore`.

## Which elements it reaches

The walk follows the element tree as it is mounted, so:

- a `z-*` element hoisted into its layer container is covered with its siblings — the container sits under
  the same parent ([styling-z-index.md](styling-z-index.md));
- a `V.Portal`'s content is covered when the Portal's target is inside the subtree, and not otherwise,
  wherever the `V.Portal` call itself sits.

An element that joins the subtree later takes the mode at the end of the reconcile pass that mounts it —
or, for a render inside a batched update, at the end of that batch, before its layout effects run. That
holds for a tree mounted with `V.Mount`, or reached through a `V.Portal`, into an element of another
tree's subtree: the inserting tree's pass takes care of it, and the tree that owns the utility does not
have to render again. Rows a `V.VirtualList` renders as it is resized or scrolled join when the render
that places them ends. A part a control creates on its own between renders joins at whichever pass comes
next. An element moved out of the subtree by app code (a `refCallback` reparenting it) gets its own mode
back at the tree's next reconcile pass; an element that unmounts gets it back as it is torn down.

## Variants

Both utilities are re-derived on every toggle, like the other utilities Velvet realises itself
([styling-variants.md](styling-variants.md#payloads-velvet-realises-itself)), so `dark:pointer-events-none`,
`md:pointer-events-auto` and `group-hover:pointer-events-auto` take effect on the element as the gate opens
and come off as it shuts — on an element of a tree mounted or portalled into another tree's subtree too.

## Limits

- A `layoutId` member takes no pointer while it is drawn over its lead ([motion.md](motion.md)), across its
  whole subtree, and a `pointer-events-auto` inside that subtree does not take an element back meanwhile.
