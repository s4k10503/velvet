### Fixed

- `VelvetFonts.DefaultFamily` reaches a label with no font class, as the `font-family` Tailwind's preflight gives
  `html` reaches a `<p>` with no class: Velvet writes the family inline on the element a tree is mounted on, on a
  portal's container that is not inside one already, and on the root of a layer or world-space panel, and what is
  under them inherits it. It used to reach an element with a weight, italic or width class and what was under it,
  so a label with no font class elsewhere kept whatever font its stylesheets and ancestors gave it. A family an
  element names still decides for what is under it, and a font an author's stylesheet rule or inline style gives
  one of those elements, or an element above it, is inherited instead of the default family. A change to the
  registry reaches the written elements with no render.
