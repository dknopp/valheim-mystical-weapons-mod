# Project context

Mystical Weapons from Far Off Lands: a Valheim BepInEx mod (C#, .NET Framework 4.8) adding
weapons, armor and the Deathly Hallows from other worlds. It's a public repository with two
maintainers, Dan (dknopp) and Stephen (smpecoraro).

- No Jotunn and no asset bundles. Models are built in code (`MeshBuilder`) or loaded from OBJ
  files in `models/`; armor is recoloured vanilla.
- Items live in `Weapons/`, `Armor/` and `Hallows/`. `Weapons/Anduril.cs` is the reference
  pattern: stats and recipe as `Default*` overrides, extra settings via `BindConfig`, Harmony
  patches nested in the item class.
- `README.md` is the user-facing source of truth for items, building and adding items, and must
  stay current when items change (including its items table).
- Feature PRs target `develop`; release PRs go from `develop` to `main`. `Version` in
  `Plugin.cs` is bumped only when cutting a release, never in feature PRs.

Never allowed:
- Assembly, machine code, byte-code, network calls, or system calls beyond what a reasonable
  game mod needs.
- Renaming an existing `PrefabName` or `Plugin.Guid`: that breaks players' saved items and config.
- Secrets, tokens, webhook URLs or personal paths in committed files.
- Valheim's own textures, models or sounds copied into the repository (take them from the game
  at runtime), or outside models whose licence doesn't allow changes and redistribution. Outside
  models must be credited in `models/CREDITS.md` and the README.
