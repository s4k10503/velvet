### Added

- `caret-*`, `selection:bg-*` and `selection:text-*` colour a text field's caret and selection, as
  Tailwind's `caret-color` and `selection:` utilities do:
  - each takes a palette colour, `white`, `black`, `transparent`, a bracketed value or an opacity
    modifier, and `caret-current` follows the text colour;
  - variants apply, a field inherits an ancestor's utility, and `-inherit` passes to the parent;
  - two of one utility on an element resolve in the order Tailwind emits them, not the order the
    classes were added in;
  - a colour goes back to the theme's, or the field's own, when no utility asks for one;
  - while a selection utility applies and the field holds focus and a non-empty selection, the
    selected text is drawn again above UI Toolkit's selection highlight, so an opaque highlight reads
    as a background.
