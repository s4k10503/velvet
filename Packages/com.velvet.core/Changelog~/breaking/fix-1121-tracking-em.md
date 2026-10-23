### Changed

- `tracking-tighter`, `tracking-tight`, `tracking-wide`, `tracking-wider` and `tracking-widest` are Tailwind's
  em scale (-0.05em to 0.1em) of the element's own font size, where they were pixel values baked at a
  16px font. Text at any other size is spaced in proportion to its size, and a mounted element follows its
  font size when that changes. The classes no longer have a rule in the bundled stylesheets, so a panel that
  resolves them reads them from Velvet's resolver, as it does `tracking-[0.025em]`.
- The `overline` rule sits with its top edge on the first line's top, where it used to sit 15% of the font
  size below it.
