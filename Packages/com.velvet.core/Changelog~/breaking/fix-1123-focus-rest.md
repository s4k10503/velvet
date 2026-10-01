### Changed

- Of `singleTabStop` groups nested in each other, the outermost one below the nearest `contain` scope decides,
  as the outermost of nested toolbars does in React Aria's `useToolbar`, and a plain focus scope nested in a group
  is part of the group. Tab from anywhere inside leaves all of them, arrows move across nested groups, and entry
  lands on the member last used at any depth. Before, the innermost scope decided: Tab from inside a plain scope
  in a group moved to the next member, a backward entry landed on the group's last member, and arrows could not
  cross from a nested group to the outer one's other members.

- Every focus scope around a focused element records the landing, not only the innermost one: a
  `singleTabStop` group re-enters at a member inside a nested scope, and a `contain` scope pulls focus back to
  one.

- A `restoreFocus` scope returns focus to the element focused when the scope mounted, React Aria's
  `nodeToRestore`. Before, it returned focus to the element focus first entered the scope from, and a scope
  first entered through a scope nested in it restored nothing.

- Focus leaving the content of a portal declared inside a `contain` scope, by Tab or by a move to another
  element, is pulled back into the scope, on the portal's own panel or across panels, as React Aria's contained
  scope pulls back focus leaving a child scope. Before, it escaped. A blur to nothing from that content is left
  alone, as React Aria's is.

- The `focus-visible:` variant and `UseFocusRing` follow React Aria's input modality, one reading written by every
  panel Velvet renders into. After a pointer press, release or move, a programmatic `Focus()` no longer lights
  the focus-visible state until a key press or release or a navigation move; before, only a press on the element
  itself kept it dark. While an element holds focus, a key press lights it and a pointer press darkens it.
