### Added

- **Project Settings ▸ Velvet** lets a project leave the utility stylesheet's holder, or any of the four
  bundled shaders, out of its player builds. Both were carried into every build with no way to exclude
  them short of deleting them from the package. A player built without the holder throws from
  `VelvetStyleUtilities.Sheet`, naming the setting; a paint whose shader was left out and reaches the
  player by no other route draws nothing and logs the missing shader once.

### Fixed

- A player build no longer fails on a read-only `ProjectSettings/GraphicsSettings.asset` or
  `ProjectSettings/ProjectSettings.asset` when it has nothing to write there: a project that lists the
  shaders in Always Included Shaders, or preloads the stylesheet's holder, itself — or opts out of them —
  and has no injection left over from an earlier build builds from a read-only file. The build still
  refuses a read-only file it would have to write.
