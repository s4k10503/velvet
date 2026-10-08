### Changed

- A field control's `className` background (`bg-*`, gradients included), border (`border`, `border-*`, and the
  `border-solid` / `-dashed` / `-dotted` line style), radius (`rounded`, `rounded-*`), padding (`p-*`),
  `shadow-*`, `ring-*` and `outline-*` utilities now paint the box the value is shown in, as the same classes do
  on an `<input>` or a `<select>`, on `V.TextField`, `V.IntegerField`, `V.DropdownField` and `V.Custom<T>` for a
  text-input or popup field type. They used to land on the outer control and leave the theme's own background
  and border on the box. The state, theme, responsive and relational variants written on them go with it, and
  `peer-*` / `group-*` still find their source from the control. A field that relied on the outer control
  carrying them (a padded, bordered or shadowed frame around a labelled field) now has them on the box and
  nothing on the frame. Layout and size utilities stay on the outer control.
