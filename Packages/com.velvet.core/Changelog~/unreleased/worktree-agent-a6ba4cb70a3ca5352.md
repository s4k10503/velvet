### Fixed

- A `backdrop-*` change, a dashed or dotted divider's colour, and a background or border colour repainted under a
  `shadow-*`, a `border-dashed` or `border-dotted`, or a layoutId Motion's per-axis corners now ease along the
  function their transition's class gives, as the transition UI Toolkit runs for the same class does: Tailwind's curve
  for `ease-in`, `ease-out` and `ease-in-out` (`transition-all` and `transition-colors` give `ease-in-out`), and the
  function an `ease-[…]` class or a theme's `ease-<name>` names. Velvet runs each of these transitions itself and eased
  them by UI Toolkit's own curve for the keyword the element resolves instead: half way through a
  `transition-colors`, a dashed divider's colour stood half way where Tailwind's curve stands at about 78%. A change
  back part way through is shortened by the curve it ran on.
- An element whose classes give its transitions `ease-in`, `ease-out` or `ease-in-out`, and whose rules give them more
  than one duration, delay or timing function, keeps Tailwind's curve while a Motion narrows its transitions around
  the properties it drives. The timing lists the narrowing writes inline were read as the element's own, which left
  those transitions on UI Toolkit's curve for the keyword.
- A Motion's tween on an element carrying an `ease-[…]` class, or a theme's `ease-<name>`, now runs on the Motion's own
  `Easing` where that is the keyword nearest the class's function. The class's function replaced it.
- A `transition-filter` tween whose `ease-[…]` class runs `steps(…, jump-start)` or `steps(…, jump-both)` holds its
  start through a positive `transition-delay`, as CSS does; it took its first step as the delay began.
- A `transition-filter` tween whose filter is changed back to the one it shows, as when a hover ends inside the
  delay, now stops there. It ran on to the filter it had been heading for and stayed on it.
- A sortable item whose transition lists are written inline, by an `ease-[…]` class or by a Motion narrowing its
  transitions, gets them back when a drag that shifted it ends, and a narrowing that ends afterwards hands back the
  item's own. The drag's `translate` and `scale` over 200 ms on an ease stayed in their place.
