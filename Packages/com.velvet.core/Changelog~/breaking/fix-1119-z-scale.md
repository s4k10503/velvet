### Changed

- `z-*` takes Tailwind v4's values: any non-negative integer written without a leading zero (`z-15`), and a
  leading `-` negates a bracket value as well as a bare one (`-z-[5]` is -5). Only `z-0` through `z-50` in
  steps of ten were recognised before; `z-15` and `-z-[5]` were ignored.
- Equal `z-*` values stack in tree order: the sibling declared later paints on top, also after a keyed
  reorder or when an earlier sibling mounts later. They used to stack in the order they first mounted.

### Fixed

- A `z-*` change that keeps an element in the same layer (`z-10` to `z-30`) re-sorts it without detaching it
  from the panel, so it raises no `DetachFromPanelEvent`.
