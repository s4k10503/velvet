### Added

- `bg-linear-[…]`, `bg-radial-[…]` and `bg-conic-[…]` take a CSS gradient argument list, as Tailwind v4 does:
  an optional angle, `to_{side}`, `at_{position}` or `from_{n}deg` first argument, optionally with an
  `in_{space}` interpolation, then 2 to 16 colour stops, each with up to two percentage positions —
  `bg-linear-[90deg,#0f172a_0%,#0f172a_45%,#ffffff_50%,#0f172a_55%,#0f172a_100%]` draws a narrow highlight on
  a steady background. Missing positions are filled in by CSS's colour-stop fix-up, a list with no first
  argument runs to bottom, a malformed list leaves the class inert, and skewed elements and
  `animate-gradient` / `animate-shimmer` draw the list as they draw `from-` / `via-` / `to-`. The
  `from-` / `via-` / `to-` stops follow the list's, as Tailwind places them, and a `/` modifier after a
  bracketed list leaves the class inert, as it does there. A radial bracket that is no stop list keeps its
  earlier reading as a centre. A new gradients guide, styling-gradients.md, covers the gradient utilities
  and where they differ from CSS and Tailwind.
  `bg-radial-[…]` also takes a CSS shape and size (`circle`, `ellipse`, the four extent keywords, or radii), stop
  positions may lie outside 0%–100%, and `via-` alone fades in from and out to its own transparent colour.
  A stop may sit at a length in pixels, or on a conic at an angle, and a bare position between two stops is a
  colour hint that moves the half-way mix of the pair.
  The interpolation space may also be `srgb-linear`, `lab`, `lch` or `hsl`, with a hue method
  (`longer`, `shorter`, `increasing`, `decreasing`) for the polar ones.
  A hard edge or narrow band is baked at the element's length (up to 2048 texels along an axis, 512 by 512
  otherwise) instead of 128, so it stays sharp.
