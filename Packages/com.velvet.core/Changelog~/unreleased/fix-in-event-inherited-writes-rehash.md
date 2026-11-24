### Fixed

- An element no longer takes an inherited value Velvet wrote on an element other than its parent. Velvet writes some
  inherited properties on an element inside the style pass that restyles it: the middle alignment of a label, a button
  or a field's input text and the copy of its font it is drawn in while it shows no rich text, a text colour
  `text-inherit` or `text-current` reads off the parent, a white-space a text effect writes, and a text colour, font
  size or letter spacing read off a custom property a rule declares on the element. A child that pass styled after
  such a write was cached under the value the write replaced, so an element of the same type and classes styled
  afterwards under a parent still holding that value was given the written one.
