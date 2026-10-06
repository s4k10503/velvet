### Fixed
- Restore the element's inline transition duration, delay, and easing after a Motion tween completes or is interrupted. Preserve code changes whose keyword or list entries differ from Velvet's last temporary value when it next writes or releases the tween's timing; identical reassignments and changes undone before observation do not replace the saved timing.
