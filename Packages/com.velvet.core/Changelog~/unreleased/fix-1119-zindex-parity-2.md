### Fixed

- A `group-*:` or `peer-*:` consumer lights at once when its source is already under the pointer, or already
  holds focus inside it, as the consumer mounts or a keyed reorder moves it. It used to wait for the next
  pointer or focus event on the source, and a moved consumer dropped a lit hover until then.
- A `group` or `peer` source that code outside a render adds to, removes from or reorders within a panel is
  picked up or released at the panel's next scheduler update. It used to wait for the next render.
