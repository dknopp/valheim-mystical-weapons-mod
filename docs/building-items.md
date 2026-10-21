# Building items: process and lessons

Stephen's notes on how to build an item with as few game launches as possible, and what earlier items taught us.
README.md ("Adding an item") has the basics; `docs/art-direction.md` has how items look and act.

## My item checklist

What I check on a new item, or when bringing one of mine up to date:
- [ ] Art-direction questions answered ("Before building an item")
- [ ] Shape checked against the reference's proportions and parts
- [ ] Looks at home next to its vanilla peer, in numbers and in the item report's pictures (README,
      "Item report"). Start from 0.5 to 2 times the peer's triangles, or up to 1,000 if that's more, and a
      texture at most one size above the peer's. Go past the triangle count when the item's signature
      shapes need it (see "Look" in `art-direction.md`); Thunderfury was planned at 250-400 and came
      out at 510
- [ ] Stats, cost and station checked against "Tier, cost and balance"; hidden power chosen on purpose
- [ ] Its one lore effect works, triggered the lore's way, with its numbers in config
- [ ] Inventory icon reads at a glance
- [ ] Log clean: `Loaded N mystical items.` and no errors or warnings from this mod
- [ ] Effects show for other players on a server (at least before a release)
- [ ] README items table updated
- [ ] Changed defaults on an existing item: the PR says players must reset that item's config
      section (see Config below)
- Optional: screenshots in the PR (next to its vanilla peer, in hand)

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

## Lessons

### Cloned items and attacks
- A cloned item keeps its source's **item-wide effects** (`m_hitEffect`, `m_triggerEffect`,
  `m_hitTerrainEffect`, `m_spawnOnHit`...), and every attack plays them on top of its own. When you
  swap in another weapon's attack, move the source's item-wide effects onto the attack they belong to.
  (Sulfuras: the Demolisher's blast played on every battleaxe combo hit until it moved onto the slam.)
- To give an item another weapon's moves, clone that weapon's `Attack` and copy its
  `m_animationState` too (Sulfuras: battleaxe combo on primary, the Demolisher's slam on secondary).

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
  name, e.g. `flame`, `gnista` for embers) and place each directly where you want it. The source's
  layout and directions belong to its own shape.
- Reset each system's emitter shape: vanilla flames often emit from the source's own mesh.
- A system with no material may still draw: Nidhögg the Thundering's `Lightning` draws its sparks as
  trails. Copy such a system whole and scale the copy (scaling mode Hierarchy) instead of resizing
  its settings one by one. Log each system's renderer, material, trails and sub-emitters before choosing.
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

### Glow
- A point light on an item hardly shows in daylight; emissive textures carry the glow.

### Config
- Changing a default in code doesn't change an existing config file: BepInEx keeps the value already
  there. After changing a default, edit or delete that line in the test profile's
  `BepInEx/config/dankn.valheim.mysticalweapons.cfg`.
