### Fixed

- A Portal that unmounts or moves to another element now takes its rows out of the element it rendered into first to
  last, as React removes a deleted portal's children. It took them out last to first before.
- When a Portal leaves an element with another Portal into that element declared inside its rows, a Component that a
  third Portal mounts there now keeps patching its own row. Its next render could miss that row before.
