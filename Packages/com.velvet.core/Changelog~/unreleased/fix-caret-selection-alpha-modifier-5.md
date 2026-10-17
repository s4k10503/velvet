### Fixed

- An important modifier written behind the variants, as Tailwind spells it (`hover:!translate-y-[10]`), is read as
  important where Velvet weighs a whole class list, so `!translate-x-4 hover:!translate-y-[10]` no longer
  translates on hover. One written ahead of the variants (`!hover:translate-y-[10]`) leaves the class inert, as
  Tailwind generates no rule for it, where those passes used to read it as important.
