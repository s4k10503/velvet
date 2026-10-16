### Fixed

- A filter transition that fades out an `opacity()` the element's inline filter held, written there by code other
  than the filter utilities, fades it to `1`, CSS's no-op value. It faded it to `0`, hiding the element on the
  way out.
