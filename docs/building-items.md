# Building items: process and lessons

Stephen's notes on how to build an item with as few game launches as possible, and what earlier items taught us.
`contributing.md` ("Adding an item") has the basics; `docs/art-direction.md` has how items look and act.

## My item checklist

What I check on a new item, or when bringing one of mine up to date:
- [ ] Art-direction questions answered ("Before building an item")
- [ ] Shape checked against the reference's proportions and parts
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
- [ ] README items tables updated
- [ ] Changed defaults on an existing item: the PR says players must reset that item's config
      section (see Config below)
- [ ] Views in the PR: front, profile and three-quarter in one picture, from
      `tools/blender/render_views.py` (how to run it is at its top), committed as
      `docs/images/<Prefab>-views.jpg` and linked in the PR description.
      Code-built items: export them with ModelExporter first. A model not flat in XY needs the turn
      argument to face the camera (the Warglaives: 270, to show the medallion rather than the grip).
      Optional: in-hand shots from the game (F12)

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

### Multiplayer
- Show an effect to everyone by working it out on every client from synced state: the equipment
  hashes in `VisEquipment` (`m_currentShoulderItemHash`, `m_currentRightItemHash`...) and the crouch
  animation (the cloak's fade, Shardplate's size, the Bat'leth's kill bonus).
- A death runs where the victim is owned, often not the killer's client: send the killer anything
  over a vanilla RPC (`RPC_AddAdrenaline`).
- Scale a player's model (`VisEquipment.m_visual`), not the player, so the collider still fits doors
  and beds (Shardplate).

### Recipes and death
- The game lists a recipe only once the player has picked up every ingredient, so a boss trophy hides
  the recipe until the boss is beaten. Give it `m_amountPerLevel` 0, or every upgrade takes another.
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
