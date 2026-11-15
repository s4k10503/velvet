### Performance

- Mounting, hiding and revealing many sibling Portals no longer costs the square of their count. A Suspense hiding a
  primary of 1,000 Portals into one target takes 0.25 s where it took 17.7 s, and the work per Portal grows by about
  twice from 250 to 1,000 Portals rather than by about eight times. The hide looks up the Portals under each hidden
  root and the fibers each Portal owns by index, a Portal's children are told apart from the ones before them without
  reading every child of the declaring component, and a portal row's hold on the default font family decides again
  only the held elements it can change.
