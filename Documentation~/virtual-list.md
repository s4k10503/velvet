# Virtualized lists

`V.VirtualList` renders a large collection into a `ScrollView`, keeping only the rows in and near the
viewport in the tree — the counterpart of react-window's `List`, and with `horizontal: true` of a
FlashList laid out as a row.

| Parameter | Meaning |
|-----------|---------|
| `items` | The source collection. Required. |
| `keySelector` | The key a row is found again by across range changes — see [Keys](#keys). Required. |
| `itemHeight` | Every row's height in pixels, which must be greater than 0 — or a function from an item's index to its height, react-window's `rowHeight` function. |
| `renderer` | Builds the node one item renders. Required. A `null` renders nothing for that item. |
| `overscan` | Rows rendered beyond each edge of the viewport. Defaults to 3; must not be negative. |
| `key`, `className`, `name` | The list's own key among its siblings, and the `ScrollView`'s classes and name. |
| `listRef` | A `Ref<VirtualListHandle>` that holds the list's handle while it is mounted — see [Scrolling to an item](#scrolling-to-an-item). |
| `horizontal` | Lays the items out in a row the list scrolls sideways, FlashList's `horizontal`. Defaults to `false`. |

In a horizontal list every height and vertical extent below is a width and a horizontal one: `itemHeight`
gives each item's width, the viewport's width decides the range, and the horizontal scroller is the one
`ScrollToItem` moves.

A null `items`, `keySelector`, `renderer` or height function throws `ArgumentNullException`; an
`itemHeight` of 0 or less and a negative `overscan` throw `ArgumentOutOfRangeException`.

## Sizing and the rendered range

The `ScrollView`'s content holds a spacer as tall as every item's height added up, so the scrollbar
covers the whole collection, and a container of the rendered rows placed at the offset of the range's
first item. Each row element is given its own item's height. A height function is asked for every item
each time the list renders.

The rendered range is the items the viewport overlaps at the current scroll offset, widened by
`overscan` on each side and clamped to the collection. An item ending at the viewport's near edge, or
starting at its far edge, is outside the visible range. It is recomputed when the scroll offset changes,
when the `ScrollView`'s geometry changes, and when the component holding the list renders it again —
which renders the range from the new `items` and `renderer`.

## Scrolling to an item

`listRef.Current.ScrollToItem(index, align, behavior)` scrolls the list the way react-window's `scrollToRow`
does, against the viewport height the list's last layout measured:

| `align` | Where the item goes |
|---------|---------------------|
| `Auto` (default) | Nowhere, if it is already in view; otherwise as little as brings it into view |
| `Smart` | `Auto` if it is already in view, otherwise `Center` |
| `Center` | Its middle at the viewport's middle |
| `End` | Its end at the viewport's end |
| `Start` | Its start at the viewport's start |

The existing `ScrollToItem(index, align)` overload keeps the default behavior. The three-argument
overload requires an explicit `behavior`:

| `behavior` | How the list gets there |
|------------|-------------------------|
| `Auto` (default) | As `Instant`: the DOM's `auto` follows an element's `scroll-behavior`, which a `ScrollView` does not carry |
| `Instant` | In one step |
| `Smooth` | Animated over 300 ms, eased in and out |

A change to the active scroller's value from outside the list cancels the current smooth scroll and
any target awaiting content layout. A range change that clamps the current value preserves the target.
A subsequent `ScrollToItem` replaces both the animation and deferred target, including when a
value-change handler calls it before the earlier request returns. Changing orientation or disposing
the list cancels them too.

No alignment scrolls past either end of the list. An item a render has just added is reached once the
list's content has been laid out for it. An item taller than the viewport counts as in view
while the viewport lies within it. An index outside the items throws `ArgumentOutOfRangeException`.
`listRef.Current.Element` is the list's `ScrollView`. The ref holds the handle from the list's mount to its
unmount, and lets go of it when a render gives the list another ref. A handle retained after disposal
does nothing when `ScrollToItem` is called.

## Keys

`keySelector`'s key is what a range change matches rows by, as `key` is for a React list.

- **A row whose key is still in the range is patched rather than remounted**, wherever its item moved, so
  it keeps its state — unless the renderer now returns a different kind of node for it. A key the
  renderer put on the node it returns plays no part in that match.
- **A key is any string.** It is compared only with the other keys of the same list, so the character
  `VNode.Key` refuses is accepted here.
- **A `null` key is no key**, the answer `V.List` gives the same selector: the row renders, and a range
  change finds it again by its item index.
- **Items sharing a key all render, told apart by their item index**: each keeps the row rendered at its
  own index while it stays in the range, including across a range change that takes the other out of the
  range or brings it back. A row follows its key to another index only once the item at its old index
  returns a different key, or the list no longer reaches that index.
  Two of them rendered in one range log a warning naming the key; where the earlier one's renderer
  returned `null`, the later one takes the key with no warning.

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
