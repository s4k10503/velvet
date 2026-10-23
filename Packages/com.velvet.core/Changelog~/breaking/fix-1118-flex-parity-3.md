### Changed

- Before it is attached to a panel, an element carrying no direction class (`flex`, `flex-row`, `flex-col`
  or a reversed form) spaces a gap as the column it lays out in, as it does once attached: `gap-4` alone
  writes `margin-top` between its children, and `gap-x-4` alone, a column gap in a column, writes nothing.
  Both used to space it as a row, on `margin-left`, until it was attached.
