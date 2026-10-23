### Fixed

- An important `z-*` (`!z-10`, `z-10!`, `!z-[5]`, `z-[5]!`) wins over every plain `z-*` on the same element,
  wherever it sits in the class list, as Tailwind's `z-index: 10 !important` does. The modifier was accepted
  and ignored, so `!z-10 z-40` stacked the element at 40.

- A `peer-*:` consumer reacts to a preceding `peer` source that is itself z-managed (such as `absolute z-10`).
  The search read the placeholder the source leaves at its declared slot, which carries none of the
  source's classes, so the consumer never found it.

- A `group-*:` or `peer-*:` consumer lights at once when its source is already under the pointer, or already
  holds focus inside it, as the consumer mounts or a keyed reorder moves it. It used to wait for the next
  pointer or focus event on the source, and a moved consumer dropped a lit hover until then.
- A `group` or `peer` source that code outside a render adds to, removes from or reorders within a panel is
  picked up or released at the panel's next scheduler update. It used to wait for the next render.
