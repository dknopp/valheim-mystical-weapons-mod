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
  helm's placement values (`CustomModelRotation (0,180,0)`, `CustomModelScale 0.78`,
  `CustomModelOffset (0,0,0.08)`, `CustomModelLightPosition`). All pieces use
  `CustomModelMaterialSource => "ArmorFlametalChest"`, since the Drake helmet's own material is matte and looked flat.
- `MysticalArmor.cs`: armor base class. `UsePerBoneModels` loads `models/<Prefab>/`: if it holds
  `skin.obj` it becomes `attach_skin` (skinned), otherwise each `<Bone>.obj` becomes a rigid
  `attach_<Bone>`. `UseCustomModel` handles the helm. `PrepareCustomMaterials` and `GradeTexture`
  recolour textures on load (Tint × CustomModelBrightness, plus a Glow wash). `RecolorBodyPaint`
  recolours the vanilla `_ChestTex`/`_LegsTex` under-armor body paint so gaps between plates read as
  dark blue rather than black. `ReloadCustomModel` is used by the reload command.
- `ObjImport.cs` / `MeshBuilder.cs`: the OBJ loader. Positions and normals are X-mirrored, because OBJ is
  right-handed and Unity left-handed. Two custom line types for skinning:
  `bp <bone> <16 floats>` (a bind pose: Unity space, row-major, in player bone order) and
  `vw i0 w0 i1 w1 i2 w2 i3 w3` after each `v` line. Imported triangles that have normals are added
  **single-sided**, wound to face their normals (see pitfalls).
- `SkinBoneNames.cs`: a component that stores the bone names, plus a Harmony postfix on
  `VisEquipment.AttachArmor` that rebinds skinned armor bones **by name**, so it survives a game update
  that reorders the skeleton.
- `Armor/GhostBody.cs` (experimental): while the breastplate is worn, the body becomes a
  translucent spirit in the Shardplate's `Glow` colour, so any body showing through the plates reads as
  Stormlight rather than skin. The real body renderer only stops drawing (`forceRenderingOff`), so
  Valheim can keep updating it, and a second SkinnedMeshRenderer with the same mesh and bones draws
  the ghost. The material is a flat, unlit `Sprites/Default` tinted with `Glow`, which the user
  chose over borrowing Valheim's Ghost creature material or adding skin-texture detail. `Opacity`
  (0.35) is in code. The material is built once, so colour changes need a restart.
  - **The animator must be set to `AlwaysAnimate` while the ghost is on.** It culls based on its
    renderers, and with the real body hidden the skeleton froze (the body was stuck in place and no
    animations played). The original culling mode is restored on unequip.
  - Tried and removed: shrinking the forearm and hand vertices of the body mesh toward their bones
    (`BodyShrink`, 50%). It still clipped in-game.
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
| `GIRTH["torso"]` | (1.38, 1.62) | width, depth. Was 1.45/1.70; the user asked for "tighten" |
| `TORSO_VK` | waist 140→1.24, bottom −260→0.75 | the user asked to lengthen the torso; the shoulders stay fixed |
| `GROIN_TOP / STRETCH` | 1.18 / 1.3 | codpiece bottom about 0.88–0.91 m |
| `GIRTH["arm"]` | 2.05 | `HAND_GIRTH` 1.65 |
| `ARM_FORWARD` | 0, 0, −0.025, −0.025 | shoulder, elbow, wrist, fingertip. Krajkoa's vambrace sat 2–3 cm forward of Valheim's forearm bone, so the forearm and wrist showed through the back. A forward nudge made this worse; the wrist now moves back 2.5 cm. The elbow stays at 0: moving it back exposed the inside of the elbow |
| `ARM_FLARE` | 1, 1.05, 1.25, 1.25 | extra girth for the arm plates only (not the gauntlets), blended along the arm. Moving the wrist back alone made the front of the wrist poke through, because Valheim's wrist is about 14 cm deep front to back |
| `GIRTH["leg"]`, `LEG_BACK` | 1.45, 1.4 | `LEG_BACK` deepens the back of the thigh (hamstrings, glutes) |
| `LEG_VK` thigh top | −98→1.19 | a "tighter and longer thigh" change was rejected; that's not what the user wanted |
| Helm | scale 0.78, offset z +0.08, rot y 180 | the helm was too big at its fitted size of 1.78× |

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
