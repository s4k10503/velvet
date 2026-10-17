### Changed

- On the edge a `divide-dashed` or `divide-dotted` divider draws, a border width or color of the child's
  own applies as it does under a solid divider: a width class or bracket width is drawn dashed at that
  width, and the dash takes the child's own border color where it wins the edge. A color the child's own
  code writes over the edge, one its style rules give it (a theme class, a `hover` rule) and one a
  `V.Motion` drives there are painted in the dash and repaint it when they change. The dash used to be
  drawn in the divide color at the divider's width, and a width class drew that edge solid.
