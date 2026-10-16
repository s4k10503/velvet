### Changed

- On the edge a `divide-dashed` or `divide-dotted` divider draws, a border width or color of the child's
  own applies as it does under a solid divider: a width class or bracket width is drawn dashed at that
  width, and the dash takes the child's own border color where it wins the edge. The dash used to be drawn
  in the divide color at the divider's width, and a width class drew that edge solid.

- While a `V.Motion` animates a length or a color, the animated value stands over the element's own
  variant layers (`dark:`, `sm:`, `hover:` and the rest) and over a `space-*`, `divide-*` or grid
  value, as an animation does in CSS, and a gap adds to an animated margin as CSS adds an item's margin to
  its gap. An important divide value (`!divide-gray-200`) and an important declaration of the element's
  own that turns on during the animation (`hover:!w-[80px]`) keep what they win, as `!important` outranks
  an animation. A plain variant layer toggling during the animation used to replace the animated value
  until the next frame, or for the rest of a delay, and a gap gave way to an animated margin.
