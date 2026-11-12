### Fixed

- A bracket whose value opens on a parenthesis with no function name before it, such as `brightness-[(2)]`,
  `hue-rotate-[(2deg)]` or `translate-x-[(10px)]`, is rejected as the browser rejects what Tailwind writes for it,
  so the element draws none of that family's utilities; it used to be taken as a function that might come to
  anything. A negated one, such as `-hue-rotate-[(2deg)]`, is still taken, since Tailwind writes it inside
  `calc()`. A gradient stop position written that way, such as `from-[length:(10px)]`, puts the stop at its
  initial position, as another position the browser rejects does.
- An element with a border-width utility and a colour transition, such as `border transition-colors`, shows its
  text colour on every side as soon as it mounts, where every side after the top used to fade in from
  transparent. A border colour code writes inline and clears again transitions back to the text colour, where it
  used to fade to transparent first.
