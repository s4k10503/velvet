### Changed

- `shadow-*` paints only outside the element's border box, as CSS paints a box-shadow, and leaves the
  element's own background and border to paint as they would without it. A translucent background shows no
  shadow through it, a bordered card keeps its border under `overflow-hidden`, `border-dashed` and
  `divide-dashed` draw on a shadowed element, and a background a variant, gesture or drag class, theme change
  or play brings in shows on it. The shadow used to paint under the whole box and repaint the background and
  border over it, in the colours they had when the shadow attached, with a solid border.

### Fixed

- A `has-[.foo]:` ancestor did not match a `.foo` a play showed on a descendant whose variant payload, such as
  `first:foo`, also put `.foo` there.
- A variant payload such as `hover:foo` turning off could take `.foo` off the element while a play still showed it.
