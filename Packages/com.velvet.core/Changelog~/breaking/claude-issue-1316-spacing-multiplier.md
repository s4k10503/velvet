### Changed

- A stylesheet of your own that declares a class named like a spacing step Velvet now resolves (`.w-100`,
  `.p-30`, `.top-13`) is shadowed by the inline value that class name now produces. Rename the rule, or write the
  value as a bracket class and drop the name. Likewise, a shorthand and a longhand of one family in one `className`
  (`pt-[2px] p-[8px]`) no longer leave the shorthand's value on the side the longhand names: the longhand holds
  it, as in Tailwind, and a stylesheet class that takes part in such a pair leaves the element's class list.
