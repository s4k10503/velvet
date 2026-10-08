### Changed

- `text-balance` and `text-pretty` break the lines of the text a leaf displays instead of narrowing its
  box, so the box keeps the width its classes and its parent give it, as with CSS's `text-wrap`.
  `w-full text-balance` and `w-64 text-balance` balance, where a declared width used to switch balance
  off; a balanced label no longer shrinks, so a background, a border or an alignment relative to it
  sees the unbalanced box, and it no longer takes width from siblings in its row. Where the lines break
  follows Chromium's score line breaker: `text-balance` acts on up to six lines, `text-pretty` on up to
  four, and neither on fewer than four words. The text a component passes in is not rewritten; the breaks
  exist only in the displayed string, which is written `white-space: pre-wrap` over a string Velvet
  collapses itself.

- `text-balance` and `text-pretty` inherit, as CSS's `text-wrap-style` does. Put the class on a
  container and every text leaf under it breaks its lines; `text-wrap` or `text-nowrap` on a nearer
  element resets it. They used to act only on the element carrying the class.

- Breaks fall only at spaces. Text with no spaces (CJK), and a paragraph holding a tab or another white
  space beside the space, is left to the engine's own wrapping; the narrowing used to balance it.
