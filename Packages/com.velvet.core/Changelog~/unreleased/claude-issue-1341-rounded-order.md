### Fixed

- Overlapping `rounded-*` utilities in one `className` now resolve as Tailwind orders them, the one Tailwind emits
  later holding the shared corner whichever is written first: `rounded-t-[2px] rounded-r-[8px]` and
  `rounded-r-[8px] rounded-t-[2px]` both give the top-right corner 8px. It covers the shorthand against a side or a
  corner, a side against a corner, a side against a side (`rounded-l` and `rounded-b` on the bottom-left), and the
  logical forms (`rounded-s-*`, `rounded-e-*`, `rounded-ss-*`, `rounded-se-*`, `rounded-es-*`, `rounded-ee-*`)
  against the physical ones, for a bracket value and for a scale step alike, and in a `V.Motion` pose. A stylesheet
  class that takes part is written inline, so it leaves the element's class list. The bundled `rounded-*` rules
  are declared in Tailwind's order too, so two classes on one element no longer depend on the order of the sheet.
