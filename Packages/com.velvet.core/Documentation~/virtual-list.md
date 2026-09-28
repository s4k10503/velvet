# Virtualized lists

`V.VirtualList` renders a large collection of fixed-height items into a vertical `ScrollView`, keeping
only the rows in and near the viewport in the tree — the counterpart of react-window's fixed-size list.

| Parameter | Meaning |
|-----------|---------|
| `items` | The source collection. Required. |
| `keySelector` | The key a row is found again by across range changes — see [Keys](#keys). Required. |
| `itemHeight` | Every row's height in pixels, and the step the rendered range is computed in. Must be greater than 0. |
| `renderer` | Builds the node one item renders. Required. A `null` renders nothing for that item. |
| `overscan` | Rows rendered beyond each edge of the viewport. Defaults to 3; must not be negative. |
| `key`, `className`, `name` | The list's own key among its siblings, and the `ScrollView`'s classes and name. |

A null `items`, `keySelector` or `renderer` throws `ArgumentNullException`; an `itemHeight` of 0 or less
and a negative `overscan` throw `ArgumentOutOfRangeException`.

## Sizing and the rendered range

The `ScrollView`'s content holds a spacer `itemHeight × items.Count` tall, so the scrollbar covers the
whole collection, and a container of the rendered rows placed at the offset of the range's first
item. Each row element is given `itemHeight` as its height.

The rendered range is the items the viewport overlaps at the current scroll offset, widened by
`overscan` on each side and clamped to the collection. It is recomputed when the scroll offset changes,
when the `ScrollView`'s geometry changes, and when the component holding the list renders it again —
which renders the range from the new `items` and `renderer`.

Only a fixed item height and a vertical list are provided, and the list exposes no call that scrolls to
an item.

## Keys

`keySelector`'s key is what a range change matches rows by, as `key` is for a React list.

- **A row whose key is still in the range is patched rather than remounted**, wherever its item moved, so
  it keeps its state — unless the renderer now returns a different kind of node for it. A key the
  renderer put on the node it returns plays no part in that match.
- **A key is any string.** It is compared only with the other keys of the same list, so the character
  `VNode.Key` refuses is accepted here.
- **A `null` key is no key**, the answer `V.List` gives the same selector: the row renders, and a range
  change finds it again by its item index.
- **A key an earlier item of the rendered range already returned** logs a warning naming it, and the
  repeated item renders the way an item with a `null` key does. Where the earlier item's renderer
  returned `null`, the key is free and the later item takes it with no warning.

A row that leaves the range is unmounted: its effects clean up, and scrolling it back into the range
mounts it afresh, so state held in the row — hook state, or text typed into a field — does not survive
the round trip. Keep what must survive in a store or in the component that renders the list.

## Rows and their context

Rows render under the context that encloses the `V.VirtualList`, so a `V.Provider` above the list reaches
every row. An item's `refCallback:` has run by the time the range update that mounted it returns, though
that update is driven by a scroll rather than by a render.

A throw from the renderer, or from creating or patching a row's element, is not contained by the list: it
reaches the caller, the range update it ended releases the rows it had placed, and the list shows no rows
until the next range update. An error boundary above the list that catches a row's render during a range
update leaves none of the rows that update built mounted.
