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
| Shardplate Helm / Breastplate / Greaves | `ShardplateHelm`, `ShardplateBreastplate`, `ShardplateGreaves` | The Stormlight Archive | Flametal recoloured slate with Stormlight-blue seams. Full set: +150 carry weight, faster stamina, resistant to blunt/slash/pierce |

All items are crafted at the forge. Recipes, stats, crafting station and model/colour tweaks are
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
from the r2modman `Default` profile, and copies the DLL into that profile. Override with
`-p:ValheimDir=...` and `-p:BepInExDir=...`.

## Adding an item

- **Weapon:** subclass `MysticalWeapon` in `Weapons/`. Pick vanilla `SourcePrefabs` to clone, set stats
  and recipe, and build the model with `MeshBuilder` (blades, prisms, slabs). Hand at the origin, grip along +Y.
- **Armor:** subclass `MysticalArmor` in `Armor/` with a source armor piece, tint and glow colour.

Then add it to `Plugin.Items`.
