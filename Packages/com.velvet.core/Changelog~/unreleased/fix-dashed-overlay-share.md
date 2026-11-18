### Fixed

- A child of a `divide-dashed` (or `divide-dotted`) parent that also carried a dashed border or a shadow and clipped its
  content (`overflow-hidden`) drew only one of the two. Its divider never settled onto the shared overlay where the
  border's was already up, so the divider edge was painted by nothing; where it did, it replaced the border. The border
  and the divider are now both drawn.
