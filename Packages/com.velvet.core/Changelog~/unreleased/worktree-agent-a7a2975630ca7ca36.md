### Fixed

- A shadow size's `/N` keeps scaling the colour class beside it when another size without one holds the layers:
  `shadow-lg/50 shadow-xl shadow-red-500` draws `shadow-xl` in red at 50%, and `shadow-lg/150 shadow-xl
  shadow-red-500` draws no box-shadow, as the browser does with Tailwind's `--tw-shadow-alpha`. A later size used
  to reset the scale to 100%. The same applies to `inset-shadow-*`.
- A shadow size without a `/N` takes the layers from a plain size with one, whichever is written first, as
  Tailwind emits the size with the `/N` first: `shadow-lg/150 shadow-lg` draws `shadow-lg`'s layers at their own
  10%. The size with the `/N` used to win wherever its name sorted after the other's. The same applies to
  `inset-shadow-*`.
- A `-none` with an opacity modifier (`shadow-none/50`), which Tailwind writes no rule for, no longer stops the
  size before it from reading a rejected colour: `shadow-lg shadow-none/50 shadow-[color:0_4px_8px]` draws no
  box-shadow, as the browser does, where it used to draw `shadow-lg`. The same applies to `inset-shadow-*`.
- `shadow-inherit` or `shadow-initial` with an opacity modifier (`shadow-inherit/50`), which Tailwind writes no
  rule for, no longer replaces a rejected shadow colour beside it: `shadow-lg shadow-inherit/50
  shadow-[color:0_4px_8px]` draws no box-shadow, as the browser does, where it used to draw `shadow-lg`. The same
  applies to `inset-shadow-inherit` and `inset-shadow-initial`.
- The bare `shadow` with an opacity modifier (`shadow/50`) draws its layers at that alpha. It used to draw
  nothing.
- The outer box-shadow of an element that clips its content (`overflow-hidden`) takes `shadow-current` and `em`
  lengths from the element itself. It used to take them from its parent.
