### Changed

- `whileHoverClass`, `whileTapClass` and `whileFocusClass` apply their classes as `hover:`, `active:` and
  `focus:` apply a payload, ranked after every rule the className declares with that variant alone, and the
  drag-and-drop channels `whileDraggingClass`, `whileOverClass` and `whileDragActiveClass` apply theirs the
  same way at `data-[…]:`'s rank. While the state is on, one outranks a utility of the element's own that
  carries no variant or `!`, for the same property, and, for the gesture channels, the className's rule for
  its own variant; Tailwind's order decides the rest, so a tap or focus class outranks a hover one and a
  className `focus:` rule outranks `whileHoverClass`. The element's own value comes back when the state goes
  off. A bracket value such as `bg-[#f00]` applies through them, and so do `shadow-*`,
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
- `shadow-*` paints only outside the element's border box, as CSS paints a box-shadow, and leaves the
  element's own background and border to paint as they would without it. A translucent background shows no
  shadow through it, a bordered card keeps its border under `overflow-hidden`, `border-dashed` and
  `divide-dashed` draw on a shadowed element, and a background a variant, gesture or drag class, theme change
  or play brings in shows on it. The shadow used to paint under the whole box and repaint the background and
  border over it, in the colours they had when the shadow attached, with a solid border.
- A `V.Motion` draws `shadow-*`, `skew-*` and `border-dashed` / `border-dotted`, from its className, its
  variants and the `while*Class` and drag channels, as any element does. It used to draw none of them, and
  logged a warning for a shadow.
- The classes a play shows — a `V.Motion` pose's, an `AnimatePresence` enter or exit class — apply bracket
  values and the utilities Velvet realises itself (`gap-*`, `shadow-*`, `ring-*`, `skew-*`, gradients,
  `animate-*`, `border-dashed`, `uppercase`, …) while the play shows them, and one of the latter that the
  element's resting pose carries stops applying while the play has the pose's class off. A play's class used
  to apply only a utility backed by a plain USS rule.

### Fixed

- A `V.Motion` hovered through a `hover:` rule of its className while its enter played kept the class of the
  pose it entered from once the pointer left, and showed that pose instead of the one it entered. A variant
  change played while a `V.Motion` was hovered let the new variant's class take the property from its
  `whileHoverClass` class.
- A `has-[.foo]:` ancestor did not match a `.foo` a play showed on a descendant whose variant payload, such as
  `first:foo`, also put `.foo` there.
- A variant payload such as `hover:foo` turning off could take `.foo` off the element while a play still showed it.
- A skewed or `border-dashed` element kept painting the background and border colours it had when its skew or
  dashed border attached after a variant, gesture or drag class, a theme change or a play changed them, until
  an unrelated re-render.
