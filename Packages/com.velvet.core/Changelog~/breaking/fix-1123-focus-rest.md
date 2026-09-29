### Changed

- A `singleTabStop` group is the nearest group around the focused element, so a plain focus scope nested
  inside a group no longer hides its members from the group: Tab from inside it leaves the group, and Tab or
  Shift-Tab entering the group lands on its roving stop. Before, Tab moved to the next member, and a backward
  entry landed on the group's last member.

- Every focus scope around a focused element records the landing, not only the innermost one. A
  `restoreFocus` scope first entered through a scope nested in it now returns focus on unmount, where before it
  restored nothing; a `singleTabStop` group re-enters at a member inside a nested scope, and a `contain` scope
  pulls focus back to one.

- The `focus-visible:` variant and `UseFocusRing` read their panel's last input, React Aria's input modality
  kept per panel: after a pointer press anywhere in the panel, a programmatic `Focus()` no longer lights the
  focus-visible state until a key press or a navigation move there. Before, only a press on the element itself
  kept it dark.
