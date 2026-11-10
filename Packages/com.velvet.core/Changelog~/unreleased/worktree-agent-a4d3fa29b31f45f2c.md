### Fixed

- A child whose `shadow-inherit` reads a colour its parent mixes with an opacity modifier outside 0..100%
  (`shadow-lg/150 shadow-red-500`) draws no box-shadow, rings included. Tailwind mixes the colour through
  `--tw-shadow-alpha`, so the browser finds it invalid, the child inherits that invalid value, and its box-shadow
  computes to none; it used to draw the parent's colour at full strength. The same applies to
  `inset-shadow-*`.
- `!shadow-initial` (or a later `shadow-initial`) over a shadow colour class leaves each layer its own colour, as
  the browser does; it used to be ignored.
- `shadow-lg/150 shadow-inherit` draws its shadow. `shadow-inherit` reads no `--tw-shadow-alpha`, so the browser
  keeps the box-shadow; it used to be discarded as an invalid colour would discard it.
