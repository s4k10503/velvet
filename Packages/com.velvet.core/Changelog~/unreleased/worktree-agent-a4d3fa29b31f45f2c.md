### Fixed

- A child whose `shadow-inherit` reads a colour its parent mixes with an opacity modifier outside 0..100%
  (`shadow-lg/150 shadow-red-500`) draws no box-shadow, rings included. Tailwind mixes the colour through
  `--tw-shadow-alpha`, so the browser finds it invalid, the child inherits that invalid value, and its box-shadow
  computes to none; it used to draw the parent's colour at full strength. The same applies to
  `inset-shadow-*`.
- `shadow-initial` leaves each layer its own colour where it outranks the shadow colour class beside it, as it
  does when important (`!shadow-initial`) or when Tailwind emits it after that class (`shadow-black`); it used to
  be ignored.
- `shadow-lg/150 shadow-inherit` draws its shadow. `shadow-inherit` reads no `--tw-shadow-alpha`, so the browser
  keeps the box-shadow; it used to be discarded as an invalid colour would discard it.
