# Styling notes: z-index stacking

Velvet's `z-*` utilities bring CSS `z-index` to `position: absolute` descendants, compared among
sibling elements that share one direct parent. Unity UI Toolkit has no `z-index` property (and no
flex `order` analog either) — paint order, pointer-pick order, and Yoga's own flex layout
placement are all tied to a single physical child list, so this is a framework-level feature, not
a USS rule.

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
**documented no-op** — see [Scope cuts](#scope-cuts) for why.

| Utility | Resolved z |
|---|---|
| `z-0` / `z-10` / `z-20` / `z-30` / `z-40` / `z-50` | Tailwind's fixed named scale |
| `-z-10` … `-z-50` | negated named scale |
| `z-[N]` / `z-[-N]` | arbitrary integer (the bracket carries its own sign) |

The named scale is fixed — `z-15` is not a thing, `z-[15]` is, mirroring real Tailwind. Each form
also accepts the important modifier (`!z-10`, `z-10!`, `!z-[5]`, `z-[5]!`) like any other utility;
the modifier itself is a no-op here — z-index is a physical relocation, not a style-cascade layer
`!important` arbitrates.

## How it works

Unity's `VisualElement.hierarchy` moves the same node in the Yoga layout tree that it moves for
paint — physically reordering children for stacking would also reorder their flex layout, and
would corrupt the reconciler's own diff of every *other* sibling on the very next render (the
reconciler assumes a live child's physical index tracks its logical one). So `z-*` never
physically reorders the declaring children list itself. Instead:

- The first `z-*` element of each sign (non-negative / negative) under a stacking parent lazily
  creates a **layer container** — a plain, reconciler-invisible `VisualElement` sized to the
  parent's own content box. The **front** layer (non-negative z) is always the parent's last
  child; the **back** layer (negative z) is always its *first* child, so it still paints behind
  the ordinary children even though it is created later.
- A z-marked element's real content relocates into its layer container, sorted by resolved z
  (mount order breaks ties), while a hidden, zero-footprint **placeholder** — a real, displayed,
  zero-size element (not `display: none`, which would drop it from the focus ring) — is left at
  its declared position so the reconciler, `first:`/`last:`/`odd:`/`even:`/`nth-child` structural
  variants, and Tab order all still see it there.
- Because the layer container is geometrically coincident with the stacking parent's own content
  box, a relocated `absolute` child's `left`/`top` (already parent-relative in UI Toolkit) resolve
  to the exact same on-screen position whether the child sits at its declared slot or inside the
  container — no coordinate re-projection needed.
- The mechanism reuses the same deferred-mount pipeline `V.Portal` already relies on (creating or
  growing a layer container only runs from the same post-reconcile-pass safe point a Portal mount
  resolves from) and the same reconciler-invisible-child convention the internal filter
  bounds-spacer uses, so the existing diff/patch code for ordinary children is unaffected.

## Scope cuts

- **In-flow `z-*` is a no-op.** The classic "overlapping cards with a negative margin and `z-10`,
  no `.absolute`" pattern needs `.absolute` too — reordering an in-flow child for paint would also
  reorder its Yoga layout position (there is no separate flex `order` to reorder instead), so
  Velvet does not attempt it.
- **`z-*` on `V.Motion` is also a no-op — now with a warning.** A Motion's create path never
  relocates it into a layer container: the element identity its own enter/exit tween is bound to
  must stay put, the same reason `shadow-*` and `clip-path-*` are already documented no-ops on a
  Motion. Wrap the Motion around a z-managed `Div` instead — an `AnimatePresence` keyed child built
  this way (the common "animated, top-most modal" shape) enters and exits against the *real*,
  relocated element for its whole lifetime, including a `PopLayout` exit's out-of-flow pin; a
  cancelled exit (the key re-added mid-animation) restores it the same way an ordinary, non-`z-*`
  presence child does. The `variants` enter/exit *classes* resolve against the wrapped Motion's own
  element — the same element its resting `variants[animate]` classes live on — so the wrapped shape
  animates identically to a direct Motion child. Style the Motion, not the wrapper, for anything
  that should animate with the variants: the wrapper itself does not fade with a variant swap.
- **Negative z never escapes the element's own parent's background.** UI Toolkit has exactly one
  paint traversal; a child can only paint after its own parent's background within that walk.
  Escaping "behind the parent" would mean hoisting the child to become the parent's own preceding
  *sibling*, which breaks containing-block/clipping semantics (an `overflow-hidden` parent would
  no longer clip it) — a genuine engine dead end, not a missing feature.
- **Comparison is sibling-scope only.** Velvet does not implement full CSS stacking-context
  formation or nesting (`opacity < 1`, `transform`, `filter`, `isolation`, … as stacking-context
  triggers elsewhere in the spec). Every z comparison is against the direct siblings sharing one
  immediate parent, matching the dominant real-world use case rather than full spec fidelity.
- **Tab order follows the declared position, not the layer.** The placeholder is a real Tab stop
  at the element's declared slot; Tab reaching it forwards focus into the relocated element, and
  Tab leaving the relocated element's own subtree redirects to the declared position's next
  sibling — so `z-*` never changes keyboard navigation order.
- **A resort preserves focus.** Every z transition (a mount-order tie resolving, a sign flip, a
  patch-time z change) detaches and re-inserts the real element — UI Toolkit clears
  `FocusController.focusedElement` the instant an element leaves its panel's visual tree, even for
  an immediate same-panel reattachment — so the relocation rescues and restores focus when the
  moving element (or a descendant of it) holds it, instead of silently dropping it mid-interaction.
- **`group-`/`peer-` mostly cross the layer boundary, with one gap.** A z-managed element's
  physical parent is its layer container, one hop different from its logical parent.
  `group-*:` ancestor lookups are unaffected (the container is a transparent hop on the way up).
  A `peer-*:` **consumer** that is itself z-managed still resolves an ordinary preceding `peer`
  source correctly (the search walks from the consumer's declared position, not its physical one).
  The reverse does not: a `peer-*:` **source** that is itself z-managed is not found by an
  ordinary consumer, because the source's placeholder — the only thing physically sitting among
  the consumer's preceding siblings — carries none of the source's marker classes. Give a z-managed
  peer source an ordinary (non-`peer`) wrapper if a consumer needs to react to it.
