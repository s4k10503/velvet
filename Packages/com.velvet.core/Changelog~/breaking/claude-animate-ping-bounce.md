### Changed

- `animate-pulse` and `animate-spin` start from their element's own value, as a CSS animation fills the
  keyframes it leaves unnamed. `opacity-75 animate-pulse` runs between 0.75 and 0.5 rather than between 1
  and 0.5, and `rotate-45 animate-spin` turns on from 45 degrees rather than from 0. The own value is the
  element's class value, a named one or an arbitrary one, read again every frame; an element with no opacity
  or rotation class runs as before. A Motion `Spring` or `Bezier` frame under the loop is still written over
  rather than taken for the element's own.
