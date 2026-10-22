### Changed

- An important modifier written ahead of the variants, which Tailwind generates no rule for, leaves the class
  inert: `!selection:bg-red-500` no longer colours a text field's selection. Write `selection:!bg-red-500`.
