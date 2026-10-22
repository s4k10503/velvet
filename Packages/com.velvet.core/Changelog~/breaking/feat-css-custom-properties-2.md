### Changed

- A `font-[…]` family is read as Tailwind reads a bracket: `_` is a space and `\_` an underscore, so
  `font-[Open_Sans]` names `Open Sans`. It used to keep the underscore; write `font-[My\_Font]` for a family
  whose name holds one. An `addr:` key keeps its underscores.
