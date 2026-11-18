### Fixed

- A `shadow-[…]`, `shadow-(--x)`, `inset-shadow-[…]`, `ring-[length:…]` or `inset-ring-[length:…]` reading a custom
  property that resolves to nothing now draws no box-shadow at all, as the browser does. It kept the ring beside a
  shadow, and the shadow beside a ring width. A `ring-[number:var(--w)]`, a bracket that is not a length, is the ring's
  colour and still leaves the shadow drawn.
- A colour class, a `/N` or a `-inherit` beside a bracketed shadow now reaches only the layers Tailwind writes the
  colour variable into. `shadow-[+1px_+1px] shadow-red-500`, `shadow-[round(1px,1px)_round(2px,1px)] shadow-red-500`
  and `shadow-[0_0_hypot(1px)_hypot(2px)]` are drawn in the element's text colour, where they were painted red, and a
  layer written with no colour (`shadow-[0_4px_8px]`) is drawn in the text colour, where it took `rgb(4 12 24 / 0.2)`.
  A layer coming from a `var()` is judged by its source: `shadow-[var(--x)] shadow-blue-500` keeps the colour `--x`
  holds, and `[--a:5px] [--b:red] ring shadow-[#ggg] shadow-[0_0_var(--a)_var(--b)]` draws the ring and the shadow
  rather than discarding both.
- `shadow-[var(--x)]/50`, a shadow reader with a numeric modifier, is read: it was ignored.
- An important shadow reader (`!shadow-[var(--x)]`) now ranks among the element's important shadow sizes by Tailwind's
  order: beside `!shadow-2xs`, which Tailwind emits before it, the reader holds the layers, where the size did.
- A reader of a custom property (`inset-shadow-[var(--x)]`, `gap-[var(--g)]`, …) resolved to a payload that was put
  ahead of a utility of its own family Tailwind emits before it, where a class of another family came earlier in the
  class list: `ring-2 inset-shadow-2xs inset-shadow-[var(--x)]` drew `inset-shadow-2xs` where Tailwind draws the layer
  `--x` holds. A class of another family no longer moves it.
- `font-[var(--w)]` and `font-(--w)` named the family `var(--w)` and `(--w)` beside the weight the variable resolved
  to.
