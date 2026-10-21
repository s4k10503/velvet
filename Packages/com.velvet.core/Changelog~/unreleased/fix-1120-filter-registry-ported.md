### Fixed

- A filter transition that fades out an `opacity()` the element's inline filter held, written there by code other
  than the filter utilities, fades it to `1`, CSS's no-op value. It faded it to `0`, hiding the element on the
  way out.

### Changed

- `VelvetFilters.Register` rejects a name holding `(` or `)` and the name `none`, with a warning. A
  `filter-[...]` body of that shape is CSS filter syntax (`filter-[blur(4px)]`, `filter-[url(#x)]`,
  `filter-[none]`), which stays an inert class beside the rest of the chain rather than naming a missing filter.
