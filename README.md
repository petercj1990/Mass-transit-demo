# KINFORGE: Unity MVP (The Craters slice)

A playable first slice of the KINFORGE design doc: one commander, kin-bands of five Vhorn that fight on their own and learn,
the Craters biome, and the Rustmaw raid boss. Everything is generated from code (terrain, models, effects, UI), so there are
no art assets to import.

## Open it

1. Get this branch on its own:
   ```bash
   git clone --branch claude/kinforge-mvp --single-branch https://github.com/petercj1990/Mass-transit-demo.git kinforge
   ```
   (Optional: move it into its own repo with `git remote set-url origin <new repo>` then `git push -u origin claude/kinforge-mvp:main`.)
2. Unity Hub > **Add project from disk** > pick the `kinforge` folder. It targets **Unity 6 (6000.0 LTS)**, uses the built-in
   render pipeline and needs no extra packages. Unity 2022.3 LTS also works. If Hub says the exact editor version is
   missing, pick any Unity 6 or 2022.3 version you have installed.
3. On first open an editor script creates `Assets/Kinforge/Scenes/Main.unity` and opens it. Press **Play**.
   The game also starts if you press Play in any scene. You can use the menu **Kinforge > Open Main Scene and Play**.
4. If you see an "Input is disabled" message: **Edit > Project Settings > Player > Other Settings > Active Input Handling**
   = **Both** (the prototype uses the classic Input Manager).

The world uses a fixed seed (1337) so feedback is comparable. The help screen (F1) has a "New random world" button.

## Controls

| Action | Input |
| --- | --- |
| Pan / rotate / zoom camera | WASD or arrows / Q, E / mouse wheel; middle-drag pans, Alt+middle-drag rotates; F8 toggles edge-scroll |
| Select kin-band | Click a Vhorn, drag a box, or press 1-6 (double-tap jumps the camera); Tab cycles; Shift adds |
| Move / attack / gather / recover / go home | Right-click ground / enemy / resource / kin-core cache / base |
| Combat commands | Z Charge, X Defensive Formation, C Hold Position, V Kite, B Focus Fire, N Spread Out, M Stack, G Fall Back, T Fortify, R Rest |
| Orders | H return home and deposit, J garrison, K stop |
| Panels | F1 help, F2 clan base, F3 battle reports, Esc closes; click a unit to inspect it, click a building for the base |
| Playtest helpers | F7 drill the selected kin-bands to Doctrine tier 3 (unlocks Titan Form at once), F9 game speed x1/x2/x4, F10 skip Rustmaw to its next phase, F11 reveal map |

The player never casts abilities: units pick targets, positions and abilities themselves under the command you set.
The game never pauses (F9 speed-up exists only for testing).

## What to try (the loop, about 45-60 minutes)

1. Fight the nearby Cratermaw dens with your first kin-band (Ember Kin). Watch the battle report: Doctrine XP, tier-ups,
   and Instinct notes such as "keeps more distance vs Cratermaws".
2. Right-click ferrite chunks in crater beds to gather. Squads haul loads home automatically, and hide comes from kills.
3. Open the base (F2), upgrade the War Hall and recruit a second kin-band (fresh recruits start at Doctrine tier 0).
4. Garrison a squad (J) so the Smelter turns ferrite into alloy and the Salvage Bench crafts ferrite arms and plate.
5. Craft the Breach Sigil to open Breach Hold (the big walled crater in the north) and take two squads in.
6. Around minute 18-24 your veteran Shieldbearer reaches Doctrine tier 3 and unlocks **Titan Form** (an alert and a
   flex tell you). In hard fights it grows to titan size for 15 s. Watch it at Rustmaw: under Spread Out or Defensive
   Formation it steps into the charge lane and stops the charge dead, and under Stack it plants itself in front of the
   squad so kin in its shadow shrug off most of a fire wave. To try it straight away, select a kin-band and press F7.
7. Rustmaw: Spread Out against its line charges (Warwright ramparts in its path make it crash and stagger, a braced
   Titan stops it dead), Charge its vents when exposed, right-click the Scrap Menders in phase 2 and Focus Fire them,
   and in phase 3 Stack behind a rampart or a Titan for each fire wave, then Charge between waves.

## What's in, mapped to the design doc

| Design doc | In this MVP |
| --- | --- |
| Command, don't click | 10 of the 12 combat commands (all but Ambush and Lay Siege), each with its Warwright and Siegewright behaviour from the doc's table, plus move, gather, haul, garrison, recover and stop |
| Squads and roles | Shieldbearer, Mender, Bruiser, Siegewright, Warwright, each with 3 abilities from the doc's kit table (the 3rd unlocks at Doctrine tier 2); the Shieldbearer's 4th, Titan Form, unlocks at tier 3 |
| Titan Form (tank war-form) | The tank grows 1.6x wider and 1.7x taller for 15 s (55 s cooldown): +35% health (fraction kept), +20 armour, 1.5x threat, 20% slower, hard to knock back or stun. A living wall: it slows enemies at its shield, catches shots aimed past it, blocks pounces onto the back line, stops Rustmaw's charge when braced (2 s stagger, 4 s with a rampart right behind it) and shelters up to 3 kin from each fire wave (-70%). Ends with the fight, on a march, when downed, or if wedged in a narrow gap |
| Utility AI | Per-role scoring of every option each think (4x per second), shaped by the command, gated by Doctrine tier, tuned by Instinct. The inspector shows the live top 5 scores ("AI thinking") |
| Doctrine | 5-tier mastery track per command per unit; XP from time under a command, kills, hard fights, wins, and using a command where it suits the enemy; tiers change behaviour (e.g. Defensive Formation tier 3 tanks intercept chargers on the healer, tier 4 rotates to cover flanks, Focus Fire tier 3 targets healers first, tier 4 switches off targets being healed) |
| Instinct | Real learning: a few numbers per unit per (command, enemy family), tuned between fights by a bounded (1+1) evolution strategy; bounds widen with Doctrine tier; lock and reset buttons; plain-language notes in battle reports |
| Threat | WoW-style threat tables, 3x tank threat, Taunt Roar, healing threat on every engaged enemy, DPS can pull aggro |
| Physical combat | Projectiles travel, miss and are blocked by walls, rocks and the first hostile body in the way; tanks body-block; melee swing arcs; line of sight; high ground bonus; Warwright walls block both sides |
| Damage types | Kinetic, Thermal (burn), Cryo (slow, freeze), Arc (chains), Corrosive (strips armour), Psionic (defined, unused); bestiary discovers resistances on hit |
| Injury, death, resurrection | Downed > Injured (-20% health and speed) > Killed; Stabilize; Rest clears injuries; squadmates carry kin-cores; Kin Cradle regrows with Doctrine and Instinct intact; unbanked XP is lost on death; a squad wipe drops a core cache another squad must recover |
| Economy | Ferrite, crater stone, grass fibre, hide, ark salvage; carry limits slow loaded squads; garrison-gated Smelter and Salvage Bench; War Hall squad cap; recruiting |
| The Craters | Seeded terrain with craters, ferrite in crater beds, ark wreckage, meteor strikes with tells that leave fresh ferrite, Cratermaw packs (pounce), Rockback crawlers (kinetic-resistant, tail slam), feral drones, Scrap Menders; camps respawn; war parties hunt squads in the field |
| Rustmaw | The doc's worked example: The Charge, Scrap Brood and Overheat, with weak-point vents, rampart crashes, mender welding, cover-blocked fire waves, soft enrage, raid reset |
| Fog of war | Squads reveal; explored terrain stays mapped; enemies show only in sight |

**Not in this slice (next milestones):** roads, energy, conduits, convoys and outposts (the supply pillar); Fissures,
Growth and Citadel; Forge, Core Lathe, ability cores, specializations and boss relics/commander powers; Ambush and Lay
Siege; enemy cities; squad cohesion; Drill Grounds; co-op multiplayer.

## Decisions I made (please push back on any of these)

- **Engine: Unity, built-in pipeline, everything procedural.** No imported art, no prefabs; primitives and generated
  textures in the doc's palette (grey stone, pale green-gold grass, rust ferrite, amber kin-marks).
- **Simulation is engine-agnostic C#** (`Assets/Kinforge/Scripts/Sim`, no UnityEngine reference) at a fixed 20 Hz, with
  Unity only rendering and taking input. This matches the doc's networking note (host simulates, clients render),
  makes balance testable headlessly, and is deterministic per seed.
- **Squad cap starts at 1 and grows to 3** with War Hall upgrades (the doc proposes 8 late-game).
- **Recruits start at Doctrine tier 0.** The 3rd ability slot opens at tier 2 in any command.
- **Death costs unbanked Doctrine XP** (the doc's proposed rule; toggle `Balance.DeathLosesUnbankedDoctrine`).
- **Friendly fire off** for direct attacks; meteors hit everyone.
- **Warwright terrain changes are temporary** (ramparts last 25 s); turrets 30 s; caps per squad: 2 turrets, 1 mortar,
  4 ramparts, 2 spike fields.
- **Raid gating:** a crafted Breach Sigil opens Breach Hold; the Kin Cradle pauses while the raid is active.
- **Smart AI is earned:** charge-blocking walls happen in defensive postures from tier 2; hiding behind walls from fire
  waves happens under Stack or Fortify, or for tier 4+ veterans in any command. The commander's calls matter early on.
- **Fall Back** retreats all the way to base; any move order cancels it.
- **Titan Form is a timed transform the AI casts, not a toggle.** It unlocks at Doctrine tier 3 in any command, so the
  veteran squad's tank usually has it by the first raid and the second squad's does not. It ends when the fight does
  or the squad marches, so a giant never paths across the map. It reads boss tells like the Warwright's charge walls:
  braced postures from tier 3, any command from tier 4. Only a tank that braces stops a charge (one caught by surprise
  is trampled), it steps aside for a rampart further down the lane, and a clash staggers for less than a rampart crash,
  so the Warwright's answer stays the strongest. Shelter is partial and capped at 3 kin, so walls remain full cover.
- **UI is IMGUI** (robust, code-only) and functional rather than pretty.

## Balance, measured headlessly

Numbers live in `Sim/Data/Balance.cs`, `UnitDefs.cs` and `AbilityDefs.cs`. The headless lab (`Tools/SimLab`) runs
scripted fights. Current Rustmaw results over 10 runs (2 squads, ferrite gear):

| Squads | Passive commander (Defensive Formation only) | Commander reading the phases |
| --- | --- | --- |
| Doctrine tier 2 | 30% win | 80% win |
| Doctrine tier 3 | 60% win | 90% win |

Against open-world camps a fresh squad wins most fights; Rockbacks punish Charge and Focus Fire at low tiers (about 30%
wins) while Defensive Formation, Hold, Kite and Fortify win.

Titan Form, measured with paired seeds (the same worlds with the ability on and off, ferrite gear). `titan` is the flat
test arena (100 seeds); `raid` is real generated Breach Holds with terrain and walls (200-300 worlds), either with every
command trained to the tier shown or, like the campaign bot's squads, only Defensive Formation:

| Squad tiers | Flat arena: passive commander | Flat arena: commander reading the phases | Real holds, all commands trained | Real holds, Defensive Formation only | Charges stopped per raid |
| --- | --- | --- | --- | --- | --- |
| 3 and 2 (a typical first raid: one Titan) | 42% > 50% | 80% > 78% | 60% > 59% | 78% > 85% | about 0.6 |
| 3 and 3 (two Titans) | 61% > 68% | 78% > 92% | 65% > 84% | 87% > 90% | about 1.1 |

With one Titan the raid is about as hard as before (within noise) but has bigger moments; two veteran Titans make it
clearly easier, and an attentive commander still gains far more than a passive one. All nine campaign seeds tried
(1337, 7, 42, 99, 5, 3, 11, 1, 2) end in victory with the ability on and off; the first Titan unlocks between 17:52
and 24:39. Single campaigns swing either way (a few seconds' difference early on changes everything after), so judge
balance from the paired runs.

## Project layout

```
Assets/Kinforge/Scripts/Sim     engine-agnostic simulation (world gen, AI, combat, learning, economy, boss)
Assets/Kinforge/Scripts/Game    Unity layer: bootstrap, camera, input, views, effects, HUD
Assets/Kinforge/Editor          first-open scene setup + Kinforge menu
Tests/Kinforge.Sim.Tests        xUnit tests for the sim (dotnet test)
Tools/SimLab                    balance lab: dotnet run -- fights | boss 10 3 2 | smoke 1 20 | learn | campaign 1337
                                titan 100 3 2 2 / raid 200 3 2 2 [1] (Titan Form on vs off); KF_TITAN=0 switches it off
                                (frames / bossframes dump per-tick positions for top-down debug renders)
Tools/UnityCompileCheck         compiles Assets/ against UnityEngine reference assemblies without the editor
Tools/EditorCompileCheck        same for the editor script
```

Run the checks with the .NET 8 SDK:

```bash
dotnet test Tests/Kinforge.Sim.Tests
dotnet build Tools/UnityCompileCheck
dotnet run --project Tools/SimLab -- boss 10 3 2
```

## Feedback I'd most like

- Does commanding feel like *command, don't click*? Are the 10 commands distinct enough, and are you making calls under pressure?
- Can you see units getting better (Doctrine tiers, kin-mark glow, battle-report notes)? Is the learning legible?
- Is Rustmaw readable (tells, phase hints) and fair? Too hard or too easy with two squads?
- Titan Form: does the rise read from the RTS camera, and does stopping Rustmaw's charge feel epic? Is the giant big
  enough (1.6x wide, 1.7x tall), too strong in phase 3, or unlocking too late?
- Economy pacing: how long until the Sigil? Is garrisoning for the workshops interesting or a chore?
- Camera, selection and HUD: what got in your way?
- Which of the open questions in the doc do you want settled differently from my choices above?
