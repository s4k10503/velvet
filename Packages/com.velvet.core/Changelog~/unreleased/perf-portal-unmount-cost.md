### Changed

- Unmounting many Portals no longer scans, once per Portal or per fiber, the list of held default-font roots, every
  recorded Portal range, a Portal's list of the inline fibers it owns, or every placed Portal. Releasing a hold on a
  root leaves the other held roots where they are; deciding whether a removed element holds a Portal target reads that
  element's subtree; a fiber leaves its Portal's list by its own node; and hiding or revealing a Suspense primary finds
  the Portals under the elements it holds from those elements rather than from every Portal placed.
