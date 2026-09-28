### Fixed

- `z-auto` resets the element to unstacked, so `z-10 z-auto` no longer stacks at 10.
- An important `font-*` or text-effect utility (`!font-[weight:700]`, `!leading-[24px]`, `!uppercase`,
  `!whitespace-pre-line`) applies and wins over the element's plain ones, and an important variant payload
  (`hover:!font-[…]`) wins over an important base one. `!font-[…]` and every important text-transform,
  text-decoration, `leading-*` and `whitespace-pre-line` token used to be dropped entirely.
- A `peer-*:` consumer, or a `peer` source, carrying `clip-path-*` is matched through the wrapper that holds
  its slot. A consumer's search started inside the wrapper and found no sibling, and a clipped
  `peer-checked:` source's checked state was never read.
