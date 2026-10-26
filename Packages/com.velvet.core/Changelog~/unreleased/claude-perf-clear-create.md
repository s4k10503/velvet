### Fixed

- Removing a row that is a plain container, as clearing or replacing a list of such rows does, no longer
  unregisters the callbacks of the Buttons and text leaves inside it: they leave with the row. In the editor
  those unregistrations were most of the time clearing a list took, because the time one unregistration takes
  grows with the number made before it. A row that is itself a Button or a Label, or that holds a Portal's
  target, unregisters as before, since those elements are used again.
