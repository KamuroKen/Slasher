# Project conventions

- Name player gameplay scripts and classes after their role using `Player` (for example, `PlayerMovement`, `PlayerCamera`, `PlayerAttack`), never after the current character artwork such as Bunny. Keep player logic reusable when the character's visual assets change.
- When renaming Unity scripts, rename the class and file together, preserve the `.meta` GUID, and update serialized class identifiers/references.
- Build and edit levels manually in the Unity editor using Grid, Tilemap, Tile Palette, and existing tiles. Do not create or run Python level-generation files unless the user explicitly requests or approves that approach. If Python would make a future level task simpler, propose it first and wait for agreement. Do not substitute other generation scripts or direct scene-YAML generation for the requested manual tile-painting workflow.
