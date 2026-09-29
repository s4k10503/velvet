### Changed

- A same-property tie between variants resolves the way Tailwind's generated CSS resolves it: by specificity,
  then by the order Tailwind emits the rules. A media or feature query adds no specificity, so `supports-[…]:`,
  the breakpoints and `dark:` sit with the base utility, followed by `[&>*]:`, which carries one class on each
  child. `group-*`, `peer-*`, `first:`/`last:`/`only:`/`odd:`/`even:`, the element states, `has-[…]:`,
  `aria-[…]:`, `data-[…]:` and `nth-N:` each add a pseudo-class or attribute selector and rank above all of those,
  in that order, with every arbitrary `[&:…]:` selector last. What changes for a working application:
  - `has-[…]:`, `aria-[…]:` and `data-[…]:` win over the element states and `group-*`/`peer-*`, where they lost:
    `hover:w-[10px] aria-[busy=true]:w-[20px]` on a hovered, busy element was 10 px wide and is now 20 px.
  - The structural variants win over `md:`, `dark:`, `group-*` and `peer-*`, where they lost to all of them, and
    `[&:nth-child(N)]:` and the other `[&:…]:` forms win over every single named variant.
  - A container's `[&>*]:` payload wins over a child's own `md:`, `dark:` and `supports-[…]:` payloads, where it
    lost to them; it still loses to the child's own pseudo-class variants.
  - A stacked variant carries the specificity of all its parts added together and ranks by every part, where it
    took its stronger part's place: `hover:focus:w-[20px] active:w-[10px]` on a hovered, focused, pressed element
    was 10 px wide and is now 20 px; `dark:hover:shadow-lg hover:shadow-sm` in dark mode paints `shadow-lg`
    whichever is written later; and a container's `[&>*]:hover:` payload now beats the child's own `hover:`.
  - An `aria-[…]:` and a `data-[…]:` payload no longer tie: `data-[…]:` wins, as Tailwind emits it later.
