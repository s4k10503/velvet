### Changed

- A `has-[…]:`, `data-[…]:` or `aria-[…]:` payload wins a same-property tie against `hover:`, `focus:`,
  `focus-visible:`, `active:` and `disabled:`, as it does in Tailwind, which emits has, aria and data after those
  states. The element state used to win, so `hover:w-[10px] aria-[busy=true]:w-[20px]` on a hovered, busy
  element was 10 px wide where it is now 20 px.
