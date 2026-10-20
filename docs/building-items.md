# Building items: process and lessons

How to build an item with as few game launches as possible, and what earlier items taught us.
README.md ("Adding an item") has the basics; `docs/art-direction.md` has how items look and act.

## Process

1. **Decide before building.** For a new look, attack or effect, list the decisions on one screen
   (what it looks like, numbers, what triggers it, which vanilla pieces it borrows) and agree on them
   first. Changing a decision after a test round costs a whole launch.
2. **Find the vanilla pieces first.** Search the game's asset list (below) for prefabs to borrow
   (projectiles, effects, particles, attacks) instead of guessing names.
3. **Log what you borrow.** At setup, log each borrowed piece's name and key settings. One look at
   the log after a launch then shows what actually happened, instead of another round of guessing.
4. **Claude reads the log itself.** It's at `BepInEx/LogOutput.log` in the Gale test profile; after a
   test, just describe what you saw and let Claude check the log.
5. **When something doesn't show, probe it at runtime.** A temporary `MonoBehaviour` added to the
   object, which logs its state a couple of seconds after it appears (playing, particle count,
   position, scale), separates "not there" from "there but in the wrong place". Remove it before the PR.
6. **Test one round per batch of changes,** with a short checklist of what to look for.

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

### Borrowed particles
- Don't copy a vanilla item's whole effect: take only the systems you need (pick them by material
  name, e.g. `flame`, `gnista` for embers) and place each directly where you want it. The source's
  layout and directions belong to its own shape.
- Reset each system's emitter shape: vanilla flames often emit from the source's own mesh.
- Emit from just outside solid geometry (`BoxShell` slightly bigger than the part); particles born
  inside a thick mesh never show.
- For flames: world simulation space, small start speed and a negative gravity modifier, so they
  rise whichever way the item points and trail behind a swing.

### Glow
- A point light on an item hardly shows in daylight; emissive textures carry the glow.

### Config
- Changing a default in code doesn't change an existing config file: BepInEx keeps the value already
  there. After changing a default, edit or delete that line in the test profile's
  `BepInEx/config/dankn.valheim.mysticalweapons.cfg`.
