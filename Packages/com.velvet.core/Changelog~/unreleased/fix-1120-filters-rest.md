### Fixed

- A filter change on an element whose hand-authored `transition-property` names `filter` animates without the
  element carrying `transition-filter`, removing a filter as adding one does. Adding a filter applied at once
  and removing every filter was animated by UI Toolkit.
- A `transition-property` naming `background-size` or `-unity-background-scale-mode` leaves filter changes
  instant where no entry runs for `filter`, and where one does, the filter change runs for that entry's
  duration, delay and curve. UI Toolkit animated them by the `background-size` entry.
- `grayscale-[N]`, `invert-[N]` and `sepia-[N]` clamp an amount above 1 to 1, as CSS does.
- Under `transition-all`, a `brightness-*` and a `contrast-*` added to the end of the filter list together
  fade the contrast in from `1`, and an element leaving the panel part way through a removed contrast's fade
  is left with no inline filter rather than `contrast(1)`.
- `grayscale-[N%]`, `invert-[N%]`, `sepia-[N%]`, `contrast-[N%]`, `brightness-[N%]` and `saturate-[N%]` take a
  percentage, as Tailwind writes them.
- Under `animate-hue`, a filter utility's change starts no filter transition. When `animate-hue` ends, the filter
  under it, a hovered or other variant filter included, is written at once, and a filter transition still running
  shows again until it ends. The variant filter was lost, a filter-free element faded its hue out, and a running
  transition was cut to its end or restarted.
- Removing `animate-pulse` or `animate-spin` leaves a running filter transition alone, where it restarted.
