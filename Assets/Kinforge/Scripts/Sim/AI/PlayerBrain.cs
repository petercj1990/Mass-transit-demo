using System;

namespace Kinforge.Sim
{
    /// <summary>
    /// Utility AI for Vhorn units. Every think, each role scores its options (attack this target,
    /// use that ability, move to that spot) and takes the best. The squad's command reshapes those
    /// scores, Doctrine tier unlocks smarter behaviours, and Instinct numbers fine-tune them.
    /// The player never casts abilities directly.
    /// </summary>
    public static class PlayerBrain
    {
        public static void Think(World w, Unit u)
        {
            if (u.Squad == null) return;
            var c = w.BrainCtx;
            c.Setup(w, u);

            ConsiderDodge(c);

            var sq = c.Sq;
            bool marching = SquadSystem.IsMarching(w, sq);
            if (sq.Task == SquadTask.FallBack) FallBack(c);
            else if (marching) March(c);
            else if (!sq.InCombat || c.Enemies.Count == 0) Peacetime(c);
            else
            {
                switch (u.Role)
                {
                    case Role.Tank: Tank(c); break;
                    case Role.Healer: Mender(c); break;
                    case Role.Melee: Bruiser(c); break;
                    case Role.Siege: Siegewright(c); break;
                    case Role.Warwright: Warwright(c); break;
                }
                // Anyone can fall back to their slot if nothing better comes up.
                c.Offer(30f, BrainContext.MoveTo(c.Slot(u.Role)), c.Debug ? "Hold slot" : null);
            }
            c.Commit();
        }

        // =====================================================================================
        // Shared
        // =====================================================================================

        private static void ConsiderDodge(BrainContext c)
        {
            var u = c.U;
            bool growing = u.Action == UnitAction.Casting && u.ActionAbility == AbilityId.TitanForm;
            foreach (var t in c.W.Telegraphs)
            {
                if (t.Removed || t.Threatens != Faction.Player || t.Remaining <= 0f) continue;
                // A grown Titan may step INTO a charge lane, so this comes before the "am I in it" check.
                if (u.TitanTime > 0f && TitanHolds(c, t)) continue;
                if (!t.Contains(u.Pos, u.Radius)) continue;
                if (growing) continue; // finish growing: the cast is short and the giant can take it
                float reaction = c.P(InstinctParam.Reaction);
                if (t.CoverBlocks && (!c.W.Nav.SightClear(t.Pos, u.Pos) || Titan.Shelters(c.W, t.Pos, u)))
                {
                    // Already behind a wall: stay there until it lands.
                    if (c.W.Time - t.CreatedAt >= reaction * 0.5f)
                        c.Offer(95f, BrainContext.MoveTo(c.U.Pos), c.Debug ? "Hold cover" : null);
                    continue;
                }
                if (c.W.Time - t.CreatedAt < reaction) continue;
                var it = BrainContext.MoveTo(c.SafePoint(t));
                it.Kind = IntentKind.Dodge;
                c.Offer(96f, it, c.Debug ? "Dodge " + t.Label : null);
            }
        }

        private static void March(BrainContext c)
        {
            var u = c.U;
            var sq = c.Sq;
            Vec2 slot = sq.MarchPoint + PlayerUnits.FormationOffset(u.Kind, sq.MarchHeading, 1f);
            slot = c.W.Nav.NearestWalkable(slot, 3);
            if (u.Def.IsRanged && c.Enemies.Count > 0 && sq.Task != SquadTask.FallBack)
            {
                var e = c.NearestEnemy(u.Pos, out float d);
                if (e != null && d <= u.Def.Attack.Range)
                {
                    var it = BrainContext.AttackIntent(e);
                    it.HasStand = true;
                    it.Stand = slot;
                    it.StandTolerance = 1.5f;
                    it.FireOnMove = true;
                    c.Offer(52f, it, c.Debug ? "Fire on the march" : null);
                }
            }
            c.Offer(50f, BrainContext.MoveTo(slot), c.Debug ? "March" : null);
        }

        private static void FallBack(BrainContext c)
        {
            var u = c.U;
            var sq = c.Sq;
            Vec2 heading = sq.MarchHeading;
            Vec2 slot = sq.MarchPoint + PlayerUnits.FormationOffset(u.Kind, heading, 1f);
            if (u.Role == Role.Tank && sq.InCombat) slot = sq.MarchPoint - heading * 3.5f; // rearguard
            c.Offer(60f, BrainContext.MoveTo(c.W.Nav.NearestWalkable(slot, 3)), c.Debug ? "Fall back" : null);
            if (!sq.InCombat || c.Enemies.Count == 0) return;

            Vec2 behind = c.Center + (c.EnemyCenter - c.Center).Normalized * 4f;
            if (u.Role == Role.Warwright && c.Tier >= 1 && c.Ready(AbilityId.RaiseRampart) && !c.OwnWallNear(behind, 3f))
            {
                Vec2 along = (c.EnemyCenter - c.Center).Normalized.Right;
                c.Offer(70f, BrainContext.AbilityAt(AbilityId.RaiseRampart, behind, along), c.Debug ? "Collapse path behind" : null);
            }
            if (u.Role == Role.Siege && c.Tier >= 1 && c.Ready(AbilityId.BoltTurret))
                c.Offer(66f, BrainContext.AbilityAt(AbilityId.BoltTurret, u.Pos, Vec2.Zero), c.Debug ? "Leave a turret behind" : null);
            if (u.Role == Role.Tank && c.Ready(AbilityId.TauntRoar) && c.EnemiesWithin(u.Pos, 7f) > 0)
                c.Offer(64f, new Intent { Kind = IntentKind.Ability, Ability = AbilityId.TauntRoar, Point = c.EnemyCenter }, c.Debug ? "Cover the retreat" : null);
            if (u.Role == Role.Healer)
                OfferHeals(c, 0.6f, 62f);
        }

        /// <summary>Out of combat: hold formation, gather, rest, fortify, top people up.</summary>
        private static void Peacetime(BrainContext c)
        {
            var u = c.U;
            var sq = c.Sq;
            var w = c.W;
            Vec2 slot = sq.MarchPoint + PlayerUnits.FormationOffset(u.Kind, sq.Forward, 1f);

            if (sq.Task == SquadTask.Gather)
            {
                var node = w.FindNode(sq.TaskTargetId);
                if (node != null && u.LoadFraction < 0.999f)
                {
                    float ang = PlayerUnits.RoleIndex(u.Role) * KMath.TwoPi / 5f + node.Id * 0.7f;
                    Vec2 spot = node.Pos + Vec2.FromAngle(ang) * (node.Radius + u.Radius + 0.5f);
                    spot = w.Nav.NearestWalkable(spot, 3);
                    c.Offer(45f, new Intent { Kind = IntentKind.Gather, TargetId = node.Id, Point = spot }, c.Debug ? "Gather " + node.Label : null);
                }
                else if (node != null)
                {
                    c.Offer(40f, BrainContext.MoveTo(w.Nav.NearestWalkable(node.Pos + (u.Pos - node.Pos).Normalized * (node.Radius + 2.5f), 3)), c.Debug ? "Wait, full" : null);
                }
            }
            else if (sq.Stance == CommandType.Rest)
            {
                float ang = PlayerUnits.RoleIndex(u.Role) * KMath.TwoPi / 5f;
                Vec2 spot = w.Nav.NearestWalkable(sq.Anchor + Vec2.FromAngle(ang) * 2.4f, 3);
                c.Offer(45f, new Intent { Kind = IntentKind.Rest, Point = spot }, c.Debug ? "Rest" : null);
                if (u.Role == Role.Warwright && c.Tier >= 1 && c.Ready(AbilityId.RaiseRampart) && !c.OwnWallNear(sq.Anchor, 6f))
                {
                    Vec2 wall = sq.Anchor + sq.Forward * 4f;
                    c.Offer(50f, BrainContext.AbilityAt(AbilityId.RaiseRampart, wall, sq.Forward.Right), c.Debug ? "Low wall round the camp" : null);
                }
            }
            else if (sq.Stance == CommandType.Fortify)
            {
                BuildFort(c, 58f);
                c.Offer(35f, BrainContext.MoveTo(c.Slot(u.Role)), c.Debug ? "Fort slot" : null);
            }
            else
            {
                c.Offer(30f, BrainContext.MoveTo(w.Nav.NearestWalkable(slot, 3)), c.Debug ? "Formation" : null);
            }

            if (u.Role == Role.Healer && c.Ready(AbilityId.MendBeam)) OfferHeals(c, 0.9f, 52f);
        }

        // =====================================================================================
        // Shieldbearer
        // =====================================================================================

        private static void Tank(BrainContext c)
        {
            var u = c.U;
            var w = c.W;
            float peel = c.P(InstinctParam.Peel);
            float leash = c.P(InstinctParam.Leash);
            Vec2 slot = c.Slot(Role.Tank);
            bool anchored = c.Cmd == CommandType.DefensiveFormation || c.Cmd == CommandType.HoldPosition
                            || c.Cmd == CommandType.Fortify || c.Cmd == CommandType.Stack;

            foreach (var e in c.Enemies)
            {
                float s = c.TargetScore(e, DamageType.Kinetic, 8f);
                if (c.TargetsSquadmate(e, out var victim) && victim != u)
                {
                    float p = peel * (victim.Role == Role.Healer ? 1.4f : 1f);
                    if (c.Tier >= 3 && victim.Role == Role.Healer && (c.Cmd == CommandType.DefensiveFormation || c.Cmd == CommandType.Fortify)) p += 15f; // intercept
                    s += p;
                }
                if (e.CurrentTargetId == u.Id) s += 8f;
                if (anchored)
                {
                    float fromSlot = Vec2.Distance(e.Pos, slot) - e.Radius;
                    float limit = c.Cmd == CommandType.HoldPosition ? 2.5f : leash;
                    if (fromSlot > limit + 2f) s -= c.Cmd == CommandType.HoldPosition ? 70f : 25f;
                }
                var it = BrainContext.AttackIntent(e);
                if (c.Cmd == CommandType.Kite && e.CurrentTargetId == u.Id && Vec2.Distance(e.Pos, u.Pos) < 3.5f && u.AttackCooldown > 0.2f)
                {
                    // Drag it in a circle around the squad so the kiters can shoot it.
                    Vec2 around = (u.Pos - c.Center);
                    if (around.LengthSq < 1f) around = -c.Fwd;
                    Vec2 next = c.Center + around.Normalized.Rotate(0.9f) * 6f;
                    c.Offer(56f, BrainContext.MoveTo(w.Nav.NearestWalkable(next, 3)), c.Debug ? "Drag " + e.Def.Name : null);
                }
                c.Offer(s, it, c.Debug ? $"Attack {e.Def.Name}" : null);
            }

            // Taunt Roar
            if (c.Ready(AbilityId.TauntRoar))
            {
                var def = AbilityDefs.Get(AbilityId.TauntRoar);
                int near = 0, loose = 0;
                bool healerHit = false;
                foreach (var e in c.Enemies)
                {
                    if (Vec2.Distance(e.Pos, u.Pos) > def.Radius + e.Radius) continue;
                    near++;
                    if (e.CurrentTargetId != u.Id && e.CurrentTargetId != 0)
                    {
                        loose++;
                        if (c.Healer != null && e.CurrentTargetId == c.Healer.Id) healerHit = true;
                    }
                }
                float score = -1f;
                if (c.Tier <= 1) score = near > 0 ? 72f : -1f;
                else if (c.Tier <= 4) score = (loose >= c.P(InstinctParam.TauntLoose) || near >= 3) ? 74f + loose * 4f : -1f;
                else score = (healerHit || loose >= 2) ? 82f : -1f;
                if (score > 0f) c.Offer(score, new Intent { Kind = IntentKind.Ability, Ability = AbilityId.TauntRoar, Point = c.EnemyCenter }, c.Debug ? $"Taunt Roar ({loose} loose)" : null);
            }

            // Shield Bash
            if (c.Ready(AbilityId.ShieldBash))
            {
                Unit caster = null;
                foreach (var e in c.Enemies)
                {
                    if ((e.Action == UnitAction.Casting || e.Action == UnitAction.Channel) && Vec2.Distance(e.Pos, u.Pos) <= u.ReachTo(e, 1.8f) + 2.5f)
                    {
                        caster = e;
                        break;
                    }
                }
                if (c.Tier >= 3 && caster != null)
                    c.Offer(86f, BrainContext.AbilityOn(AbilityId.ShieldBash, caster), c.Debug ? "Interrupt " + caster.Def.Name : null);
                var cur = w.FindUnit(u.Intent.TargetId);
                if (cur != null && cur.Faction == Faction.Enemy && cur.IsTargetable && Vec2.Distance(cur.Pos, u.Pos) <= u.ReachTo(cur, 1.8f) + 0.3f)
                {
                    bool worthIt = c.Tier < 5 || cur.Kind == UnitKind.Rustmaw || cur.Kind == UnitKind.Rockback;
                    if (worthIt) c.Offer(c.Tier >= 3 ? 58f : 66f, BrainContext.AbilityOn(AbilityId.ShieldBash, cur), c.Debug ? "Shield Bash" : null);
                }
            }

            // Shockwave Slam
            if (c.Ready(AbilityId.ShockwaveSlam))
            {
                var def = AbilityDefs.Get(AbilityId.ShockwaveSlam);
                Vec2 dir = (c.EnemyCenter - u.Pos).Normalized;
                int count = 0;
                bool onHealer = false;
                foreach (var e in c.Enemies)
                {
                    if (!AbilitySystem.InCone(u.Pos, dir, def.Radius + u.Radius + e.Radius, def.ArcDeg, e.Pos)) continue;
                    count++;
                    if (c.Healer != null && e.CurrentTargetId == c.Healer.Id) onHealer = true;
                }
                float score = -1f;
                if (c.Tier <= 1 && count >= 1) score = 64f;
                else if (count >= 2) score = 64f + count * 3f + (c.Tier >= 4 && onHealer ? 10f : 0f);
                if (score > 0f) c.Offer(score, BrainContext.AbilityAt(AbilityId.ShockwaveSlam, u.Pos + dir * 3f, dir), c.Debug ? $"Shockwave ({count})" : null);
            }

            // Rotate back when badly hurt (Defensive Formation tier 5)
            if (c.Tier >= 5 && (c.Cmd == CommandType.DefensiveFormation || c.Cmd == CommandType.Fortify)
                && u.HpFraction < c.P(InstinctParam.Retreat) && c.Melee != null && c.Melee.HpFraction > 0.5f)
            {
                c.Offer(76f, BrainContext.MoveTo(c.Slot(Role.Siege)), c.Debug ? "Rotate to the back" : null);
            }

            if (anchored && Vec2.Distance(u.Pos, slot) > 1.2f) c.Offer(36f, BrainContext.MoveTo(slot), c.Debug ? "Front of the line" : null);

            if (u.Abilities.Length > 3) TitanChoices(c);
        }

        // ---- Titan Form ---------------------------------------------------------------------
        // No Instinct parameters here: Instinct draws from the world RNG, and new draws would shift
        // every fight even for squads that never grow.

        /// <summary>Reads Rustmaw's tells like the Warwright's charge walls: braced postures from tier 3, any command from tier 4.</summary>
        private static bool TitanReads(BrainContext c)
        {
            bool bracing = c.Cmd == CommandType.SpreadOut || c.Cmd == CommandType.DefensiveFormation || c.Cmd == CommandType.HoldPosition
                           || c.Cmd == CommandType.Fortify || c.Cmd == CommandType.Stack;
            return (c.Tier >= 3 && bracing) || c.Tier >= 4;
        }

        /// <summary>Plants itself as cover for the fire wave: Stack and Fortify from tier 3, anything from tier 4 (the cover rule).</summary>
        private static bool TitanShelterReads(BrainContext c) =>
            TitanReads(c) && (c.Cmd == CommandType.Stack || c.Cmd == CommandType.Fortify || c.Tier >= 4);

        private static float TitanReach(BrainContext c) => c.Tier >= 5 ? 5f : (c.Tier >= 4 ? 4f : 2f);

        private static void TitanChoices(BrainContext c)
        {
            var u = c.U;
            var w = c.W;
            var boss = w.Boss;
            if (!w.TitanEnabled || u.TitanTime > 0f || c.Cmd == CommandType.Kite) return; // a slow giant cannot kite
            if (!c.Ready(AbilityId.TitanForm) || !Titan.HasRoom(w, u.Pos)) return;
            bool bossFight = c.Boss != null && boss != null && boss.Active;
            int n8 = c.EnemiesWithin(u.Pos, 8f);
            float score = -1f;
            string why = null;
            if (c.Tier <= 1)
            {
                if (c.EnemiesWithin(u.Pos, 6f) >= 1)
                {
                    score = 71f; // raw instinct: grows at first contact
                    why = "first contact";
                }
            }
            else if (c.Tier == 2)
            {
                if (n8 >= 3 || (bossFight && Vec2.Distance(c.Boss.Pos, u.Pos) < 8f + c.Boss.Radius))
                {
                    score = 73f;
                    why = "big fight";
                }
            }
            else
            {
                bool healerHunted = HealerHunted(c);
                float squadHp = SquadHp(c);
                if (n8 >= 3)
                {
                    score = 72f + 3f * Math.Min(n8 - 3, 4);
                    why = $"pressure {n8}";
                }
                if (healerHunted && score < 78f)
                {
                    score = 78f;
                    why = "wall off the Mender";
                }
                if (squadHp < 0.55f && score < 76f)
                {
                    score = 76f;
                    why = "the kin are hurting";
                }
                if (c.Tier >= 4 && bossFight && !(boss.Phase == 2 && n8 >= 4)) score = -1f; // save it for the tells
                if (c.Tier >= 4 && !bossFight && n8 < 4 && squadHp >= 0.5f && !HasEnemy(c, UnitKind.Rockback)) score = -1f;
                if (bossFight && boss.Burrowed && !healerHunted) score = -1f;
                if (bossFight && TitanReads(c) && BraceForCharge(c))
                {
                    score = 90f;
                    why = "brace for the charge";
                }
                if (bossFight && TitanShelterReads(c) && ShelterSoon(c) && score < 88f)
                {
                    score = 88f;
                    why = "shelter the kin";
                }
                if (c.Tier >= 5 && bossFight && boss.ClashReady(w) && boss.NextChargeIn > 0.5f && boss.NextChargeIn <= 2.5f && score < 84f)
                {
                    score = 84f;
                    why = "before the charge";
                }
                if (c.Tier >= 5 && bossFight && OtherTitanNear(c, 15f)) score = -1f; // stagger the raid's giants
            }
            if (score > 0f)
                c.Offer(score, new Intent { Kind = IntentKind.Ability, Ability = AbilityId.TitanForm, Point = c.EnemyCenter }, c.Debug ? "Titan Form: " + why : null);
        }

        /// <summary>A charge is coming down a lane with kin in it, and this tank could grow and get in front in time.</summary>
        private static bool BraceForCharge(BrainContext c)
        {
            var w = c.W;
            if (w.Boss == null || !w.Boss.ClashReady(w)) return false;
            float grownR = c.U.Def.Radius * Balance.TitanScale;
            foreach (var t in w.Telegraphs)
            {
                if (t.Removed || !t.Catchable || t.Remaining < 0.75f) continue;
                if (!TitanHoldPoint(c, t, grownR, out Vec2 hold)) continue;
                if (Vec2.Distance(c.U.Pos, hold) <= TitanReach(c)) return true;
            }
            return false;
        }

        /// <summary>
        /// Where a Titan stands to stop a charge: in the lane, past the boss's nose and in front of the
        /// farthest squadmate it would hit, and short of any rampart across the lane, so a crash into
        /// the rampart still happens (and the Titan never stands inside a wall).
        /// </summary>
        private static bool TitanHoldPoint(BrainContext c, Telegraph t, float radius, out Vec2 hold)
        {
            var u = c.U;
            var w = c.W;
            hold = u.Pos;
            float victimAlong = -1f;
            foreach (var m in c.Sq.Members)
            {
                if (m == u || !m.IsActive || !t.Contains(m.Pos, m.Radius)) continue;
                float al = Vec2.Dot(m.Pos - t.Pos, t.Dir);
                if (al > victimAlong) victimAlong = al;
            }
            if (victimAlong < 0f) return false;
            var owner = w.FindUnit(t.OwnerId);
            float bossR = owner != null ? owner.Radius : 2.4f;
            float minAlong = bossR + radius + 0.5f;
            float maxAlong = victimAlong - 0.5f;
            float wallAlong = Titan.FirstBlockAlong(w, t.Pos, t.Dir, t.Length, bossR * 0.7f, false, out _);
            if (wallAlong >= 0f) maxAlong = Math.Min(maxAlong, wallAlong - radius - 1f);
            if (maxAlong < minAlong) return false;
            float along = KMath.Clamp(Vec2.Dot(u.Pos - t.Pos, t.Dir), minAlong, maxAlong);
            hold = w.Nav.NearestWalkable(t.Pos + t.Dir * along, 2);
            return w.Nav.LineWalkable(u.Pos, hold, radius, true);
        }

        /// <summary>
        /// While grown, instead of dodging: hold the charge lane in front of the kin (reading the tell),
        /// stand firm when already in it (instinct, any tier), or plant the shield between the boss and
        /// the stacked squad for a fire wave. Returns true if it offered one of those.
        /// </summary>
        private static bool TitanHolds(BrainContext c, Telegraph t)
        {
            var u = c.U;
            var w = c.W;
            var boss = w.Boss;
            if (t.Catchable)
            {
                if (boss == null || u.TitanClashed || u.TitanTime <= 0.3f || !boss.ClashReady(w)) return false;
                if (TitanReads(c) && TitanHoldPoint(c, t, u.Radius, out Vec2 hold))
                {
                    float d = Vec2.Distance(u.Pos, hold);
                    if (d <= TitanReach(c) && d / Math.Max(0.5f, UnitMotor.BaseSpeed(u)) < t.Remaining - 0.2f)
                    {
                        u.TitanBraceUntil = w.Time + BraceHold;
                        c.Offer(97f, BrainContext.MoveTo(hold), c.Debug ? "Hold the line" : null);
                        return true;
                    }
                }
                var owner = w.FindUnit(t.OwnerId);
                float bossR = owner != null ? owner.Radius : 2.4f;
                Vec2 rel = u.Pos - t.Pos;
                float along = Vec2.Dot(rel, t.Dir);
                float lat = MathF.Abs(Vec2.Dot(rel, t.Dir.Right));
                // A rampart further down the lane gives a longer stagger than a clash: step aside and let
                // Rustmaw crash into it, unless it is close enough behind the giant to make an anvil.
                float wallAlong = Titan.FirstBlockAlong(w, t.Pos, t.Dir, t.Length, bossR * 0.7f, true, out _);
                bool spoilsCrash = wallAlong > along && wallAlong - along > u.Radius + 3f;
                if (!spoilsCrash && lat <= 2.5f && along >= bossR + 0.5f && along <= t.Length + u.Radius)
                {
                    u.TitanBraceUntil = w.Time + BraceHold;
                    c.Offer(97f, BrainContext.MoveTo(u.Pos), c.Debug ? "Stand firm" : null);
                    return true;
                }
                return false;
            }
            if (t.CoverBlocks && c.Boss != null && TitanShelterReads(c))
            {
                Vec2 stack = ShelterStack(c);
                if (!w.Nav.SightClear(t.Pos, stack)) return false; // a wall already covers the stack
                Vec2 toBoss = (t.Pos - stack).Normalized;
                if (toBoss == Vec2.Zero) toBoss = c.Fwd;
                Vec2 p = w.Nav.NearestWalkable(stack + toBoss * (u.Radius + 1.6f), 3);
                if (!w.Nav.LineWalkable(u.Pos, p, u.Radius, true)) p = u.Pos;
                c.Offer(97f, BrainContext.MoveTo(p), c.Debug ? "Plant the shield" : null);
                return true;
            }
            return false;
        }

        /// <summary>A brace lasts a little longer than one think, so it holds until the AI next decides.</summary>
        private const float BraceHold = 0.5f;

        private static Vec2 ShelterStack(BrainContext c) => c.Cmd == CommandType.Stack ? c.Anchor : c.Center;

        /// <summary>A fire wave is about to go off and no wall shields where the squad stands.</summary>
        private static bool ShelterSoon(BrainContext c)
        {
            var boss = c.W.Boss;
            if (boss == null || !boss.Overheated || !(boss.BlastPending || boss.NextBlastIn < 2f)) return false;
            return c.W.Nav.SightClear(c.Boss.Pos, ShelterStack(c));
        }

        private static bool HealerHunted(BrainContext c)
        {
            if (c.Healer == null || Vec2.Distance(c.Healer.Pos, c.U.Pos) > 9f) return false;
            foreach (var e in c.Enemies)
            {
                if (e.CurrentTargetId == c.Healer.Id && Vec2.Distance(e.Pos, c.Healer.Pos) < 5f) return true;
            }
            return false;
        }

        private static float SquadHp(BrainContext c)
        {
            float s = 0f;
            int n = 0;
            foreach (var m in c.Sq.Members)
            {
                if (!m.IsActive) continue;
                s += m.HpFraction;
                n++;
            }
            return n > 0 ? s / n : 1f;
        }

        private static bool HasEnemy(BrainContext c, UnitKind k)
        {
            foreach (var e in c.Enemies)
            {
                if (e.Kind == k) return true;
            }
            return false;
        }

        private static bool OtherTitanNear(BrainContext c, float r)
        {
            foreach (var o in c.W.Units)
            {
                if (o != c.U && o.IsPlayer && o.TitanTime > 0f && Vec2.Distance(o.Pos, c.U.Pos) < r) return true;
            }
            return false;
        }

        // =====================================================================================
        // Mender
        // =====================================================================================

        private static void OfferHeals(BrainContext c, float threshold, float baseScore)
        {
            var u = c.U;
            var w = c.W;
            if (!c.Ready(AbilityId.MendBeam)) return;
            float tankBias = c.P(InstinctParam.TankBias);
            foreach (var a in w.Units)
            {
                if (!a.IsActive || !a.IsPlayer) continue;
                if (a.Squad != c.Sq && Vec2.Distance(a.Pos, u.Pos) > 16f) continue;
                float frac = a.Hp / a.MaxHp;
                if (frac >= threshold) continue;
                float s = baseScore + (1f - frac) * 70f;
                if (a.Role == Role.Tank) s += tankBias;
                if (c.Tier >= 2 && w.Time - a.LastHurtTime < 1.5f) s += 6f;
                if (a == u) s += 4f;
                c.Offer(s, BrainContext.AbilityOn(AbilityId.MendBeam, a), c.Debug ? $"Mend {a.Name} ({frac:P0})" : null);
            }
        }

        private static void Mender(BrainContext c)
        {
            var u = c.U;
            var w = c.W;
            OfferHeals(c, c.P(InstinctParam.HealThreshold), 58f);

            if (c.Ready(AbilityId.RegenField))
            {
                Vec2 bestP = Vec2.Zero;
                int bestN = 0;
                foreach (var a in c.Sq.Members)
                {
                    if (!a.IsActive) continue;
                    int n = 0;
                    foreach (var b in c.Sq.Members)
                    {
                        if (b.IsActive && b.HpFraction < 0.85f && Vec2.Distance(a.Pos, b.Pos) <= 4.5f) n++;
                    }
                    if (n > bestN)
                    {
                        bestN = n;
                        bestP = a.Pos;
                    }
                }
                if (c.Cmd == CommandType.Stack) bestP = c.Anchor;
                float score = -1f;
                if (c.Tier <= 1 && bestN >= 1) score = 63f;
                else if (bestN >= c.P(InstinctParam.FieldMin)) score = 66f + bestN * 4f + (c.Cmd == CommandType.Stack ? 8f : 0f);
                if (c.Boss != null && w.Boss != null && w.Boss.BlastPending && c.Tier >= 3) { score = 84f; bestP = c.Cmd == CommandType.Stack ? c.Anchor : c.Center; }
                if (score > 0f) c.Offer(score, BrainContext.AbilityAt(AbilityId.RegenField, bestP, Vec2.Zero), c.Debug ? $"Regen Field ({bestN})" : null);
            }

            if (c.Ready(AbilityId.Stabilize))
            {
                foreach (var a in c.Sq.Members)
                {
                    if (a.Life != LifeState.Downed || Vec2.Distance(a.Pos, u.Pos) > 20f) continue;
                    float s = 82f;
                    if (c.Tier >= 3)
                    {
                        c.NearestEnemy(a.Pos, out float ed);
                        if (ed < 4f) s = 60f;
                    }
                    c.Offer(s, new Intent { Kind = IntentKind.Ability, Ability = AbilityId.Stabilize, TargetId = a.Id, Point = a.Pos }, c.Debug ? "Stabilize " + a.Name : null);
                }
            }

            // Sear Bolt when there's nothing to heal.
            foreach (var e in c.Enemies)
            {
                float s = c.TargetScore(e, DamageType.Thermal, 5f) * 0.45f;
                var it = BrainContext.AttackIntent(e);
                it.HasStand = true;
                it.Stand = MenderStand(c);
                it.StandTolerance = 2.5f;
                c.Offer(s, it, c.Debug ? "Sear Bolt " + e.Def.Name : null);
            }

            // Anything hunting the healer gets dragged back to the tank, not across the map.
            var threat = c.NearestEnemy(u.Pos, out float dist);
            float keep = c.P(InstinctParam.KeepDistance);
            if (threat != null && dist < keep * 0.55f && (threat.CurrentTargetId == u.Id || c.Tier >= 2))
            {
                Vec2 refuge;
                if (c.Tank != null && Vec2.Distance(c.Tank.Pos, u.Pos) > 2.5f)
                {
                    Vec2 behind = (c.Tank.Pos - threat.Pos).Normalized;
                    if (behind == Vec2.Zero) behind = -c.Fwd;
                    refuge = c.W.Nav.NearestWalkable(c.Tank.Pos + behind * 2.2f, 3);
                }
                else refuge = c.RetreatPoint(threat.Pos, 3f);
                c.Offer(70f, BrainContext.MoveTo(refuge), c.Debug ? "Run to the tank" : null);
            }

            Vec2 stand = MenderStand(c);
            if (Vec2.Distance(u.Pos, stand) > 2f) c.Offer(40f, BrainContext.MoveTo(stand), c.Debug ? "Healer position" : null);
        }

        private static Vec2 MenderStand(BrainContext c)
        {
            float keep = c.P(InstinctParam.KeepDistance);
            Vec2 slot = c.Slot(Role.Healer);
            if (c.Cmd == CommandType.Stack || c.Cmd == CommandType.Fortify || c.Cmd == CommandType.SpreadOut) return slot;
            if (c.Cmd == CommandType.Kite || c.Cmd == CommandType.Charge)
            {
                Vec2 refP = c.Tank != null ? c.Tank.Pos : c.Center;
                Vec2 away = (refP - c.EnemyCenter).Normalized;
                if (away == Vec2.Zero) away = -c.Fwd;
                return c.W.Nav.NearestWalkable(refP + away * (keep * 0.6f), 3);
            }
            return slot;
        }

        // =====================================================================================
        // Bruiser
        // =====================================================================================

        private static void Bruiser(BrainContext c)
        {
            var u = c.U;
            var w = c.W;
            float focusLow = c.P(InstinctParam.FocusLow);
            float flank = c.P(InstinctParam.FlankAngle) * KMath.Deg2Rad;
            Vec2 slot = c.Slot(Role.Melee);
            bool anchored = c.Cmd == CommandType.DefensiveFormation || c.Cmd == CommandType.HoldPosition
                            || c.Cmd == CommandType.Fortify || c.Cmd == CommandType.Stack;
            Unit bestTarget = null;
            float bestS = float.MinValue;

            foreach (var e in c.Enemies)
            {
                float s = c.TargetScore(e, DamageType.Kinetic, focusLow);
                if (anchored)
                {
                    Vec2 refP = c.Tank != null ? c.Tank.Pos : c.Anchor;
                    float far = Vec2.Distance(e.Pos, refP);
                    float limit = c.Cmd == CommandType.HoldPosition ? 3.5f : 7f;
                    if (far > limit) s -= c.Cmd == CommandType.HoldPosition ? 60f : 25f;
                }
                if (c.Tier >= 3 && c.TargetsSquadmate(e, out var victim) && victim.Role == Role.Healer) s += 12f;
                if (c.Cmd == CommandType.Kite && e.CurrentTargetId == u.Id && e.Def.Attack.IsMelee) s -= 10f;
                var it = BrainContext.AttackIntent(e);
                if (c.Tier >= 2 && c.Tank != null && c.Cmd != CommandType.HoldPosition && c.Cmd != CommandType.Stack && e.Kind != UnitKind.ScrapHound)
                {
                    Vec2 fromTank = (c.Tank.Pos - e.Pos).Normalized;
                    if (fromTank != Vec2.Zero)
                    {
                        float side = Vec2.Cross(fromTank, u.Pos - e.Pos) >= 0f ? 1f : -1f;
                        Vec2 flankDir = fromTank.Rotate(-side * flank);
                        it.HasStand = true;
                        it.Stand = w.Nav.NearestWalkable(e.Pos + flankDir * u.ReachTo(e, u.Def.Attack.Range * 0.8f), 2);
                        it.StandTolerance = 1.4f;
                    }
                }
                if (e.Kind == UnitKind.Rustmaw && w.Boss != null && w.Boss.VentsExposed && c.Tier >= 3)
                {
                    it.HasStand = true;
                    it.Stand = w.Nav.NearestWalkable(e.Pos - Vec2.FromAngle(e.Facing) * u.ReachTo(e, 1f), 3);
                    it.StandTolerance = 1.5f;
                }
                c.Offer(s, it, c.Debug ? $"Attack {e.Def.Name}" : null);
                if (s > bestS)
                {
                    bestS = s;
                    bestTarget = e;
                }
            }

            var cur = bestTarget;
            if (cur == null) return;
            float d = Vec2.Distance(u.Pos, cur.Pos);
            bool inReach = d <= u.ReachTo(cur, 2.2f) + 0.2f;

            if (c.Ready(AbilityId.Cleave))
            {
                var def = AbilityDefs.Get(AbilityId.Cleave);
                Vec2 dir = (cur.Pos - u.Pos).Normalized;
                int count = 0;
                bool inSpikes = false;
                foreach (var e in c.Enemies)
                {
                    if (AbilitySystem.InCone(u.Pos, dir, u.Radius + def.Range + 0.9f + e.Radius, def.ArcDeg, e.Pos))
                    {
                        count++;
                        if (c.OwnFieldNear(StructureKind.SpikeField, e.Pos, 4f)) inSpikes = true;
                    }
                }
                float score = -1f;
                if (c.Tier <= 1 && inReach) score = 68f;
                else if (count >= c.P(InstinctParam.AoeMin) && inReach) score = 66f + count * 4f + (c.Tier >= 4 && inSpikes ? 8f : 0f);
                if (score > 0f) c.Offer(score, BrainContext.AbilityOn(AbilityId.Cleave, cur), c.Debug ? $"Cleave ({count})" : null);
            }

            if (c.Ready(AbilityId.GutHook))
            {
                float best = -1f;
                Unit hookT = null;
                foreach (var e in c.Enemies)
                {
                    float de = Vec2.Distance(u.Pos, e.Pos);
                    if (de > 9f + e.Radius || de < 3.5f) continue;
                    if (!w.Nav.SightClear(u.Pos, e.Pos)) continue;
                    float s = -1f;
                    if (c.Tier <= 1) s = e == cur ? 64f : -1f;
                    else if (e.Def.Mass >= 4f) s = -1f;
                    else
                    {
                        s = e == cur ? 60f : 50f;
                        if (c.Tier >= 3 && c.TargetsSquadmate(e, out var v) && v.Role == Role.Healer) s = 76f;
                        if (c.Tier >= 4 && e.Def.IsRanged) s = Math.Max(s, 70f);
                    }
                    if (s > best)
                    {
                        best = s;
                        hookT = e;
                    }
                }
                if (hookT != null && best > 0f) c.Offer(best, BrainContext.AbilityOn(AbilityId.GutHook, hookT), c.Debug ? "Gut Hook " + hookT.Def.Name : null);
            }

            if (c.Ready(AbilityId.CorrodeEdge) && inReach)
            {
                bool armoured = cur.Armor >= 40f || w.Bestiary.KnownMultiplier(cur.Def.Family, DamageType.Corrosive, cur.Def) > 1.1f;
                float score = c.Tier <= 2 ? 65f : (armoured ? 74f : 50f);
                c.Offer(score, BrainContext.AbilityOn(AbilityId.CorrodeEdge, cur), c.Debug ? "Corrode Edge" : null);
            }

            if (c.Tier >= 2 && u.HpFraction < c.P(InstinctParam.Retreat))
            {
                var threat = c.NearestEnemy(u.Pos, out float td);
                if (threat != null && threat.CurrentTargetId == u.Id && td < 4f)
                {
                    Vec2 to = c.Healer != null ? c.Healer.Pos : c.Slot(Role.Siege);
                    c.Offer(72f, BrainContext.MoveTo(to), c.Debug ? "Back off to the healer" : null);
                }
            }

            if (anchored && Vec2.Distance(u.Pos, slot) > 1.5f) c.Offer(34f, BrainContext.MoveTo(slot), c.Debug ? "Flank slot" : null);
        }

        // =====================================================================================
        // Siegewright
        // =====================================================================================

        private static void Siegewright(BrainContext c)
        {
            var u = c.U;
            var w = c.W;
            float keep = c.P(InstinctParam.KeepDistance);
            float focusLow = c.P(InstinctParam.FocusLow);
            Vec2 slot = c.Slot(Role.Siege);
            Unit bestTarget = null;
            float bestS = float.MinValue;
            float range = u.Def.Attack.Range;

            foreach (var e in c.Enemies)
            {
                float s = c.TargetScore(e, DamageType.Kinetic, focusLow) + Vec2.Distance(u.Pos, e.Pos) * 0.8f; // range matters less
                if (!w.Nav.SightClear(u.Pos, e.Pos)) s -= 10f;
                var it = BrainContext.AttackIntent(e);
                it.HasStand = true;
                it.StandTolerance = 2.5f;
                it.Stand = SiegeStand(c, e, slot, keep, range, out bool moving);
                it.FireOnMove = moving;
                c.Offer(s, it, c.Debug ? $"Shoot {e.Def.Name}" : null);
                if (s > bestS)
                {
                    bestS = s;
                    bestTarget = e;
                }
            }

            var cur = bestTarget;
            if (cur != null && c.Ready(AbilityId.BurstVolley))
            {
                float d = Vec2.Distance(u.Pos, cur.Pos);
                if (d <= range + cur.Radius && w.Nav.SightClear(u.Pos, cur.Pos))
                {
                    bool setUp = cur.Status.SlowPct > 0f || cur.Status.RootTime > 0f || cur.Status.FrozenTime > 0f
                                 || cur.Status.ShredStacks > 0 || cur.Status.StunTime > 0f;
                    bool bigMoment = cur.HpFraction < 0.35f || cur.Kind == UnitKind.ScrapMender
                                     || (cur.Kind == UnitKind.Rustmaw && w.Boss != null && (w.Boss.VentsExposed || w.Boss.Overheated || w.Boss.Staggered));
                    float score;
                    if (c.Tier <= 1) score = 70f;
                    else if (c.Tier <= 3) score = 68f + (cur.HpFraction < 0.4f ? 6f : 0f);
                    else if (c.Tier == 4) score = 66f + (setUp ? 14f : 0f) + (bigMoment ? 8f : 0f);
                    else score = bigMoment ? 84f : 70f - c.P(InstinctParam.HoldBurst) * 30f + (setUp ? 10f : 0f);
                    // Focus Fire tier 5: line up bursts with the Bruiser.
                    if (c.Tier >= 5 && c.Cmd == CommandType.FocusFire && c.Melee != null && !c.Melee.AbilityReady(AbilityId.Cleave) && !bigMoment) score -= 8f;
                    c.Offer(score, BrainContext.AbilityOn(AbilityId.BurstVolley, cur), c.Debug ? "Burst Volley" : null);
                }
            }

            if (c.Ready(AbilityId.BoltTurret))
            {
                Vec2 p = TurretSpot(c, cur, slot);
                int nearby = c.EnemiesWithin(p, 16f);
                float score = c.Tier <= 1 ? 64f : (nearby > 0 ? 66f : -1f);
                if (score > 0f) c.Offer(score, BrainContext.AbilityAt(AbilityId.BoltTurret, p, Vec2.Zero), c.Debug ? "Bolt Turret" : null);
            }

            if (c.Ready(AbilityId.MortarPit))
            {
                int bestCluster = 0;
                foreach (var e in c.Enemies)
                {
                    int n = c.EnemiesWithin(e.Pos, 3f);
                    if (n > bestCluster) bestCluster = n;
                }
                Vec2 p = c.W.Nav.NearestWalkable(c.Anchor - c.Fwd * 2.5f, 3);
                if (c.Cmd == CommandType.Kite || c.Cmd == CommandType.Charge) p = u.Pos;
                float score = c.Tier <= 2 ? 63f : (bestCluster >= 3 || c.Boss != null ? 70f : 52f);
                c.Offer(score, BrainContext.AbilityAt(AbilityId.MortarPit, p, Vec2.Zero), c.Debug ? $"Mortar Pit (cluster {bestCluster})" : null);
            }

            var threat = c.NearestEnemy(u.Pos, out float td2);
            if (threat != null && threat.Def.Attack.IsMelee && td2 < 3.5f && (c.Tier >= 2 || c.Cmd == CommandType.Kite))
                c.Offer(66f, BrainContext.MoveTo(c.RetreatPoint(threat.Pos, 5f)), c.Debug ? "Get clear" : null);
        }

        private static Vec2 SiegeStand(BrainContext c, Unit e, Vec2 slot, float keep, float range, out bool moving)
        {
            var w = c.W;
            var u = c.U;
            moving = false;
            Vec2 stand;
            switch (c.Cmd)
            {
                case CommandType.Kite:
                {
                    var threat = c.NearestEnemy(u.Pos, out float d);
                    if (threat != null && d < keep)
                    {
                        moving = true;
                        return c.RetreatPoint(threat.Pos, keep - d + 2f);
                    }
                    stand = u.Pos;
                    break;
                }
                case CommandType.Charge:
                case CommandType.FocusFire:
                    stand = e.Pos + (u.Pos - e.Pos).Normalized * Math.Min(keep, range - 2f);
                    break;
                default:
                {
                    float dSlot = Vec2.Distance(slot, e.Pos);
                    stand = dSlot <= range ? slot : slot + (e.Pos - slot).Normalized * (dSlot - range + 2f);
                    break;
                }
            }
            if (c.Tier >= 4) stand = c.HighGroundNear(stand, e, 4f);
            if (!w.Nav.SightClear(stand, e.Pos) && c.Tier >= 2)
            {
                // Step sideways to find a firing lane past cover.
                for (int i = 1; i <= 3; i++)
                {
                    Vec2 side = (e.Pos - stand).Normalized.Right * (i * 2f);
                    if (w.Nav.SightClear(stand + side, e.Pos) && !w.Nav.BlocksMoveAt(stand + side)) return stand + side;
                    if (w.Nav.SightClear(stand - side, e.Pos) && !w.Nav.BlocksMoveAt(stand - side)) return stand - side;
                }
            }
            return w.Nav.NearestWalkable(stand, 3);
        }

        private static Vec2 TurretSpot(BrainContext c, Unit target, Vec2 slot)
        {
            var u = c.U;
            float fwd = c.P(InstinctParam.TurretForward);
            Vec2 p;
            if (c.Tier == 0) p = u.Pos + c.Fwd * 1.5f;
            else switch (c.Cmd)
            {
                case CommandType.DefensiveFormation:
                case CommandType.Fortify:
                    p = slot + c.Fwd * (1.5f + fwd);
                    break;
                case CommandType.Charge:
                    p = target != null ? u.Pos + (target.Pos - u.Pos).Normalized * (3f + fwd) : u.Pos;
                    break;
                case CommandType.HoldPosition:
                    p = c.Anchor - c.Fwd * (2f - fwd);
                    break;
                case CommandType.FocusFire:
                    p = c.Focus != null ? u.Pos + (c.Focus.Pos - u.Pos).Normalized * (3f + fwd) : u.Pos;
                    break;
                case CommandType.SpreadOut:
                    p = u.Pos + c.Rt * ((c.W.CountStructures(c.Sq, StructureKind.BoltTurret) % 2 == 0 ? 4f : -4f));
                    break;
                case CommandType.Stack:
                    p = c.Anchor;
                    break;
                default:
                    p = u.Pos + c.Fwd * fwd;
                    break;
            }
            return c.W.Nav.NearestWalkable(p, 3);
        }

        // =====================================================================================
        // Warwright
        // =====================================================================================

        private static void Warwright(BrainContext c)
        {
            var u = c.U;
            var w = c.W;
            Vec2 slot = c.Slot(Role.Warwright);

            foreach (var e in c.Enemies)
            {
                float s = c.TargetScore(e, DamageType.Kinetic, 6f) - 12f;
                if (Vec2.Distance(e.Pos, slot) > 5f) s -= 30f;
                c.Offer(s, BrainContext.AttackIntent(e), c.Debug ? $"Hammer {e.Def.Name}" : null);
            }

            if (c.Ready(AbilityId.RaiseRampart)) Ramparts(c);
            if (c.Ready(AbilityId.SpikeField)) Spikes(c);
            if (c.Ready(AbilityId.SnareCage)) Cage(c);

            if (c.Tier >= 2 && u.HpFraction < c.P(InstinctParam.Retreat))
            {
                var threat = c.NearestEnemy(u.Pos, out float td);
                if (threat != null && threat.CurrentTargetId == u.Id && td < 4f)
                    c.Offer(68f, BrainContext.MoveTo(c.Healer != null ? c.Healer.Pos : c.Slot(Role.Siege)), c.Debug ? "Back off" : null);
            }
            if (Vec2.Distance(u.Pos, slot) > 1.5f) c.Offer(34f, BrainContext.MoveTo(slot), c.Debug ? "Engineer slot" : null);
        }

        private static bool Shielded(BrainContext c, Vec2 spot, Vec2 from) => !c.W.Nav.SightClear(from, spot);

        private static void OfferWall(BrainContext c, Vec2 p, Vec2 along, float score, string why)
        {
            if (c.OwnWallNear(p, 2.5f)) return;
            p = c.W.Nav.NearestWalkable(p, 3);
            c.Offer(score, BrainContext.AbilityAt(AbilityId.RaiseRampart, p, along), c.Debug ? "Rampart: " + why : null);
        }

        private static void Ramparts(BrainContext c)
        {
            var w = c.W;
            var u = c.U;
            float wallDist = c.P(InstinctParam.WallDistance);

            // Boss tells: wall across a charge line, or between the boss and the stack before a fire wave.
            // Engineers do this in the defensive postures from tier 2, and in any command once veterans.
            bool bracing = c.Cmd == CommandType.SpreadOut || c.Cmd == CommandType.DefensiveFormation || c.Cmd == CommandType.HoldPosition
                           || c.Cmd == CommandType.Fortify || c.Cmd == CommandType.Stack;
            if ((c.Tier >= 2 && bracing) || c.Tier >= 4)
            {
                foreach (var t in w.Telegraphs)
                {
                    if (t.Removed || t.Remaining < 0.7f || t.Threatens != Faction.Player) continue;
                    if (t.Shape == TelegraphShape.Line && t.Length > 8f)
                    {
                        Unit victim = null;
                        float best = float.MaxValue;
                        foreach (var m in c.Sq.Members)
                        {
                            if (!m.IsActive || !t.Contains(m.Pos, m.Radius)) continue;
                            float along = Vec2.Dot(m.Pos - t.Pos, t.Dir);
                            if (along < best)
                            {
                                best = along;
                                victim = m;
                            }
                        }
                        float at = victim != null ? Math.Max(4f, best - 3f) : Math.Min(t.Length * 0.6f, 12f);
                        Vec2 p = t.Pos + t.Dir * at;
                        if (Vec2.Distance(u.Pos, p) < 13f) OfferWall(c, p, t.Dir.Right, 92f, "block the charge");
                    }
                    else if (t.CoverBlocks)
                    {
                        Vec2 stack = c.Cmd == CommandType.Stack ? c.Anchor : c.Center;
                        Vec2 p = stack + (t.Pos - stack).Normalized * 2.4f;
                        if (Vec2.Distance(u.Pos, p) < 13f && !Shielded(c, stack, t.Pos)) OfferWall(c, p, (t.Pos - stack).Normalized.Right, 90f, "cover from the blast");
                    }
                }
            }

            // Shield the soft back line from ranged fire.
            if (c.Tier >= 2)
            {
                Unit shooter = null;
                float sd = float.MaxValue;
                foreach (var e in c.Enemies)
                {
                    if (!e.Def.IsRanged) continue;
                    float d = Vec2.Distance(e.Pos, c.Center);
                    if (d < sd)
                    {
                        sd = d;
                        shooter = e;
                    }
                }
                if (shooter != null)
                {
                    var victim = w.FindUnit(shooter.CurrentTargetId);
                    Unit protect = victim != null && victim.Squad == c.Sq && victim.Role != Role.Tank ? victim : (c.Healer ?? c.Siege);
                    if (protect != null)
                    {
                        Vec2 dir = (shooter.Pos - protect.Pos).Normalized;
                        OfferWall(c, protect.Pos + dir * wallDist, dir.Right, 70f, "cover from fire");
                    }
                }
            }

            if (c.Tier == 0)
            {
                Vec2 dir = (c.EnemyCenter - u.Pos).Normalized;
                OfferWall(c, u.Pos + dir * 1.8f, dir.Right, 62f, "cover where it stands");
                return;
            }

            Vec2 toEnemy = (c.EnemyCenter - c.Center).Normalized;
            if (toEnemy == Vec2.Zero) toEnemy = c.Fwd;
            switch (c.Cmd)
            {
                case CommandType.DefensiveFormation:
                {
                    float side = Vec2.Dot(c.EnemyCenter - c.Anchor, c.Rt) >= 0f ? 1f : -1f;
                    OfferWall(c, c.Anchor + c.Rt * (4.5f * side) + c.Fwd * 0.5f, c.Fwd, 64f, "flank");
                    OfferWall(c, c.Anchor - c.Rt * (4.5f * side) + c.Fwd * 0.5f, c.Fwd, 60f, "other flank");
                    break;
                }
                case CommandType.Charge:
                {
                    var t = c.Focus ?? (c.Tank != null ? w.FindUnit(c.Tank.Intent.TargetId) : null);
                    if (t != null && t.Faction == Faction.Enemy)
                    {
                        Vec2 away = (t.Pos - c.Center).Normalized;
                        OfferWall(c, t.Pos + away * 3.5f, away.Right, 64f, "seal its escape");
                    }
                    break;
                }
                case CommandType.HoldPosition:
                    OfferWall(c, c.Anchor + c.Fwd * (3f + wallDist * 0.5f), c.Rt, 62f, "wall in front");
                    break;
                case CommandType.SpreadOut:
                    foreach (var m in c.Sq.Members)
                    {
                        if (!m.IsActive || m.Role == Role.Tank) continue;
                        Vec2 dir = (c.EnemyCenter - m.Pos).Normalized;
                        Vec2 p = m.Pos + dir * 2f;
                        if (!c.OwnWallNear(p, 4f))
                        {
                            OfferWall(c, p, dir.Right, 62f, "cover for " + m.Name);
                            break;
                        }
                    }
                    break;
                case CommandType.Stack:
                    if (c.Boss != null && !c.Boss.Untargetable)
                    {
                        // One solid wall between the stack and the boss beats a ring.
                        Vec2 toBoss = (c.Boss.Pos - c.Anchor).Normalized;
                        if (!Shielded(c, c.Anchor, c.Boss.Pos)) OfferWall(c, c.Anchor + toBoss * 2.4f, toBoss.Right, 80f, "shield the stack");
                        break;
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        Vec2 dir = toEnemy.Rotate((i - 1) * 0.9f);
                        OfferWall(c, c.Anchor + dir * 2.6f, dir.Right, 64f - i, "ring wall");
                    }
                    break;
                case CommandType.Fortify:
                    BuildFort(c, 64f);
                    break;
            }
        }

        /// <summary>Field fort: a front wall and two side walls with the back left open as a gate.</summary>
        private static void BuildFort(BrainContext c, float score)
        {
            var u = c.U;
            Vec2 a = c.Anchor;
            Vec2 f = c.Fwd;
            Vec2 r = c.Rt;
            if (u.Role == Role.Warwright && c.Ready(AbilityId.RaiseRampart))
            {
                OfferWall(c, a + f * 4f, r, score + 2f, "fort front");
                OfferWall(c, a + r * 4f + f * 1.2f, f, score + 1f, "fort side");
                OfferWall(c, a - r * 4f + f * 1.2f, f, score, "fort side");
            }
            if (u.Role == Role.Siege && c.Ready(AbilityId.BoltTurret) && c.W.CountStructures(c.Sq, StructureKind.BoltTurret) < Balance.MaxTurretsPerSquad)
            {
                Vec2 p = a + f * 2f + r * (c.W.CountStructures(c.Sq, StructureKind.BoltTurret) == 0 ? 1.5f : -1.5f);
                c.Offer(score - 2f, BrainContext.AbilityAt(AbilityId.BoltTurret, c.W.Nav.NearestWalkable(p, 2), Vec2.Zero), c.Debug ? "Fort turret" : null);
            }
        }

        private static void Spikes(BrainContext c)
        {
            var u = c.U;
            float aoeMin = c.P(InstinctParam.AoeMin);
            float lead = c.P(InstinctParam.TrapLead);
            Vec2 p;
            float baseScore = 63f;
            switch (c.Cmd)
            {
                case CommandType.DefensiveFormation:
                case CommandType.HoldPosition:
                case CommandType.Fortify:
                    p = c.Anchor + c.Fwd * (c.Cmd == CommandType.HoldPosition ? 5f : 4.5f);
                    baseScore = 65f;
                    break;
                case CommandType.Kite:
                case CommandType.FallBack:
                    p = c.Center + (c.EnemyCenter - c.Center).Normalized * 3f;
                    baseScore = 66f;
                    break;
                case CommandType.Charge:
                case CommandType.FocusFire:
                    p = c.Focus != null ? c.Focus.Pos : c.EnemyCenter;
                    break;
                default:
                    p = c.EnemyCenter;
                    break;
            }
            if (c.Tier <= 1) p = c.EnemyCenter;
            if (c.Tier >= 4)
            {
                // Predict where the pack is running.
                Vec2 vel = Vec2.Zero;
                foreach (var e in c.Enemies) vel += e.Velocity;
                if (c.Enemies.Count > 0) p += vel / c.Enemies.Count * lead;
                baseScore += 4f;
            }
            if (Vec2.Distance(u.Pos, p) > 14f) return;
            int n = c.EnemiesWithin(p, 6f);
            bool ok = c.Tier <= 1 ? c.EnemiesWithin(u.Pos, 12f) > 0 : n >= aoeMin;
            if (c.Cmd == CommandType.Kite || c.Cmd == CommandType.FallBack) ok = c.EnemiesWithin(c.Center, 10f) > 0;
            if (!ok || c.OwnFieldNear(StructureKind.SpikeField, p, 3f)) return;
            c.Offer(baseScore, BrainContext.AbilityAt(AbilityId.SpikeField, c.W.Nav.NearestWalkable(p, 2), Vec2.Zero), c.Debug ? $"Spike Field ({n})" : null);
        }

        private static void Cage(BrainContext c)
        {
            var u = c.U;
            float best = -1f;
            Unit target = null;
            foreach (var e in c.Enemies)
            {
                if (Vec2.Distance(u.Pos, e.Pos) > 14f || e.Status.RootTime > 0.5f) continue;
                float s = -1f;
                if (c.Tier <= 1)
                {
                    if (c.Tank != null && c.Tank.Intent.TargetId == e.Id) s = 62f;
                }
                else
                {
                    if (c.Focus != null && e == c.Focus && (c.Cmd == CommandType.FocusFire || c.Cmd == CommandType.Charge)) s = 76f;
                    if (c.Tier >= 3)
                    {
                        if (e.Kind == UnitKind.ScrapMender) s = Math.Max(s, 80f);
                        if (c.TargetsSquadmate(e, out var v) && v.Role == Role.Healer) s = Math.Max(s, 74f);
                        if (Vec2.Dot(e.Velocity, e.Pos - c.Center) > 2f && e.HpFraction < 0.5f) s = Math.Max(s, 66f);
                    }
                    if (s < 0f && c.Tank != null && c.Tank.Intent.TargetId == e.Id) s = 58f;
                }
                if (s > best)
                {
                    best = s;
                    target = e;
                }
            }
            if (target != null && best > 0f) c.Offer(best, BrainContext.AbilityOn(AbilityId.SnareCage, target), c.Debug ? "Snare Cage " + target.Def.Name : null);
        }
    }
}
