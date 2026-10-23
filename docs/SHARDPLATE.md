# Shardplate armor set: handoff notes for editing

How the Shardplate Helm, Breastplate and Greaves are built, how to change them, and what has already
been tried. Read this before touching the set; most "obvious" fixes were tried and reverted, and the
reasons are recorded below.

## The pieces at a glance

| Item (prefab) | Vanilla clone | Model | How it attaches |
|---|---|---|---|
| `ShardplateHelm` | `HelmetDrake` | `models/ShardplateHelm.obj` (+ `.png`), rigid | Under the helmet's `attach` child, fitted to the Drake helmet then rotated/scaled/offset in code |
| `ShardplateBreastplate` | `ArmorFlametalChest` | `models/ShardplateBreastplate/skin.obj` + 4 textures + `icon.png` | Skinned mesh, Valheim's `attach_skin` |
| `ShardplateGreaves` | `ArmorFlametalLegs` | `models/ShardplateGreaves/skin.obj` + 1 texture + `icon.png` | Skinned mesh, `attach_skin` |

Source art: Krajkoa's "Shardplate" on Sketchfab, CC BY 4.0. Credit is required and present in the
README, `models/CREDITS.md` and each item's description. Keep it. The original glTF download lives
**outside the repo** at `C:\Users\dankn\projects\valheim-mods\shardplate-source\scene.gltf`; the user
downloaded it, because Sketchfab requires a login.

## Code map

- `Armor/Shardplate.cs`: the item classes. Stats, recipes, set bonus, colour defaults, and the
  helm's placement values (`CustomModelRotation (-6,180,0)`, `CustomModelScale 0.78`,
  `CustomModelStretch (1.04,1,1)`, `CustomModelOffset (0,-0.040,0.08)`, `CustomModelLightPosition`). All pieces use
  `CustomModelMaterialSource => "ArmorFlametalChest"`, since the Drake helmet's own material is matte and looked flat.
- `MysticalArmor.cs`: armor base class. `UsePerBoneModels` loads `models/<Prefab>/`: if it holds
  `skin.obj` it becomes `attach_skin` (skinned), otherwise each `<Bone>.obj` becomes a rigid
  `attach_<Bone>`. `UseCustomModel` handles the helm. `PrepareCustomMaterials` and `GradeTexture`
  recolour textures on load (Tint × CustomModelBrightness, plus a Glow wash). `RecolorBodyPaint`
  recolours the vanilla `_ChestTex`/`_LegsTex` under-armor body paint so gaps between plates read as
  dark blue rather than black. `ReloadCustomModel` is used by the reload command.
  - **Body paint:** the breastplate and greaves keep Flametal's body paint, recoloured blue.
    Tried and dropped: Troll Leather's paint in its own colours, and a plain under-suit in the
    armor's colour (Troll Leather's coverage filled flat). The user preferred the recoloured Flametal.
- `ObjImport.cs` / `MeshBuilder.cs`: the OBJ loader. Positions and normals are X-mirrored, because OBJ is
  right-handed and Unity left-handed. Two custom line types for skinning:
  `bp <bone> <16 floats>` (a bind pose: Unity space, row-major, in player bone order) and
  `vw i0 w0 i1 w1 i2 w2 i3 w3` after each `v` line. Imported triangles that have normals are added
  **single-sided**, wound to face their normals (see pitfalls).
- `SkinBoneNames.cs`: a component that stores the bone names, plus a Harmony postfix on
  `VisEquipment.AttachArmor` that rebinds skinned armor bones **by name**, so it survives a game update
  that reorders the skeleton.
- `Armor/BodyMask.cs`: hides the body wherever a worn Shardplate piece covers it, so it can't
  poke through and the plates don't have to be inflated to contain it. The breastplate and greaves
  each hide only what they cover, so mixed sets keep the body under other armor.
  - **Coverage test:** done once per body mesh and piece, in the rest pose. Both the armor mesh and,
    through the armor's bind poses, the body are in Visual space. Each body vertex casts 5 rays
    outward (its normal plus 4 tilted 25°), each starting `Pierce` (6 cm, raised from 3 cm when the plates were slimmed) inside the skin and
    reaching `Reach` (12 cm) out, tested against the armor triangles in a 4 cm grid. The vertex is
    covered if 3 rays hit.
  - **Limbs only:** only vertices at least half weighted to limb bones can be hidden: Arm,
    ForeArm, Hand and fingers, UpLeg, Leg, Foot and ToeBase (`IsLimb`). Those are what poke through
    as they bend. The torso, hips, shoulders, neck and head always draw, which avoids holes there:
    the collar used to hide scattered neck triangles.
  - **Hands and forearms** (breastplate only, `Piece.Always` / `Piece.Lenient`): hand and finger
    skin is always hidden, with no ray test, and forearm skin counts as covered if any one ray hits.
    After the arms were slimmed, rays slipped out through the gauntlets' open palm, the finger gaps
    and the vambrace–cuff gap, and the wrists and hands showed through as they bent. The log adds
    "(N Hand vertices without a ray test)".
  - **Hiding:** triangles with 2 or 3 covered corners are dropped from a copy of the body mesh,
    which a Harmony postfix on `VisEquipment.UpdateVisuals` swaps in while the piece is worn. The
    original comes back when it's unequipped.
  - **Log:** `Shardplate body mask: <piece> covers N of M body vertices` and `hid T of U body
    triangles`.
  - `mw_reload` clears the cache.
  - **Limit:** coverage is tested in the rest pose, so a sharply bent joint can open a small hole
    where hidden body would have shown.
- `ModelUtil.cs`: model replacement and fitting (`ReplaceModel`, `ModelPlacement`), renderers
  (`AddRenderer` creates a SkinnedMeshRenderer when the model has weights), icon rendering, texture
  recolouring through a GPU readback (`RecolorTexture`, `RecolorIcon`), the texture cache, and material logging.
- `DevTools.cs`:
  - `mw_reload` console command (needs `devcommands`): reloads config, models and textures from
    disk and re-equips the local player. **Use it instead of restarting** after re-exporting models.
    DLL code changes still need a restart.
  - Skeleton dump: an empty file `dump-skeleton` next to the DLL makes the mod write
    `player_skeleton.json` at the main menu. The copy in use is `tools/blender/player_skeleton.json`
    (53 bones, rest pose, Unity "Visual" space, metres, +Z forward, character's left is -X).
    Delete the marker afterwards.

## The pipeline (body armor)

`tools/blender/fit_shardplate_body.py` does everything offline in Blender (5.2 is installed at
`C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`):

1. Loads the glTF and splits it into loose parts, about 1,270 plates. Drops the helm parts (handled
   separately) and the **scale skirt** (removed at the user's request).
2. **Maps each plate onto Valheim's skeleton region by region.** Krajkoa's suit has a long torso and
   short legs, and Valheim's player has long legs, so no single uniform scale fits:
   - Torso (materials `core`, `helmet`): vertical knots `TORSO_VK`, girth `GIRTH["torso"]`.
   - Legs (`material`): knots `LEG_VK` centred on each leg's bones, girth `GIRTH["leg"]`, extra depth
     behind the thigh `LEG_BACK`.
   - Arms (`material_5` arm plates, `MetalGlove` gauntlets): per segment along Krajkoa joints `ARMK`
     onto Valheim's Arm→ForeArm→Hand→fingertip, girth `GIRTH["arm"]` (gauntlets use `HAND_GIRTH`).
   - Groin plate: `is_groin()` picks the codpiece plates; `GROIN_*` stretches them down between the thighs.
   - **Gap filler (front):** once the plates are fitted, the belt has a ~7 × 7 cm see-through window
     at its centre, front and back (where the codpiece and hip plates part). With the body hidden
     under the armour you could see through it. At the front, `add_gap_filler` adds a small curved
     `core` plate at x ±`GAP_X` (6 cm) and heights `GAP_Y` (0.97–1.08 m), following the depth of the
     surrounding plates `GAP_INSET` (1 cm) behind them, textured from the nearest real vertex.
   - **Back:** both automatic fixes were rejected by the user: a filler like the front one looked
     smeared, and widening the top rear skirt plates to meet gave a V with a dark recess above it.
     The back gets a hand-modelled patch instead (below).
   - **Hand-modelled patches:** every export saves `tools/blender/shardplate_fit.blend` (fitted
     plates in rest pose, Blender axes, metres; ~70 MB, not tracked) with a **Patches** collection.
     Model extra pieces in that collection and save the file (Ctrl+S). The next export:
     - reads the patches from it (or, on a fresh checkout, from `shardplate_patches.blend`);
     - writes them alone to `tools/blender/shardplate_patches.blend` (small, tracked; it's deleted
       when no patches are left);
     - regenerates the reference with them;
     - adds each as a plate of the item its material belongs to (torso materials → breastplate,
       `material` → greaves; arm materials aren't supported), weighted like its neighbours.

     A patch without UVs is textured from the nearest torso vertex; a duplicated plate keeps its own
     UVs. Modifiers (e.g. Subdivision Surface) are applied on export. The generated front filler
     appears in the reference as `gap_filler_front`; moved into Patches, it replaces the generated
     one, so it can be reshaped or smoothed by hand. In the reference, the character faces +Y (Blender's Front view, Numpad 1, shows the back),
     the left is −X, and heights are Z in metres. Close Blender, or File > Revert after exporting:
     the export rewrites the file.
3. **Gauntlets:** `align_gauntlets()` scales each gauntlet to Valheim's hand length and shifts it
   onto the finger bones (thumb excluded). `rigid_hand_piece()` then gives each gauntlet piece its
   nearest bone by centroid:
   - finger pieces up to `HAND_PIECE_MAX` (8 cm) ride rigidly on that finger bone and are snapped
     onto it (`FINGER_SNAP`, `FINGER_SNAP_MAX`);
   - finger pieces from 8 to 12 cm span a knuckle, so they are weighted along that finger's chain
     (Hand + finger 1–3, power 8). Krajkoa's middle-finger segment is 8.5 cm, and under the old
     8 cm rule it rode the hand, so the middle finger didn't bend when gripping;
   - pieces over `HAND_PLATE_MIN` (12 cm), the back-of-hand and knuckle plates, ride the hand.
4. **Weights** (`assign_weights`): inverse distance to bone segments, top 4, limited per region by
   `allowed_bones()`:
   - torso: Hips, Spine, Spine1, Spine2, Neck, both Shoulders and both UpLegs;
   - legs: Hips and that leg's chain;
   - upper arm and pauldrons: Shoulder, Arm, ForeArm;
   - forearm: Arm, ForeArm (no hand, so turning the wrist doesn't twist the vambrace);
   - gauntlet: one bone per piece.
   Falloff power is 6 for arms and 4 elsewhere.
   - Hip plates (faulds): torso vertices below the hip joint get a growing share of the thigh bone on
     their side, smoothstepped from 0 at the hip joint to `FAULD_FOLLOW` (0.7) at `FAULD_LOW`
     (0.75 m). Within `FAULD_MID` (6 cm) of the centre line they split between both thighs. This
     makes them swing like tassets. Without it the thighs cut through them when walking, which no
     amount of torso width fixed. 1.0 was no better than 0.7.
5. Decimates to `BUDGET` (20k triangles chest, 10k legs), bakes each material's emissive texture into
   its base colour at 1024px, writes `skin.obj` + `Shardplate.mtl` + `Shardplate_<n>.png` per item, and
   renders `icon.png` using the mod's default colouring.

### Commands

Run these from the repo root. Exporting **directly into `models/`** works, but exporting to a scratch
folder first lets you inspect before installing:

```bash
"/c/Program Files/Blender Foundation/Blender 5.2/blender.exe" -b --factory-startup --python tools/blender/fit_shardplate_body.py -- "C:/Users/dankn/projects/valheim-mods/shardplate-source/scene.gltf" tools/blender/player_skeleton.json <OUT_DIR> export
```

- Use `preview` instead of `export` to write `fit_front.png` and `fit_side.png`: the fitted plates over Valheim's
  body sample points and skeleton (x-ray). It also prints `GIRTH ... -> N% of body points poke through`.
- `tools/blender/pose_test.py`: deforms the exported `skin.obj` files exactly as Unity will and
  renders rest beside posed. Pass `twohand` as the last argument for a two-handed weapon pose plus a stride.
- `tools/blender/arm_poke.py`: checks a `skin.obj` for arm poke-through without Blender (run it with
  Blender's bundled Python, `.../Blender 5.2/5.2/python/bin/python.exe`). For each arm body sample point
  it compares the point's distance from its arm bone with the armor's distance in that direction, and
  prints the points that reach past the plates. In its output, `dir` 0 is behind the arm, ±90 is the
  sides and 180 is the front. "Out by" includes a 1 cm margin for plate thickness, so a value under
  0.01 means the point is inside the plates.
- `pose_test.py ... <out.png> fist [degrees]` curls every finger (not the thumb), 55° per joint by
  default, and renders close-ups of the right hand: check that each finger's plates follow it.
- `tools/blender/hip_poke.py <export dir>...`: counts the greaves vertices outside the breastplate's
  torso plates, per height slice and angle sector, to check that the hip plates cover the thigh tops.
  Run it with Blender's bundled Python.
- `tools/blender/hand_check.py`: a close-up of the right forearm and gauntlet with Valheim's finger
  bones drawn in colour (thumb red, index yellow, middle green, ring blue, little finger purple).

### Deploying

1. Replace `models/ShardplateBreastplate` and `models/ShardplateGreaves` with the new export.
2. **Delete the deployed copies first.** The build copies but never deletes, so stale files linger:
   `%APPDATA%\com.kesomannen.gale\valheim\profiles\Default\BepInEx\plugins\Dank-MysticalWeapons\models\ShardplateBreastplate`
   (and `...Greaves`).
3. `dotnet build -c Release` deploys the DLL and `models/` to the user's **Gale** profile.
4. If the game is running, the user types `mw_reload` in the F5 console.
5. Check `BepInEx\LogOutput.log` for `attached 1 custom pieces` and the
   `material from ..., shader Custom/Creature` lines.

## Current tuning values

| Setting | Value | Notes |
|---|---|---|
| `GIRTH["torso"]` | (1.34, 1.46) | width, depth. Widened 10% from the slim (1.22, 1.32) because the greaves' tops poked through the hip plates in-game. Standing, `hip_poke.py` now finds none outside (20 before). Mid-stride (thigh 35° forward), up to 10.7 cm still poke through (12.4 cm before): the faulds barely follow the thighs, so width alone can't fix it. The slim value had been slimmed from (1.38, 1.62), the user's pick of three rendered options; the body mask hides the body under it. Before that, 1.45/1.70 was tightened at the user's request |
| `TORSO_VK`, `TORSO_LENGTHEN` | waist 140→1.24 − `WAIST_DROP`, bottom −260→0.75 − `WAIST_DROP` | `TORSO_LENGTHEN` 0.25 makes the torso from waist to shoulders 25% longer, so the waist (belt, flare of the hip plates, top of the groin plate) drops about 9.5 cm to roughly 1.145 m. Everything below drops with it, `GROIN_TOP` included; the shoulders stay fixed. Earlier the user had asked to lengthen the torso by moving the waist and bottom knots |
| `GROIN_TOP / STRETCH` | 1.18 / 1.3 | codpiece bottom about 0.88–0.91 m |
| `GIRTH["arm"]` | 1.5 | slimmed from 2.05, which was 1.6× Krajkoa's own proportions and looked bulky. `HAND_GIRTH` stays 1.65 |
| `ARM_FORWARD` | 0, 0, −0.025, −0.025 | shoulder, elbow, wrist, fingertip. Krajkoa's vambrace sat 2–3 cm forward of Valheim's forearm bone, so the forearm and wrist showed through the back. A forward nudge made this worse; the wrist now moves back 2.5 cm. The elbow stays at 0: moving it back exposed the inside of the elbow |
| `ARM_FLARE` | all 1 (off) | was 1, 1.05, 1.25, 1.25 to contain Valheim's thick wrist, until the body mask made that unnecessary. Extra girth for the arm plates only (not the gauntlets), blended along the arm. Moving the wrist back alone made the front of the wrist poke through, because Valheim's wrist is about 14 cm deep front to back |
| `GIRTH["leg"]`, `LEG_BACK` | (1.3, 1.35), 1.15 | width, depth; slimmed from (1.45, 1.45) because the legs looked too wide next to the slim torso. `LEG_BACK` deepens the back of the thigh (hamstrings, glutes); it was 1.4 |
| `KNEE_DROP` | 0.20 | Krajkoa's knee (z −380) is placed 20% below Valheim's knee joint (0.48 m instead of 0.60 m). At the joint the knee plates looked too high; the user lowered them 10% and then 10% more |
| `THIGH_DROP` | 0.26 | the greaves above the knee are shortened by 26%, so their top (Krajkoa z −98 and 0, at 1.19 and 1.30 m) drops to about 1.005 and 1.09 m; the hips and thigh tops looked too high (the user tried 20%, then 25%, then 26%). This opens a small notch at the front of the belt where the body shows. Earlier, a "tighter and longer thigh" change was rejected |
| `THIGH_TOP_GIRTH` | 0.85 | the thigh plates taper smoothly from full girth at the knee to 85% at the top of the greaves, because the upper thighs poked through the breastplate's hip plates (first 0.9, then a bit more) |
| Helm | scale 0.78, stretch (1.04, 1, 1), offset (0, −0.040, +0.08), rot (−6, 180, 0) | the helm was too big at its fitted size of 1.78×. To line up the eye-slit with the eyes it's tipped chin-down 6° (negative X, since the model faces −Z before the 180° turn) and lowered 11% of its fitted height (0.362 attach units). The user tuned these in steps (5° and 10%, then 1% more each). `CustomModelStretch` widens it 4% shoulder to shoulder (2%, then 2% more) |

**Proportions:** compare a part's girth scale with how far it was stretched along its bones
(Krajkoa's own proportions = 1×). After slimming, the arms are about 1.0–1.2× and the torso about
1.0–1.1× wide and 1.0–1.2× deep. The legs were already 0.9–1.0×. Inflating the plates to contain the
body is what threw the proportions off; the body mask handles clipping instead.

## Pitfalls already hit (don't repeat these)

- **Double-sided triangles render dark.** Valheim's `Custom/Creature` shader draws both faces. An
  inward-facing copy at the same depth can win, which makes metal render flat and black. Imported
  models must be single-sided, wound to face their normals (already handled in `MeshBuilder.Tri`).
- **Material colour and emission are ignored** on these armor materials, so recolour the
  **textures** instead (`GradeTexture`). The body under-armor paint comes from `_ChestTex`/`_LegsTex`
  on `m_armorMaterial`; recolouring `_Color` does nothing to it.
- **Blender's automatic (bone heat) weights fail** on this many separate plates, and every vertex falls
  back to a single bone. That's why the script computes its own inverse-distance weights.
- **Spine bones on the arm plates** made plates near the armpit stay behind when the arms swing
  forward (two-handed weapons), so arms use shoulder and arm bones only.
- **Hand or finger bones on the vambrace** twist it with the wrist.
- **Gauntlet fitting:** solving for scale by nearest-point fitting collapses the gauntlet (scale 0.28).
  Capping the shift along the hand made the fit worse. The current version uses a length-based scale and a
  translation-only shift, with a median gap of about 4 cm.
- **Rigid per-bone attachment** (`attach_<Bone>`) was the first approach; the user rejected it as a "rigid frame".
  The code path remains (set `SKINNED = False` in the script) but isn't used.
- **The Drake helmet is a poor fit reference:** its long crest skews the fit. The helm is placed by
  fixed values in code. **The user does not want sizing or placement in the config; colours are fine.**
- **Relative output paths:** Blender saves images relative to its own location. The script resolves
  paths to absolute; keep it that way.
- Writing Python patches with `\\` line continuations inside bash heredocs broke silently twice.
  Prefer the Edit tool for script changes.

- **Changing the body under the armor was tried and removed** (none of it is in the code):
  - Shrinking the forearm and hand vertices of the body mesh toward their bones (50%) still clipped in-game.
  - A translucent Stormlight-blue "ghost" body, drawn by a second SkinnedMeshRenderer while the real
    body was hidden, worked. With a flat `Sprites/Default` tint the user liked the look. It was
    removed because it switched on with the breastplate alone, so a mixed armor set turned the body
    under other pieces into ghost too. Lessons if it comes back:
    - The player's Animator culls based on its renderers. Hiding the body (`forceRenderingOff`) froze
      the skeleton, so set the Animator to `AlwaysAnimate` meanwhile.
    - Valheim's `Ghost` prefab material wasn't found or usable.
    - Gate it on the full set.
  - Shrinking the ghost mesh toward the skeleton was also tried; the user preferred normal size.

- **A generated gorget was tried and removed:** three elliptical lames from the collar up into the
  helm, weighted to Spine2, Neck and Head. The user dropped it; the neck gap under the helm is still
  open, pending the user's helm changes.

## Ideas not yet done

- Pauldrons follow the arm when it's raised high; weight them more to the `Shoulder` bone if the
  user wants them to stay on the shoulders.
- A dropped breastplate or greaves still shows the recoloured Flametal ground model.
- The body sample in `player_skeleton.json` is sparse (every 4th vertex, 253 points). Re-dumping
  every vertex would give a better poke-through check (change `i += 4` in `DevTools.DumpPlayerSkeleton`).

## Repo and git state at handoff

- Branch `shardplate-full-set` (based on `blender-model-import`, which has a PR waiting to be opened).
  The breastplate and greaves are committed (`011a691`); check `git status` for later work.
- `main` has a rule requiring changes through pull requests; the user's admin account bypasses it.
  Ask before pushing to `main`, and prefer a branch plus a pull request. The `gh` CLI isn't installed,
  so give the user the "compare" URL to open the pull request.
