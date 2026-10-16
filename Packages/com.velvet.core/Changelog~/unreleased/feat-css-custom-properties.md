### Added

- CSS custom properties as classes: `[--name:value]` declares one on an element and its descendants inherit it,
  `var(--name)` and `var(--name, fallback)` read it in a utility's bracket (`w-[calc(var(--w)*2)]`,
  `from-[var(--brand)]`), and Tailwind v4's `bg-(--brand)` shorthand and `[property:value]` arbitrary properties
  read it too. Custom properties a USS stylesheet declares are read as well. A `var()` that does not resolve
  computes the property to `unset`, as in a browser; the guide names the painted utilities that are dropped
  instead. These classes were inert before. See
  `Documentation~/styling-custom-properties.md`.
