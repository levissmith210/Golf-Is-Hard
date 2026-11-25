AERO ISLES - INTERACTIVE COURSE

The saved FrutigerAeroNineHole scene now includes:
- Hole 2: two cyan shot targets lift the pearl bridge gates.
- Hole 3: a moving putting deck, with the cup and its physical opening attached.
- Hole 5: a shot target retracts the central pinball bumpers, opening a straight lane.
- Hole 6: the final green island drifts, carrying its cup, trees, and collision surfaces.
- Hole 7: a shot target opens the gate to the leap-ramp shortcut.
- Hole 9: a wider-travelling moving putting deck.

Cyan means resting/closed. Lime means activated/open. A deliberate ball impact toggles a target; repeated contacts from the same stroke count once.
The original glass/halfpipe routes remain available. Shot switches persist until hit again or the hole restarts.

ADJUSTING IN THE INSPECTOR
Expand a hole and its Interactive Course Mechanics object.
- AeroMovingCupPlatform: travel, complete cycle time, and cup reference. Keep the entire cup/deck assembly together.
- AeroShotSwitch: linked mechanisms, minimum impact speed, cooldown, colors, and optional activation/deactivation audio clips.
- AeroReactiveMechanism: activated position/rotation, travel duration, colors, and optional movement sounds.
- AeroHoleMechanics: owning hole, component lists, and the short introduction hint.

Only the active playable hole runs. Menus and results hold its mechanisms still. Completing a moving cup stops it so the ball can settle.
Restart and returning to the title restore original positions and switch states. Ball friction and safety checkpoints account for moving ground.

The course builder includes these features on future rebuilds. For an existing unmodified Frutiger course, use:
Tools > Golf is Hard > Add Moving Cups and Shot Switches
The installer skips holes already containing Interactive Course Mechanics; rerunning it does not duplicate them.
