# Mystical Weapons from Far Off Lands

Valheim BepInEx mod (C#, .NET Framework 4.8) adding weapons and armor from other worlds.
No Jotunn, no asset bundles: models are built in code (`MeshBuilder`) or loaded from an OBJ in
`models/` (see "Editing models in Blender" in README.md); armor is recoloured vanilla.
Two maintainers: Dan (dknopp, repo owner) and Stephen. README.md is the user-facing doc and the
source of truth for items, building and adding items; keep it current when items change.

## Build and test
- `dotnet build -c Release` builds and copies the DLL into a mod manager profile (see README).
- Each of us points the build at a Gale **test** profile by setting the `BepInExDir` user
  environment variable (e.g. `...\com.kesomannen.gale\valheim\profiles\mod-testing\BepInEx`).
  Don't edit the .csproj to change paths.
- Test by launching Valheim from that Gale profile, then check `BepInEx/LogOutput.log` in the
  profile for `Loaded N mystical items.` and for any errors or warnings from this mod.

## Adding an item
Follow "Adding an item" in README.md, then add a row to the README items table.
Use `Weapons/Anduril.cs` as the model: stats/recipe as `Default*` overrides, extra settings via
`BindConfig`, Harmony patches nested in the item class.
Never rename an existing `PrefabName` or `Plugin.Guid`: that breaks players' saved items and config.
`docs/` has Stephen's notes on how he builds items (`art-direction.md` for look, effects and
balance; `building-items.md` for process and lessons from earlier items). Use what helps; they
aren't rules for the repo.

## Workflow
- One branch per change, merged through a pull request.
- Bump `Version` in Plugin.cs only when cutting a release, not in feature PRs.

## Rules
- The repo is public: no secrets, tokens, webhook URLs, or personal paths in committed files.
- Never copy Valheim's own textures, models or sounds into the repo; take them from the game at
  runtime. Outside models only if their licence allows changes and redistribution (e.g. CC BY),
  credited in `models/CREDITS.md` and the README. Never commit models ripped from a source's own
  game (they belong to its publisher) or reference images and models used only for looking at.
- Don't run the old standalone Dank-Batleth mod alongside this one: this mod includes the bat'leth
  and skips its own if the old one is loaded.
