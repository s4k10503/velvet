### Fixed

- An important modifier written behind its variants, as Tailwind spells it, is honoured where it used to be
  ignored: `selection:!bg-red-500` colours a text field's selection, `hover:!pt-2` beside `hover:!p-[52px]`
  pads the top by 8px while hovered rather than 52px, and the space a silhouette paint reserves for a state
  border counts `hover:!border-8` as it counts `hover:border-8`.
