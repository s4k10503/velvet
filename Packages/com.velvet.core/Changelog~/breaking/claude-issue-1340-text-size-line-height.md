### Changed

- Each `text-xs`…`text-9xl` now sets Tailwind v4's paired line height with its font size, so text under one is
  laid out on the paired line height (`text-sm` is 1.4286 times its size, `text-5xl` and up 1). A
  `leading-*` on the same element still wins whatever the class order or `!`, and a nearer `text-<size>` replaces
  an ancestor's `leading-*`.
- The text a `Label` or `Button` displays now carries the `<line-height=…>` tag a `leading-*` already wrote, so
  code that reads it back off the element sees the tag, and a label with `enableRichText = false` shows the markup
  as literal text, the caveat `fonts.md` already gives for `leading-*`.
- `text-<size>/<leading>` (`text-sm/6`, `text-[14px]/6`, `hover:text-sm/6`) sets the size's font size and the matching
  `leading-*` line height, and `text-[length:14px]` reads as `text-[14px]`.
