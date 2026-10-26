### Fixed

- A `V.Portal` whose children suspend with no `V.Suspense` above it, in the render that mounts it, now renders them
  once the resource resolves. The retry used to mount a second placeholder and a second copy of the children,
  which read the resource again and suspended again, so the Portal stayed empty and the host's changed effect never
  ran. The host's rows stay where the render put them, the Portal holds none of its children until the retry
  mounts them, and the Portals queued behind it in the same pass mount instead of being dropped.
