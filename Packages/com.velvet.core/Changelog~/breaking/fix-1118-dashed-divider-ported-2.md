### Changed

- On the edge a `divide-dashed` or `divide-dotted` divider draws, a border width or color of the child's own
  applies as it does under a solid divider: a width class or bracket width is drawn dashed at that width, and
  the dash takes the child's own border color where it wins the edge. A color the child's own code writes over
  the edge, one its style rules give it (a theme class, a `hover` rule) and one a `V.Motion` drives there are
  painted in the dash and repaint it when they change. The dash used to be drawn in the divide color at the
  divider's width, and a width class drew that edge solid.

- `divide-dashed`, `divide-dotted`, `border-dashed` and `border-dotted` lay out their dashes and dots as
  Chrome does: from 3px up a dash is two widths long with a one-width gap rather than three and two, the gap
  is fitted so an edge starts and ends on a dash, an edge too short for two dashes is drawn solid, and a dot
  up to 3px is a square a width long rather than a round one. The dashes used to run at a fixed period from
  the start of the line, so the last one could stop short of the end or be cut off at it.

- A `border-solid`, `border-dashed` or `border-dotted` of a divided child's own sets the line style of its
  divider edge over the divide's, as Tailwind's zero-specificity divide rule leaves it: `border-solid` on a
  child of a `divide-dashed` row draws its divider solid. An important divide style (`!divide-dashed`) keeps
  the edge. The divide's style used to be drawn whatever the child's.

- A color a divided child's own code writes inline on a divided edge outranks a plain `divide-{color}`, on a
  solid divider and a dashed one, and the child's own color classes on a dashed one, as an inline style does.
  The divider used to write its color back over it on its next pass, and a dashed one painted the divide color
  or the child's classes' color instead.

- `border-dashed` and `border-dotted` paint each side on its own in that side's color and at its width: on a
  box with square corners from corner to corner, so each side starts and ends on a dash, and on a box with a
  rounded corner along the rounded outline. Each side is cut to its own corner at the miter, so a translucent
  color is laid once where two sides meet, and a side a dashed divider paints is left to it. They used to lay
  one pattern around the whole outline in the left side's color and at its width, overlapping a dashed divider
  on a rounded child, and drew nothing on a box with no left border. Releasing the outline's captured colors
  no longer clears a divider's color or mask from the child's edges.

- A dashed divider on a rounded child follows the child's curve rather than running straight to its corners. A
  child that gains `border-dashed` or `border-dotted` in a reversed dashed row takes its outline's color from
  the divider rather than drawing nothing, and a color the child's code wrote over a dashed edge stays on the
  edge when the divider turns solid.

- A thin dotted line at least three widths long starts and ends on a whole dot, as Chrome draws it.
