### Added

- A `disabled:` variant, as Tailwind's. Its payload holds while the element or any ancestor is disabled — the
  condition USS `:disabled` matches — whether a Velvet `enabled:` prop or a `SetEnabled` call made it so, and
  it stacks with the other variants in either order (`dark:disabled:`, `disabled:hover:`). On a tie it ranks
  above `active:`, where Tailwind emits it. `StyleVariantKind.Disabled` is the new member.

- `group-disabled:` and `peer-disabled:`, as Tailwind's. They read the marked `group` ancestor or `peer` sibling
  on the same terms `disabled:` reads the element, and rank above the other `group-*` and `peer-*` states
  respectively on a tie. `StyleVariantKind.GroupDisabled` and `StyleVariantKind.PeerDisabled` are the new members.

- `nth-N:` and `nth-last-N:`, Tailwind's functional forms of `[&:nth-child(N)]:` and `[&:nth-last-child(N)]:`.
  As in Tailwind, `N` is a whole number written without leading zeros, and `nth-0:` matches no child.

### Fixed

- Two variant rules that set the same inline-resolved property keep a value each, so one turning off no longer
  removes the other's: `odd:bg-[#fff] even:bg-[#eee]` colours the odd rows, and
  `data-[state=open]:w-[10px] data-[state=closed]:w-[20px]` keeps 10 px while the state is open.

### Changed

- `StyleVariantClass.BreakpointPx` and `StyleVariantClass.IsResponsive` refuse a `StyleVariantKind` value naming
  no member with an `ArgumentOutOfRangeException` naming the parameter, where they threw the exception a switch
  expression raises for a value none of its arms matches.
