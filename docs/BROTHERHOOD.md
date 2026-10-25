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
| Cape | Lox cape (`CapeLox`), changed in 4c from the linen cape | Long and wide enough to hide the robe's tails when crouching, which the linen cape let through; its fur collar is in the hero reference too. Its hem is ragged; repainted it looks fine | Linen cape (`CapeLinen`) |
| Hidden blades | Flesh Rippers (`FistFenrirClaw`) | Mountain-tier fist weapon; punches read as a hidden-blade stab; the model rides on the forearms | Skoll and Hati (Mistlands knives) |

The Fenris hood, the first idea for the cowl, reads as shaggy fur, not cloth, so it was dropped.
Eitr-weave and Embla are one family (Embla is Eitr-weave's Ashlands version), so the pieces should
look like one outfit. The mesh doesn't set the tier (see below). Cloning these copies their own
bonuses (eitr regeneration, any vanilla set), which we clear, as Shardplate does.

**How the look is made** (chunk 4b, 2026-10-24): our own textures painted on the vanilla meshes, after
the hero reference: a dark grey hood that keeps the vanilla shading, slate coat and cape with muted pewter edge trims
(the cape a dark fur collar), charcoal trousers and dark grey side tails, brown leather (belt, pouch, straps, bracers, gloves, boots) with its own grain
and worn edges, and a red sash and cape lining. A tint alone colours a whole piece one way, which
can't give that mix. The textures are in `models/textures/`, painted by
`tools/textures/paint_brotherhood.py` on the vanilla textures' layouts (read from the game on our own
machines, never committed); `MysticalArmor` swaps them in and keeps the vanilla shading under them.
What we found:

- The sash is the undershirt the game paints on the body, which shows as a band between the vest and
  the belt. A band painted on the vest itself showed through the cape when crouching. The
  undershirt's neck is charcoal, a dark scarf under the hood.
- The cape's two sides share one texture, so the lining is a second material on the same mesh that
  draws only the inside (`LiningColour` in config). It keeps the texture's cut-outs (the Lox cape's
  ragged hem), and a mask from the paint script keeps it to the hide, so the fur collar's tufts aren't
  red underneath.
- The undershirt and sleeves had chainmail in their normal and metal maps; they get a fine linen weave
  instead (our own, not the cape's, which is a bark texture). The vanilla mage hood shines like metal;
  only its clasps do now.
- Our belt mark is a small feather in a ring, not the Assassin's Creed crest.
- The vanilla textures are 128 pixels, so their shading is blocky under our 512-pixel paint. Most of
  the blocks came from the vanilla normal maps, not the colour: the robe and boots get the
  vanilla normal maps enlarged and softened (big folds kept, steps gone; the hood did too until 4c, see
  below), and cloth takes none of
  the vanilla colour shading, only our own soft folds and a faint mottle. Leather keeps the vanilla
  shading under its grain.
- Detail pass (4b-2), after the hero reference's coat: a darker slate; silver piping and a twisted
  cord inside the trims of the vest, tails and hood; a faint lighter damask (our own stem-and-spiral
  motif) over the vest, tails and hood (the hood's later dropped, below); larger scrollwork low on the front tail; dashed stitching along
  the leather edges. The brooch is metal like the belt buckle. The cape keeps the vanilla bark bump
  (a smooth twill was tried and looked too clean).
- The damask was made a step more visible after the first in-game look (4c).
- **The hood is plain** (4c, 2026-10-25): the coat's slate under the vanilla shading, so the vanilla hood's own
  trim shows in tone, and the vanilla normal map: our softened one blurred the empty texture between its pieces
  into their edges, which lit the joins (the centre seam most) as lines. Its blocky lighting is back, as on
  the vanilla hood. Its texture is a patchwork of small pieces packed into one area, so our painted trim ran
  along the gaps between pieces (a stripe over the crown and down the back) and the damask broke at every join;
  the pieces are smaller than a motif, so no placement avoids it. An attempt that placed a mirrored ornament on
  the peak and kept the trim to the real edges (face opening, hem) worked in the paint, but a shading line along
  the mesh's centre crease still read as a seam, so it was dropped. It's on branch `brotherhood-hood-ornament`:
  the hood's texel layout was read from in-game renders of the hood with a texture that encodes each texel's own
  coordinates (unlit, lights off; `references/brotherhood/DevRender.cs.txt` writes them). A detailed hood needs
  our own model with clean UVs (helmets can take a custom OBJ).

Custom models only if a shape still looks wrong in game once the textures are on.

## Tier and recipe

- **Mountains.** Armour matches the Fenris pieces: 10 each (+2 per level), read from the game on
  2026-10-24; the cape takes the linen and Lox capes' 1 (+1), without the Lox cape's frost resistance.
- **Workbench level 2 for every piece,** as Fenris. The bases' Galdr table (Eitr-weave, Embla) can't
  be built until the Mistlands, so their stations would lock a Mountain set out of its biome.
- **Recipes:** wolf hair bundles, wolf pelts, leather scraps and a little silver (clasp, belt, cape
  pin), in Fenris quantities (20 wolf hair a piece). The hidden blades take the Flesh Rippers' 10
  silver and forge level 3.
- **Frost resistance on the robe,** as the Fenris coat: the set has to be wearable in the Mountains,
  and its cape takes the slot a wolf cape would.
- **No movement bonus** (Fenris gives +3% a piece): the set's speed is for sneaking only.
- **Strong unseen, weak once seen** (2026-10-24): the set's balance is that an assassin who stays
  hidden is powerful and one who's revealed is at a disadvantage in a straight fight. So the armour
  stays light: Fenris's 10 a piece, the lightest Mountain armour, and well under Padded's 26, the
  Plains peer, where the Moder-gated blades are mostly used. There are no resistances beyond the
  robe's frost, and every bonus but Leap of Faith needs a crouch or an unaware target. If play
  testing shows the set holds up too well in open fights, lower the pieces' `Armor` config before
  adding anything new.
- **Moder's trophy for the hidden blades** (changed 2026-10-24; first decided as no gate). They unlock
  Assassination, so you earn it by finishing the Mountains and play it in the Plains, as Shardplate
  does. Bonemass, the repo's usual pick for a Mountain item, would gate nothing: anyone mining silver
  has beaten him. The armour stays ungated: on its own it's stealth gear at Fenris values.
- **Stealth bonuses kept** (reconsidered 2026-10-24): Assassination needs an unaware target, so
  stealth is what makes the first hit possible, and the Cloak of Invisibility already does more.

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
  - Champions (base health 1200 or more, not bosses: Seeker soldiers, Gjall, Morgen, Fallen
    Valkyries, Jotun warriors, frost trolls, Hildir's bosses): 25%, rising to 50%; never a kill. A share
    never falls behind as the biomes get harder, so without this the set would one-shot the big
    creatures of every later biome.
  - Bosses: 10%, rising to 20% at the same skill; once per summoning.
  - Where shares differ, the lowest applies (they don't stack).
  - If the blades' normal sneak-attack damage is higher, that applies instead.
- Players can't be assassinated. The vanilla sneak-attack test never applies to players (they have no alerted state), so a PvP option would need its own idea of "unaware"; dropped until someone asks.
- Checked against the game's creature health on 2026-10-24 (see "Balance check" below).

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
| Eagle Vision message on, and how often | on; at most every 30 s |
| Assassination on | on |
| Giant: base health from | 500 |
| 2-star and giant share at no skill | 50% |
| Champion: base health from | 1200 |
| Champion share at no skill and at full skill | 25%, 50% |
| Boss share at no skill and at full skill | 10%, 20% |
| Skill average for the full share | 80 |
| Leap of Faith on | on |
| Landing roll on, and the fall it needs | on; 4 m (where vanilla fall damage starts) |

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
- **Landing roll:** a fall longer than 4 m (the game's own fall-damage threshold, counted from the top of the jump) plays the dodge roll, the way you face; the velocity at touchdown is deflected by the ground, so it rolled sideways. Unlike a
  dodge it costs no stamina, makes no noise creatures hear (so an unaware target stays unaware) and
  gives no dodge protection; the dodge's sound still plays. Not in water, mid-attack, already dodging,
  staggered or over-encumbered. The animation syncs, so others see it.
- **Attacking from a crouch:** the game has no crouched attack; it stands you up for the swing and
  crouches you again after. While crouch is on, with 2 pieces your stealth factor holds where it was until
  the crouch animation is back (it would otherwise climb), and Eagle Vision stays lit.
- **One icon:** the set shows a single status icon, named and pictured by the highest bonus worn
  (the cape for Unseen Step, the hood for Eagle Vision and the Creed: the hood is the set's face); the
  bonuses below it still apply. The blades' Unarmed bonus has no icon.
- **Set tooltip:** as Diablo shows sets, every piece ends its tooltip with the set: the pieces (grey
  if not worn) and each bonus by the pieces it needs (orange once you have it, grey until then). The
  hidden blades show the whole block too (a shortened one, tried first, hid the set). `ArmorSet` builds
  it after the game's own tooltip, trimmed of its trailing blank lines, so later sets get it too. Item
  descriptions keep only their lore; what a bonus does is in its own line.
- **Eagle Vision message:** its name shows mid-screen (the game's centre message) as it comes on with a
  crouch, at most every 30 seconds, since sneaking is crouching on and off.

## Balance check (2026-10-24)

Read from the game (Valheim 1.0.20) with a scratch DevAutoRun probe.

- **Armour** matches Fenris: 10 (+2) a piece, the robe resistant to frost like the Fenris coat. Fenris
  also gives +3% speed a piece and a set bonus; ours trades those for the sneak bonuses.
- **Hidden blades** equal the Flesh Rippers: 60 damage (+4 a level), x6 sneak attack. That sneak attack
  already kills most creatures under about 400 health in vanilla, so Assassination's "always kills
  normal creatures" adds little below the giants.
- **Giants from 500, killed at skill 80:** Bjørn (500), Troll (600), Charred warriors (600),
  Abomination and Stone golem (800), Goblin brute (800), Lox and Moose (1000). Fenrings (300), Dvergr
  (350) and Seekers (200) are normal.
- **Champions from 1200, never a kill:** Unbjørn and Hildir's skeleton (1200), Jotun warrior (1300),
  Seeker soldier, Gjall and Fallen Valkyrie (1500), Morgen (1600), Barka (2200), Dyrnwyn's bearer
  (2500), frost troll (3000), Hildir's other bosses (3700, 4200). The tier was added after this
  check, first at 2000, then lowered to 1200 so the share stops scaling past the Plains.
- **Bosses:** 10% to 20% is 50 to 100 of Eikthyr (500), 250 to 500 of the Elder, 500 to 1000 of
  Bonemass, 750 to 1500 of Moder, 1000 to 2000 of Yagluth, 1250 to 2500 of the Queen, 2500 to 5000
  of Fader. The Frozen King's three phases are separate creatures, so each can be assassinated once.
  Bosses are rarely unaware after the summoning, so this is mostly an opener.

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
4. Split in three:
   - 4a: the landing roll, README rows and the balance check above. **Done 2026-10-24.**
   - 4b: our own textures on the vanilla meshes (see "How the look is made"). **Done 2026-10-24:**
     the first look checked in game; after it, the sash moved to the undershirt and the cape got its
     lining, leather grain and linen weave, checked in in-engine renders.
   - 4b-2: a finer detail pass on the textures (see "How the look is made"). **Done 2026-10-24:**
     checked in game and in in-engine renders. The hood's part of it was dropped in 4c (see above).
   - 4c: the hidden blades' own model, then the PR to `develop`. **Done 2026-10-25:** the blades stay
     code-built (a few centimetres on screen; Blender wasn't worth it): a ridged dagger stood on its edge so
     the camera behind the player sees its flat, sliding out of a short leather sheath sunk into the bracer,
     cool steel. No weapon effect: the vanilla backstab burst already marks an assassination (bloodied blades
     after one is an idea for play testing). Holstering hides them, which reads as retracting. Also the
     plain hood (above), the stronger damask and the hood as the set icon; after the first in-game look,
     the Lox cape, a darker hood and side tails, the Eagle Vision message and the full set block on the
     blades' tooltip.
5. Later: our own emblem for the set's status icon, in the spirit of the Assassin's Creed crest but
   not the crest itself (Ubisoft's trademark; our art is CC BY in a public repo).
