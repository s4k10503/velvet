### Fixed

- A font family whose entries all hold only italic faces now serves an upright request with an italic face,
  as CSS does when no face of the requested style exists. The request used to find no asset and fall back to
  the default font.
