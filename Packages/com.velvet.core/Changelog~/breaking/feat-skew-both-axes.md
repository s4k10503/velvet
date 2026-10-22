### Fixed

- An important skew (`!skew-x-6`, `skew-x-6!`) applies and outranks every plain skew on its axis; it was ignored,
  and a plain skew beside it was drawn instead. An important skew behind a variant (`hover:!skew-x-6`) outranks
  an important base skew while the variant holds; it was read as a plain one, which lost to it.
- A skew bracket whose angle the browser rejects leaves the element unskewed when it is important (`!skew-x-[5px]`)
  or writes both axes (`skew-[10]`), as it already did for a plain `skew-x-[…]` or `skew-y-[…]`; it was ignored,
  and the other skew classes on the element were drawn.
- A bare `skew-x-*` or `skew-y-*` value with a leading zero (`skew-x-07`) is declined, as Tailwind generates no rule
  for it; it was read as that many degrees.
