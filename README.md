# Mystical Weapons from Far Off Lands

A [Valheim](https://www.valheimgame.com/) mod that brings legendary weapons and armor from other
worlds into the game: Andúril from Middle-earth, Shardblades and Shardplate from Roshar, Sulfuras,
Thunderfury and the Warglaives of Azzinoth from Azeroth, a Klingon bat'leth and the three Deathly Hallows.

Every item is crafted from Valheim's own materials, sits at a sensible point in the game's
progression and has one power from its story. Models are drawn in Valheim's style, and the mod
needs nothing but BepInEx: no Jotunn and no asset bundles.

## Items

### Weapons

| Item | From | What it does | Crafted at |
|---|---|---|---|
| **Bat'leth** | Star Trek | Crescent-bladed two-hander with wide sweeping swings (based on the Iron Battleaxe). | Forge 2 |
| **Sylspear** | The Stormlight Archive | Glowing Shardspear. Grants **Windrunner** while held: slow fall and faster stamina regen. No durability. | Forge 3 |
| **Shardblade** | The Stormlight Archive | Huge, light two-hander that also chops trees and mines rock. No durability. | Forge 4 |
| **Andúril** | The Lord of the Rings | Rune-etched longsword with spirit damage and x1.5 damage against all undead (skeletons, ghosts, draugr, wraiths). Its secondary attack calls the Dead Men of Dunharrow: up to two ghostly Oathbreakers (the Dead Raiser's summon, for stamina instead of eitr and blood) that grow with your Swords skill. Reforged from an Iron Sword, the shards of Narsil. | Black forge 2 |
| **Sulfuras, Hand of Ragnaros** | World of Warcraft | Ragnaros's two-handed hammer (Clubs skill): a three-hit combo, with the Demolisher's ground slam on the secondary attack. Hits may hurl a fireball that sets the target burning, and flames lick off its head. Reforged from a Demolisher. | Black forge 3 |
| **Thunderfury, Blessed Blade of the Windseeker** | World of Warcraft | One-handed sword with a split blade and a crackling ball of lightning at its root (based on Nidhögg). Hits may loose chain lightning that leaps to other creatures nearby. Reforged from Nidhögg the Thundering. | Black forge 4 |
| **Warglaives of Azzinoth** | World of Warcraft | Illidan's twin glaives, one in each hand (Knives skill, based on Skoll and Hati), wreathed in fel flames and crossed on the back when holstered. Hits add fel poison. Reforged from Skoll and Hati. | Black forge 3 |

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

**With a mod manager** (Gale, r2modman): find *MysticalWeapons* on Thunderstore and install it.
BepInEx comes with it.

**By hand:**
1. Install [BepInExPack_Valheim](https://thunderstore.io/c/valheim/p/denikson/BepInExPack_Valheim/).
2. Download the zip from the latest [GitHub release](https://github.com/dknopp/valheim-mystical-weapons-mod/releases)
   and copy `MysticalWeapons.dll` **and** the `models` folder from its `plugins` folder into
   `BepInEx/plugins/MysticalWeapons/`.
   Without `models`, the Shardplate Helm, Sulfuras, Thunderfury and the Warglaives fall back to simpler built-in looks.

Install it on the server **and** on every player's game.

**Beta builds** of the next version are published as *MysticalWeapons_BETA* for play testing.
Install the beta or the stable version, never both.

Don't install the old standalone Bat'leth mod as well: this mod already includes the bat'leth.

## Configuration

Settings live in `BepInEx/config/omm.valheim.mysticalweapons.cfg`, created on first launch. Each
item has its own section, named after its prefab (`Batleth`, `Sylspear`, `Shardblade`, `Anduril`,
`Sulfuras`, `Thunderfury`, `WarglaivesOfAzzinoth`, `ShardplateHelm`, `ShardplateBreastplate`, `ShardplateGreaves`, `ElderWand`,
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

### Next up:
 - Rebalance existing weapons to make sure they're well aligned with natural game progression
 - Experiment with Shardplate - can the full set cause the character size to increase by 25%?. Can we add a storm
   and/or electricity animation when attacking?
 - Invisibility cloak currently gives a player infinite stealth while crouched but doesn't have an visual effects
   indicating invisibility - can we swap the player model for a dark ethereal and translucent blue while crouched?
 - It might be more interesting if the elder wand cycled through various spells as you attacked. The spell casting
   could also be slow but powerful to match the "power" of the elder wand without being unfair.
 - Sylspear could be more powerful and later in the game. It also might be cool if when sheathed with 'r' instead
   of being placed on the players back it transforms into a whisplight to simulate a spren. The modifiers could be
   adjusted too, slower fall speed, and higher jump might be more windrunner-y.
 - Incorporate Sting from LOTR that glows in the presence of graydwarves, this could be an earlier game item that
   matches an iron or silver sword but has the unique glow animation and behavior.

## Credits

- Shardplate Helm model: based on ["Shardplate"](https://sketchfab.com/3d-models/shardplate-5a4fca1f665b469c9669f387aa879b34)
  by [Krajkoa](https://sketchfab.com/Krajkoa), licensed under
  [CC BY 4.0](http://creativecommons.org/licenses/by/4.0/). Helm and collar extracted from the full
  suit, rescaled, emissive texture merged into the base colour. See `models/CREDITS.md`.
- Mod icon: generated with ChatGPT's image generator (OpenAI), which assigns the output's rights to
  its user.
- Textures on the code-built weapons are taken from Valheim's own items while the game runs; none
  of the game's assets are included in this mod.

## License

Free to use, change and share. The code is under the [MIT licence](LICENSE); our models,
textures and icon are under [CC BY 4.0](LICENSE-ASSETS.md), so credit "Mystical Weapons by
OldManMods" if you reuse them. Third-party models keep their own licences (see Credits).
