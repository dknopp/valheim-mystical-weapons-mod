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
Follow `docs/art-direction.md` for how items look and what their lore effects do, and
`docs/building-items.md` for the build-and-test process and lessons (read both before starting an item).

## Workflow
1. `git pull` on main.
2. One branch per change.
3. Open a pull request and get the other person's approval; the author merges after that.
   - Dan reviews Stephen's PRs, including the code.
   - Stephen reviews Dan's by playtesting and sanity-checking. Small PRs can just be approved.
4. Bump `Version` in Plugin.cs only when cutting a release, not in feature PRs.

## Keeping the docs useful
- When guidance in `docs/` turns out wrong, or you find a better way, fix the doc in that item's PR
  and add a "Docs changes" line to the PR description.
- Keep them short: checklists over prose, each rule tied to a real item as its example, and a check
  automated in code beats a rule written down. When adding a rule, merge or cut one if you can.
  Aim for about one screen here and two per doc.

## Rules
- The repo is public: no secrets, tokens, webhook URLs, or personal paths in committed files.
- Don't run the old standalone Dank-Batleth mod alongside this one: this mod includes the bat'leth
  and skips its own if the old one is loaded.
