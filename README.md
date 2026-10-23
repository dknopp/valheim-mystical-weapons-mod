# Mystical Weapons from Far Off Lands

A [Valheim](https://www.valheimgame.com/) mod that brings legendary weapons and armor from other
worlds into the game: Andúril from Middle-earth, Shardblades and Shardplate from Roshar, Sulfuras
from Azeroth, a Klingon bat'leth and the three Deathly Hallows.

Every item is crafted from Valheim's own materials, sits at a sensible point in the game's
progression and has one power from its story. Models are drawn in Valheim's style, and the mod
needs nothing but BepInEx: no Jotunn and no asset bundles.

## Items

### Weapons

| Item | From | What it does | Crafted at |
|---|---|---|---|
| Bat'leth | `Batleth` | Star Trek | Crescent blade, wide two-handed swings (based on the Iron Battleaxe) |
| Sylspear | `Sylspear` | The Stormlight Archive | Glowing Shardspear. Grants **Windrunner** (slow fall, faster stamina) while held. No durability |
| Shardblade | `Shardblade` | The Stormlight Archive | Huge two-hander that chops trees and mines rock. Light, no durability |
| Andúril | `Anduril` | The Lord of the Rings | Rune-etched longsword with spirit damage and x1.5 damage against all undead (skeletons, ghosts, draugr, wraiths). Recipe consumes an Iron Sword - the shards of Narsil |
| Sulfuras, Hand of Ragnaros | `Sulfuras` | World of Warcraft | Ragnaros's two-handed hammer (based on the Demolisher, Clubs skill): a three-hit battleaxe combo, with the Demolisher's ground slam on the secondary attack (`ComboStamina`, `SlamStamina`, `SlamDamageMultiplier` settings). Red elementium with flame swirls and glowing runes; spikes white-hot at the tips (custom model, `models/Sulfuras.obj`), with Valheim's flametal grain laid over the body in game (`TextureGrain` setting). A swing that lands may hurl a fireball at the creature struck: fire damage that sets it burning (`FireballChance`, `FireballCooldown`, `FireballDamage` settings), and small flames lick off the head (`HeadFlames` setting). Recipe consumes a Demolisher. |
| Thunderfury, Blessed Blade of the Windseeker | `Thunderfury` | World of Warcraft | One-handed sword (based on Nidhögg): a split blade with a glowing ball of lightning at its root, small sparks crackling around it (`OrbCrackle` setting); custom model, `models/Thunderfury.obj`, with Valheim's iron grain laid over it in game (`TextureGrain` setting). A swing that lands may loose chain lightning at the creature struck, which leaps to others nearby (`LightningChance`, `LightningCooldown`, `LightningDamage` settings). Recipe consumes Nidhögg the Thundering; two Thunder Stones stand in for the Bindings of the Windseeker. |
| Shardplate Helm / Breastplate / Greaves | `ShardplateHelm`, `ShardplateBreastplate`, `ShardplateGreaves` | The Stormlight Archive | Helm modelled on Krajkoa's Shardplate (see Credits); breastplate and greaves are Flametal recoloured slate with Stormlight-blue seams. Full set: +150 carry weight, faster stamina, resistant to blunt/slash/pierce |
| The Elder Wand | `ElderWand` | Harry Potter | Casts spell bolts (based on the Staff of Frost). Uses stamina instead of eitr by default. No durability |
| The Resurrection Stone | `ResurrectionStone` | Harry Potter | Utility slot. Die while wearing it and your skills are not lowered |
| The Cloak of Invisibility | `InvisibilityCloak` | Harry Potter | Monsters cannot see you while you sneak (they can still hear you) |

**Deathly Hallows set:** wand in hand + stone + cloak = **Master of Death**: 1.5x health regen, 1.25x stamina regen, resistant to poison, immune to spirit damage.

Most items are crafted at the forge; the wand and cloak at the workbench. Recipes, stats, crafting station and model/colour tweaks are
configurable in `BepInEx/config/dankn.valheim.mysticalweapons.cfg`. `[General] TexturedModels = false`
turns off textures borrowed from vanilla items, such as Sulfuras's flametal grain (restart to apply).

### Armor

| Item | From | What it does | Crafted at |
|---|---|---|---|
| **Shardplate Helm**, **Breastplate**, **Greaves** | The Stormlight Archive | Slate plate with Stormlight-blue seams; the helm is modelled on Krajkoa's Shardplate. Full set: +150 carry weight, faster stamina regen, resistant to blunt, slash and pierce. | Forge 5 |

### The Deathly Hallows (Harry Potter)

| Item | What it does | Crafted at |
|---|---|---|
| **The Elder Wand** | Casts spell bolts (based on the Staff of Frost). Uses stamina instead of eitr by default. No durability. | Workbench 4 |
| **The Resurrection Stone** | Utility slot. Die while wearing it and your skills aren't lowered. | Forge 3 |
| **The Cloak of Invisibility** | Monsters can't see you while you sneak (they can still hear you). | Workbench 4 |

**Master of Death:** wand in hand, stone and cloak worn: 1.5x health regen, 1.25x stamina regen,
resistant to poison, immune to spirit damage.

## Installing

1. Install [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/)
   (a mod manager such as Gale or r2modman does this for you).
2. Copy `MysticalWeapons.dll` **and** the `models` folder into `BepInEx/plugins/MysticalWeapons/`.
   Without `models`, the Shardplate Helm and Sulfuras fall back to simpler built-in looks.
3. Install it on the server **and** on every player's game.

Don't install the old standalone Bat'leth mod as well: this mod already includes the bat'leth.

## Configuration

Settings live in `BepInEx/config/dankn.valheim.mysticalweapons.cfg`, created on first launch. Each
item has its own section, named after its prefab (`Batleth`, `Sylspear`, `Shardblade`, `Anduril`,
`Sulfuras`, `ShardplateHelm`, `ShardplateBreastplate`, `ShardplateGreaves`, `ElderWand`,
`ResurrectionStone`, `InvisibilityCloak`; also the names to use with `spawn`). There you can turn the
item off or change its recipe, crafting station, stats and effects (fireball chance, undead damage,
glow colours and more).

- `[General] TexturedModels = false` turns off the textures borrowed from vanilla items (such as
  Sulfuras's flametal grain) for flat colours. Restart to apply.
- After an update changes an item's defaults, your existing config keeps the old values. Delete
  that item's section to get the new ones.

## Custom models

You can replace any weapon's model with your own: drop a `<Prefab>.obj` (for example
`Shardblade.obj`) into the plugin's `models` folder and restart. Custom models only change how the
item looks on your machine. See [Editing models in Blender](docs/contributing.md#editing-models-in-blender).

## Contributing

Bug reports and ideas are welcome as GitHub issues. To build the mod or add an item, see
[docs/contributing.md](docs/contributing.md); for how the code and build fit together, see
[docs/architecture.md](docs/architecture.md).

Thunderfury ships a custom model built by `tools/blender/make_thunderfury_texture.py` the same way;
`Thunderfury.cs` keeps only a rough fallback shape.

## Credits

- Shardplate Helm model: based on ["Shardplate"](https://sketchfab.com/3d-models/shardplate-5a4fca1f665b469c9669f387aa879b34)
  by [Krajkoa](https://sketchfab.com/Krajkoa), licensed under
  [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/). Helm and collar extracted from the full suit,
  rescaled, emissive texture merged into the base colour. See `models/CREDITS.md`.

## Adding an item

- **Weapon:** subclass `MysticalWeapon` in `Weapons/`. Pick vanilla `SourcePrefabs` to clone, set stats
  and recipe, and build the model with `MeshBuilder` (blades, prisms, slabs). Hand at the origin, grip along +Y.
  Texture each part with `.Borrow(VanillaTextures.Iron, tiling, gloss)` on its `MaterialSpec`: the texture is
  taken from that vanilla item while the game runs and tinted with the part's colour.
  The clone's material keeps the source item's settings: textures and its metal colour are reset, but if the
  model still looks tinted, check the `inherited colours` line for it in `LogOutput.log` (black metal, for
  example, has a green metal colour).
- **Armor:** subclass `MysticalArmor` in `Armor/` with a source armor piece, tint and glow colour.

Then add it to `Plugin.Items`.

### Item report

To see how items look next to vanilla, set `[Debug] ItemReport = true` in the config and launch to
the main menu. `BepInEx/MysticalItemReport` then holds a picture of each item beside its vanilla peer
from eight angles, and `report.txt` (triangles and texture sizes next to the peer, plus the most
detailed vanilla item of the same kind). The peer is the cloned source unless the item sets
`VanillaPeer`. Triangle counts are about twice what Blender shows.

### Picture for the pull request

`tools/blender/render_views.py` renders an item's model from the front, in profile and at three-quarters
in one picture (the script's header has the command). Commit it as `docs/images/<Prefab>-views.jpg` and
link it in the pull request. Code-built items need exporting first (see "Editing models in Blender").
