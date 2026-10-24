### Fixed

- A text item carrying `min-w-[auto]` or `min-h-[auto]` now keeps the automatic minimum size along its parent's
  main axis, as it does with no minimum class. The `[auto]` form stood the floor down, so a `Label` or `Button`
  in a row could shrink below its widest word. Code that set `min-w-[auto]` to let such an item shrink must
  write `min-w-0` instead.
