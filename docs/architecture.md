# Architecture

How the mod works inside the game, how it's built and deployed, and how a release is cut. For the
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
| `.github/` | CI workflows and the pull request template |

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
`BepInEx/config/dankn.valheim.mysticalweapons.cfg`. Each item binds a section named after its
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
is `Dank-MysticalWeapons` when building into Gale's `Default` profile (Gale's naming) and
`MysticalWeapons` otherwise. Launching the game from that profile loads the new build.

**ModelExporter.** `tools/ModelExporter` references the mod project and copies the game's
assemblies next to its exe, so it can run the item classes' `BuildModel` code outside the game.
`Properties/AssemblyInfo.cs` makes the mod's internals visible to it.

**CI.** GitHub can't build the mod (the game's assemblies aren't available there), so CI only
scans the source: CodeQL (C# and the Python Blender scripts, without building), Semgrep, and
Trivy on PRs into `main`. `restrict-main-merges.yml` fails any PR into `main` that doesn't come
from `develop`.

## Releases

There is no automated packaging, and no release has been tagged yet. The intended process:

1. On a branch from `develop`, bump `Version` in `Plugin.cs` and merge it into `develop`.
2. Open a pull request from `develop` into `main` and merge it once reviewed.
3. Build from `main` with `dotnet build -c Release`. The release is `bin/Release/MysticalWeapons.dll`
   plus the `models/` folder, installed together into `BepInEx/plugins/MysticalWeapons/`.
4. Tag the merge commit (`v<Version>`) and attach a zip of the DLL and `models/` to a GitHub release.

Never change `Plugin.Guid` or an existing `PrefabName` in a release: players' items and config are
keyed on them.
