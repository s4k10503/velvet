# Styling notes: z-index stacking

Velvet's `z-*` utilities bring CSS `z-index` to `position: absolute` descendants, compared among
sibling elements that share one direct parent. Unity UI Toolkit has no `z-index` property (and no
flex `order` analog either): paint order, pointer-pick order, and Yoga's own flex layout placement
are all tied to a single physical child list.

```csharp
V.Div(className: "relative w-64 h-64", children: new VNode[]
{
    V.Div(className: "absolute inset-0 bg-blue-500"),
    V.Div(className: "absolute inset-0 z-10 bg-red-500"),   // paints in front
    V.Div(className: "absolute inset-0 -z-10 bg-gray-200"), // paints behind the other two
});
```

## The scope gate: `absolute` + `z-*`

`z-*` only takes effect on an element that is **also** out of flow — either the `absolute` utility
class, or `V.Anchored` (which forces `position: absolute` itself). On an in-flow element `z-*` is a
**documented no-op** — see [Scope cuts](#scope-cuts). A `V.Motion` takes `z-*` the same way a `V.Div`
does.

| Utility | Resolved z |
|---|---|
| `z-auto` | none: the element is left unstacked, so `z-10 z-auto` resets the earlier `z-10` |
| `z-<N>` | a non-negative integer written without a leading zero (`z-0`, `z-10`, `z-15`), as Tailwind v4 takes it |
| `z-[N]` / `z-[-N]` | arbitrary integer (the bracket carries its own sign) |
| `-z-<N>` / `-z-[N]` | the negation of either form above |

Each form also accepts the important modifier (`!z-10`, `z-10!`,
`!z-[5]`, `z-[5]!`): an important `z-*` wins over every plain one on the same element wherever it sits
in the class list, and within either group the later class wins.

## How it works

`VisualElement.hierarchy` moves the same node in the Yoga layout tree that it moves for paint, and
the reconciler assumes a live child's physical index tracks its logical one — so `z-*` never
physically reorders the declaring children list. Instead:

- The first `z-*` element of each sign (non-negative / negative) under a stacking parent lazily
  creates a **layer container** — a plain, reconciler-invisible `VisualElement` sized to the
  parent's own content box. The **front** layer (non-negative z) is always the parent's last
  child; the **back** layer (negative z) is always its *first* child.
- A z-marked element's real content relocates into its layer container, sorted by resolved z and,
  among equal values, by declared position, as CSS stacks equal z in tree order (a keyed reorder of
  equal-z siblings re-sorts the layer once the pass ends), while a hidden, zero-footprint **placeholder** — a real, displayed,
  zero-size element (not `display: none`, which would drop it from the focus ring) — is left at
  its declared position so the reconciler, `first:`/`last:`/`odd:`/`even:`/`nth-child` structural
  variants, and Tab order all still see it there.
- The layer container is geometrically coincident with the stacking parent's content box, so a
  relocated `absolute` child's `left`/`top` (already parent-relative in UI Toolkit) resolve to the
  same on-screen position at its declared slot or inside the container — no re-projection.
- Creating or growing a layer container runs only from the post-reconcile-pass safe point a
  `V.Portal` mount resolves from, and the container uses the same reconciler-invisible-child
  convention as the internal filter bounds-spacer.

## Scope cuts

- **In-flow `z-*` is a no-op.** The classic "overlapping cards with a negative margin and `z-10`,
  no `.absolute`" pattern needs `.absolute` too: reordering an in-flow child for paint would move
  its Yoga layout position with it, and there is no separate flex `order` to reorder instead.
- **Negative z never escapes the element's own parent's background.** UI Toolkit has exactly one
  paint traversal; a child can only paint after its own parent's background within that walk.
  Escaping "behind the parent" would mean hoisting the child to become the parent's own preceding
  *sibling*, which breaks containing-block/clipping semantics (an `overflow-hidden` parent would
  no longer clip it).
- **Comparison is sibling-scope only.** Velvet does not implement CSS stacking-context formation
  or nesting (`opacity < 1`, `transform`, `filter`, `isolation`, … are stacking-context triggers
  elsewhere in the spec); every z comparison is against the direct siblings sharing one immediate
  parent.
- **Tab order follows the declared position, not the layer.** The placeholder is a real Tab stop
  at the element's declared slot; Tab reaching it forwards focus into the relocated element, and
  Tab leaving the relocated element's own subtree redirects to the declared position's next
  sibling — so `z-*` never changes keyboard navigation order.
- **A relocation preserves focus.** A z change within one layer re-sorts the layer without the
  element leaving the panel. Gaining or losing a z, and a sign flip between the front and back
  layers, move the real element to another parent, which detaches and re-inserts it — UI Toolkit
  clears `FocusController.focusedElement` the instant an element leaves its panel's visual tree,
  even for an immediate same-panel reattachment — so the relocation rescues and restores focus
  when the moving element (or a descendant of it) holds it.
- **`group-`/`peer-` cross the layer boundary.** A z-managed element's physical parent is its layer
  container, one hop different from its logical parent. `group-*:` ancestor lookups are unaffected
  (the container is a transparent hop on the way up). A `peer-*:` search walks declared siblings: a
  z-managed consumer searches from its declared position, and a z-managed `peer` source is found
  through the placeholder at its declared slot.
