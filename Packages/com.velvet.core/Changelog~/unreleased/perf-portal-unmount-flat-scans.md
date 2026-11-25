### Changed

- Unmounting many sibling Portals no longer costs the square of their count. Taking down each removed element walked
  every Portal placed and read each one's range from its rows, read every row of a Portal's target to find that
  Portal's own, and removing a Portal rewrote the start of every range behind it on the target the Portals share. A
  removal now finds the Portals under the element it takes down from that element's subtree and reads only their rows,
  and a target keeps its ranges' starts against an offset of its own, so a removal writes whichever side of it holds
  fewer ranges. Unmounting many sibling Portals that each render a component among their children still reads every
  such component on the target once per Portal.
