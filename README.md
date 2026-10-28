# KINFORGE: Unity MVP (The Craters slice)

A playable first slice of the KINFORGE design doc. You are the raid leader of one kin-band of five Vhorn, seen from a
third-person camera that rides with the party, like leading a WoW group through a dungeon. You steer where they go and
stand, and pick the strategy they fight under. They choose targets and abilities and spend their own mana and stamina,
and they learn. The slice covers the Craters biome and the Rustmaw raid boss. Everything is generated from code (terrain,
models, effects, UI), so there are no art assets to import.

## Open it

1. Get this branch on its own:
   ```bash
   git clone --branch claude/kinforge-mvp --single-branch https://github.com/petercj1990/Mass-transit-demo.git kinforge
   ```
   (Optional: move it into its own repo with `git remote set-url origin <new repo>` then `git push -u origin claude/kinforge-mvp:main`.)
   Already have it? `git pull origin claude/kinforge-mvp`.
2. Unity Hub > **Add project from disk** > pick the `kinforge` folder.
   - It targets **Unity 6 (6000.0 LTS)**, uses the built-in render pipeline and needs no extra packages.
   - Unity 2022.3 LTS also works. If Hub says the exact editor version is missing, pick any Unity 6 or 2022.3 version
     you have installed.
3. On first open an editor script creates `Assets/Kinforge/Scenes/Main.unity` and opens it. Press **Play**.
   - The game also starts if you press Play in any scene, or from the menu **Kinforge > Open Main Scene and Play**.
4. If you see an "Input is disabled" message, set **Edit > Project Settings > Player > Other Settings > Active Input
   Handling** to **Both**. The prototype uses the classic Input Manager.
5. **Controller:** on load, the editor script adds the stick, trigger and d-pad axes the game reads (named "KF ...") to
   the Input Manager. You can also run **Kinforge > Set Up Gamepad Axes**. Button numbers follow the Xbox layout on
   Windows (A=0, B=1, X=2, Y=3, LB=4, RB=5, View=6, Menu=7, stick clicks 8/9).

The world uses a fixed seed (1337) so feedback is comparable. The help screen (F1) has a "New random world" button.

## Controls

| Action | Keyboard and mouse | Gamepad |
| --- | --- | --- |
| Walk the kin-band (camera-relative) | WASD or arrows | Left stick |
| Look around | Drag with either mouse button, Q/E turn | Right stick |
| Zoom / swing behind the party | Mouse wheel / Space | Triggers / right stick click |
| Strategy | Hotbar 1-0: 1 Charge, 2 Defend, 3 Hold, 4 Kite, 5 Focus Fire, 6 Spread Out, 7 Stack, 8 Fall Back, 9 Rest, 0 Fortify (the old letter keys still work too) | LB/RB cycle; B Fall Back, left stick click Rest |
| Act on things | Right-click ground to march, an enemy to focus it, a resource to gather, a kin-core cache to recover, the base to go home | A: recover / gather / garrison at whatever is next to the banner; X: focus the enemy ahead |
| Orders | H go home and deposit, J garrison, K stop | Y go home |
| Map | Minimap top-right (right-click to march there); M opens the full map of explored ground | |
| Panels | Tab inspects the next Vhorn, F1 help, F2 clan base, F3 battle reports, Esc closes; click a unit to inspect it or a building for the base | D-pad left/right inspect, up/down zoom; View = base, Menu = help |
| Playtest helpers | F7 drill the kin-band to Doctrine tier 3 (unlocks Titan Form at once), F9 game speed x1/x2/x4, F10 skip Rustmaw to its next phase, F11 reveal map | |

Steering moves the party's **banner** (the glowing pole). The kin-band forms up around it out of combat. In combat they
fight around it, and walking the banner drags the fight with you: pull them out of fire or kite a boss across the room.
Letting go of the stick stops the banner. You never cast abilities yourself, and the game never pauses (the F9 speed-up
exists only for testing).

## The HUD

- **Party frames (left):** one bar each for health, mana (blue) and stamina (gold, orange when exhausted).
  - The dark red end of a health bar is **wounds**: health no heal can restore until the kin-band rests at home. The
    number reads `health / heal ceiling`.
  - Tags show downed, injured, carrying a kin-core and similar states.
- **Hotbar (bottom):** the ten strategies, plus Go home, Garrison and Stop. Hover a button for what it does, what it is
  strong against and what it risks.
- **Minimap (top-right):** centred on the banner, north up. It only shows ground the kin-band has explored, and enemies
  only while in sight. Unexplored ground is black in the world too.

## What to try (the loop, about 30-45 minutes)

1. **Venture out.** Walk Ember Kin out to the nearby Cratermaw dens and fight them. Watch the party frames: the
   Shieldbearer's stamina drains on Taunt Roar and Shield Bash, the Mender's mana on every heal, and everyone's heal
   ceiling creeps down as wounds pile up.
2. **Gather.** Ferrite chunks lie in crater beds and hide comes from kills. The squad hauls loads and slows when heavy.
3. **Push your luck.** Every fight leaves wounds. Rest (9) refills mana and stamina in the field, but only home closes
   wounds. Go one more camp deeper for the ferrite, or head home now? A kin-band that pushes too far fights its next
   battle at half health.
4. **Come home** (H, or J to garrison). Garrisoned, wounds close in seconds, the haul is deposited and Doctrine is
   banked. A garrison also works the Smelter (ferrite into alloy) and the Salvage Bench (ferrite arms and plate).
5. **Get stronger.** Open the base (F2):
   - **Upgrade the War Hall.** Each level drills the kin-band: +12% health and +25% mana and stamina.
   - Craft ferrite arms and plate.
   - Craft the Breach Sigil to open Breach Hold (the big walled crater in the north).
6. **Titan Form.** Around minutes 18-25 the Shieldbearer reaches Doctrine tier 3 and unlocks it (an alert and a flex
   tell you). It costs 50 mana: in hard fights it grows to titan size for 15 s. To try it straight away, press F7.
7. **Rustmaw.** Go in rested, in full ferrite gear. A kin-band without ferrite gear does not win.
   - Phase 1: Spread Out against its line charges, and steer clear of the lane. Warwright ramparts in its path make
     it crash and stagger, and a braced Titan stops it dead. Charge its vents while they are exposed.
   - Phase 2: right-click the Scrap Menders and Focus Fire them.
   - Phase 3: Stack behind a rampart or the Titan for each fire wave, then Charge between waves.

If the whole kin-band falls, the clan carries their kin-cores home and the Kin Cradle regrows them with Doctrine,
Instinct and gear intact. Anything unbanked is lost, and regrowth is twice as slow if the stockpile can't pay.

## What's in, mapped to the design doc

| Design doc | In this MVP |
| --- | --- |
| Command, don't click | You lead one kin-band directly (steer, march, gather, haul, garrison, recover, stop) and set 10 of the 12 combat strategies (all but Ambush and Lay Siege). Each strategy has its Warwright and Siegewright behaviour from the doc's table |
| Squads and roles | Shieldbearer, Mender, Bruiser, Siegewright, Warwright, each with 3 abilities from the doc's kit table (the 3rd unlocks at Doctrine tier 2). The Shieldbearer's 4th, Titan Form, unlocks at tier 3 |
| Mana and stamina | Every Vhorn has both, sized to its role. Spells, walls, siege works and Titan Form cost mana; the Mend Beam drains it while channelled. Heavy blows, shield work and dodges cost stamina, and melee swings cost a little. Below 12% stamina a Vhorn is exhausted: 20% slower, 30% slower attacks, weaker dodges. Regen is slow in combat, faster out of it, fast at rest and fastest garrisoned |
| AI resource management | Abilities the Vhorn can't afford wait. From Doctrine tier 2 each unit keeps a reserve (an Instinct parameter it learns per strategy and enemy family) and scores spending below it as a cost. Tier 0-1 Vhorn spend everything |
| Wounds (push your luck) | 10% of damage taken becomes wounds, and being downed adds 15%, up to 75% of max health. Heals stop at the heal ceiling. Wounds close only at home: fast while garrisoned, slowly inside the base |
| Titan Form (tank war-form) | The tank grows 1.6x wider and 1.7x taller for 15 s (55 s cooldown): +35% health (fraction kept), +20 armour, 1.5x threat, 20% slower, hard to knock back or stun. A living wall: it slows enemies at its shield, catches shots aimed past it, blocks pounces onto the back line, stops Rustmaw's charge when braced (2 s stagger, 4 s with a rampart right behind it) and shelters up to 3 kin from each fire wave (-70%). It ends with the fight, on a march, when downed, or if wedged in a narrow gap |
| Utility AI | Per-role scoring of every option on each think (4x per second), shaped by the strategy, gated by Doctrine tier and tuned by Instinct. The inspector shows the live top 5 scores ("AI thinking") |
| Doctrine | 5-tier mastery track per strategy per unit. XP comes from time under a strategy, kills, hard fights, wins, and using a strategy where it suits the enemy. Tiers change behaviour: for example, Defensive Formation tier 3 tanks intercept chargers on the healer, and Focus Fire tier 3 targets healers first |
| Instinct | Real learning: a few numbers per unit per (strategy, enemy family), including how much mana and stamina to hold back. A bounded (1+1) evolution strategy tunes them between fights, and the bounds widen with Doctrine tier. Lock and reset buttons; plain-language notes in battle reports |
| Threat | WoW-style threat tables, 3x tank threat, Taunt Roar, healing threat on every engaged enemy; DPS can pull aggro |
| Physical combat | Projectiles travel, miss, and are blocked by walls, rocks and the first hostile body in the way. Tanks body-block; melee has swing arcs. Line of sight; high-ground bonus; Warwright walls block both sides |
| Damage types | Kinetic, Thermal (burn), Cryo (slow, freeze), Arc (chains), Corrosive (strips armour), Psionic (defined, unused). The bestiary discovers resistances on hit |
| Injury, death, resurrection | Downed > Injured (-20% health and speed) > Killed. Stabilize; Rest clears injuries; squadmates carry kin-cores. The Kin Cradle regrows units with Doctrine and Instinct intact. Unbanked XP is lost on death |
| Economy | Ferrite, crater stone, grass fibre, hide, ark salvage. Carry limits slow loaded squads. The Smelter and Salvage Bench need a garrison. War Hall levels drill the kin-band stronger |
| The Craters | Seeded terrain with craters, ferrite in crater beds and ark wreckage. Meteor strikes come with tells and leave fresh ferrite. Enemies: Cratermaw packs (pounce), Rockback crawlers (kinetic-resistant, tail slam), feral drones, Scrap Menders. Camps respawn, and war parties hunt the kin-band in the field |
| Rustmaw | The doc's worked example, tuned for one kin-band: The Charge, Scrap Brood and Overheat, with weak-point vents, rampart crashes, mender welding, cover-blocked fire waves, a soft enrage and a raid reset |
| Fog of war | Only explored ground is drawn, on the minimap, the map and in the world. Enemies show only in sight |

**Not in this slice (next milestones):**
- Supply: roads, energy, conduits, convoys and outposts.
- Camps and outposts to rest at further from home (the design's answer to "resting further away from base").
- Biomes and enemy cities: Fissures, Growth and Citadel; enemy cities.
- Crafting progression: Forge, Core Lathe, ability cores, specializations, boss relics and commander powers.
- Squads: Ambush and Lay Siege; squad cohesion; Drill Grounds; more than one kin-band.
- Co-op multiplayer.

## Decisions I made (please push back on any of these)

- **Engine: Unity, built-in pipeline, everything procedural.** No imported art and no prefabs; primitives and generated
  textures in the doc's palette (grey stone, pale green-gold grass, rust ferrite, amber kin-marks).
- **The simulation is engine-agnostic C#** (`Assets/Kinforge/Scripts/Sim`, no UnityEngine reference).
  - It runs at a fixed 20 Hz, with Unity only rendering and taking input.
  - This matches the doc's networking note (host simulates, clients render).
  - It makes balance testable headlessly, and it is deterministic per seed.
- **One kin-band.** Recruiting is gone. War Hall upgrades drill the band stronger instead: 3 levels, each +12% health
  and +25% mana and stamina.
- **Third-person, not RTS.** The camera orbits the banner at 6-32 m and never pulls out to a map view. Steering moves
  the banner (and cancels marching or gathering); click-to-march, the minimap and the map remain for long trips.
- **Strategies on a hotbar** (1-0) rather than letter keys, because WASD now walks the party.
- **Wounds are the push-your-luck cost.** They come from a share of damage taken plus a chunk per down. Rest in the
  field restores mana and stamina but not wounds. A kin-band typically loses 5% of the tank's ceiling per easy fight and
  15-20% per hard one, so after two to five fights out, going home starts to matter.
- **Only player units have mana and stamina.** Enemies are balanced on cooldowns as before.
- **A wipe is never a dead end.** With one kin-band nobody can fetch the cores, so the clan carries them home after 6 s,
  and the Cradle regrows units it can't pay for at half speed.
- **Rustmaw retuned for one kin-band:** 8,500 health (was 16,000), lighter hits (swipe 60, charge 80, eruption 55, fire
  wave 90), 2 hounds per wave, soft enrage after 100 s. It needs ferrite gear and Doctrine around tier 3.
- **Recruits start at Doctrine tier 0.** The 3rd ability slot opens at tier 2 in any strategy.
- **Death costs unbanked Doctrine XP** (the doc's proposed rule; toggle `Balance.DeathLosesUnbankedDoctrine`).
- **Friendly fire is off** for direct attacks; meteors hit everyone.
- **Warwright terrain changes are temporary.**
  - Ramparts last 25 s and turrets 30 s.
  - Caps per squad: 2 turrets, 1 mortar, 4 ramparts, 2 spike fields.
- **Raid gating:** a crafted Breach Sigil opens Breach Hold; the Kin Cradle pauses while the raid is active.
- **Smart AI is earned.**
  - Charge-blocking walls happen in defensive postures from tier 2.
  - Hiding behind walls from fire waves happens under Stack or Fortify, or for tier 4+ veterans in any strategy.
  - So the commander's calls matter early on.
- **Titan Form is a timed transform the AI casts, not a toggle.**
  - It unlocks at Doctrine tier 3 in any strategy and costs 50 mana.
  - It ends when the fight does or the squad marches, so a giant never paths across the map.
  - It reads boss tells the way the Warwright's charge walls do: braced postures from tier 3, any strategy from tier 4.
  - The Warwright's answer stays the strongest:
    - Only a tank that braces stops a charge; one caught by surprise is trampled.
    - It steps aside for a rampart further down the lane.
    - A clash staggers for less time than a rampart crash.
  - Shelter is partial and capped at 3 kin, so walls remain full cover.
- **The UI is IMGUI** (robust, code-only) and functional rather than pretty.

## Balance, measured headlessly

Numbers live in `Sim/Data/Balance.cs`, `UnitDefs.cs` and `AbilityDefs.cs`. The headless lab (`Tools/SimLab`) runs
scripted fights.

**Rustmaw vs one Doctrine tier 3 kin-band in ferrite gear.** 60 seeds on the flat test arena, Titan Form off then on.
"Passive" holds Defensive Formation; "reading the phases" is a scripted commander that switches strategies:

| War Hall level | Passive commander | Commander reading the phases |
| --- | --- | --- |
| 1 | 50% / 60% | 50% / 53% |
| 2 | 47% / 65% | 57% / 77% |
| 3 | 80% / 82% | 65% / 72% |

- **Gear is the gate:** without ferrite arms and plate the same kin-band wins 0-2% at any War Hall level.
- **Real Breach Holds** (100 generated worlds with terrain and walls, commander reading the phases, Titan Form off then
  on): 48% / 36% at War Hall 1, and 48% / 67% at War Hall 3.

**Campaign bot.** A scripted player plays the whole loop on real worlds:
- It gathers and hunts hide, and goes home to close wounds, smelt and craft.
- It upgrades the War Hall twice, crafts arms, plate and the Sigil, then raids with the phase commander.
- Results across nine seeds (1337, 7, 42, 99, 5, 3, 11, 1, 2) with Titan Form on and off: all 18 runs end in victory.
  - 12 of the 18 win on the first raid, at 26:50-31:06.
  - The other six wipe, regrow and retry, winning by 33:24-73:07.

**Open-world fights** are essentially unchanged by the redesign:
- A fresh kin-band wins most camp fights.
- Rockbacks punish Charge, Focus Fire and Stack at low tiers, while Defensive Formation, Hold, Kite, Spread Out and
  Fortify win.

## Project layout

```
Assets/Kinforge/Scripts/Sim     engine-agnostic simulation (world gen, AI, combat, learning, economy, boss)
Assets/Kinforge/Scripts/Game    Unity layer: bootstrap, third-person camera, input (keyboard, mouse, gamepad), views, effects, HUD
Assets/Kinforge/Editor          first-open scene setup, gamepad axes + Kinforge menu
Tests/Kinforge.Sim.Tests        xUnit tests for the sim (dotnet test)
Tools/SimLab                    balance lab: dotnet run -- fights | smoke 1 20 | learn | campaign 1337
                                titan 60 3 -1 2 (one kin-band, Titan Form off vs on) / raid 100 3 -1 2 (real Breach Holds)
                                env: KF_HALL=1..3 War Hall level, KF_TITAN=0 Titan Form off, KF_DMG=1 damage-taken breakdown
                                (frames / bossframes dump per-tick positions for top-down debug renders)
Tools/UnityCompileCheck         compiles Assets/ against UnityEngine reference assemblies without the editor
Tools/EditorCompileCheck        same for the editor script
```

Run the checks with the .NET 8 SDK:

```bash
dotnet test Tests/Kinforge.Sim.Tests
dotnet build Tools/UnityCompileCheck
dotnet run --project Tools/SimLab -c Release -- titan 60 3 -1 2
```

## Feedback I'd most like

- Does it feel like leading a raid group now?
  - Is steering the banner with WASD or the stick responsive?
  - Does the camera sit right, and do you feel in the action?
- **Push your luck:** do wounds make you think about when to turn back?
  - Is the pace right? Wounds are currently 10% of damage taken. Should they bite harder or softer?
- **Mana and stamina:** can you read them on the party frames, and do you see the kin-band managing them? (Watch a
  veteran Mender hold back versus a green one.)
- **Strategies:** are the 10 distinct, and are you making calls under pressure?
- **Learning:** can you see units getting better (Doctrine tiers, kin-mark glow, battle-report notes)?
- **Rustmaw:** is it readable and fair for one kin-band? Too hard or too easy?
- **Titan Form:** does the rise read well up close, and does stopping Rustmaw's charge feel epic?
- **Economy pacing:** how long until the Sigil? Is going home to garrison a good beat or a chore?
- **Controller:** does it work out of the box, and which buttons feel wrong?
- **The design doc:** which of its open questions do you want settled differently from my choices above?
