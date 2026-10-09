### Changed

- `StyleOverrides.BackgroundColor` and `StyleOverrides.Color` now win over an arbitrary-value utility writing
  the same property (`bg-[#…]`, `text-[#…]`, `bg-red-500/50`) whichever was written last, as React's `style`
  prop wins over `className`. Before, a utility applied after the override — a changed class on a patch, or a
  `hover:` or other variant turning on — replaced it. An important utility (`!bg-[#…]`) still wins over the
  override, and removing the override hands the property back to the utility instead of clearing it. The
  `styling-backgrounds.md` guide states the rule.
