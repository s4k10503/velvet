### Fixed

- A Portal that unmounts or moves to another element now takes its rows out of the element it rendered into first to
  last, so on a panel each row's `DetachFromPanelEvent` arrives in row order, as React removes a deleted portal's
  children. The rows left last to first before. Where those rows hold a Portal into the same element, a Component that
  another Portal mounts there now keeps patching its own row; its next render could reach another slot before.
