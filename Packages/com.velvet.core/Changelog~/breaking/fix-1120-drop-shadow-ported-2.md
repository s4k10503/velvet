### Changed

- On a field control, the text colour, `opacity-*`, the transforms (`translate-*`, `rotate-*`, `scale-*`,
  `skew-*`, `origin-*`), `animate-*`, and the text styling (`font-*`, `italic`, the text size and alignment,
  `tracking-*`, the text effects such as `uppercase`, and `truncate`) land on the input box, beside the other
  surface utilities, as CSS applies them to an `<input>` alone. They were on the outer control, where they also
  reached a declared `label:`. The transition utilities (`transition-*`, `duration-*`, `ease-*`) land on both, so
  a change to a box's background or filter tweens; they were on the control alone, which left the box's changes
  instant.

### Fixed

- `text-ellipsis` alone on a field elides the input's text while the field is unfocused, as it does on an
  `<input>`: the box and its text take `overflow-hidden` beside `text-ellipsis` and `text-clip`, as `truncate`
  already gave them.

- A `shadow-*` or `skew-*` beside a filter utility on a field, a variant one included, reserves room for its
  paint under the filter, as it does on any other element.
