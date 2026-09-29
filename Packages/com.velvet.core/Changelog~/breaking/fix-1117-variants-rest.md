### Changed

- A same-property tie between variants resolves in the order Tailwind emits them: `hover:`, `focus:`,
  `focus-visible:`, `active:` and `disabled:`, then `has-[…]:`, `aria-[…]:` and `data-[…]:`, then
  `supports-[…]:`, the breakpoints and `dark:`, each later one winning. The element states used to win over
  all of those, so `hover:w-[10px] aria-[busy=true]:w-[20px]` on a hovered, busy element was 10 px wide where it
  is now 20 px, and `hover:w-[20px] dark:w-[10px]` on a hovered element in dark mode was 20 px wide where it is
  now 10 px. A stacked variant still layers at the stronger of its two parts, which for `dark:hover:` is now the
  `dark:` layer.
