using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>
    /// Titan Form, the Shieldbearer's slot-4 ability. A veteran's kin-core remembers the clan's
    /// war-form and, flooded with forge-heat, regrows the body at titan size for a few seconds.
    /// The grown tank is a living wall: it shoulders enemies aside and slows those at its shield,
    /// catches shots aimed past it, blocks pounces onto the back line, stops Rustmaw's charge dead
    /// and shelters kin behind it from fire waves. Every modifier scales with <see cref="Unit.TitanT"/>,
    /// so a tank that never grows behaves exactly as before.
    /// </summary>
    public static class Titan
    {
        public const byte FadeTimer = 0, FadeCramped = 1, FadeNoRoom = 2, FadeFelled = 3, FadeLeftFight = 4;

        /// <summary>Clear ground around a point, so a grown body is not born inside a wall.</summary>
        public static bool HasRoom(World w, Vec2 p)
        {
            if (w.Nav.BlocksMoveAt(p)) return false;
            for (int i = 0; i < 8; i++)
            {
                if (w.Nav.BlocksMoveAt(p + Vec2.FromAngle(i * KMath.TwoPi / 8f) * Balance.TitanRoom)) return false;
            }
            return true;
        }

        /// <summary>
        /// Room here or one step away: something built during the cast (a flank rampart) only nudges
        /// the giant aside as it grows, since bodies are pushed out of walls every tick.
        /// </summary>
        public static bool HasRoomNear(World w, Vec2 p)
        {
            if (HasRoom(w, p)) return true;
            for (int i = 0; i < 8; i++)
            {
                Vec2 q = p + Vec2.FromAngle(i * KMath.TwoPi / 8f);
                if (HasRoom(w, q) && w.Nav.LineWalkable(p, q, 0.5f)) return true;
            }
            return false;
        }

        /// <summary>The cast has finished: grow, or fizzle if there is no room (or the feature is off).</summary>
        public static void Begin(World w, Unit u)
        {
            var slot = u.Slot(AbilityId.TitanForm);
            var def = AbilityDefs.Get(AbilityId.TitanForm);
            if (!w.TitanEnabled || !HasRoomNear(w, u.Pos))
            {
                if (slot != null) slot.Cooldown = Balance.TitanFizzleCooldown;
                w.Emit(SimEventType.TitanFade, u.Id, 0, u.Pos, FadeNoRoom);
                return;
            }
            // The full cooldown starts only now, so a dodge, stun or knockback during the cast never wastes it.
            if (slot != null) slot.Cooldown = def.Cooldown;
            u.TitanTime = def.Duration;
            u.TitanEndReason = FadeTimer;
            u.TitanClashed = false;
            u.TitanStuck = 0f;

            // Challenge: everything close turns on the giant for a moment.
            var hits = w.AbilityScratch;
            w.QueryUnits(u.Pos, def.Radius, Faction.Enemy, hits);
            foreach (var e in hits) Combat.Taunt(w, e, u, Balance.TitanChallengeSeconds);

            if (u.Fight != null) u.Fight.TitanGrows++;
            w.Emit(SimEventType.TitanRise, u.Id, 0, u.Pos, def.Radius);
            if (!w.TitanSeen)
            {
                w.TitanSeen = true;
                w.Alert($"{u.Name} rises as a Titan! Enemies bog down at its shield, shots slam into it, Rustmaw's charge breaks on it, and kin behind it are sheltered from fire waves. The tank decides when.", u.Pos, 3);
            }
        }

        /// <summary>Counts the form down and runs the grow / shrink ramp (StatusSystem, active units only).</summary>
        public static void Tick(World w, Unit u, float dt)
        {
            if (u.TitanTime > 0f)
            {
                var sq = u.Squad;
                if (sq != null)
                {
                    // A giant never paths across the map: the form ends when the fight does.
                    if (SquadSystem.IsMarching(w, sq)) Cap(u, Balance.TitanMarching, FadeLeftFight);
                    else if (!sq.InCombat) Cap(u, Balance.TitanOutOfCombat, FadeLeftFight);
                }
                u.TitanTime -= dt;
                if (u.TitanTime <= 0f)
                {
                    u.TitanTime = 0f;
                    u.TitanStuck = 0f;
                    w.Emit(SimEventType.TitanFade, u.Id, 0, u.Pos, u.TitanEndReason);
                }
            }
            float target = u.TitanTime > 0f ? 1f : 0f;
            if (u.TitanT != target) SetT(u, KMath.MoveTowards(u.TitanT, target, dt / Balance.TitanRampSeconds));
        }

        /// <summary>Shortens the form to at most <paramref name="seconds"/>, remembering why it ends.</summary>
        public static void Cap(Unit u, float seconds, byte reason)
        {
            if (u.TitanTime <= seconds) return;
            u.TitanTime = seconds;
            u.TitanEndReason = reason;
        }

        /// <summary>The only writer of TitanT and of a grown unit's radius. Keeps the health fraction.</summary>
        public static void SetT(Unit u, float t)
        {
            float max = u.MaxHp;
            float frac = max > 0f ? u.Hp / max : 0f;
            bool alive = u.Hp > 0f;
            u.TitanT = t;
            u.Radius = t > 0f ? u.Def.Radius * (1f + (Balance.TitanScale - 1f) * t) : u.Def.Radius;
            if (alive) u.Hp = Math.Max(1f, Math.Min(1f, frac) * u.MaxHp);
        }

        /// <summary>Ends the form at once (downed, killed, garrisoned, regrown). Leaves Hp 0 alone.</summary>
        public static void EndInstant(World w, Unit u, byte reason)
        {
            if (u.TitanTime <= 0f && u.TitanT <= 0f) return;
            u.TitanTime = 0f;
            u.TitanStuck = 0f;
            SetT(u, 0f);
            w?.Emit(SimEventType.TitanFade, u.Id, 0, u.Pos, reason);
        }

        public static float TauntRadius(Unit u, AbilityDef def) => def.Radius + Balance.TitanTauntBonus * u.TitanT;

        /// <summary>Ground enemies at a Titan's shield move slower (zone of control). 1 when no Titan is near.</summary>
        public static float LoomMul(World w, Unit e)
        {
            if (e.Faction != Faction.Enemy || e.Def.Flying || e.Kind == UnitKind.Rustmaw) return 1f;
            float best = 1f;
            foreach (var t in w.Units)
            {
                if (t.TitanT <= 0f || !t.IsPlayer || !t.IsTargetable) continue;
                float r = t.Radius + e.Radius + Balance.TitanLoomReach;
                if (Vec2.DistanceSq(t.Pos, e.Pos) > r * r) continue;
                best = Math.Min(best, 1f - Balance.TitanLoomSlow * t.TitanT);
            }
            return best;
        }

        /// <summary>A grown player Titan standing across the line from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static Unit ShieldOnLine(World w, Vec2 from, Vec2 to, Unit exclude, float bodyRadius)
        {
            Vec2 d = to - from;
            float len = d.Length;
            if (len < 1.5f) return null;
            Vec2 dir = d / len;
            foreach (var t in w.Units)
            {
                if (t == exclude || t.TitanT <= 0f || !t.IsPlayer || !t.IsTargetable) continue;
                float along = Vec2.Dot(t.Pos - from, dir);
                if (along < 1f || along > len - 0.5f) continue;
                if (Vec2.DistanceToSegment(from, to, t.Pos) <= t.Radius + bodyRadius) return t;
            }
            return null;
        }

        /// <summary>
        /// A grown Shieldbearer braced in the nose of Rustmaw's charge, nearest first. Only a braced
        /// giant (one whose AI chose to meet the charge) stops it; one caught by surprise is trampled.
        /// </summary>
        public static Unit ChargeBlocker(World w, Unit boss, Vec2 next, Vec2 dir)
        {
            Unit best = null;
            float bestAlong = float.MaxValue;
            foreach (var u in w.Units)
            {
                if (!u.IsPlayer || !u.IsTargetable || u.TitanTime <= 0f || u.TitanClashed || u.Status.Disabled) continue;
                if (w.Time > u.TitanBraceUntil) continue;
                Vec2 d = u.Pos - next;
                float along = Vec2.Dot(d, dir);
                float lat = MathF.Abs(Vec2.Dot(d, dir.Right));
                if (along <= 0f || along > boss.Radius + u.Radius + 0.2f) continue;
                if (lat > boss.Radius * 0.7f + u.Radius) continue;
                if (along < bestAlong || (along == bestAlong && best != null && u.Id < best.Id))
                {
                    bestAlong = along;
                    best = u;
                }
            }
            return best;
        }

        /// <summary>
        /// Walks three parallel lines (centre and both edges, like the charge's own probes) and returns
        /// the distance to the first blocked cell, or -1. With <paramref name="rampartOnly"/> only a player
        /// rampart counts, and <paramref name="wall"/> is that rampart.
        /// </summary>
        public static float FirstBlockAlong(World w, Vec2 from, Vec2 dir, float length, float halfWidth, bool rampartOnly, out Structure wall)
        {
            wall = null;
            float best = -1f;
            Vec2 side = dir.Right * halfWidth;
            for (int i = -1; i <= 1; i++)
            {
                Vec2 a = from + side * i;
                int cell = w.Nav.FirstBlockedCell(a, a + dir * length, false);
                if (cell < 0) continue;
                var s = w.FindStructure(w.Nav.OwnerOfCell(cell));
                bool rampart = s != null && s.Kind == StructureKind.Rampart && s.Faction == Faction.Player;
                if (rampartOnly && !rampart) continue;
                float along = Vec2.Dot(w.Nav.CellCenter(cell) - from, dir);
                if (best < 0f || along < best)
                {
                    best = along;
                    wall = rampart ? s : null;
                }
            }
            return best;
        }

        /// <summary>Is point <paramref name="p"/> in the shadow Titan <paramref name="t"/> casts away from <paramref name="src"/>?</summary>
        public static bool InShadow(Unit t, Vec2 src, Vec2 p)
        {
            float dT = Vec2.Distance(src, t.Pos);
            if (Vec2.Distance(src, p) <= dT) return false;
            if (Vec2.Distance(t.Pos, p) > t.Radius + Balance.TitanShelterDepth) return false;
            return Vec2.DistanceToSegment(src, p, t.Pos) <= t.Radius + Balance.TitanShelterPad;
        }

        /// <summary>True if any grown Titan shelters <paramref name="u"/> from a blast at <paramref name="src"/>.</summary>
        public static bool Shelters(World w, Vec2 src, Unit u)
        {
            foreach (var t in w.Units)
            {
                if (t == u || t.TitanT <= 0f || !t.IsPlayer || !t.IsTargetable) continue;
                if (InShadow(t, src, u.Pos)) return true;
            }
            return false;
        }

        /// <summary>Who each Titan shelters from a blast at <paramref name="src"/>: up to the cap per Titan, nearest first.</summary>
        public static void ComputeShelter(World w, Vec2 src, Dictionary<int, int> shelteredBy, List<Unit> scratch)
        {
            shelteredBy.Clear();
            foreach (var t in w.Units)
            {
                if (t.TitanT <= 0f || !t.IsPlayer || !t.IsTargetable) continue;
                scratch.Clear();
                foreach (var u in w.Units)
                {
                    if (u == t || !u.IsPlayer || !u.IsTargetable || shelteredBy.ContainsKey(u.Id)) continue;
                    if (InShadow(t, src, u.Pos)) scratch.Add(u);
                }
                scratch.Sort((a, b) =>
                {
                    int c = Vec2.DistanceSq(a.Pos, t.Pos).CompareTo(Vec2.DistanceSq(b.Pos, t.Pos));
                    return c != 0 ? c : a.Id.CompareTo(b.Id);
                });
                for (int i = 0; i < scratch.Count && i < Balance.TitanShelterCap; i++) shelteredBy[scratch[i].Id] = t.Id;
            }
        }

        /// <summary>Consistency checks for tests and the balance lab; null when everything holds.</summary>
        public static string CheckInvariants(World w)
        {
            foreach (var u in w.Units)
            {
                if (u.TitanT <= 0f && u.Radius != u.Def.Radius) return $"{u}: radius {u.Radius} without Titan Form";
                if (u.TitanT < 0f || u.TitanT > 1f) return $"{u}: TitanT {u.TitanT} out of range";
                if (u.Life != LifeState.Active && (u.TitanTime != 0f || u.TitanT != 0f)) return $"{u}: {u.Life} but still a Titan";
                if (u.Garrisoned && (u.TitanTime != 0f || u.TitanT != 0f)) return $"{u}: garrisoned as a Titan";
                if ((u.TitanTime > 0f || u.TitanT > 0f) && u.Hp > u.MaxHp + 0.01f) return $"{u}: {u.Hp} hp over max {u.MaxHp}";
                if (u.TitanTime > 0f)
                {
                    var slot = u.Slot(AbilityId.TitanForm);
                    if (slot == null || !slot.Unlocked) return $"{u}: Titan without the unlocked slot";
                }
            }
            return null;
        }
    }
}
