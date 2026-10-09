### Fixed

- Honor leading and trailing important modifiers on `divide-*` utilities. `!divide-x-4` and `divide-x-4!`
  were dropped, so the divider was not drawn. An important axis, width, colour or line style now beats a
  plain one wherever it sits on the element, including through an active variant, and an important width or
  colour is drawn over a divided child's own plain border width or colour instead of giving way to it.
  A child's own important border (`!border-r-[3px]`) wins over an important divide, as its higher specificity does in CSS.
