### Fixed

- A `rotate` or `scale` that a stylesheet of your own declares for a skewed element is composed into the skew, as the
  individual `rotate` and `scale` properties compose with `transform`, and is composed again when a restyle moves it.
  It used to be dropped: the element was drawn with its skew alone. An application whose skewed elements carry such a
  rule now sees them rotated or scaled. A `rotate` about an axis other than z is declined, and the element keeps its
  skew and the classes' rotate.
- A skewed element carries a `velvet-skew-observed` class while its skew is composed, which the bundled stylesheet gives
  the custom property that tells Velvet a restyle happened.
