### Fixed

- A hand-authored `transition-property` naming `filter` together with `all`, `background-size` or
  `-unity-background-scale-mode` animates a filter change once, by UI Toolkit's own animation. Velvet's
  `transition-filter` tween ran on the same property at the same time.
- Under `transition-all` or a bare `duration-*`, a `contrast-*` added or removed at the end of the filter
  list fades from or to `1`, CSS's identity, where it faded from or to `0`, which is flat grey.
  Under `transition-filter`, removing a `contrast-*` that leaves no filter no longer fades it toward `0`
  again once the tween has ended.
- The `transition-filter` tween waits out a `transition-delay`, takes its duration, delay and curve from
  the `transition-*` entry naming `filter` rather than from the first, and reads a duration written in
  `ms` as milliseconds rather than seconds.
