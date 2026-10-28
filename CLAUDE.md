# Mystical Weapons from Far Off Lands

Valheim BepInEx mod (C#, .NET Framework 4.8) adding weapons, armor and the Deathly Hallows from
other worlds. No Jotunn, no asset bundles: models are built in code (`MeshBuilder`) or loaded from
an OBJ in `models/`; armor is recoloured vanilla. Public repo, two maintainers: Dan (dknopp, repo
owner) and Stephen (smpecoraro). README.md is the user-facing doc (items, install, config); keep its
items tables current when items change. `CHANGELOG.md` (Thunderstore's Changelog tab) gets a line
under today's date for every change players notice (see "Workflow" in `docs/contributing.md`).
Developer docs are in `docs/`.

## Layout
- `Plugin.cs`: BepInEx entry point. `Plugin.Items` lists every item; Harmony postfixes on
  `ObjectDB.Awake`/`CopyOtherDB` and `ZNetScene.Awake` register prefabs and recipes. Each item's
  registration runs inside `Guard` so one broken item can't break the game's startup.
- `Core/MysticalItem.cs`: base class. Clones a vanilla prefab (`SourcePrefabs`), renames it to
  `PrefabName`, calls `Setup`/`Customize`, binds the common config (enabled, recipe, station, boss
  trophy: the `BossTrophies` table gates the items stronger than their tier). `CloneVanilla` and
  `RegisterNetExtras` copy and register prefabs an item spawns (creatures, projectiles).
- `Core/MysticalWeapon.cs`: items with a code-built model (`BuildModel`, `Materials`) and damage config.
  A `models/<PrefabName>.obj` next to the DLL replaces the code-built model at load.
- `Core/MysticalArmor.cs`: recoloured vanilla armor, or our own painted textures from `models/textures/`
  (painted by `tools/textures/`); rigid pieces (helmets) can take a custom OBJ.
- `Weapons/`, `Armor/`, `Hallows/`, `Items/` (everything else): one file per item (the Hallows set bonus is
  `DeathlyHallows.cs`; the portal a `PortalScroll` opens is `Items/TownPortal.cs`).
- Also in `Core/`: `MeshBuilder.cs` (blades, prisms, slabs, ribbons), `ModelUtil.cs` (model swap,
  dual-wield hands and back holster, materials, see-through `SpectralMaterial`, icon rendering, custom model loading), `VanillaTextures.cs` (textures borrowed from
  vanilla items at runtime), `ObjImport.cs`/`ObjExport.cs`, `ItemReport.cs` (debug pictures next to
  vanilla peers).
- `Properties/AssemblyInfo.cs`: exposes internals to `tools/ModelExporter`.
- `models/`: shipped custom models (OBJ + MTL + PNG) and `CREDITS.md`. The build copies it next to
  the DLL.
- `tools/ModelExporter`: console app that exports the code-built models to OBJ without the game.
  `tools/blender/`: headless Blender scripts that produce the shipped models (see each header).
- `docs/`: `contributing.md` (setup, workflow, adding items, editing models in Blender) and
  `architecture.md` (how the mod, build, CI, packaging and releases work). `item-ideas.md` is our
  shared list of items we might add. Stephen's notes on how
  he builds items: `art-direction.md` (look, effects and balance), `building-items.md` (process
  and lessons) and `blender-mcp.md` (letting Claude drive Blender). Use what helps; they aren't
  rules for the repo.

## Build and test
- `dotnet build -c Release` builds and copies the DLL and `models/` into a mod manager profile.
- Each of us points the build at a Gale **test** profile by setting the `BepInExDir` user
  environment variable (e.g. `...\com.kesomannen.gale\valheim\profiles\mod-testing\BepInEx`).
  Don't edit the .csproj to change paths.
- There are no automated tests. CI (`build.yml`) builds against Valheim's dedicated server,
  downloaded on each run, so a PR that doesn't compile fails there. Test behaviour by launching
  Valheim from the Gale test profile, then check `BepInEx/LogOutput.log` there for
  `Loaded N mystical items.` and for errors or warnings from this mod.
- Hands-off test: `tools/DevAutoRun/run.ps1` launches the test profile, loads the test world,
  runs the checks in `tools/DevAutoRun/Probes.cs`, quits without saving and prints the log
  (exit 0 = pass). Run it after a code change, before asking a maintainer to test in game.
  Dev-only plugin, never packaged. Details in `docs/contributing.md`.
- Publishing (`publish.yml`, packaged from `thunderstore.toml`): every push to `develop` publishes
  the `MysticalWeapons_BETA` Thunderstore package for play testing; a `v<Version>` tag on `main`
  publishes the stable `MysticalWeapons` package and a GitHub release.
- Changing a default in code doesn't change an existing config: BepInEx keeps the saved value. Edit
  or delete the line in `BepInEx/config/omm.valheim.mysticalweapons.cfg` to see the new default.

## Adding an item
Follow "Adding an item" in `docs/contributing.md`, add it to `Plugin.Items`, then add a row to the
README items tables. Media is optional, but a big addition (new item or set, or a major rework) that
has a GIF or screenshots gets a dated entry at the top of the README's "What's new" (up to three; the oldest's media then
moves down to its own section); fixes and small changes don't. Use `Weapons/Anduril.cs` as the model: stats/recipe as `Default*` overrides,
extra settings via `BindConfig`, Harmony patches nested in the item class. Lessons from earlier items
(borrowed attacks, status effects, multiplayer) are under "Lessons" in `docs/building-items.md`.

## Conventions
- Match the surrounding code: terse `///` summaries on classes and non-obvious members, short
  comments that say why, `internal` types, `Plugin.Log` for logging with the prefab name first.
- Model space: hand at the origin, grip along +Y, blade flat in XY, Z is thickness, units are metres.
- Every tunable number a player might want to change is a config entry with a clear description;
  placement of models is code only.
- Prefer Harmony postfixes. A prefix that skips the original (`return false`) needs a comment
  saying why.
- On-hit effects patch `Character.Damage` and check the attacker's current weapon by
  `m_dropPrefab.name`, as `Anduril.cs` does.
- Log what you borrow from vanilla (prefab names, key settings) at setup, at Info level.

## Workflow
- One branch per change, merged through a pull request into `develop`. `main` only takes PRs
  from `develop` (enforced by `.github/workflows/restrict-main-merges.yml`).
- Review PRs with Claude Code's `/review <pr>` before asking the other maintainer to approve.
- Write the PR description from `.github/pull_request_template.md` (Summary, Testing, Checklist). Item
  PRs also link a views picture committed as `docs/images/<Prefab>-views.jpg` (see the checklist in
  `docs/building-items.md`). Do both before opening the PR.
- Bump `Version` in Plugin.cs only when cutting a release, not in feature PRs.

## Rules
- The repo is public: no secrets, tokens, webhook URLs, or personal paths in committed files.
- Licences: code is MIT (`LICENSE`), our own art CC BY 4.0 (`LICENSE-ASSETS.md`).
- Never rename an existing `PrefabName`, config section/key or `Plugin.Guid`: that breaks players'
  saved items and config.
- Never copy Valheim's own textures, models or sounds into the repo; take them from the game at
  runtime. Outside models only if their licence allows changes and redistribution (e.g. CC BY),
  credited in `models/CREDITS.md` and the README. Never commit models ripped from a source's own
  game (they belong to its publisher) or reference images and models used only for looking at.
- Don't run the old standalone Dank-Batleth mod alongside this one: this mod includes the bat'leth
  and skips its own if the old one is loaded.

## Reviewing changes
Guidance for `/review` and any other code review of this repo. Report real problems with a file
and line, most severe first; say plainly when a PR looks good. Don't pad with style nits.

**Blocking** (must be fixed before merge):
- Breaks existing saves or configs: renamed or removed `PrefabName`, `Plugin.Guid`, config section
  or key; changed meaning of an existing config value.
- Can crash or stall the game: an exception that can escape registration (`ObjectDB`/`ZNetScene`
  patches) or a per-frame hook; a null dereference on a missing vanilla prefab, a missing
  `models/` file or another mod's item; blocking I/O or heavy allocation in `Update`, `Damage` or
  other hot paths.
- Multiplayer problems: an effect that only works for the local player but should show for others,
  state kept only on one client that should be on the ZDO, damage applied twice (patch runs on
  every client instead of the attacker's), item duplication.
- A Harmony patch that is destructive without reason (a skipping prefix, a transpiler) or that
  targets a method by a string name that doesn't exist in the current game.
- A patch on a shared method (`Character.Damage`, `Player.OnDeath`...) that doesn't first check
  it's our item, so it changes vanilla behaviour for everyone.
- Repo rules above: secrets or personal paths, Valheim assets or unlicensed models committed,
  version bumped in a feature PR.
- Unexpected capabilities: network calls, process execution, reflection or file access beyond
  the plugin's own folder and config.

**Should fix**:
- New or changed item not in the README items tables, `Plugin.Items`, or (for outside models)
  `models/CREDITS.md` and the README credits.
- A change players notice with no `CHANGELOG.md` line, or a changed default whose line doesn't
  say which config section to reset.
- Balance clearly out of line: stats or recipe far above vanilla items of the same tier and
  station level, or an effect that trivialises a boss or biome. Compare with the vanilla source
  item named in `SourcePrefabs`.
- A changed default on an existing item without a note in the PR that players must reset that
  config section.
- Magic numbers a player would want to tune that aren't in config; config descriptions that are
  wrong or unclear.
- Comments or docs the change made wrong; debug probes, commented-out code or stray files left in.
- Duplicated logic that already exists in `MysticalItem`, `MysticalWeapon`, `ModelUtil` or
  `MeshBuilder`.

**Not worth flagging**: taste in names, item lore or looks; British spelling (the repo uses it);
long single-line expressions that match the surrounding code; the lack of unit tests.

Things a reviewer can't check from the diff (in-game look, feel, log output) belong in the PR's
test notes; ask for them if they're missing on an item change.
