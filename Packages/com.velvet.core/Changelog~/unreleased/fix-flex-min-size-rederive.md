### Fixed

- A text item's automatic minimum width or height is now derived again after three changes that left it at the old
  value: a grid that stops sizing the item (it had no minimum until its text or box changed), an inline
  `overflow: hidden` written or cleared on it (the next derive, from its own geometry, its text or a parent event
  that reads the box, now sees it; nothing derives on the style write itself), and a parent resize under a bracket
  length that reads a percentage, such as `max-w-[calc(50%+10px)]` or `w-[calc(50%+10px)]` (the cap is now taken
  against the parent's box at the derive rather than from the pixels the last tick wrote).
