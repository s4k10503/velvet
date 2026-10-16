### Changed

- On the edge a `divide-dashed` or `divide-dotted` divider draws, a border width or color of the child's
  own applies as it does under a solid divider: a width class or bracket width is drawn dashed at that
  width, and the dash takes the child's own border color where it wins the edge. The dash used to be drawn
  in the divide color at the divider's width, and a width class drew that edge solid.

- A `V.Motion` animates a border color one side at a time, as CSS animates the four `border-*-color`
  longhands: sides showing different colors each start from their own, an important color on one side
  (`!border-t-[#f00]`) keeps that side alone out of the animation, and the per-side forms
  (`border-t-red-500`, `border-x-[#f00]`, `border-s-red-500`) animate. A side a `divide-*` color or a dashed divider holds
  starts from the color it shows and animates toward a border color class of the child's own where that
  class wins the side. The animation used to land at once wherever the four sides differed, a class
  target landed at once on a side a divider held, and a one-sided important color kept all four
  sides out.

- While a `V.Motion` animates a length or a color, the animated value stands over the element's own
  variant layers (`dark:`, `sm:`, `hover:` and the rest) and over a `space-*`, `divide-*` or grid
  value, as an animation does in CSS, and a gap adds to an animated margin as CSS adds an item's margin to
  its gap. An important divide value (`!divide-gray-200`) and an important declaration of the element's
  own (`!w-[80px]`, or `hover:!w-[80px]` turning on during the animation) keep what they win, as
  `!important` outranks an animation; one a `V.Motion` carried when it mounted used to give way to its
  enter. A plain variant layer toggling during the animation used to replace the animated value
  until the next frame, or for the rest of a delay, and a gap gave way to an animated margin.

- A `V.Motion` whose transition sets `When = TransitionWhen.BeforeChildren` holds its children back only
  for the channels its animation plays: a Bezier or Spring whose classes animate nothing, an important
  declaration keeping them out included, holds them for its delay alone. Its children used to wait for
  the whole duration, or for a spring over channels that never played.
