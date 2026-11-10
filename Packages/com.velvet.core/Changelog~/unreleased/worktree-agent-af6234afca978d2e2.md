### Fixed

- A `V.VirtualList` row that stacks by a `z-*` class now paints where its z puts it among the other rows, and keeps
  doing so as the list renders its range again. A row a render gave the class went on painting in its own slot, and
  the reconciler kept holding it after it left the list; a row created with the class disappeared at the list's next
  render of its range; and a row a scroll brought into view was not placed by that scroll. Such a row also takes the
  list's item length, which went to the element standing in its slot.
- A `V.VirtualList` row that is a `V.Portal` now mounts its children in the target before the scroll that brings it
  into view returns, a Component row beside it in that range included.
- A `V.Motion` holding a `layoutId` now runs a change of its own translate during its move, and after the move while
  that translate still runs, on the curve the same change runs by on an element holding no id where a class gives
  that one Tailwind's curve, as `ease-out` and `ease-in-out` do. It ran UI Toolkit's own curve for the keyword
  instead, and on a one-second `ease-out` fell some 5px behind over a 40px translate.
