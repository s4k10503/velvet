### Fixed

- Siblings sharing a key in a container of plain elements are reported with a warning on every render that
  repeats the key, mount included, as React's “Encountered two children with the same key” is. A repeat
  went unreported at mount and on a render keeping the siblings in the same order, and a render whose
  children no longer repeated a key was reported for the render before it.
