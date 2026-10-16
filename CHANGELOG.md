# Changelog

Versions follow [semver](https://semver.org).

## [Unreleased]

### Added
- The axe takes a couple of hits to get through a plane (`HitsPerPlane`, default 2). Vertical cuts split from the top down, horizontal ones notch in from the side
- Heavy Hit axe mode: takes three planes in one blow for double durability
- Chop sounds made from layered vanilla sounds, different for a partial hit, a finished plane and a heavy hit
- Right click with an axe, saw, knife or chisel to turn the log a quarter turn
- Server settings for the axe get sent to players when they join
- Chopping blocks get made in the world: sneak + hold right click a standing log with an axe. One for every wood
- Freeform in the recipe list: carve whatever, turn and tip it any time, and switch to a real recipe later if there's wood for it
- Taking a cut log off gives a partly hewn log that remembers its shape and recipe, put it back to carry on. They can be put down in the world too, and burn like firewood
- Half logs (and debarked half logs from debarked logs): a block for every wood that stacks, can be placed, and goes back on the chopping block to split into firewood
- Bits cut off from the log fall away like on the anvil. Big ones drop as a partly hewn log, smaller ones as firewood. In freeform the axe splits a log into two half logs and each half into two quarters of firewood
- New recipes: rough-hewn fence and gate, long bowstave (oak, maple, walnut), plank slabs, plank stairs, half logs, and firewood from half logs
- Redwood log sections can be hewn, aged logs give aged firewood
- `DisableVanillaLogRecipes` config option takes the grid recipes for planks, firewood, debarked logs and beams out
- The saw, knife and chisel have their own sounds, finishing a recipe has a finish sound and a burst of chips
- The knife strips bark a 3x3 patch at a time
- Sapwood ring between the bark and the heartwood
- The chisel uses the hammer and chisel animation, logs, half logs and chopping blocks get carried in both arms

### Changed
- Firewood recipe cross is three planes wide
- The chopping block is round like the log on it
- Hewing yields at or above the grid: support beams 4, firewood 5, ladders 4
- Finished items go straight into the inventory
- A cut log only offers recipes it still has the wood for
- Ruined logs give firewood for what's left instead of always 2 (`RuinedFirewoodDrop` is gone)

### Removed
- The grid recipe for the chopping block

## [0.1.0] - 2026-10-12

### Added
- Hewing in the handbook, wood chips, tree ring grain on cut faces
- Config file, tool durability, interaction hints, hold to chop
- Ctrl + right click to lay the log on its side, planks recipe

### Changed
- Updated to Vintage Story 1.22 and .NET 10
- Chopping block uses the vanilla multiblock system
- Recipe outline works like the anvil

### Fixed
- Logs couldn't be placed on the chopping block in multiplayer
- Breaking the chopping block ate the log
