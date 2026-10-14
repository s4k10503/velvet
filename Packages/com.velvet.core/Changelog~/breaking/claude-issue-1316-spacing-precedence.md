### Changed

- A shorthand and a longhand of one family in one `className` (`pt-[2px] p-[8px]`) no longer leave the
  shorthand's value on the side the longhand names: the longhand holds it, as in Tailwind, and a stylesheet class
  that takes part in such a pair leaves the element's class list.
