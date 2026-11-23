### Fixed

- A text item's automatic minimum width or height now follows three changes it kept the old value through: a grid
  that stops sizing the item (it was left without a minimum until its text or box changed), an inline
  `overflow: hidden` set or cleared on it (the minimum stayed written, or stayed unwritten), and a parent resize
  under a bracket length that reads a percentage, such as `max-w-[calc(50%+10px)]` (the cap stayed at the pixels
  of the last derive).
