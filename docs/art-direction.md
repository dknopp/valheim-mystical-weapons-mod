# Art direction

Every item should look and act like the real thing from its own story, drawn in Valheim's style.

## Before building an item
Answer these in order, and note the answers in the PR description:
1. **Lore look:** what does it look like in its source? Silhouette, proportions, signature details,
   and finish: pristine (polished steel, Elvish make) or worn (battle-scarred, rough-forged)?
2. **Vanilla texture fit:** is there a vanilla texture (iron, leather, wood, bark, stone) that
   supports that finish once tinted? Use it only if it does; a mottled, worn texture on a famously
   flawless blade is wrong, and a flat colour with the right gloss beats the wrong texture.
3. **Custom work:** only if neither fits, plan a custom model or texture: Blender and an OBJ in
   `models/` (see "Editing models in Blender" in README.md), with licences checked as below,
   and a vanilla grain laid over the custom texture so it matches Valheim's painterly look (see Look).
4. **Place in the journey:** suggest a tier, cost and stats (see "Tier, cost and balance" below);
   the maintainer building it decides before any code.

## Look
- Start from the source: silhouette, proportions and signature details (Andúril's swept
  crossguard, the bat'leth's crescent).
- Gather references, best first: a downloadable 3D model of the item (Sketchfab and similar), a 3D
  viewer you can rotate (Wowhead's for Sulfuras), or several images from different angles. Get
  front, side and top views, note a few proportions (grip to blade, head width to shaft), and list
  the parts: how pieces step, taper and join, thickness as well as outline, how many spikes or
  studs and where. Check the model against those proportions and parts before building.
- A detailed downloaded model is a reference to rebuild low-poly, not a mesh to decimate: decimated
  sculpts look lumpy next to Valheim's clean shapes. Use a downloaded mesh directly only if it is
  already low-poly and its licence allows it (see Outside assets).
- Draw it the Valheim way: low-poly, bold shapes that read in the inventory icon; worn, mostly
  matte materials unless the source item is famously pristine; textures 512px or smaller.
- Prefer vanilla textures borrowed from the running game and tinted (`.Borrow(...)` on a
  `MaterialSpec`) over flat colours or new textures, when they fit the finish (step 2 above).
- Custom textures: paint big, bold shapes (swirls, runes, colour bands) rather than fine noise,
  which turns to mush at 512px. A custom texture on its own looks too clean and digital next to
  Valheim's hand-painted art, so lay a vanilla grain over it in game (`.WithGrain(...)` on the
  part's `MaterialSpec`). Pick the grain of what the item is made of, which also ties the look to
  its tier (flametal for Sulfuras). Start around 50% strength and tune it in game through a config
  setting (Sulfuras: `TextureGrain`, 55%); too strong and the custom design disappears.
- Leave glowing parts without grain (Sulfuras's white-hot spike tips): glow reads crisper clean.
  Glow follows texture brightness, so the brightest details glow most; a light on the item barely
  shows in daylight.
- One signature glow, where the lore puts it (Andúril's runes, Syl's tip), not all over.
- Shapes in code; fine surface detail (lettering, patterns) as a texture via the Blender route, or
  left out. Geometry only for detail that reads at arm's length: Andúril's runes are blocky shapes,
  real Elvish script would be a texture.

## Name and flavour text
- Use the name the source uses ("Andúril", "The Elder Wand"), not a description of it.
- Description: one or two sentences in the voice of the source world. Name what it is and hint at
  its power ("glows blue when orcs are near"); no numbers, game terms or explanations of the
  mechanic. Valheim's own descriptions are a good length guide.

## Effects and abilities from lore
- Each item's best-known power becomes one in-game effect, triggered the way the story triggers it.
- Stay true to the source: don't add powers or damage types it doesn't have (no frost damage on
  a blade that never froze anything). Base damage follows what the item is: a sword slashes and
  pierces; elemental damage only where the lore puts it.
- Prefer vanilla effects (glows, particles, hit effects) recoloured in code over new ones.
- Ambient effects come from a vanilla item of the same element (Sulfuras's head flames are
  Dyrnwyn's): they read as native and need no new art. Keep them small and on the signature part,
  as part of its one glow rather than a second one.
- Map lore elements to what they do in Valheim, not only their colour: fire sets targets burning,
  frost slows, spirit only hurts undead.
- Procs fire on the lore's trigger ("chance on hit" for Sulfuras's fireball), with a cooldown so
  they stay a moment rather than spam.
- Big effects for big moments: blasts, shockwaves and screen shake belong to heavy moves (a slam,
  a proc); everyday swings stay clean.
- Pick the moveset from how the source item is wielded, borrowing that vanilla weapon's attacks
  (Sulfuras is a war hammer: a battleaxe combo and a sledge's slam), not whatever the cloned item
  came with. Moves sell the fantasy as much as the model.
- Map lore enemies to Valheim creatures:
  orcs/goblins → Fulings, Greylings, Greydwarfs; undead → skeletons, draugr, ghosts, wraiths;
  dragons → drakes, Moder; trolls → trolls.
- Bonuses are configurable, and power comes from the recipe and crafting station, not the look.

## Tier, cost and balance
Guidance, not rules: a starting suggestion for where an item sits in the journey, which the
maintainer building it adjusts to taste. By default, items are traditional in-biome weapons: strong
in their home biome and perhaps the next, then outclassed like vanilla gear, with the tone and power
fantasy of the source deciding where that is.
- **Home biome:** pick it from the lore (Andúril is a king's sword reforged, so
  mid-game silver; Sulfuras is a Firelord's weapon,
  so Ashlands) and from a **vanilla peer**: the vanilla item it is closest to, ideally the one its
  recipe consumes (Andúril consumes an Iron Sword, Sulfuras a Demolisher). The peer sets the
  tier, the minimum cost and what to compare stats against.
- **Cost:** the peer plus roughly 1.5 to 2 times its cost again in home-biome materials, plus one
  signature material from the lore (Surtling cores for the Eye of Sulfuras). Same station as the
  peer, at its level or one above. It should feel like a project for that biome, not a first craft.
- **Stats:** base damage within about 10% of the peer, with the same per-level growth. The lore
  power is the edge, ideally conditional (against undead, on a proc, near goblins) rather than
  always-on stats. A proc's average gain is chance x damage x how often it can fire; Sulfuras's
  fireball adds very roughly 5 to 8%.
- **Hidden power counts:** no durability, stamina instead of eitr, extra carry weight or stealth are
  real power. Choose them on purpose and weigh them against the rest.
- **Lasting longer:** if the lore calls for an item that grows with its wielder, two options to
  suggest rather than default to: upgrade levels gated by later forge levels and materials (damage per
  level tracking each new biome's peer; materials that change per level need a small patch), or a
  config option that scales it with bosses defeated.
- Players on a shared server each have their own config file, so numbers are only as fixed as
  everyone's config.

Example: Andúril's edge is spirit damage and x1.5 against undead, which only matters against the
enemies its lore names.

## Outside assets
- Never copy Valheim's own textures, models or sounds into the repo; take them from the game at
  runtime. They belong to Iron Gate and the repo is public.
- Only use outside models whose license allows changes and redistribution (e.g. CC BY); credit
  them in `models/CREDITS.md` and the README.
- Skip models ripped from the source's own game: they belong to its publisher whatever licence the
  uploader gives them.
- References (images, downloaded models) are for looking at, not committing: any licence is fine
  for that, but don't add them to the repo.
