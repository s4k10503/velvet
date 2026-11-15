### Fixed

- A weight, italic or width class on an element with no `font-<name>` of its own now keeps the family of the font an
  author's stylesheet or inline style gives an element above it, as CSS keeps an inherited `font-family`, and takes
  that family's face where one is registered; a font no registered family holds is kept and the weight or italic is
  emulated on it, where the face was taken from `VelvetFonts.DefaultFamily` before.
- A change to the font registry, whether `DefaultFamily` changing, a family registered or unregistered or the registry
  cleared, now reaches the elements whose font a `font-<name>` or weight class resolved, which kept the asset they
  first resolved until they were created again. The elements that inherit their parent's font are
  read again at each update of their panel only while one with no `font-<name>` ancestor exists.
