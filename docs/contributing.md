# Contributing

How to build the mod, test a change and get it merged. For how the code is put together, see
[architecture.md](architecture.md). Stephen's notes on how he designs and builds items are in
[art-direction.md](art-direction.md) and [building-items.md](building-items.md).

## Setup

You need:
- Valheim (Steam), and the .NET SDK (any recent version; the mod targets .NET Framework 4.8).
- A mod manager profile with BepInEx: [Gale](https://github.com/Kesomannen/gale) or r2modman.
  Make a separate **test** profile for development, so builds don't land in the profile you play on.
- Optional: [Blender](https://www.blender.org/) 4.x for models, and
  [Claude Code](https://claude.com/claude-code) for reviews.

Point the build at your test profile with a user environment variable, once:

```powershell
[Environment]::SetEnvironmentVariable("BepInExDir", "$env:APPDATA\com.kesomannen.gale\valheim\profiles\mod-testing\BepInEx", "User")
```

Restart your terminal or editor afterwards. Without it, the build uses Gale's (or r2modman's)
`Default` profile. Valheim is expected in the default Steam folder; if yours is elsewhere, set
`ValheimDir` the same way. Don't change these paths in the `.csproj`.

## Build and test

```bash
dotnet build -c Release
```

This compiles `MysticalWeapons.dll` and copies it, with the `models/` folder, into
`BepInEx/plugins/<mod folder>/` of your profile ([architecture.md](architecture.md#build-and-deploy)
has the details).

The **Build** check on each pull request compiles the mod against Valheim's dedicated server (see
[architecture.md](architecture.md#ci)). There are no automated tests, so test behaviour in the game:

1. Launch Valheim from your test profile.
2. Check `BepInEx/LogOutput.log` in the profile: it should say `Loaded N mystical items.` with no
   errors or warnings from this mod.
3. Try the change in a world (`devcommands` and `spawn <Prefab>` help).
4. For anything other players see (effects, models), check it on a server with a second player
   before a release.

Changing a default in code doesn't change an existing config file: BepInEx keeps the saved value.
Edit or delete the line in the profile's `BepInEx/config/omm.valheim.mysticalweapons.cfg`.

## Workflow

1. Branch from `develop`, one branch per change.
2. Commit, push and open a pull request into `develop`. Fill in the template's test notes.
3. Run Claude Code's `/review <pr number>` on it and fix what it finds. The review guidelines are in
   [CLAUDE.md](../CLAUDE.md) ("Reviewing changes").
4. The other maintainer reviews and merges.

Every merge into `develop` publishes a beta to Thunderstore (`MysticalWeapons_BETA`) for play
testing on our server. `main` only takes pull requests from `develop`, when cutting a stable
release; tagging it publishes `MysticalWeapons` (see [architecture.md](architecture.md#releases)).
Don't bump `Version` in `Plugin.cs` in a feature PR.

## Rules

- The repo is public: no secrets, tokens, webhook URLs or personal paths in committed files.
- Never rename an existing `PrefabName`, config section or key, or `Plugin.Guid`: that breaks
  players' saved items and config.
- Never copy Valheim's own textures, models or sounds into the repo; take them from the game at
  runtime (see `VanillaTextures`).
- Outside models only if their licence allows changes and redistribution (for example CC BY),
  credited in `models/CREDITS.md` and the README. Never commit models ripped from another game, or
  reference images and models you only used for looking at.

## Adding an item

1. **Weapon:** subclass `MysticalWeapon` in `Weapons/`, using `Weapons/Anduril.cs` as the pattern.
   Pick vanilla `SourcePrefabs` to clone (for animations, sounds and attacks), set stats and recipe
   as `Default*` overrides, and build the model in `BuildModel` with `MeshBuilder` (blades, prisms,
   slabs, ribbons). Hand at the origin, grip along +Y. Extra settings go in `BindConfig`; Harmony
   patches are nested in the item's class. If it is much stronger than its vanilla tier or has a
   unique effect, give it a boss trophy in `BossTrophies` (`Core/MysticalItem.cs`), as "Tier, cost
   and balance" in `art-direction.md` describes.
   Texture a part with `.Borrow(VanillaTextures.Iron, tiling, gloss)` on its `MaterialSpec`: the
   texture is taken from that vanilla item while the game runs and tinted with the part's colour.
   The clone's material keeps the source item's settings; if the model still looks tinted, check the
   `inherited colours` line for it in `LogOutput.log` (black metal, for example, has a green metal
   colour), or set `CleanMaterials`.
2. **Armor:** subclass `MysticalArmor` in `Armor/` with a source armor piece, tint and glow colour.
3. Add it to `Plugin.Items`.
4. Add a row to the items tables in the [README](../README.md).

### Item report

To see how items look next to vanilla, set `[Debug] ItemReport = true` in the config and launch to
the main menu. `BepInEx/MysticalItemReport` then holds a picture of each item beside its vanilla
peer from eight angles, and `report.txt` (triangles and texture sizes next to the peer, plus the
most detailed vanilla item of the same kind). The peer is the cloned source unless the item sets
`VanillaPeer`. Triangle counts are about twice what Blender shows.

## Editing models in Blender

Weapon models are built in code, but you can export them, edit them in Blender and drop them back in.

1. Build `tools/ModelExporter` (`dotnet build -c Release tools/ModelExporter`), then run
   `tools/ModelExporter/bin/Release/net48/ModelExporter.exe exports`. It writes
   `exports/<Prefab>.obj` + `.mtl` for every code-built item (`exports/` isn't committed).
2. In Blender: **File > Import > Wavefront (.obj)** with default settings. Units are metres. The
   hand sits at the origin and the grip runs along +Y (Blender's +Z after import). Run
   **Mesh > Normals > Recalculate Outside** once.
3. Keep the material names (`Anduril_0`, `Anduril_1`, ...): the number picks which of the weapon's
   colours that part gets (blade, grip, glowing runes...). Unknown materials use the first colour.
4. Export with **File > Export > Wavefront (.obj)**, default axes, and tick **Triangulated Mesh**.
5. Put the file at `BepInEx/plugins/<mod folder>/models/<Prefab>.obj` and restart the game. The log
   says `using custom model` when it's picked up; the inventory icon is drawn from it too. Delete
   the file to go back to the built-in model.

To ship a model with the mod, put it in the repo's `models/` folder; the build copies it next to
the DLL.

Models can be textured: a `map_Kd` texture in the `.mtl` is applied to that material (PNG or JPG).
Helmets work too: `models/ShardplateHelm.obj` is scaled and centred to fill the vanilla helmet,
then rotated and nudged by fixed values in the item's code. Its texture is recoloured on load from
the `Tint`, `Glow`, `CustomModelBrightness` and `CustomModelGlow` settings, and `CustomModelLight`
adds a soft light in front of it. Placement is deliberately not configurable; colours are.

`ModelExporter.exe exports --verify` re-imports each exported file and checks it matches the
original. `ModelExporter.exe --load models/ShardplateHelm.obj` shows how the mod will read a model.

## Painting armour textures

Armour keeps its vanilla mesh, but a piece can wear our own textures from `models/textures/` (see
`MysticalArmor` in `architecture.md`). They are painted on the layout of the vanilla texture they
replace, which you read from the game on your own machine and never commit: the opaque islands of the
vanilla texture are the parts of the mesh. `tools/textures/paint_brotherhood.py` paints the Brotherhood
set this way; its header says what it needs. Which island is which part is easiest to find in game,
with a test texture that colours each island differently.

The shipped models are made by headless Blender scripts in `tools/blender/` (each header has the
command), so they can be regenerated:
- `make_sulfuras_texture.py` bakes Sulfuras's procedural textures onto the code-built shape. The
  shape in `Sulfuras.cs` is then only the fallback, so after changing it, re-export and re-run the
  script or the game keeps showing the old model.
- `make_shardplate_helm.py` extracts and prepares the helm from Krajkoa's Sketchfab model.
- `thunderfury_model.py` builds Thunderfury in Blender part by part, and
  `make_thunderfury_texture.py` bakes and exports it the same way as Sulfuras. `Thunderfury.cs`
  keeps only a rough fallback shape.

Building a model with Claude driving Blender directly (as for Thunderfury) needs the Blender MCP:
see [blender-mcp.md](blender-mcp.md).

### Picture for the pull request

`tools/blender/render_views.py` renders an item's model from the front, in profile and at
three-quarters in one picture (the script's header has the command). Commit it as
`docs/images/<Prefab>-views.jpg` and link it in the pull request. Code-built items need exporting
first (see the steps above).
