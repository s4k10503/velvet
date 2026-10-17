### Changed

- Two `rounded-*` utilities that write different properties and share a corner (`rounded-t-[2px] rounded-r-[8px]`, `rounded-l-sm rounded-b-lg`,
  `rounded-r rounded-t-lg`) no longer leave the corner to whichever is written last or declared last in the
  stylesheet: the one Tailwind emits later holds it, and a stylesheet class that takes part in such a pair leaves
  the element's class list.
