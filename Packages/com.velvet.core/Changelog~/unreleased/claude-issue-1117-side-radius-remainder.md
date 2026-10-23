### Fixed

- A side radius that a variant or a later class takes one corner of keeps its other corner: `rounded-l-lg md:rounded-t-lg`
  paints the top radius and the left one's bottom corner. The side class used to stay on beside the payload, and the
  stylesheet's declaration order decided the shared corner.
