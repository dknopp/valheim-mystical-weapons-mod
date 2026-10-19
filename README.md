# Mystical Weapons from Far Off Lands

A Valheim BepInEx mod that adds legendary weapons and armor from other worlds.
No Jotunn and no asset bundles: weapon models are built in code, armor is recoloured vanilla plate.

## Items

| Item | Prefab | From | Notes |
|---|---|---|---|
| Bat'leth | `Batleth` | Star Trek | Crescent blade, wide two-handed swings (based on the Iron Battleaxe) |
| Sylspear | `Sylspear` | The Stormlight Archive | Glowing Shardspear. Grants **Windrunner** (slow fall, faster stamina) while held. No durability |
| Shardblade | `Shardblade` | The Stormlight Archive | Huge two-hander that chops trees and mines rock. Light, no durability |
| Andúril | `Anduril` | The Lord of the Rings | Rune-etched longsword with spirit damage and x1.5 damage against all undead (skeletons, ghosts, draugr, wraiths). Recipe consumes an Iron Sword - the shards of Narsil |
| Shardplate Helm / Breastplate / Greaves | `ShardplateHelm`, `ShardplateBreastplate`, `ShardplateGreaves` | The Stormlight Archive | Helm modelled on Krajkoa's Shardplate (see Credits); breastplate and greaves are Flametal recoloured slate with Stormlight-blue seams. Full set: +150 carry weight, faster stamina, resistant to blunt/slash/pierce |
| The Elder Wand | `ElderWand` | Harry Potter | Casts spell bolts (based on the Staff of Frost). Uses stamina instead of eitr by default. No durability |
| The Resurrection Stone | `ResurrectionStone` | Harry Potter | Utility slot. Die while wearing it and your skills are not lowered |
| The Cloak of Invisibility | `InvisibilityCloak` | Harry Potter | Monsters cannot see you while you sneak (they can still hear you) |

**Deathly Hallows set:** wand in hand + stone + cloak = **Master of Death**: 1.5x health regen, 1.25x stamina regen, resistant to poison, immune to spirit damage.

Most items are crafted at the forge; the wand and cloak at the workbench. Recipes, stats, crafting station and model/colour tweaks are
configurable in `BepInEx/config/dankn.valheim.mysticalweapons.cfg`.

## Installing

Requires [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
Copy `MysticalWeapons.dll` to `BepInEx/plugins/MysticalWeapons/`.
Install on the server **and** on every client.

## Building

Needs the .NET SDK and Valheim + BepInEx installed.

```
dotnet build -c Release
```

The build references Valheim from `C:\Program Files (x86)\Steam\steamapps\common\Valheim` and BepInEx
from the `Default` profile of Gale (or r2modman if Gale is not installed), and copies the DLL into that
profile. Override with `-p:ValheimDir=...` and `-p:BepInExDir=...`.

Do not install the old standalone Bat'leth mod alongside this one: Mystical Weapons includes the bat'leth.

## Editing models in Blender

Weapon models are built in code, but you can export them, edit them in Blender and drop them back in.

1. Export: `tools/ModelExporter/bin/Release/net48/ModelExporter.exe exports` (after building
   `tools/ModelExporter`) writes `exports/<Prefab>.obj` + `.mtl` for every code-built item.
2. In Blender: **File > Import > Wavefront (.obj)** with default settings. Units are metres. The hand
   sits at the origin and the grip runs along +Y (Blender's +Z after import). Run
   **Mesh > Normals > Recalculate Outside** once.
3. Keep the material names (`Anduril_0`, `Anduril_1`, ...): the number picks which of the weapon's
   colours that part gets (blade, grip, glowing runes...). Unknown materials use the first colour.
4. Export with **File > Export > Wavefront (.obj)**, default axes, and tick **Triangulated Mesh**.
5. Put the file at `BepInEx/plugins/<mod folder>/models/<Prefab>.obj`, e.g.
   `Dank-MysticalWeapons/models/Shardblade.obj`, and restart the game. The log says
   `using custom model` when it is picked up; the inventory icon is drawn from it too.

Custom models are cosmetic and only affect the machine they are installed on, so copy them to every
player who should see them. Delete the file to go back to the built-in model.

Models can be textured: a `map_Kd` texture in the `.mtl` is applied to that material (PNG or JPG).
Helmets work too: a `models/ShardplateHelm.obj` is scaled and centred to fill the vanilla helmet,
then rotated and nudged by fixed values in the item's code. Its texture is recoloured on load from
the `Tint`, `Glow`, `CustomModelBrightness` and `CustomModelGlow` settings, and `CustomModelLight`
adds a soft light in front of it. Placement is deliberately not configurable; colours are.

`ModelExporter.exe exports --verify` re-imports each exported file and checks it matches the original.
`ModelExporter.exe --load models/ShardplateHelm.obj` shows how the mod will read a custom model.

The repo's `models/` folder holds custom models that ship with the mod; the build copies it into the
plugin folder. `exports/` holds reference exports of the code-built weapons.

## Credits

- Shardplate Helm model: based on ["Shardplate"](https://sketchfab.com/3d-models/shardplate-5a4fca1f665b469c9669f387aa879b34)
  by [Krajkoa](https://sketchfab.com/Krajkoa), licensed under
  [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/). Helm and collar extracted from the full suit,
  rescaled, emissive texture merged into the base colour. See `models/CREDITS.md`.

## Adding an item

- **Weapon:** subclass `MysticalWeapon` in `Weapons/`. Pick vanilla `SourcePrefabs` to clone, set stats
  and recipe, and build the model with `MeshBuilder` (blades, prisms, slabs). Hand at the origin, grip along +Y.
- **Armor:** subclass `MysticalArmor` in `Armor/` with a source armor piece, tint and glow colour.

Then add it to `Plugin.Items`.
