### Fixed

- A shadow size whose opacity modifier lies outside 0..100% (`shadow-lg/150`) beside a shadow colour class no
  longer hands that colour to a child's `shadow-inherit`. Tailwind mixes the colour through `--tw-shadow-alpha`, so
  the browser finds it invalid, and the child keeps each layer's own colour; it used to take the parent's colour
  at full strength. The same applies to `inset-shadow-*`.
- `shadow-lg/150 shadow-inherit` draws its shadow. `shadow-inherit` reads no `--tw-shadow-alpha`, so the browser
  keeps the box-shadow; it used to be discarded as an invalid colour would discard it.
