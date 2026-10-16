### Changed

- On the edge a `divide-dashed` or `divide-dotted` divider draws, a border width or color of the child's
  own applies as it does under a solid divider: a width class or bracket width is drawn dashed at that
  width, and the dash takes the child's own border color where it wins the edge. The dash used to be drawn
  in the divide color at the divider's width, and a width class drew that edge solid. A `V.Motion`
  animating that child's border color to a bracket color (`border-[#3b82f6]`) with no start of its own
  starts from the color the dash takes, where it used to land at once.

- While a `V.Motion` animates a length or a color, the animated value stands over the element's own
  variant layers (`dark:`, `sm:`, `hover:` and the rest) and over a `space-*`, `divide-*` or grid
  value, as an animation does in CSS, and a gap adds to an animated margin as CSS adds an item's margin to
  its gap. An important divide value (`!divide-gray-200`) and an important declaration of the element's
  own (`!w-[80px]`, or `hover:!w-[80px]` turning on during the animation) keep what they win, as
  `!important` outranks an animation; one a `V.Motion` carried when it mounted used to give way to its
  enter. A plain variant layer toggling during the animation used to replace the animated value
  until the next frame, or for the rest of a delay, and a gap gave way to an animated margin.
