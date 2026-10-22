# Architecture

How the mod works inside the game, how it's built, tested in CI and deployed, and how a release is
published. For the
day-to-day steps, see [contributing.md](contributing.md).

## Repository layout

| Path | What it holds |
|---|---|
| `Plugin.cs` | BepInEx entry point, the list of items (`Plugin.Items`) and the registration patches |
| `Core/` | The framework every item builds on: base classes, mesh building, model loading, textures, debug report |
| `Weapons/`, `Armor/`, `Hallows/` | One class per item (the Hallows' set bonus is `DeathlyHallows.cs`) |
| `Properties/AssemblyInfo.cs` | Lets `tools/ModelExporter` use the mod's internal types |
| `models/` | Custom models that ship with the mod (OBJ, MTL, PNG) and their credits |
| `tools/ModelExporter/` | Console app that exports the code-built models to OBJ, without starting the game |
| `tools/blender/` | Headless Blender scripts that produce the models in `models/` |
| `docs/` | This documentation and Stephen's item-building notes |
| `.github/` | CI workflows, the shared `setup-valheim` action and the pull request template |
| `thunderstore.toml` | Thunderstore package definition (see Packaging) |

Everything in `Core/` and the item folders shares the `MysticalWeapons` namespace (item folders add
`.Weapons`, `.Armor`, `.Hallows`).

## How the mod works in the game

### Startup and registration

1. BepInEx loads `MysticalWeapons.dll` and calls `Plugin.Awake`. It binds the config for every item
   in `Plugin.Items` (so the config file is complete before a world loads), then applies all Harmony
   patches in the assembly with `PatchAll`.
2. Valheim creates its item database (`ObjectDB`) on the main menu and again when a world loads. A
   postfix on `ObjectDB.Awake` and `ObjectDB.CopyOtherDB` calls `Plugin.RegisterItems`, which waits
   until vanilla items are present, then for each enabled item:
   - creates its prefab once (`MysticalItem.GetOrCreatePrefab`, below) and adds it to the database,
     unless another mod already registered that name (for example the old standalone Bat'leth);
   - registers extras such as status effects (`RegisterExtras`);
   - adds its recipe, parsed from the config (`AddRecipe`).
3. A postfix on `ZNetScene.Awake` adds the same prefabs to the network scene, so items can be
   dropped, picked up and synced in multiplayer.

Each item's registration runs inside `Plugin.Guard`: an exception is logged with the item's name
and the game carries on, instead of breaking the main menu.

### Building an item

Every item clones a vanilla prefab rather than being made from scratch, so it inherits working
animations, sounds, attacks and equip behaviour. `MysticalItem.GetOrCreatePrefab`:

1. finds the first of `SourcePrefabs` in the game;
2. instantiates it under an inactive holder object (so none of its components wake up) and renames
   it to `PrefabName`;
3. sets the name and description, then calls `Setup` and `Customize` to turn it into the new item.

Two base classes implement `Setup`:

- **`MysticalWeapon`** (weapons, and trinkets with their own model): loads `models/<PrefabName>.obj`
  if it exists, otherwise builds the mesh in code (`BuildModel` with `MeshBuilder`). It borrows
  vanilla textures for parts that ask for them, renders the inventory icon from the mesh, hides the
  vanilla renderers and puts the new model under the clone's `attach` point, lined up with the
  original handle. Damage comes from config.
- **`MysticalArmor`**: body armor is skinned to the player's skeleton, so it keeps the vanilla model
  and recolours it (tint and glow from config). Rigid pieces such as helmets can use a custom OBJ
  instead, scaled to fill the vanilla piece.

Item-specific behaviour lives in the item's own class as nested Harmony patches. For example,
`Anduril.UndeadBanePatch` prefixes `Character.Damage` and boosts hits from Andúril against undead.
Patches on shared game methods always check first that our item is involved.

### Models and textures

- **`MeshBuilder`** builds meshes from simple parts (tapered blades from cross-section stations,
  prisms, slabs from 2D outlines, ribbons) into one submesh per material.
- **`ObjImport` / `ObjExport`** read and write Wavefront OBJ. Material `<anything>_<N>` maps to
  submesh N, matching the item's `Materials`. X is mirrored between Unity and Blender.
- **`VanillaTextures`** takes textures from vanilla items while the game runs, turns them into
  greyscale detail and tints them per part. No game asset is ever stored in the repo.
- **`ModelUtil`** swaps the model into the prefab, makes materials from the cloned item's shader,
  loads custom models and textures, and rasterises inventory icons in software.
- **`ItemReport`** (off by default, `[Debug] ItemReport`) renders each item next to its vanilla
  peer and writes triangle and texture counts, for checking that new items fit the game's look.

### Configuration

All settings go through BepInEx's `ConfigFile` into
`BepInEx/config/omm.valheim.mysticalweapons.cfg`. Each item binds a section named after its
`PrefabName` with the common keys (`Enabled`, `Recipe`, `CraftingStation`, `StationLevel`, plus
`Damage` for weapons or `Armor`, `Tint` and `Glow` for armor) and its own keys in `BindConfig`.
Section and key names are part of players' saved setup and must not be renamed.

## Build and deploy

`MysticalWeapons.csproj` is an SDK-style project targeting .NET Framework 4.8 (Valheim's Unity
runtime). It compiles every `.cs` file in the repo except under `tools/`.

**References.** Nothing from the game is committed or downloaded from NuGet. The project references
the game's own assemblies where they're installed:
- `ValheimDir` (default: the Steam install folder) → `valheim_Data/Managed/assembly_valheim.dll`,
  `assembly_utils.dll` and the Unity modules the mod uses.
- `BepInExDir` → `core/BepInEx.dll` and `core/0Harmony.dll`. If unset, the build uses Gale's
  `Default` profile when it exists, otherwise r2modman's.

All references are `Private="false"`, so none are copied to the output: the game and BepInEx
already provide them. The only NuGet package is `Microsoft.NETFramework.ReferenceAssemblies`, which
lets the .NET SDK build for .NET Framework without Visual Studio.

**Deploy.** After every build, the `DeployToProfile` target copies the DLL to
`<BepInExDir>/plugins/<PluginFolder>/` and the repo's `models/` folder next to it. `PluginFolder`
is `OldManMods-MysticalWeapons` when building into Gale's `Default` profile (Gale's naming) and
`MysticalWeapons` otherwise. Launching the game from that profile loads the new build.

**ModelExporter.** `tools/ModelExporter` references the mod project and copies the game's
assemblies next to its exe, so it can run the item classes' `BuildModel` code outside the game.
`Properties/AssemblyInfo.cs` makes the mod's internals visible to it.

## CI

The game's assemblies can't be committed or redistributed, so CI fetches them on each run instead.
`.github/actions/setup-valheim` downloads:
- **Valheim's dedicated server** (Steam app `896660`) with SteamCMD's anonymous login. It's free
  and has the same `assembly_valheim.dll`, `assembly_utils.dll` and Unity assemblies the client
  uses, under `valheim_server_Data/Managed`.
- **BepInExPack_Valheim** from Thunderstore, for `BepInEx.dll` and `0Harmony.dll`. Its version is the
  action's `bepinex-version` input; keep it in step with the dependency in `thunderstore.toml`.

Both land in the runner's temp folder and are discarded after the run. The build is pointed at
them with `-p:ManagedDir=...` and `-p:BepInExDir=...`; nothing in the `.csproj` is CI-specific.

| Workflow | Runs on | What it does |
|---|---|---|
| `build.yml` | Every PR, and pushes to `develop`/`main` | Builds the mod and ModelExporter. Once `icon.png` exists, also builds the Thunderstore zip and attaches it to the run, ready to install for testing |
| `publish.yml` | Pushes to `develop`, and `v*` tags | Develop: publishes the beta package. Tag: checks it, publishes the stable package and creates a GitHub release (see Releases) |
| `codeql.yml`, `semgrep.yml`, `trivy.yml` | PRs (Trivy: into `main` only) | Source scanning; CodeQL covers C# and the Python Blender scripts without building |
| `restrict-main-merges.yml` | PRs into `main` | Fails unless the PR comes from `develop` |

CI can't launch the game, so in-game testing stays manual (see contributing.md).

## Packaging

`thunderstore.toml` defines the Thunderstore package for [tcli](https://github.com/thunderstore-io/thunderstore-cli),
Thunderstore's official build and upload tool. `tcli build --package-version <Version>` writes
`build/OldManMods-MysticalWeapons-<Version>.zip`:

```
manifest.json          generated from thunderstore.toml (name, version, BepInExPack dependency)
icon.png               256x256, from the repo root
README.md              the repo README, shown on the Thunderstore page
plugins/MysticalWeapons.dll
plugins/models/...     the shipped custom models and CREDITS.md
```

Mod managers install `plugins/` into `BepInEx/plugins/OldManMods-MysticalWeapons/`, so the models land
next to the DLL where `ModelUtil` looks for them. To build the zip locally, install tcli
(`dotnet tool install -g tcli`), build the mod, then run the same command.

## Releases

There are two channels, published as separate Thunderstore packages because Thunderstore has no
pre-release versions:

| Channel | Package | Published when | Version |
|---|---|---|---|
| Beta | `OldManMods-MysticalWeapons_BETA` | Every push to `develop` that changes the mod (each merged PR) | `<major>.<minor>` from `Version`, then the workflow's run number, e.g. `1.6.57` |
| Stable | `OldManMods-MysticalWeapons` | A `v<Version>` tag on `main` | `Version` in `Plugin.cs` |

The run number only goes up, so mod managers always see the newest beta as an update. Docs-only
pushes (`docs/`, `tools/`, `CLAUDE.md`) skip the beta. The beta's Thunderstore page puts a beta
notice above the README.

Both packages are the same mod with the same `Plugin.Guid` and prefab names, so a profile must have
**one or the other**, never both: BepInEx would load only one of them. Like the stable mod, the beta
has to be on the server and on every player's game.

### Play testing a beta

1. Merge PRs into `develop`; each one publishes a new beta within a few minutes.
2. The play-test server's profile and each tester's profile install `MysticalWeapons_BETA`
   (not `MysticalWeapons`) and update it from the mod manager.
3. Fix what testing finds with more PRs into `develop`.

### Cutting a stable release

1. On a branch from `develop`, bump `Version` in `Plugin.cs` and merge it into `develop`. (That
   publishes one more beta, now numbered from the new version.)
2. Open a pull request from `develop` into `main` and merge it once reviewed.
3. Tag the merge commit on `main` and push the tag:
   ```bash
   git tag v1.7.0 origin/main
   git push origin v1.7.0
   ```
4. `publish.yml` checks that the tag is on `main` and matches `Version`, then builds, uploads
   `OldManMods-MysticalWeapons` to Thunderstore and creates the GitHub release with generated notes.

Thunderstore rejects a version it already has. If an upload fails, fix the cause and re-run; a
re-run of a run that did upload fails harmlessly. A stable release that uploaded needs a new
`Version` to change.

### One-time setup

- A Thunderstore team named `OldManMods` (or change `namespace` in `thunderstore.toml`), with a service
  account. Its API token goes in a GitHub environment named `thunderstore` as the secret
  `THUNDERSTORE_TOKEN`. In the environment's settings, limit deployments to the `develop` branch
  and `v*` tags so no other ref can use the token. Required reviewers on that environment would
  hold every beta too, so leave them off.
- A 256x256 `icon.png` in the repo root.

Never change `Plugin.Guid` or an existing `PrefabName` in a release: players' items and config are
keyed on them.
