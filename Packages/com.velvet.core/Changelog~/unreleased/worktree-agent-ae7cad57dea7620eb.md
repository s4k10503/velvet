### Changed

- A patch of an element carrying `gap-*`, `divide-*`, a grid, a box shadow or a gradient no longer builds a closure or
  a delegate to keep a relative-length poll up when none of its lengths is a measured one.
- A patch of a shadowed element no longer cuts a preset's name out of its `shadow-*` or `inset-shadow-*` class as a
  string, `shadow-inner` aside, copies the layers of a single family into a list and an array, or records the colours
  its classes declare in a new object when they are the ones it recorded last time.
- The shadow and clip-path reads pass over an important class of another family, such as `!bg-red-500`, before
  copying it without its bang, and configuring an element's variant manipulators passes over an important class that
  can be neither a theme class nor a custom-property reader, such as `!flex`, the same way.
- Inside a theme scope, asking whether a class behind a plain word that is no variant, such as `first:`, or behind
  `max-`/`min-` and a breakpoint no sheet declares, is a variant cuts out nothing. Reading a theme variable's length
  no longer wraps its text in brackets first.
- The readers configuring an element's state, responsive, container and group/peer variant manipulators cut a variant
  class's payload out only where its kind is the one being read, where before each of them cut out the payload of
  every variant class; and a class holding fewer than two `:` is skipped when looking for a stacked one.

### Fixed

- A gradient's `from-`, `via-` or `to-` palette colour reads as its own mount's sheets give it. The reading of the
  first mount to render the class was handed to later ones, so a mount whose sheets set `--color-*: initial` could
  take the palette colour, or another mount lose it. A gradient re-measured after its font or the panel changed now
  reads its stops under its mount's sheets too.
