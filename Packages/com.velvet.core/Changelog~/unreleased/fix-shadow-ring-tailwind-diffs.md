### Fixed

- A `shadow-[…]`, `shadow-(--x)`, `inset-shadow-[…]`, `ring-[length:…]` or `inset-ring-[length:…]` reading a custom
  property that resolves to nothing now draws no box-shadow at all, as the browser does. It kept the ring beside a
  shadow, and the shadow beside a ring width. A bracket typed as a colour (`shadow-[color:var(--x)]`) or, on a ring,
  any type but a length (`ring-[number:var(--w)]`) is a colour and still leaves the rest drawn.
- A colour class, a `/N` or a `-inherit` beside a bracketed shadow now reaches only the layers Tailwind writes the
  colour variable into. `shadow-[+1px_+1px] shadow-red-500`, `shadow-[round(1px,1px)_round(2px,1px)] shadow-red-500`
  and `shadow-[0_0_hypot(1px)_hypot(2px)]` are drawn in the element's text colour, where they were painted red, and a
  layer written with no colour (`shadow-[0_4px_8px]`) is drawn in the text colour, where it took `rgb(4 12 24 / 0.2)`.
  A layer coming from a `var()` is judged by its source: `shadow-[var(--x)] shadow-blue-500` keeps the colour `--x`
  holds, and `[--a:5px] [--b:red] ring shadow-[#ggg] shadow-[0_0_var(--a)_var(--b)]` draws the ring and the shadow
  rather than discarding both.
- `shadow-[var(--x)]/50`, and the same on `inset-shadow-`, `text-shadow-`, `drop-shadow-`, `bg-`, `text-`, `border-`,
  `ring-`, `outline-`, `fill-`, `stroke-`, `accent-`, `caret-`, `decoration-` and `divide-` brackets, the families
  Tailwind writes a rule for with an opacity modifier, is read as a reader: it was ignored. A utility Tailwind writes
  no rule for with one (`gap-[var(--g)]/50`) is still ignored.
- An important shadow reader (`!shadow-[var(--x)]`) now ranks among the element's important shadow sizes by Tailwind's
  order: beside `!shadow-2xs`, which Tailwind emits before it, the reader holds the layers, where the size did.
- A reader of a custom property (`inset-shadow-[var(--x)]`, `gap-[var(--g)]`, …) resolved to a payload that was put
  ahead of a utility of its own family Tailwind emits before it, where a class of another family came earlier in the
  class list: `ring-2 inset-shadow-2xs inset-shadow-[var(--x)]` drew `inset-shadow-2xs` where Tailwind draws the layer
  `--x` holds. A class of another family no longer moves it.
- `font-[var(--w)]` and `font-(--w)` named the family `var(--w)` and `(--w)` beside the weight the variable resolved
  to.
- A length in `cqw`, `cqi`, `cqh`, `cqb`, `cqmin` or `cqmax` (`shadow-[0_0_4px_1cqw_#000]/50`, `w-[50cqw]`) was
  ignored. It is now measured against the content width of the nearest `@container` ancestor, and against the panel
  where there is none.
- A `shadow-lg/[var(--a)]` or `shadow-lg/(--a)` whose property resolves to nothing now draws no box-shadow, as the
  browser does.
- An important reader of a custom property in `gap-*`, `skew-*`, `font-*` or `divide-*` now ranks among the element's
  important utilities of its family by Tailwind's order: `!gap-4 !gap-[var(--g)]` computes the variable's gap, as it
  does in the browser, where the literal's was kept.
- A `shadow-[var(--x)]`, `ring-[length:var(--w)]` or `inset-ring-[length:var(--w)]` whose property a stylesheet
  declares no longer drops the element's ring and shadow before the property is first read: the unset waits until the
  element has been laid out on a panel.
