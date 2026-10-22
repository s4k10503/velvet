### Added

- CSS custom properties as classes: `[--name:value]` declares one on an element and its descendants inherit it,
  `var(--name)` and `var(--name, fallback)` read it in a utility's bracket or its modifier's
  (`w-[calc(var(--w)*2)]`, `from-[var(--brand)]`, `bg-red-500/[var(--alpha)]`), and Tailwind v4's
  `bg-(--brand)` / `bg-red-500/(--alpha)` shorthand and `[property:value]` arbitrary properties read it too.
  Custom properties a USS stylesheet declares are read as well. A `var()` that does not resolve computes the
  property to `unset`, as in a browser. These classes were inert before. See
  `Documentation~/styling-custom-properties.md`.
- A shorthand's bracket takes one value per side, as the CSS property does: `p-[1px_2px]`, `m-[0_auto]`,
  `inset-*`, `rounded-*`, `border-[length:…]`, `border-[color:…]`, and a row and a column gap in `gap-[…]`.
- `ring-current`, `outline-current`, `leading-[inherit]`, `font-[weight:inherit]`, `font-[number:…]`,
  `font-[family-name:…]` and `font-[generic-name:…]`, and a percentage in an opacity modifier's bracket
  (`bg-red-500/[50%]`), as Tailwind writes them.
