### Changed

- A Motion `Spring` or `Bezier` variant swap now animates the font-size presets (`text-sm` → `text-2xl`), the
  tracking presets (`tracking-tight` → `tracking-wide`), `rounded-full` and the `-full` lengths (`w-full`,
  `size-full`, `left-full` and the rest of the sizing, position and `basis-` families, as 100%), which landed
  instantly before. A swap that names one of them now runs a play where it used to apply the classes at once,
  so its completion callbacks and exit waits last as long as the play does.
