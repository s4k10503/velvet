### Changed

- `skew-*` on an element with no other source of `rotate` or `scale` is drawn as the element's own transform, so the
  face, the border and the descendants shear together, `origin-*` moves the pivot, and a hidden overflow no longer
  cuts the sheared face's overhang. The element no longer carries the painted silhouette, the descendant seats or the
  suppressed native face; a pointer picks the bounding rectangle of the sheared box.
