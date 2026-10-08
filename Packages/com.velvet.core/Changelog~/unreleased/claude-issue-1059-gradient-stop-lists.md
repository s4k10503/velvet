### Added

- `bg-linear-[…]`, `bg-radial-[…]` and `bg-conic-[…]` take a CSS gradient argument list, as Tailwind v4 does:
  an optional angle, `to_{side}`, `at_{position}` or `from_{n}deg` first argument, optionally with an
  `in_{space}` interpolation, then 2 to 16 colour stops, each with up to two percentage positions —
  `bg-linear-[90deg,#0f172a_0%,#0f172a_45%,#ffffff_50%,#0f172a_55%,#0f172a_100%]` draws a narrow highlight on
  a steady background. Missing positions are filled in by CSS's colour-stop fix-up, a list with no first
  argument runs to bottom, a malformed list leaves the class inert, and skewed elements and
  `animate-gradient` / `animate-shimmer` draw the list as they draw `from-` / `via-` / `to-`. A radial bracket
  that is no stop list keeps its earlier reading as a centre. A new gradients guide, styling-gradients.md, covers the gradient utilities and where they
  differ from CSS and Tailwind.
