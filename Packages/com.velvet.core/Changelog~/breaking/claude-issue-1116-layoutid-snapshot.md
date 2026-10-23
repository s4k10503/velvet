### Changed

- A `V.Motion(layoutId:)` whose last holder of an id has left the tree leaves its box under that id until
  another Motion takes it, and a Motion that mounts under the id in a later batch tweens from that box, as
  Framer Motion's shared-layout stack keeps the snapshot of its last lead. It used to appear in place unless
  it mounted in the same batch as the one that left.
