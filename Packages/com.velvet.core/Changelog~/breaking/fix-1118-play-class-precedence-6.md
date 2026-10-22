### Changed

- A `V.Motion` animates a border color one side at a time, as CSS animates the four `border-*-color`
  longhands: sides showing different colors each start from their own, an important color on one side
  (`!border-t-[#f00]`) keeps that side alone out of the animation, and the per-side forms
  (`border-t-red-500`, `border-x-[#f00]`, `border-s-red-500`) animate. A side a `divide-*` color or a dashed
  divider holds starts from the color it shows and animates toward a border color class of the child's own
  where that class wins the side. The animation used to land at once wherever the four sides differed, a
  class target landed at once on a side a divider held, and a one-sided important color kept all four
  sides out.

- An important declaration of the element's own keeps what it wins through a play that starts off a panel,
  as it does on one; a `V.Motion` carrying `!w-[80px]` when it mounted used to have it taken by its enter.

- A dashed or dotted divider's edge runs a transition entry of the child's own that covers its border color,
  from the color the dash shows to the one it heads for, as the native border of a solid divider does. The
  dash used to change color at once.

- A `V.Motion` whose transition sets `When = TransitionWhen.BeforeChildren` holds its children back only
  for the channels its animation plays: a Bezier or Spring whose classes animate nothing, an important
  declaration keeping them out included, holds them for nothing, its delay included, as Framer Motion's
  animation promise for a variant with no value to animate resolves at once. Its children used to wait
  for the delay and the whole duration, or for a spring over channels that never played.
