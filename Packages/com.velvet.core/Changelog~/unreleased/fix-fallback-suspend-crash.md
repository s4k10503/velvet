### Fixed

- An error boundary whose fallback suspends after the boundary has swapped it in no longer throws a
  `NullReferenceException` out of the render that threw (a child's own re-render) or lets the internal suspend signal out
  of `V.Mount` (a layout effect that threw at mount). The boundary keeps the error it caught, renders its fallback again
  when the read resolves, and reports the catch once, from the render whose fallback reconciles without suspending again.
  Under a Suspense above the boundary the catch survives the Suspense showing its own fallback meanwhile.
- A boundary whose fallback element cannot be created now hands the boundary above the error of the fallback's creation,
  as React hands it the error of the fallback's render, rather than the error the fallback was for.
