# The Brotherhood (Assassin's Creed set): design decisions

A stealth set that rewards staying unseen: hood, robes, legs, cape and hidden blades. This file
records what we decided before building; update it as the set is built. The pitch is in
`docs/item-ideas.md` ("Picked next").

## Pieces and vanilla bases

Picked from grey shape renders of the vanilla candidates on the player (2026-10-24). The hero
reference is Ezio in Revelations: a peaked hood that shades the eyes, a fitted coat with split tails,
a sash, tall boots and a crisp cape.

| Piece | Vanilla mesh | Why | Runner-up |
|---|---|---|---|
| Hood | Eitr-weave hood (`HelmetMage`) | The only hood whose peak hangs over the face, with a clasp at the throat | Troll leather hood |
| Robes | Embla robe (`ArmorMageChest_Ashlands`) | Fitted and belted, with split front and back tails to the knee | Deep North mage robe (has its own hood, clashes with the hood slot) |
| Legs | Eitr-weave legs (`ArmorMageLegs`) | Fitted trousers, tall boots, a thigh strap | Troll leather legs |
| Cape | Linen cape (`CapeLinen`) | Clean and crisp, like the hero reference; the torn hide capes read too rough | Troll hide cape |
| Hidden blades | Flesh Rippers (`FistFenrirClaw`) | Mountain-tier fist weapon; punches read as a hidden-blade stab; the model rides on the forearms | Skoll and Hati (Mistlands knives) |

The Fenris hood, the first idea for the cowl, reads as shaggy fur, not cloth, so it was dropped.
Eitr-weave and Embla are one family (Embla is Eitr-weave's Ashlands version), so the pieces should
look like one outfit. The mesh doesn't set the tier (see below). Cloning these copies their own
bonuses (eitr regeneration, any vanilla set), which we clear, as Shardplate does.

Colours come later, after the shapes are approved in game: the pitch says white and red, the hero
reference is dark grey with brown leather and a red sash and cape lining.

**How the look is made:** a plain tint while the powers are built, then our own textures painted onto
the vanilla meshes (the sash, trims, leather, the crest on the belt). A tint alone colours a whole
piece one way, which can't give the hero reference's mix of colours. The mesh layouts are read from
the game at runtime on our own machines, so no Valheim files enter the repo; `MysticalArmor` needs a
small addition to swap a worn piece's texture. Custom models only if a shape still looks wrong in
game once the textures are on.

## Tier and recipe

- **Mountains.** Armour matches the Fenris pieces: 10 each (+2 per level), read from the game on
  2026-10-24; the cape takes the linen cape's 1 (+1).
- **Workbench level 2 for every piece,** as Fenris. The bases' Galdr table (Eitr-weave, Embla) can't
  be built until the Mistlands, so their stations would lock a Mountain set out of its biome.
- **Recipes:** wolf hair bundles, wolf pelts, leather scraps and a little silver (clasp, belt, cape
  pin), in Fenris quantities.
- **Frost resistance on the robe,** as the Fenris coat: the set has to be wearable in the Mountains,
  and its cape takes the slot a wolf cape would.
- **No movement bonus** (Fenris gives +3% a piece): the set's speed is for sneaking only.
- **No boss-trophy gate.** Assassination balances itself: it needs a slow, careful approach, groups
  are hard to sneak up on, and being seen ends it.

## Set bonuses

| Pieces worn | Bonus |
|---|---|
| 2 | Faster, quieter, less visible sneaking at a lower stamina cost; Sneak levels faster |
| 3 | **Eagle Vision:** while crouched, nearby creatures glow through walls: red if hostile, gold if starred; passive animals don't glow |
| 4 | **Assassination** (with the hidden blades in hand) and **Leap of Faith** (no fall damage) |

- The four armour pieces count towards the set; the blades don't. Assassination also needs the
  hidden blades in hand, so a bow sneak shot can't one-shot from range.
- Hidden blades in hand: Unarmed levels faster. They are cloned from the Flesh Rippers (Fenris claws), not
  Skoll and Hati: Mountain tier like the set, and their straight punches read as a hidden-blade stab. So the
  skill is Unarmed, not Knives.

## Assassination

- **Unaware:** the game's own sneak-attack test: the creature isn't alerted. It's synced, so every
  player agrees. Once alerted, a creature can't be assassinated until it loses track of you and calms
  down. Vanilla also lets a creature take a sneak attack only once every 5 minutes.
- **How much it takes**, as a share of the creature's max health:
  - Normal creatures (no star or 1 star): always killed.
  - 2-star creatures and "giants" (base health 500 or more: trolls, Lox, Abominations, most of the
    later biomes): 50%, rising with the average of your Unarmed and Sneak skills to a kill at 80.
  - Bosses: 10%, rising to 20% at the same skill; once per summoning.
  - Where shares differ, the lowest applies (they don't stack).
  - If the blades' normal sneak-attack damage is higher, that applies instead.
- Players can't be assassinated. The vanilla sneak-attack test never applies to players (they have no alerted state), so a PvP option would need its own idea of "unaware"; dropped until someone asks.
- These numbers are starting points: check them against vanilla creature health before release.

## Config (starting values)

| Setting | Default |
|---|---|
| Sneak speed (2 pieces) | +25% |
| Sneak stamina cost (2 pieces) | -50% |
| Sneak noise (2 pieces) | -50% |
| Sneak skill gain (2 pieces) | +50% |
| Sneak visibility (2 pieces) | -40% |
| Unarmed skill gain (blades in hand) | +50% |
| Eagle Vision range | 30 m |
| Eagle Vision on, and its colours | on; red hostile, gold starred |
| Eagle Vision opacity in sight, behind walls | 10%, 70% |
| Assassination on | on |
| Giant: base health from | 500 |
| 2-star and giant share at no skill | 50% |
| Boss share at no skill and at full skill | 10%, 20% |
| Skill average for the full share | 80 |
| Leap of Faith on | on |

## How it's built

- **Set system:** a small shared `Core/ArmorSet` lists a set's pieces and what each piece count
  grants (2, 3, 4), and adds or removes those bonuses as pieces go on and off. Vanilla allows only
  one bonus per set, at the full set. Later sets (Mistborn, werewolf) reuse it. Deathly Hallows and
  Shardplate keep their vanilla set bonus for now.
- **Skill gain:** the game's own status-effect setting that raises one skill's gain.
- **Eagle Vision:** drawn only for the wearer (others see nothing, as intended). Each lit creature's
  renderers get one extra material slot, a flat colour in Unity's built-in `UI/Default` shader drawn
  over everything, once per pixel through a stencil bit so overlapping parts don't double up
  (`ModelUtil.SpectralMaterial`'s shaders can't draw through walls). Creatures are found twice a
  second; ten times a second, two sight lines from the camera, to each one's eyes and middle, are
  checked against what blocks the game's own AI sight: both blocked, it's hidden and gets a strong silhouette (70%);
  otherwise a faint wash (10%), so its face and stance still read. A solid silhouette everywhere,
  tried first, hid which way creatures face, at night most of all. Deciding hidden per pixel against
  the game's depth, tried second, flickered: grass and the creature's own limbs counted as cover, and
  the redrawn creature fought its own depth. While crouched, it rescans twice a second for creatures hostile to the wearer in
  range; standing up puts every glow out. Training dummies glow too (the game counts them as
  enemies), which helps testing.
- **Assassination:** decided on the attacker's client from the synced alert state, applied by the
  creature's owner through the normal damage path, so it isn't applied twice (as Andúril's effects
  are).
- **Leap of Faith and sneaking:** only change the wearer, on their own client. Being harder to see is the game's own stealth setting, which it applies only while crouched and syncs to the creatures' owners; the vanilla stealth eye shows it.
- **Attacking from a crouch:** the game has no crouched attack; it stands you up for the swing and
  crouches you again after. While crouch is on, with 2 pieces your stealth factor holds where it was until
  the crouch animation is back (it would otherwise climb), and Eagle Vision stays lit.
- **One icon:** the set shows a single status icon, named and pictured by the highest bonus worn
  (Unseen Step with the cape, Eagle Vision with the hood, Creed with the hidden blades); the bonuses
  below it still apply. The blades' Unarmed bonus has no icon.
- **Set tooltip:** as Diablo shows sets, every piece ends its tooltip with the set: the pieces (grey
  if not worn) and each bonus by the pieces it needs (orange once you have it, grey until then). The
  hidden blades show only the set name, the pieces worn and the 4-piece bonus, since their own weapon
  stats already make a long tooltip. `ArmorSet` builds it, so later sets get it too. Piece descriptions
  keep only their lore.

## Build order

1. The four armour pieces as recoloured clones with their stats, `ArmorSet`, and the 2-piece
   bonus. Check that they look right in game. **Done 2026-10-24:** shapes read well in game; the hood
   and cape stay red under the grey placeholder tint until our textures.
2. Eagle Vision, starting with a test of the see-through material. **Done 2026-10-24:** tested on
   Greydwarfs and training dummies; a slight delay remains when a creature comes out of hiding, partly
   because the camera sits behind the shoulder.
3. Hidden blades on the wrists, Assassination and Leap of Faith. **Done 2026-10-24:** the blades are
   cloned from the Flesh Rippers (Unarmed), a code-built placeholder on the forearms. Tested in game:
   Assassination kills normal creatures and takes 56% of a Troll at low skill (a kill at skill 80);
   no fall damage; harder to see while crouched; stealth and Eagle Vision hold through a crouch attack;
   one set icon by tier; the set tooltip.
4. Our own textures on the vanilla meshes (see "How the look is made"), the hidden blades' own
   model and polished-steel texture, a roll on landing from a height (Leap of Faith), then README rows and
   balance checks.
