### Fixed

- A border-width utility (`border`, `border-2`, `border-x-4`, `border-s-2`, `border-[3px]`, under any variant) now draws
  in the element's text colour, as Tailwind v4's preflight `border-color: currentcolor` does, and follows the text colour
  as it changes. So do the children of a `divide-*` parent (dashed dividers included), the target of a child-scoped width
  (`*:border`, a field's input box), and an element a USS rule of a stylesheet that is not a control theme's gives a border width when the rule selects it by a
  class, its name, its type or `*`. It gives way, side by side, to any rule of a stylesheet that is not a control theme's that sets
  that side's border colour (a `border-*` colour utility or variant, a USS rule of any selector, `:hover` and
  `border-color: transparent` included) and to a colour your code writes inline, unless that is the colour Velvet last
  wrote to the side. A control theme's border does not hold it back, as preflight outranks a browser's default styles.
  A width written inline by code gives no preflight colour to an element no rule selects, and a stylesheet added to an
  element more than two levels under the panel's root is noticed when an element under it attaches.
