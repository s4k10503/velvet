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

- `whileHoverClass`, `whileTapClass`, `whileFocusClass` and `whileDraggingClass` apply their classes as a
  payload, ranked above every rule of the element's own short of an important one — `StyleOverrides` and
  className variants such as `hover:` and `focus:` included — and above a play's classes but an exit's, as Framer
  Motion's `variantPriorityOrder` ranks gesture states above the element's style and `animate` and below `exit`;
  a Spring or Bezier play still running as the state ends moves the property on from where the state left it.
  Among themselves they rank as `hover:`, `focus:`,
  `active:` and `data-[…]:` do, so a tap or focus class outranks a hover one. `whileOverClass` and
  `whileDragActiveClass` apply theirs at `data-[…]:`'s rank, after every rule the className declares with that
  variant alone. The element's own value comes back when the state goes off. A bracket value such as `bg-[#f00]` applies through them, and on an element so do `shadow-*`,
  `ring-*`, `skew-*`, gradients, `animate-*`, `border-dashed`, `gap-*` and `uppercase`. These channels used
  to rank with the element's own classes, so a base class the stylesheet declares later, a base bracket
  value or a divider's color kept the property, and a bracket value or a paint Velvet draws itself did
  nothing.
- While a play shows its classes — a `V.Motion` enter, exit or variant change on any driver, or an
  `AnimatePresence` preset — they outrank the element's own utilities and bracket values, its `StyleOverrides`,
  a divider's or space margin's value on the same edge, variant payloads and the droppable channels for the
  same property, as a CSS animation's declarations do, and lose to an important utility, behind a variant or
  not, and, but for an exit's, to a gesture class: a property a gesture class sets stays where it puts it while
  the state is on, on every driver, and a spring or bezier play other than an exit starting during the state
  leaves it out.
  A variant's classes rank as the element's own once its play ends. A class the stylesheet declares later, a
  bracket value or a droppable's class used to keep the property while the play ran, a divider's color did not
  give way to a class a variant change brought, and did not come back once an enter's class left.

- A `V.Motion` draws `shadow-*`, `skew-*` and `border-dashed` / `border-dotted`, from its className, its
  variants and the `while*Class` and drag channels, as any element does. It used to draw none of them, and
  logged a warning for a shadow.

- A `shadow-*` or `skew-*` element draws its `border-dashed` / `border-dotted` border dashed or dotted, sheared
  with a skew, and a `divide-dashed` or `divide-dotted` divider on a `shadow-*` or `skew-*` child dashed or
  dotted, as Chromium does; both used to be drawn solid.

- A `shadow-*` or `skew-*` element whose overflow is hidden keeps its border, dashed or dotted included, and the
  background beneath it, and a `shadow-*` one its outer shadow layers, where they used to lose their border, a
  border-wide ring of their background and the shadow; a skew's overhang is still cut at the padding box.

- While a `layoutId` move draws a `shadow-*`, `skew-*` or `border-dashed` `V.Motion` scaled differently on each
  axis, the face, border, dashed outline and skewed gradient those layers paint take the corner radii the move
  draws, a horizontal and a vertical one a corner, as its background does, and its shadow shows circular corners
  and its offset as written, as Framer Motion's `correctBoxShadow` shows them (motion.md says where its blur and
  spread differ). They used to be drawn square, and a shadowed one kept square shadow corners once the move
  landed.

- The classes a play shows — a `V.Motion` pose's, an `AnimatePresence` enter or exit class — apply bracket
  values and the utilities Velvet realises itself (`gap-*`, `shadow-*`, `ring-*`, `skew-*`, gradients,
  `animate-*`, `border-dashed`, `uppercase`, …) while the play shows them, and one of the latter that the
  element's resting pose carries stops applying while the play has the pose's class off. A play's class used
  to apply only a utility backed by a plain USS rule.

- On a field control a class a play shows lands where the className's does: a surface utility on the input
  box, `truncate`, `text-ellipsis` and `text-clip` on the text the box draws as well, with `overflow-hidden`,
  and a transition utility on both. On a clipped element a `clip-path-*` class a play shows re-shapes the clip.
  A `V.Motion` whose `elementType` is a field control sends its className's surface utilities to the input box,
  as `V.TextField` does; they used to stay on the outer control.

### Fixed

- A `V.Motion` hovered through a `hover:` rule of its className while its enter played kept the class of the
  pose it entered from once the pointer left, and showed that pose instead of the one it entered. A variant
  change played while a `V.Motion` was hovered let the new variant's class take the property from its
  `whileHoverClass` class.
- A `has-[.foo]:` ancestor did not match a `.foo` a play showed on a descendant whose variant payload, such as
  `first:foo`, also put `.foo` there.
- A variant payload such as `hover:foo` turning off could take `.foo` off the element while a play still showed it.

- A skewed, `shadow-*` or `border-dashed` element kept painting the background and border colours it had when
  its skew, shadow or dashed border attached after a variant, gesture or drag class, a theme change or a play
  changed them, until an unrelated re-render.

- A direct child of a skewed element showed either the skew's seat or its own translate — `translate-x-*`, a
  `V.Motion` translation, a drag offset — whichever was written last. The two compose, as CSS composes a
  parent's transform with the child's, and a translate play on such a child starts from its own translate
  rather than offset by the seat, as does a `layoutId` move.
