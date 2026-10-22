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

- `whileHoverClass`, `whileTapClass` and `whileFocusClass` apply their classes as `hover:`, `active:` and
  `focus:` apply a payload, ranked after every rule the className declares with that variant alone, and the
  drag-and-drop channels `whileDraggingClass`, `whileOverClass` and `whileDragActiveClass` apply theirs the
  same way at `data-[…]:`'s rank. While the state is on, one outranks a utility of the element's own that
  carries no variant or `!`, for the same property, and, for the gesture channels, the className's rule for
  its own variant; Tailwind's order decides the rest, so a tap or focus class outranks a hover one and a
  className `focus:` rule outranks `whileHoverClass`. The element's own value comes back when the state goes
  off. A bracket value such as `bg-[#f00]` applies through them, and on an element so do `shadow-*`,
  `ring-*`, `skew-*`, gradients, `animate-*`, `border-dashed`, `gap-*` and `uppercase`. These channels used
  to rank with the element's own classes, so a base class the stylesheet declares later, a base bracket
  value or a divider's color kept the property, and a bracket value or a paint Velvet draws itself did
  nothing.
- While a play shows its classes — a `V.Motion` enter, exit or variant change on any driver, or an
  `AnimatePresence` preset — they outrank the element's own utilities and bracket values, its `StyleOverrides`,
  a divider's or space margin's value on the same edge, variant payloads and gesture classes for the same
  property, as a CSS animation's declarations do, and lose to an important utility, behind a variant or not.
  A variant's classes rank as the element's own once its play ends. A class the stylesheet declares later, a
  bracket value or a gesture class used to keep the property while the play ran, a divider's color did not
  give way to a class a variant change brought, and did not come back once an enter's class left.

### Fixed

- A `V.Motion` hovered through a `hover:` rule of its className while its enter played kept the class of the
  pose it entered from once the pointer left, and showed that pose instead of the one it entered. A variant
  change played while a `V.Motion` was hovered let the new variant's class take the property from its
  `whileHoverClass` class.
