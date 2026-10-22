### Fixed

- An important modifier written behind the variants, as Tailwind spells it (`sm:!pt-2`), is read as important
  where Velvet weighs a whole class list, so `sm:!pt-2` beside `sm:!p-[52px]` takes its side as `sm:pt-2`
  beside `sm:p-[52px]` does. A bang ahead of the variants (`!sm:pt-2`) is no longer read as important there,
  as Tailwind generates no rule for it.
