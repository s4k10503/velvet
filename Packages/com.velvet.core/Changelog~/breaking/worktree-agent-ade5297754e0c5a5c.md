### Changed

- `StyleOverrides.BackgroundColor`, `StyleOverrides.Color` and `StyleOverrides.BackgroundImage` now win over a
  utility writing the same property — `bg-[#…]`, `text-[#…]`, `bg-red-500/50`, `bg-[addr:…]` and the gradient
  utilities — whichever was written last, as React's `style` prop wins over `className`; a keyword value
  (`StyleKeyword.Initial`, …) does too. Before, a utility applied after the override — a changed class on a
  patch, a `hover:` or other variant turning on, a gradient baked on mount — replaced it. An important utility
  (`!bg-[#…]`, `!bg-[addr:…]`) still wins over the override, and removing the override hands the property back
  to the utility instead of clearing it. `StyleBackgroundImageResolver.Apply` and `Clear` now write the
  utilities' image, which loses to a `StyleOverrides.BackgroundImage` in the same way; under such an override a
  gradient no longer writes `background-size`, and an `animate-gradient` or `animate-shimmer` beside it does not
  pan. The `styling-backgrounds.md` guide states the rule.
