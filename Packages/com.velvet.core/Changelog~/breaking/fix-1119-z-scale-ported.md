### Changed

- Equal `z-*` values stack in tree order: the sibling declared later paints on top, also after a keyed
  reorder or when an earlier sibling mounts later. They used to stack in the order they first mounted.
- A `z-*` value beyond the 32-bit integer range stacks at that end of it, as CSS clamps it (`z-2147483648`,
  `z-[3000000000]`). It used to leave the class unresolved.

### Added

- A leading `-` negates a bracket `z-*` value as it does a bare one (`-z-[5]` is -5), as Tailwind v4 emits it.

### Fixed

- A `z-*` change that keeps an element in the same layer (`z-10` to `z-30`) re-sorts it without detaching it
  from the panel, so it raises no `DetachFromPanelEvent`.
