### Fixed

- A `V.VirtualList` row that stacks by a `z-*` class is now placed in front of or behind the other rows as its z
  says, in each of these cases where it was not. A row a render gave the class went on painting in its own slot, and
  the reconciler kept holding it after it left the list; a row created with the class disappeared at the list's next
  render of its range; and a row a scroll brought into view was not placed by that scroll. Such a row also takes the
  list's item length, which went to the element standing in its slot.
- A `V.VirtualList` row that is a `V.Portal` now mounts its children in the target before the scroll that brings it
  into view returns, in the order a render mounts them, a Component row beside it in that range included.
- A `V.VirtualList` row built after a row whose error boundary swapped in its fallback in the same render of the
  list's host, as one does when a ref cleanup it owns throws while the row is patched, is now built whole, and the
  children after that boundary in its own row are patched. The first was created without its children, and the
  second kept what the render before gave them.
- A `V.VirtualList` row that a scroll mounts inside a `V.Provider` of its own now reads that Provider's value. Where a
  Provider of the same context enclosed the component rendering the list, the row read that one's value instead.
- A `V.Motion` holding a `layoutId` now runs a change of its own translate during its move, and after the move while
  that translate still runs, on the curve the same change runs by on an element holding no id where a class gives
  that one Tailwind's curve, as `ease-out` and `ease-in-out` do. It ran UI Toolkit's own curve for the keyword
  instead, and on a one-second `ease-out` fell some 5px behind over a 40px translate.
