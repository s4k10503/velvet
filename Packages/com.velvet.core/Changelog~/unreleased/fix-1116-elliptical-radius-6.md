### Fixed

- A `V.Motion` moving under a `layoutId` while drawn at a different scale on each axis draws its pixel corner radii
  circular, as Framer's shared layout does, rather than elliptical. Its background colour, background image (a
  `bg-gradient-*` included), border and ring are painted rounded by each axis's own scale, its colours following
  its classes and the transitions it declares for them, and one that clips its content (`overflow-hidden`) clips
  it to that rounded edge — at its border edge, where it has a border. Its inline background colour, border
  colours and background image hold Velvet's values while it is drawn so, and are handed back as the move lands.
  A Motion whose background image is repeated, positioned, sized in lengths, nine-sliced or a vector image keeps
  the elliptical corners.
- The face a `skew-*` or upright `shadow-*` element repaints draws each border side in its own colour and width,
  rather than every side in the left one's, two sides meeting in a corner divided in proportion to their widths.
