### Added

- `caret-*`, `selection:bg-*` and `selection:text-*` colour a text field's caret and selection, as
  Tailwind's `caret-color` and `selection:` utilities do:
  - each takes a palette colour, `white`, `black`, `transparent`, a bracketed value or an opacity
    modifier, and `caret-current` follows the text colour, `caret-current/50` at half its alpha;
  - variants apply, a field takes the nearest ancestor's `caret-*`, and `-inherit` passes to the
    parent;
  - competing utilities resolve in the order Tailwind emits them, not the order the classes were
    added in, and a `selection:*` on the field competes with every ancestor's, as Tailwind's
    `& *::selection, &::selection` does;
  - a colour goes back to the theme's, or the field's own, when no utility asks for one;
  - while a selection utility applies and the field holds focus and a non-empty selection, the
    selected text is drawn again above UI Toolkit's selection highlight, so an opaque highlight reads
    as a background.
