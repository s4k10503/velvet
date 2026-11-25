### Changed

- Five steps of unmounting, hiding and revealing Portals no longer search a list once per Portal or per fiber.
  Releasing a hold on a default-font root leaves the other held roots where they are. Deciding whether a removed
  element holds a Portal target reads that element's subtree rather than every recorded range. Releasing the
  same-panel event bridge of a target reads a count kept per target rather than every range. A fiber leaves the list
  of inline fibers its Portal owns by its own node. A Suspense hide or reveal finds the Portals under the elements it
  holds from those elements rather than from every Portal placed.
