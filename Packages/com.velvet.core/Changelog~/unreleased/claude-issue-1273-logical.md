### Added

- Tailwind's logical-direction utilities resolve, read as the physical utility each equals with the inline axis
  running left to right and the block axis top to bottom: `ms-*`, `me-*`, `mbs-*`, `mbe-*`; `ps-*`, `pe-*`, `pbs-*`,
  `pbe-*`; `start-*`, `end-*`, `inset-s-*`, `inset-e-*`, `inset-bs-*`, `inset-be-*`; `border-s`, `border-e`,
  `border-bs`, `border-be`; and `rounded-s-*`, `rounded-e-*`, `rounded-ss-*`, `rounded-se-*`, `rounded-es-*`,
  `rounded-ee-*`. They take the spacing scale, bracket values, `auto` on a margin or an inset, `full` and fractions on
  an inset, and a minus sign on a margin or an inset, under variants and `!`. They were left in the class list and
  resolved nothing. `scroll-ms-*` and the other scroll margin and padding utilities still resolve nothing.
