### Changed

- A same-property tie between variants resolves in the order Tailwind emits them, each later one winning:
  `group-*` and `peer-*`, then `first:`, `last:`, `only:`, `odd:` and `even:`, then `hover:`, `focus:`,
  `focus-visible:`, `active:` and `disabled:`, then `has-[…]:`, `aria-[…]:`, `data-[…]:` and `nth-N:`, then
  `supports-[…]:`, the breakpoints and `dark:`, and last every arbitrary `[&:…]:` selector such as
  `[&:nth-child(2)]:`. The structural variants used to lose to all of those, and the element states used to
  win over everything after them, so `hover:w-[10px] aria-[busy=true]:w-[20px]` on a hovered, busy element was
  10 px wide where it is now 20 px, and `hover:w-[20px] dark:w-[10px]` on a hovered element in dark mode was
  20 px wide where it is now 10 px. A stacked variant still layers at the stronger of its two parts, which for
  `dark:hover:` is now the `dark:` layer.
