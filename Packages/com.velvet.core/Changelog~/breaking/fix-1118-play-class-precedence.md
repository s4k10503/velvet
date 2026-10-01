### Changed

- While a tween play shows its classes — a `V.Motion` enter, exit or variant change on the default Tween
  driver, or an `AnimatePresence` preset — they outrank the element's own utilities and bracket values, a
  divider's or space margin's value on the same edge, variant payloads and gesture classes for the same
  property, as a CSS animation's declarations do, and lose to an important utility, behind a variant or not.
  A class the stylesheet declares later, a bracket value or a gesture class used to keep the property while
  the play ran, a divider's color did not give way to a class a variant change brought, and did not come
  back once an enter's class left.
