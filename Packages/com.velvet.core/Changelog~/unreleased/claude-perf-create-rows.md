### Fixed

- Creating an element whose classes carry no variant (`hover:`, `dark:`, `group-`, `data-[...]:` and the other `:` prefixed forms) or custom-property token no longer runs the ten variant configure passes, each of which scanned and allocated for nothing. An element that already holds variant state is still visited, so a re-render that drops its last variant removes the manipulator as before.
