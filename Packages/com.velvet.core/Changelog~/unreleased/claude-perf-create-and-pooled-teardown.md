### Fixed

- Clearing or replacing rows of `V.Button`s no longer unregisters each pooled Button's click-gate callbacks,
  a cost that grew with the number of elements created before it. The gate stays registered on the pooled
  Button, with its press state cleared, and the next rent of that Button uses it again.
- Creating an element no longer runs the text-effect pass for one that carries no effect class of its own.
