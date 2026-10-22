### Changed

- On the edge a `divide-dashed` or `divide-dotted` divider draws, a border width or color of the child's
  own applies as it does under a solid divider: a width class or bracket width is drawn dashed at that
  width, and the dash takes the child's own border color where it wins the edge. A color the child's own
  code writes over the edge, one its style rules give it (a theme class, a `hover` rule) and one a
  `V.Motion` drives there are painted in the dash and repaint it when they change. The dash used to be
  drawn in the divide color at the divider's width, and a width class drew that edge solid.

- `divide-dashed`, `divide-dotted`, `border-dashed` and `border-dotted` lay out their dashes and dots as Chrome
  does: from 3px up a dash is two widths long with a one-width gap rather than three and two, the gap is
  fitted so an edge starts and ends on a dash, an edge too short for two dashes is drawn solid, and a dot up
  to 3px is a square a width long rather than a round one. The dashes used to run at a fixed period from the
  start of the line, so the last one could stop short of the end or be cut off at it.
