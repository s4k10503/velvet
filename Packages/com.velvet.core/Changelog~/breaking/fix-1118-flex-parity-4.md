### Changed

- A wrapping gap container that declares its own width, height, minimum or maximum gives its items the room
  CSS gives them and takes its declared size among its siblings: `w-[150px] flex flex-wrap gap-4` holds two
  66px items on one line and its next sibling starts 150px along. The half-margins the wrap path writes used to
  leave such a container's items a gap less room, so the second item wrapped, and the container took a gap
  less than its declared size, so its next sibling started 134px along.
