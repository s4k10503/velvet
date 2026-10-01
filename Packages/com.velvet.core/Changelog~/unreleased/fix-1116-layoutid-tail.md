### Fixed

- An element's own inline transition duration, delay and easing — a `duration-[x]` utility's, or one written to
  its `style` before the tween started — are kept through a variant or preset tween. The tween's end cleared all
  three, so the element's transitions fell back to its classes until something wrote them again.
