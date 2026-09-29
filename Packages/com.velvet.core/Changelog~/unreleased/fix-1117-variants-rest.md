### Added

- A `disabled:` variant, as Tailwind's. Its payload holds while the element or any ancestor is disabled — the
  condition USS `:disabled` matches — whether a Velvet `enabled:` prop or a `SetEnabled` call made it so, and
  it stacks with the other variants in either order (`dark:disabled:`, `disabled:hover:`). On a tie it ranks
  above `active:`, where Tailwind emits it. `StyleVariantKind.Disabled` is the new member.

- `group-disabled:` and `peer-disabled:`, as Tailwind's. They read the marked `group` ancestor or `peer` sibling
  on the same terms `disabled:` reads the element, and rank above the other relational states on a tie.
  `StyleVariantKind.GroupDisabled` and `StyleVariantKind.PeerDisabled` are the new members.

- `nth-N:` and `nth-last-N:`, Tailwind's functional forms of `[&:nth-child(N)]:` and `[&:nth-last-child(N)]:`,
  for a positive integer `N`.

### Changed

- `StyleVariantClass.BreakpointPx` and `StyleVariantClass.IsResponsive` refuse a `StyleVariantKind` value naming
  no member with an `ArgumentOutOfRangeException` naming the parameter, where the exception was a
  `SwitchExpressionException`.
