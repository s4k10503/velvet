### Fixed

- A controlled value written to a bool control no longer allocates a copy of every stacked variant in the
  tree (`dark:hover:`, `dark:checked:` and the like) to settle the `checked:` and `peer-checked:` variants it
  feeds. The element-local settle now reads only the written element's own stacked variants, and both it and
  the `peer-checked:` offer, which still visits every stacked variant, walk a reused list rather than a fresh
  array. A drag release and a focus revert run the same element-local settle, over each element the press
  reached and over the reverted element, and no longer pay that copy either.

- A reconcile pass no longer allocates copies of the `group-*` / `peer-*` and stacked variant registries
  at its end, where it retargets those variants against the tree whenever the tree holds any of them; the
  retarget walks reused lists.
