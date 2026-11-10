### Fixed

- A `backdrop-*` change, a dashed or dotted divider's colour, and a background or border colour repainted under a
  `shadow-*`, a `border-dashed` or `border-dotted`, or a layoutId Motion's per-axis corners now ease along the
  function their transition's class gives, as the transition UI Toolkit runs for the same class does: Tailwind's curve
  for `ease-in`, `ease-out` and `ease-in-out` (`transition-all` and `transition-colors` give `ease-in-out`), and the
  function an `ease-[…]` class or a theme's `ease-<name>` names. Velvet runs each of these transitions itself and eased
  them by UI Toolkit's own curve for the keyword the element resolves instead: half way through a
  `transition-colors`, a dashed divider's colour stood half way where Tailwind's curve stands at about 78%. A change
  back part way through is shortened by the curve it ran on.
