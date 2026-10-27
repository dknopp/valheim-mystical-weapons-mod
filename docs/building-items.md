# Building items: process and lessons

Stephen's notes on how to build an item with as few game launches as possible, and what earlier items taught us.
`contributing.md` ("Adding an item") has the basics; `docs/art-direction.md` has how items look and act.

## My item checklist

What I check on a new item, or when bringing one of mine up to date:
- [ ] Art-direction questions answered ("Before building an item")
- [ ] Shape checked against the reference's proportions and parts, and approved in grey before
      texturing (see "Custom models: passes")
- [ ] Looks at home next to its vanilla peer, in numbers and in the item report's pictures
      (`contributing.md`, "Item report"). Start from 0.5 to 2 times the peer's triangles, or up to 1,000 if that's more, and a
      texture at most one size above the peer's. Go past the triangle count when the item's signature
      shapes need it (see "Look" in `art-direction.md`); Thunderfury was planned at 250-400 and came
      out at 510. That's Blender's count: the mod draws every face from both sides, so double it
      before comparing with the peer (the item report already does: 1,020 against Nidhögg the
      Thundering's 420)
- [ ] Stats, cost and station checked against "Tier, cost and balance"; hidden power chosen on purpose; boss trophy set if it is one of the strongest
- [ ] Its one lore effect works, triggered the lore's way, with its numbers in config
- [ ] Inventory icon reads at a glance
- [ ] Log clean: `Loaded N mystical items.` and no errors or warnings from this mod
- [ ] Effects show for other players on a server (at least before a release)
- [ ] Tried everywhere a player will meet it, not only where the test world puts you: anything that
      goes "home" at a bed indoors (wooden and dragon bed) as well as the world start
- [ ] README items tables updated; a big addition with a GIF or screenshots gets a dated "What's new" entry
- [ ] Changed defaults on an existing item: the PR says players must reset that item's config
      section (see Config below)
- [ ] An existing item reads a setting in a new way (a new model or effect): tested with the value an
      older config saved, not only the new default (see Config below)
- [ ] Views in the PR: front, profile and three-quarter in one picture, from
      `tools/blender/render_views.py` (how to run it is at its top), committed as
      `docs/images/<Prefab>-views.jpg` and linked in the PR description.
      Code-built items: export them with ModelExporter first. A model not flat in XY needs the turn
      argument to face the camera (the Warglaives: 270, to show the medallion rather than the grip).
      Optional: in-hand shots from the game (F12)
      Armour sets: the full character instead, from in-engine renders (`references/<item>/DevRender.cs.txt`
      with DevAutoRun): standing from four sides, close-ups, crouched (the Brotherhood).

## Process

1. **Settle decisions before the first test launch:** the look, numbers, triggers and which vanilla
   pieces it borrows. Changing a decision after a test round costs a whole launch.
2. **Find the vanilla pieces first.** Search the game's asset list (below) for prefabs to borrow
   (projectiles, effects, particles, attacks) instead of guessing names.
3. **Log what you borrow.** At setup, log each borrowed piece's name and key settings. One look at
   the log after a launch then shows what actually happened, instead of another round of guessing.
4. **Read the log, not only the screen.** It's at `BepInEx/LogOutput.log` in the Gale test profile;
   if you use Claude Code, let it read the log after a test.
5. **When something doesn't show, probe it at runtime.** A temporary `MonoBehaviour` added to the
   object, which logs its state a couple of seconds after it appears (playing, particle count,
   position, scale), separates "not there" from "there but in the wrong place". Remove it before the PR.
6. **Test one round per batch of changes,** with a short checklist of what to look for.
7. **Read vanilla numbers in a discovery launch.** When defaults depend on a vanilla item's numbers
   (the Warglaives' damage is about 40% above Skoll and Hati's), log them at setup and launch once
   with that config still unbound (`HasDamage` off), so no wrong default gets saved. Then set the
   defaults and delete the item's config section before the next launch.
8. **Put an effect's look in config, relative to its vanilla source** (`FelAuraSize` 0.15 = 0.15 times
   the Dead Raiser's flames). Each screenshot round is then a config edit and a restart, not a rebuild,
   and one setting per round. When an effect doesn't show at all, change how it's made rather than
   turning it up (see Glow).

## Custom models: who does what

When an item gets a custom model (Thunderfury onwards), Claude Code does the hands-on work and I direct:
- **References:** Claude gathers them, capturing front, edge and 3/4 views from a 3D viewer in the
  built-in browser into the gitignored `references/` folder, and writes the reference sheet. I only
  hunt for references if that pass turns up too little. If there's a downloadable 3D model, either of
  us fetches it into `references/<item>/`; Claude loads it into Blender beside the build (see "Look"
  in `art-direction.md` for which reference decides what).
- **Modelling:** Claude builds the model in Blender through the Blender MCP, one part at a time,
  showing a screenshot and triangle count after each. I react and steer; I may tweak by hand, but
  I'm not expected to. Before a part, Claude captures close-up angles of it; when my read of the
  reference differs from Claude's, Claude checks it in the viewer rather than going with either.
  Setting up the Blender MCP, and the problems we hit: `blender-mcp.md`.
- **Texture and export:** Claude scripts the bake and export (as for Sulfuras), so they can be re-run.

## Custom models: passes

The Warglaives looked right because of the focused passes after the first build, each starting with
a fresh, close look at the reference. Build in this order, with a review after each step:

1. **Blockout.** Silhouette and proportions only, in plain grey, following "Custom models: before the
   first pass" below. Check the size and hold in game now,
   while parts are cheap to move (the Warglaives went from 0.88 m to 1.6 m and had their hold turned
   before any detail).
2. **Construction.** List how the real thing is put together before detailing: which parts are separate
   pieces, which sits on or passes through which, what's open. Model it that way: the Warglaives are two
   blades coming out of a solid hub, not one plate with the hub stuck on top.
3. **Detail, one region and one concern per pass.** For each part (a blade's cross-section, the
   medallion, the back hooks), study close-ups of just that part, write down what you see (outline,
   depth, steps, counts), then build it and check it against those notes. Depth matters as much as
   outline: the Warglaive blades gained a raised plateau, a bevel down to the edge and a groove, none
   of which a front view shows.
4. **Compare.** Put a render of ours beside the reference at the same angle and list every difference
   before changing anything. The Warglaives' second round came from this: the back hooks are dark
   metal, not fel, and the medallion is a gold-rimmed shield with a star, not a round disc.
5. **Approve the shape in grey, then texture.** A shape change after texturing costs a whole review round.
6. **Texture from the shape.** Make the texture follow the geometry: glow on the edges, dark to bright
   across the bevel, the groove picked out, and materials changing by region (metal at the hub kindling
   into fel along the blade). A texture that ignores the shape flattens the depth the passes added.

## Custom models: before the first pass

What separated our first Blender pass from a game artist's version of the same object (the Scroll of
Hearth Portal against a downloaded Sketchfab scroll; see Lessons). These are for models with no set
design to match: everyday objects (a scroll, a pouch) or a design of our own. An iconic item from its
source (the Warglaives, Thunderfury) matches the source's form as closely as we can; don't exaggerate
or regroup it. Before building the blockout of a model with no set design, check these, so the first
grey render already looks like the refined one:

1. **Exaggerate the signature shape.** Find the one outline that says what the object is and push it past
   life: game art pushes it 1.5 to 2 times. A real rolled scroll is a near-straight tube; a game scroll
   is a bowtie, pinched hard at the tie. Check it with the grey render at icon size (128 px, from the
   angle the mod draws icons): if it doesn't read there, it won't read in a hand or on the ground.
2. **One focal point, detail stacked there.** Put the eye-catching parts in one cluster and leave the rest
   as clean large forms. That cluster is where the identity colour goes (the scroll's blue band, from
   Diablo II's icon). Small details spread over the model compete and all vanish. Within the cluster,
   the identity colour gets the largest area and accents stay smaller: the scroll's seal grew until it
   read in the icon, then outgrew the band, so the band grew longer along the roll. Judge this in a 128 px
   render at the angle the mod draws the icon from.
3. **Group thin details into masses.** At game size anything thinner than about 1/50 of the item's
   length disappears unless it's part of the outline. Many wraps of cord read as one chunky band; two
   thin loops read as nothing.
4. **Show the construction where it can be seen.** Open ends, edges and seams carry the "how it's made"
   story: the scroll's ends curl outward to show the spiral, instead of the layers hiding inside a tube.
5. **Match the source game's style, not the real object.** Model the stylised version the source game
   would draw (proportions, chunkiness), then add only the realistic touches that survive point 3.
   Physically right detail at the wrong scale was the first pass's main mistake.

## Finding vanilla prefabs

Prefab names (projectiles, `vfx_`/`sfx_`/`fx_` effects, items) are listed in the game's asset manifest:

```bash
grep -aoE "[A-Za-z0-9_/ .-]+\.prefab" "<Valheim>/valheim_Data/StreamingAssets/SoftRef/manifest_extended" | sed 's#.*/##' | grep -i fire | sort -u
```

At runtime, items come from `ObjectDB` (`FindVanilla` during setup) and everything networked
(projectiles, effects) from `ZNetScene.instance.GetPrefab(name)`. For game classes and fields, the
build is the quickest check: write the code and let the compiler say what doesn't exist.

To read how a game method works, dump its IL with Mono.Cecil (it ships with BepInEx in `core/`): a
small console app over `assembly_valheim.dll` that prints a type's fields, methods and IL. Plain
.NET Framework reflection can't load some game types (they use default interface methods). Stats
for content the wiki doesn't cover yet (the Deep North) are in the item databases such as
valheim.tools and valheim.weirdgloop.org.

## Lessons

### Cloned items and attacks
- A cloned item keeps its source's **item-wide effects** (`m_hitEffect`, `m_triggerEffect`,
  `m_hitTerrainEffect`, `m_spawnOnHit`...), and every attack plays them on top of its own. When you
  swap in another weapon's attack, move the source's item-wide effects onto the attack they belong to.
  (Sulfuras: the Demolisher's blast played on every battleaxe combo hit until it moved onto the slam.)
- To give an item another weapon's moves, clone that weapon's `Attack` and copy its
  `m_animationState` too (Sulfuras: battleaxe combo on primary, the Demolisher's slam on secondary).

- A cloned item brings its source's look as well as its moves: the Staff of Embers carries its
  burning orb over the staff whatever model replaces it, and a staff brings its two-handed hold. Pick
  the source for its hold and animation (the Elder Wand: the butcher knife's one-handed jab), then
  load the spell into its attack: `m_attackType`, `m_attackProjectile` with its counts, speeds and
  accuracy, the sounds, and the launch point (`m_attackOriginJoint`, `m_attackHeight`,
  `m_attackRange`, `m_attackOffset`; a melee attack's range launches from a sword's reach). Set
  `m_skillType` to the skill the item should train.
- The game only charges (`m_bowDraw`) and reloads (`m_requiresReload`) a weapon's **primary** attack.
  As a secondary, a charged attack fires at a 0% charge's speed (nothing comes out) and one that needs
  reloading never starts. Turn both off on a borrowed secondary. Dundr's "loading" bar is its reload.
- Don't turn off `m_loopingAttack` on a looping cast (the Staff of Frost's `staff_rapidfire`): the
  game ends the loop's animation when the button is let go, so without it the player froze mid-cast
  until they dropped the item. To limit a loop, skip bursts in an `Attack.FireProjectileBurst` prefix.
- A weapon has one damage (`m_damages`), read when its spell flies. For two attacks with different
  damage, swap it in a `Humanoid.StartAttack` prefix.
- The ObjectDB can hold several effects of one type: the first `SE_Shield` is the Fuling shaman's.
  Take the one the source item gives (the Staff of Protection's cast, `Aoe.m_statusEffect`).

### Effects on hit
- Patch `Character.Damage` (runs on the attacker's client) and check the attacker's current weapon,
  as `Anduril.cs` does.
- Anything the effect spawns that deals damage passes through the same patch. Mark it so it can't
  trigger itself (the Sulfuras fireball carries no skill, and the patch skips hits without one).
- A swing hits everything it touches in the same frame: roll once per frame for "chance per swing".
- Vanilla projectiles: instantiate the prefab, call `Setup(owner, velocity, -1, null, null, null)`,
  then set `m_damage` and `m_skill`. Fire damage sets targets burning on its own.
- Vanilla chain lightning (`ChainLightning`, an `Aoe`): it only hits and leaps to enemies
  (`BaseAI.IsEnemy`), so test on real creatures, not training dummies. Each leap is a copy of the one
  you spawned, carrying its damage, and hits every enemy within 8 m again, so cap it at one hit per
  creature per proc (Thunderfury) or a group takes it several times over. With no decompiler, a small
  reflection tool that lists a game method's calls was enough to read how `Aoe` decides what it hits.

### Borrowed particles
- Don't copy a vanilla item's whole effect: take only the systems you need (pick them by material
  name, e.g. `flame`, `gnista` for embers, or by the child they sit under: the Dead Raiser's aura is
  the systems under `equiped`, apart from its item sparkle and `UpgraderGlow`) and place each directly
  where you want it. The source's layout and directions belong to its own shape.
- Reset each system's emitter shape: vanilla flames often emit from the source's own mesh.
- A system with no material may still draw: Nidhögg the Thundering's `Lightning` draws its sparks as
  trails. Copy such a system whole and scale the copy (scaling mode Hierarchy) instead of resizing
  its settings one by one. Log each system's renderer, material, trails and sub-emitters before choosing.
- Not on anything attached to the player: the rig's bones carry its import scale (x100 on the hands),
  so with scaling mode Hierarchy the Warglaives' flames filled the screen. Use Local there and size
  the particles from config.
- To follow a moving part, emit from its own mesh: shape `SkinnedMeshRenderer` for a model on the
  hand bones (dual-wield), `MeshRenderer` for a rigid one (the back holster). The particles then
  follow the hands and cover the whole blade; raise the rate with the area (`FelAuraDensity`).
- Emit from just outside solid geometry (`BoxShell` slightly bigger than the part); particles born
  inside a thick mesh never show.
- For flames: world simulation space, small start speed and a negative gravity modifier, so they
  rise whichever way the item points and trail behind a swing.
- Squashing a whole borrowed effect squashes every system in it: flattening the portal swirl's depth
  also thinned its dark void (`Black_suck`) until you could see through it. Scale the parent for the
  look, then give back the systems that need their own depth.

### Custom models
- Read the reference part by part, not as a whole: Thunderfury's first reference sheet read an open
  slot as a painted fuller, and that only showed up close in the viewer, tilted.
- Build the model from a script, one function per part (`tools/blender/thunderfury_model.py`), and
  bake and export from the same script, so a change to any part is a re-run, not a redo.
- A downloaded glTF can import upside down or at any scale: check which end is which, then scale it to
  the item's length before comparing.
- The mod's loader fan-triangulates, so export with Triangulated Mesh on; concave faces (Thunderfury's
  U-shaped base plate) otherwise break.
- Check an export with `ModelExporter.exe --load models/<Prefab>.obj` (triangles per material,
  textures found, UVs) before the first launch.
- The OBJ import mirrors X, so a model that isn't symmetric across X can face the wrong way in game
  (the Warglaive's hub first sat behind the fist). Check the hold in game before baking the texture.
- **The scroll's three stages** (the source of "Custom models: before the first pass"). The code-built
  placeholder was a stack of cylinders: it read as a rod. Our first Blender pass modelled a real scroll
  faithfully (a spiral sheet, a peeling flap, a cord in two thin loops, a knot, tails, a seal off to one
  side), about 1,100 triangles, yet its outline was still a straight tube and every detail was too thin
  or too scattered to read. Pixel Life's Sketchfab scroll (CC BY 4.0, looked at only) fixed that with
  a bowtie pinch, one wide cross-wrapped band with the seal on it, curled ends, and a slimmer roll
  (about 7 times as long as it is wide, against our 5). Reworking ours that way took one round at the
  same triangle count. The lesson: the gap was in how much each form was exaggerated, grouped and
  focused, not in how many triangles or how much detail it had.

### Glow
- A point light on an item hardly shows in daylight; emissive textures carry the glow.
- Borrowed glow sprites hardly show either: the Dead Raiser's `UpgraderGlow`, spread along the
  Warglaives' blades, was invisible. Raising the blade's emission strength (`FelGlowStrength`, 0.6 to
  1.5) gave a halo through the game's bloom instead, brightest where the texture is palest.

### Status effects
- Sheathing (R) unequips the weapon, which ends its `m_equipStatusEffect`. To keep it going while
  sheathed, add a second effect on sheathe that ends itself once the item is drawn or gone (Sylspear).
- Copying a vanilla effect's fields onto a subclass (Syl's wisp from the Wisplight) also copies its
  live state, including its cached `m_nameHash`, which makes the game take the copy for the
  original. Skip those fields. Clear its `m_startMessage` to keep the sound without the message.
- The HUD lists only effects with an icon and `m_hidden` off, so a helper effect can hide behind
  another's icon (Syl carries the sheathed Windrunner's perks).
- A slow fall doesn't stop fall damage: it comes from the height fallen. `m_fallDamageModifier = -1`
  removes it.
- A weapon can carry an adrenaline bar like a trinket: `m_maxAdrenaline` adds to the player's
  maximum, and every equipped item's `m_fullAdrenalineSE` fires when it fills (the Bat'leth). Block
  and parry adrenaline come from the blocking item (`m_blockAdrenaline`, `m_perfectBlockAdrenaline`);
  a plain dodge gives none, only a blow taken mid-roll (`Player.RPC_HitWhileDodging`).

### Materials
- Valheim's creature shaders are opaque, the Ghost's too (`Custom/Creature`): its look comes from its
  texture and particles, so its material on another mesh draws nothing. For a see-through look, put a
  copy of each part's own texture on `Sprites/Default` (`ModelUtil.SpectralMaterial`).
- Clearing a borrowed material's `_EmissionMap` without switching its emission off makes the whole
  surface glow at the borrowed strength. The Shardplate breastplate and greaves with
  `CustomModelGlow = 0` turned into a white silhouette: the recoloured Flametal material's Stormlight
  glow (2.5x) lit every plate. To turn glow off, set `_EmissionColor` to black: `Custom/Creature`
  glows even with the `_EMISSION` keyword off (the log said "emission off" for the white plates).

### Armour sets on vanilla bases (the Brotherhood)
- **Pick bases from grey renders on the player.** Each candidate base looks different worn than in its
  icon; grey shape renders side by side settled hood, robe, legs and cape in one pass. The mesh doesn't
  set the tier: stats, station and recipe are ours.
- **Clear what the clone carries:** its set bonus, eitr regeneration, speed penalty and resistances
  (`m_setStatusEffect`, `m_eitrRegenModifier`, `m_movementModifier`, `m_damageModifiers`), or they
  come along unseen. The Lox cape's frost resistance would have.
- **Pieces from different vanilla families clip.** A cape is a separate mesh the game never bends around
  the chest piece; each vanilla cape is shaped for its own family. The Embla robe's tails went through
  the linen cape when crouching, exactly as with the vanilla pieces. A longer, wider cape (Lox) fixed
  it; check crouched, from behind, with the candidates rendered side by side.
- **Vanilla allows one set bonus, at the full set.** `Core/ArmorSet` grants a bonus per piece count and
  shows one status icon (the highest tier's) plus a set block on every piece's tooltip.
- **Tooltips:** the game's tooltip ends in blank lines; trim them before adding a block. Don't repeat in
  an item's description what a bonus line already says.
- **A centre-screen message** is `Character.Message(MessageHud.MessageType.Center, text)`. Anything
  tied to crouching needs a cooldown: sneaking is crouching on and off.

### Painting our own textures on vanilla meshes
- A tint colours a whole piece one way. For a mix (cloth, leather, metal, a red lining) paint our own
  texture on the vanilla texture's layout: its opaque islands are the mesh's parts. Read the vanilla
  textures from the game on our own machines (`ModelUtil.ReadableCopy`), never commit them, and number
  the islands from a colour-coded render before painting.
- The vanilla textures are 128 px; most of their blockiness is in the **normal maps**, not the colour.
  Enlarging and blurring a normal map smooths it, but a whole-texture blur pulls the empty space between
  islands into their edges, which lights every UV seam as a line. Fine on big islands (robe, boots);
  on a patchwork it isn't worth it (the hood kept the vanilla one).
- **Check a mesh's UV layout before planning painted detail.** The Eitr-weave hood's texture is a
  patchwork of small pieces packed into one island: painted trim ran along the gaps (a stripe over the
  crown), and a repeating pattern broke at every join because the pieces are smaller than one motif.
  Tone under the vanilla shading was the most that hood could take; detail there needs our own model.
- To see which texel shows where on a non-readable mesh: render it unlit (lights off, ambient black)
  with a texture whose colour encodes its own texel coordinates, from several sides. Neighbouring pixels
  that jump far apart on the texture are seams; texels seen against the background are real edges. It
  placed a mirrored ornament exactly, but couldn't fix the shading along the mesh's own crease
  (kept on branch `brotherhood-hood-ornament`).
- Pillow's `resize` premultiplies alpha: resize colour and alpha apart when alpha isn't transparency
  (ours is how much vanilla shading to keep), or alpha-0 areas go black.
- Both sides of a cape share one texture. A lining is a second material on the same mesh drawing only
  the inside; build its texture from the outside one to keep the cut-outs (a ragged hem), and mask it
  so fur and clasps aren't lined.
- Inherited `_Metallic` 1 with nothing to reflect renders black: set leather and cloth to 0.
- A repeating pattern sits wherever the texture puts it: shift it per panel to centre it, and leave it
  out behind bold motifs.
- **Icons:** a white tint makes the vanilla icon near-white. Recolour its parts by how red they are
  (`ModelUtil.RemapIcon`) at load; recoloured vanilla icons can't be committed.

### Small models and effort
- A model a few centimetres on screen doesn't need Blender: the hidden blades stayed code-built. What
  reads at that size is the silhouette, which way the flat faces the camera (stood on its edge, the
  blade showed as a dagger from behind, not a line) and contrast: a steel housing beside the blade read
  as one long blade; leather that blends into the glove fixed it.
- A wrist frame isn't mirrored between the arms, so a model has to be symmetric across X there: any tilt
  but 0 or 90 degrees turns the two blades opposite ways.
- When detail fights the vanilla mesh, stop early and fall back to its own shading; note the attempt and
  why it failed so the next one starts from there.

### Things placed at the player's home (the Hearth Portal)
- "Home" is the bed far more often than the world start. The portal was only tried at the world start
  until its PR was open; at a bed it stood on the mattress, faced a different way every cast and sent you
  through when you woke up.
- Place relative to the bed, not to how the player stood. The bed is usually unloaded when they act far
  away, so note its pose whenever it loads or is used (`Bed.Awake`, `Bed.Interact`), in the player's
  `m_customData`, and check it still matches their spawn point. Both vanilla beds put the spawn point
  0.44 m above their base, facing the foot, with the bed's length along its forward axis.
- On a bed beats in front of it: nothing stands on a mattress, and the floor at the foot is where chests go.
- A step-in trigger near where a player lies, sits or respawns fires when they get up (getting up from
  sleep takes about 3 s). Ignore them while in bed or seated and for a while after (10 s), say so if they
  step in then ("The portal is still settling"), and keep the wait after arriving short (3 s) so they
  can turn straight back.
- Time an emote by when its key pose lands, not when it starts, and tune it in quarter seconds in game:
  the point twice landed before the portal formed.

### Testing without playing
- The DevAutoRun launch (load the test world, run, quit) plus a temporary render script
  (`references/<item>/DevRender.cs.txt`, copied to the repo root while used) gives orthographic in-engine
  renders of the worn set from any side, crouched (`m_crouchToggled`) or with other gear swapped in.
  Raise DevAutoRun's `SettleSeconds` while it runs, and judge colours side by side in one run: the sky's
  light changes them between runs.
- To learn a vanilla prefab's layout (which axis a bed's length runs along, which end is the foot), log
  it once from a temporary probe (mesh bounds in the root's space, child names, colliders) instead of
  guessing.
- A probe that fails now and then usually trips over another probe's leftovers: the step-cancel probe
  walked into the home end earlier probes had opened at the world start. Make probes keep clear of each
  other rather than re-running until it's green.

### README media
- Video to webp: `ffmpeg` with `libwebp`, 960 wide, 12 fps, quality 65, looping. Moving grass makes it
  big; stay under 10 MB so GitHub shows it. Compare a frame with the source before settling.
- Screenshots: crop the full-size capture and scale down to 1280 wide, so a crop never upscales. In a
  pair, match the subject's size by measuring both in one side-by-side picture; by eye on separate
  images it was off by a quarter.
- Show what players will meet most: the portal at a bed, not at the world start, so nobody thinks it
  only goes to the stones.

### Multiplayer
- Show an effect to everyone by working it out on every client from synced state: the equipment
  hashes in `VisEquipment` (`m_currentShoulderItemHash`, `m_currentRightItemHash`...) and the crouch
  animation (the cloak's fade, Shardplate's size, the Bat'leth's kill bonus).
- A death runs where the victim is owned, often not the killer's client: send the killer anything
  over a vanilla RPC (`RPC_AddAdrenaline`).
- Scale a player's model (`VisEquipment.m_visual`), not the player, so the collider still fits doors
  and beds (Shardplate).

### Recipes and death
- The game lists a recipe only once the player has picked up every ingredient, but a recipe a
  character already knows (saved with the character) stays listed even when a new ingredient is added.
  To hide a trophy-gated recipe for everyone, filter `Player.GetAvailableRecipes` on
  `IsMaterialKnown(trophy)` (`TrophyGatePatch`). Give the trophy `m_amountPerLevel` 0, or every upgrade
  takes another.
- The crafting panel shows four ingredients. A fifth is still required but never shown, so the craft
  button says an ingredient is missing without saying which. Keep `DefaultRecipe` to three
  ingredients when the item has a boss trophy, four without; registration logs a warning otherwise.
- Don't give an existing config key a new meaning: players keep their saved value. Bind a new key and
  stop reading the old one (the Elder Wand's `BoltDamage` replaced `Damage`; `HasDamage => false` turns
  off the shared damage keys).
- Each upgrade level costs every ingredient's `m_amountPerLevel` again. `ParseRequirements` gives gear
  (anything with upgrade levels of its own: the Iron Sword Andúril is reforged from) 0, so it is used
  up only by the first craft; materials cost half their amount again per level.
- `Player.CreateTombStone` moves the inventory to the grave, and checks the world's keep-items
  modifiers itself. Skip it to keep everything (Master of Death), or take an item out before it and
  put it back after (the Resurrection Stone).

### Config
- Changing a default in code doesn't change an existing config file: BepInEx keeps the value already
  there. After changing a default, edit or delete that line in the test profile's
  `BepInEx/config/omm.valheim.mysticalweapons.cfg`. With Claude Code, ask it to make the change with
  a file edit: a shell command that rewrites the file gets blocked as destructive.
- A setting gets saved even while it does nothing. The Shardplate breastplate and greaves saved
  `CustomModelGlow = 0` and `CustomModelBrightness = 1` while they were recoloured vanilla armour,
  where those settings had no effect. When #20 gave them custom models with new defaults (1 and 2.4),
  anyone who had played the earlier beta kept 0 and 1, and saw a white silhouette (see Materials).
  Before an existing item starts using a setting in a new way, check what older versions saved for it
  (`git show <old commit>:<item file>`), make every value of it work (0 = off must really be off),
  and test once with the old value in the test profile's config, not only with a fresh one.
