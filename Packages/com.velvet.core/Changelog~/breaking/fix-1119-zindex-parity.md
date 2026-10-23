### Changed

- `z-*` stacks an element in flex flow as it stacks an `absolute` one, as CSS stacks a flex item: an in-flow
  `z-10` paints over its later siblings and keeps its place in the row or column. It used to be ignored on an
  element that was not `absolute`.
- A positioned child with no `z-*` (`absolute`, `relative`, `V.Anchored`) paints over its unpositioned siblings,
  wherever those are declared, as CSS paints it. It used to paint in declared order, so an overlay declared
  before the content it covers painted under it.
- `z-0` stacks in the same place as no `z-*` on a positioned element, in declared order among them. It used to
  paint over every sibling without `z-*`, those declared after it included.
- A `z-*` element is reordered within its own parent rather than moved into a layer container the parent gains:
  code walking `hierarchy` children finds it among them, at the place it paints, with the zero-size
  placeholder at its declared position. The layer containers are gone.
- Gaining, changing or losing a `z-*`, including through a variant, keeps the element on the panel: its
  transitions keep running and it keeps pointer capture and focus. Gaining one, losing one, or moving between a
  negative and a positive value used to detach and reattach it.

### Added

- `order-*` (`order-3`, `-order-2`, `order-[5]`, `order-first`, `order-last`, `order-none`, and a custom
  property through `order-[var(--o)]` or `order-(--o)`) lays a flex child out at its place in flex order and
  paints it there, Tab order staying declared order.
- `z-*` and `order-*` behind a variant (`hover:z-10`, `md:order-first`) and a position utility behind one
  (`md:absolute`) restack the element while the variant holds.
