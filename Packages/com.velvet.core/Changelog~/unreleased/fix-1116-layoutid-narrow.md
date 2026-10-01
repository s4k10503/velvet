### Fixed

- A `V.Motion` pose change played as a tween puts back, as it ends, the transition duration, delay and easing
  the element held inline before it started — a `duration-[x]` utility's, or one its code wrote to `style` —
  and a `duration-[x]` the element takes while it runs. It cleared all three, so the element's transitions fell
  back to its classes until something wrote them again.
