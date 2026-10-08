### Added

- `pointer-events-none` and `pointer-events-auto`. `pointer-events-none` makes an element and its whole
  subtree transparent to the pointer while it keeps painting and stays enabled, so a click reaches whatever is
  behind it — CSS's `pointer-events: none`, a uGUI CanvasGroup with `blocksRaycasts` off. `pointer-events-auto`
  on a descendant takes that descendant back. Elements mounted into the subtree later join it, every element
  gets back its own picking mode when the utility goes away, and both utilities work behind the `dark:`,
  responsive and `group-` variants. See `Documentation~/styling-pointer-events.md`.
