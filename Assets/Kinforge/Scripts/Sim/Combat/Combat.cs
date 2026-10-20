using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>
    /// The damage pipeline (gear, resistances, armour, high ground, weak points), healing and threat,
    /// and the injury / death / kin-core rules.
    /// </summary>
    public static class Combat
    {
        private static readonly List<Unit> Chain = new List<Unit>(8);

        public static Unit OwnerUnit(World w, Entity src)
        {
            if (src is Unit u) return u;
            if (src is Structure s) return w.FindUnitAny(s.OwnerUnitId);
            return null;
        }

        public static float Deal(World w, Entity src, Entity target, float amount, DamageType type, HitFlags flags)
        {
            if (target == null || amount <= 0f) return 0f;
            var tu = target as Unit;
            if (tu != null && tu.Life != LifeState.Active) return 0f;
            if (!target.IsTargetable) return 0f;
            bool environment = (flags & HitFlags.Environment) != 0;
            if (src != null && !environment && src.Faction == target.Faction) return 0f; // no friendly fire on direct attacks

            var srcUnit = src as Unit;
            var srcStruct = src as Structure;
            var owner = OwnerUnit(w, src);

            float mult = 1f;
            if (owner != null && owner.IsPlayer && !environment) mult *= owner.DamageMultiplier;

            if (tu != null && tu.Faction == Faction.Enemy)
            {
                mult *= tu.Def.ResistTo(type);
                if (tu.Kind == UnitKind.Rustmaw && w.Boss != null) mult *= w.Boss.DamageTakenMultiplier(w, src, flags);
            }

            if (type == DamageType.Kinetic) mult *= Balance.ArmorConstant / (Balance.ArmorConstant + target.Armor);

            if ((flags & HitFlags.Ranged) != 0 && src != null)
            {
                if (w.HeightAt(src.Pos) - w.HeightAt(target.Pos) > Balance.HighGroundHeight) mult *= 1f + Balance.HighGroundBonus;
            }

            float dmg = amount * mult;
            if (dmg <= 0f) return 0f;

            if (target is Structure ts)
            {
                ts.Hp -= dmg;
                ts.DamageAbsorbed += dmg;
                var builder = w.FindUnitAny(ts.OwnerUnitId);
                if (builder?.Fight != null && ts.Faction == Faction.Player) builder.Fight.StructureAbsorbed += dmg;
                if (owner?.Fight != null && owner.IsPlayer) owner.Fight.DamageDealt += dmg * 0.5f;
                w.Emit(SimEventType.Hit, src?.Id ?? 0, ts.Id, ts.Pos, dmg, type);
                if (ts.Hp <= 0f) DestroyStructure(w, ts);
                return dmg;
            }

            tu.Hp -= dmg;
            tu.LastHurtTime = w.Time;
            if (owner?.Fight != null && owner.IsPlayer) owner.Fight.DamageDealt += dmg;
            if (tu.Fight != null) tu.Fight.DamageTaken += dmg;

            if (tu.Faction == Faction.Enemy)
            {
                if (src != null && src.Faction == Faction.Player)
                {
                    float tm = srcUnit != null ? srcUnit.Def.ThreatMul : 1f;
                    tu.Threat?.Add(src.Id, dmg * tm);
                    if (w.Bestiary.Discover(tu.Def.Family, type))
                    {
                        float r = tu.Def.ResistTo(type);
                        if (r >= 1.15f) w.Alert($"Bestiary: {tu.Def.Name} is weak to {type}.", 0);
                        else if (r <= 0.85f) w.Alert($"Bestiary: {tu.Def.Name} resists {type}.", 0);
                    }
                }
                EnemyBrain.OnAttacked(w, tu, src);
            }
            else if (tu.IsPlayer)
            {
                if (tu.Action == UnitAction.Resting || tu.Action == UnitAction.Gathering) tu.Action = UnitAction.None;
            }

            if ((flags & HitFlags.DoT) == 0)
            {
                ApplyTypeEffects(w, src, tu, dmg, type, flags);
                w.Emit(SimEventType.Hit, src?.Id ?? 0, tu.Id, tu.Pos, dmg, type);
            }
            if (tu.Hp <= 0f) OnZeroHp(w, tu, src);
            return dmg;
        }

        private static void ApplyTypeEffects(World w, Entity src, Unit t, float dmg, DamageType type, HitFlags flags)
        {
            if (t.Life != LifeState.Active) return;
            switch (type)
            {
                case DamageType.Thermal:
                {
                    float dps = dmg * Balance.BurnFraction;
                    if (dps >= t.Status.BurnDps || t.Status.BurnTime < 1f)
                    {
                        t.Status.BurnDps = Math.Max(t.Status.BurnDps * 0.5f, dps);
                        t.Status.BurnSource = src?.Id ?? 0;
                    }
                    t.Status.BurnTime = Balance.BurnDuration;
                    break;
                }
                case DamageType.Cryo:
                    t.Status.CryoStacks++;
                    t.Status.CryoTime = 4f;
                    if (t.Status.CryoStacks >= Balance.CryoStacksToFreeze)
                    {
                        t.Status.CryoStacks = 0;
                        t.Status.FrozenTime = Balance.FreezeDuration;
                        Interrupt(w, t);
                    }
                    break;
                case DamageType.Arc:
                    if ((flags & HitFlags.NoChain) == 0)
                    {
                        w.QueryUnits(t.Pos, Balance.ArcChainRange, t.Faction, Chain);
                        Chain.Remove(t);
                        Chain.Sort((a, b) => Vec2.DistanceSq(a.Pos, t.Pos).CompareTo(Vec2.DistanceSq(b.Pos, t.Pos)));
                        int n = Math.Min(Balance.ArcChainTargets, Chain.Count);
                        var targets = Chain.GetRange(0, n);
                        foreach (var o in targets)
                        {
                            w.Emit(SimEventType.ArcChain, t.Id, o.Id, o.Pos);
                            Deal(w, src, o, dmg * Balance.ArcChainFraction, DamageType.Arc, flags | HitFlags.NoChain);
                        }
                    }
                    break;
                case DamageType.Corrosive:
                    t.Status.ShredStacks = Math.Min(Balance.MaxShredStacks, t.Status.ShredStacks + 1);
                    t.Status.ShredTime = 6f;
                    break;
            }
        }

        public static float Heal(World w, Unit healer, Unit target, float amount, bool emit = true)
        {
            if (target == null || target.Life != LifeState.Active || target.Removed) return 0f;
            float amt = amount;
            if (healer != null && healer.IsPlayer) amt *= 1f + Balance.MenderTierHeal * (healer.WeaponTier - 1);
            float actual = Math.Min(amt, target.MaxHp - target.Hp);
            if (actual <= 0f) return 0f;
            target.Hp += actual;
            target.LastHealedTime = w.Time;
            if (healer?.Fight != null) healer.Fight.HealingDone += actual;
            if (healer != null && healer.IsPlayer && healer.Squad != null)
            {
                foreach (var e in healer.Squad.Engaged)
                {
                    if (e.Threat != null && e.Life == LifeState.Active) e.Threat.Add(healer.Id, actual * Balance.HealThreatFactor);
                }
            }
            if (emit) w.Emit(SimEventType.Heal, healer?.Id ?? 0, target.Id, target.Pos, actual);
            return actual;
        }

        // ---------------------------------------------------------------------------------------
        // Crowd control helpers
        // ---------------------------------------------------------------------------------------

        public static void Interrupt(World w, Unit u)
        {
            if (u.Action == UnitAction.Casting || u.Action == UnitAction.Channel || u.Action == UnitAction.Windup)
            {
                if (u.Action != UnitAction.Windup) w.Emit(SimEventType.Interrupt, 0, u.Id, u.Pos, 0f, DamageType.Kinetic, u.ActionAbility);
                u.Action = UnitAction.None;
                u.ActionTimer = 0f;
                u.ActionShots = 0;
            }
        }

        public static void Stun(World w, Unit u, float seconds)
        {
            if (u == null || u.Life != LifeState.Active) return;
            if (u.Kind == UnitKind.Rustmaw) seconds *= 0.4f;
            u.Status.StunTime = Math.Max(u.Status.StunTime, seconds);
            Interrupt(w, u);
        }

        public static void Root(World w, Unit u, float seconds)
        {
            if (u == null || u.Life != LifeState.Active) return;
            if (u.Kind == UnitKind.Rustmaw) seconds *= 0.4f;
            u.Status.RootTime = Math.Max(u.Status.RootTime, seconds);
        }

        public static void Knockback(World w, Unit u, Vec2 dir, float dist, float time = 0.3f)
        {
            if (u == null || u.Life != LifeState.Active) return;
            float heavy = u.Def.Mass >= 4f ? u.Def.Mass / 2.5f : 1f;
            dist /= heavy;
            if (dist < 0.2f) return;
            u.ForcedVel = dir.Normalized * (dist / time);
            u.ForcedTime = time;
            if (u.Action == UnitAction.Windup || u.Action == UnitAction.Casting) Interrupt(w, u);
            w.Emit(SimEventType.Knockback, 0, u.Id, u.Pos, dist);
        }

        public static void Taunt(World w, Unit enemy, Unit by, float duration)
        {
            if (enemy?.Threat == null || enemy.Life != LifeState.Active) return;
            float top = enemy.Threat.TopValue();
            float mine = enemy.Threat.Get(by.Id);
            enemy.Threat.Set(by.Id, Math.Max(mine + 10f, top * 1.1f + 10f));
            enemy.Status.TauntTime = duration;
            enemy.Status.TauntBy = by.Id;
            enemy.CurrentTargetId = by.Id;
        }

        // ---------------------------------------------------------------------------------------
        // Injury, death, rebirth
        // ---------------------------------------------------------------------------------------

        private static void OnZeroHp(World w, Unit u, Entity src)
        {
            if (u.IsPlayer)
            {
                if (u.Life != LifeState.Active) return;
                if (!u.Injured) Down(w, u);
                else Kill(w, u);
            }
            else
            {
                EnemyDeath(w, u, src);
            }
        }

        public static void Down(World w, Unit u)
        {
            u.Life = LifeState.Downed;
            u.Hp = 0f;
            u.DownedAt = w.Time;
            ResetForIncapacitated(u);
            if (u.Fight != null) u.Fight.Downs++;
            ForgetTarget(w, u.Id);
            w.Emit(SimEventType.Downed, u.Id, 0, u.Pos);
            CheckWipe(w, u.Squad);
        }

        public static void Kill(World w, Unit u)
        {
            u.Life = LifeState.Dead;
            u.Hp = 0f;
            u.DiedAt = w.Time;
            ResetForIncapacitated(u);
            if (u.Fight != null) u.Fight.Died = true;
            ForgetTarget(w, u.Id);
            LoseUnbanked(w, u);

            // "A squadmate picks up its kin-core automatically." With nobody left standing, the
            // squad-wipe check below gathers every core into one cache instead.
            var carrier = NearestActiveSquadmate(u);
            if (carrier != null || u.Squad == null)
            {
                var cores = new List<int> { u.Id };
                cores.AddRange(u.CarriedCores);
                u.CarriedCores.Clear();
                if (carrier != null) GiveCores(w, carrier, cores);
                else DropCache(w, u.Pos, cores);
            }
            else
            {
                u.Core = CoreState.WithBody;
            }
            w.Emit(SimEventType.Killed, u.Id, carrier?.Id ?? 0, u.Pos);
            w.Alert(carrier != null
                ? $"{u.Name} ({u.Def.Name}) was killed. {carrier.Name} carries the kin-core."
                : $"{u.Name} ({u.Def.Name}) was killed.", u.Pos, 2);
            CheckWipe(w, u.Squad);
        }

        private static void LoseUnbanked(World w, Unit u)
        {
            if (!Balance.DeathLosesUnbankedDoctrine || u.Doctrine == null) return;
            float lost = u.Doctrine.LoseUnbanked();
            if (lost >= 1f) w.Alert($"{u.Name} lost {lost:0} unbanked Doctrine XP.", 1);
        }

        private static void ResetForIncapacitated(Unit u)
        {
            u.Action = UnitAction.None;
            u.ActionTimer = 0f;
            u.Path.Clear();
            u.HasMoveGoal = false;
            u.ForcedTime = 0f;
            u.Velocity = Vec2.Zero;
            u.Status = default;
            u.Intent = default;
        }

        private static Unit NearestActiveSquadmate(Unit u)
        {
            if (u.Squad == null) return null;
            Unit best = null;
            float bestD = float.MaxValue;
            foreach (var m in u.Squad.Members)
            {
                if (m == u || !m.IsActive) continue;
                float d = Vec2.DistanceSq(m.Pos, u.Pos);
                if (d < bestD)
                {
                    bestD = d;
                    best = m;
                }
            }
            return best;
        }

        public static void GiveCores(World w, Unit carrier, List<int> cores)
        {
            foreach (int id in cores)
            {
                var dead = w.FindUnitAny(id);
                if (dead == null) continue;
                dead.Core = CoreState.Carried;
                dead.CoreHolder = carrier.Id;
                if (!carrier.CarriedCores.Contains(id)) carrier.CarriedCores.Add(id);
                w.Emit(SimEventType.CorePickup, carrier.Id, id, carrier.Pos);
            }
        }

        public static CoreCache DropCache(World w, Vec2 pos, List<int> cores)
        {
            if (cores.Count == 0) return null;
            var cache = new CoreCache { Id = w.NextId(), Pos = w.Nav.NearestWalkable(pos), CreatedAt = w.Time };
            foreach (int id in cores)
            {
                var dead = w.FindUnitAny(id);
                if (dead == null) continue;
                dead.Core = CoreState.Cached;
                dead.CoreHolder = cache.Id;
                cache.UnitIds.Add(id);
            }
            w.Caches.Add(cache);
            w.Emit(SimEventType.CacheDropped, cache.Id, 0, cache.Pos, cache.UnitIds.Count);
            return cache;
        }

        /// <summary>A carrier that goes down keeps its cores; a carrier that dies hands them on.</summary>
        private static void CheckWipe(World w, Squad sq)
        {
            if (sq == null || sq.Wiped || sq.Garrisoned) return;
            foreach (var m in sq.Members)
            {
                if (m.Life == LifeState.Active && !m.Garrisoned) return;
            }

            bool anyInField = false;
            Vec2 sum = Vec2.Zero;
            int n = 0;
            foreach (var m in sq.Members)
            {
                if (m.Life == LifeState.Downed || (m.Life == LifeState.Dead && w.Time - m.DiedAt < 1f))
                {
                    anyInField = true;
                    sum += m.Pos;
                    n++;
                }
            }
            if (!anyInField) return;

            sq.Wiped = true;
            var cores = new List<int>();
            foreach (var m in sq.Members)
            {
                if (m.Life == LifeState.Downed)
                {
                    m.Life = LifeState.Dead;
                    m.DiedAt = w.Time;
                    if (m.Fight != null) m.Fight.Died = true;
                    LoseUnbanked(w, m);
                    cores.Add(m.Id);
                }
                else if (m.Life == LifeState.Dead && (m.Core == CoreState.WithBody || m.Core == CoreState.Carried))
                {
                    if (!cores.Contains(m.Id)) cores.Add(m.Id);
                }
                foreach (int c in m.CarriedCores)
                {
                    if (!cores.Contains(c)) cores.Add(c);
                }
                m.CarriedCores.Clear();
            }
            var cache = DropCache(w, n > 0 ? sum / n : sq.Centroid, cores);
            sq.Task = SquadTask.Idle;
            SquadSystem.EndFight(w, sq, true);
            w.Alert($"{sq.Name} was wiped out! Their kin-cores lie in a cache. Send another squad to recover them.", cache?.Pos ?? sq.Anchor, 2);
        }

        public static void Revive(World w, Unit u, float hpFraction, Unit by)
        {
            if (u.Life != LifeState.Downed) return;
            u.Life = LifeState.Active;
            u.Injured = true;
            u.Hp = Math.Max(1f, u.MaxHp * hpFraction);
            u.RestProgress = 0f;
            if (by?.Fight != null && by != u) by.Fight.Stabilizes++;
            w.Emit(SimEventType.Revived, u.Id, by?.Id ?? 0, u.Pos);
        }

        /// <summary>Kin Cradle rebirth: gear, Doctrine and Instinct survive in the kin-core.</summary>
        public static void Regrow(World w, Unit u, Vec2 at)
        {
            u.Life = LifeState.Active;
            u.Injured = false;
            u.Core = CoreState.WithBody;
            u.CoreHolder = 0;
            u.Pos = at;
            u.PrevPos = at;
            u.Hp = u.MaxHp;
            u.Status = default;
            u.Action = UnitAction.None;
            u.Garrisoned = false;
            if (u.Squad != null)
            {
                u.Squad.Wiped = false;
                if (u.Squad.Garrisoned) u.Garrisoned = true;
            }
            w.Emit(SimEventType.Regrown, u.Id, 0, at);
        }

        private static void ForgetTarget(World w, int id)
        {
            foreach (var e in w.Units)
            {
                if (e.Faction != Faction.Enemy) continue;
                e.Threat?.Remove(id);
                if (e.CurrentTargetId == id) e.CurrentTargetId = 0;
                if (e.Intent.TargetId == id) e.Intent = default;
            }
        }

        public static void EnemyDeath(World w, Unit u, Entity killer)
        {
            if (u.Life != LifeState.Active) return;
            u.Life = LifeState.Dead;
            u.DiedAt = w.Time;
            u.Hp = 0f;
            u.Action = UnitAction.None;
            u.Velocity = Vec2.Zero;
            u.Status = default;

            var killerUnit = OwnerUnit(w, killer);
            if (killerUnit != null && killerUnit.IsPlayer)
            {
                killerUnit.Kills++;
                if (killerUnit.Fight != null) killerUnit.Fight.Kills++;
            }
            w.Bestiary.AddKill(u.Def.Family);

            var squad = killerUnit?.Squad;
            if (squad == null)
            {
                Unit near = w.NearestUnit(u.Pos, Faction.Player, 25f);
                squad = near?.Squad;
            }
            if (squad != null) GiveLoot(squad, u.Def.Loot);

            w.Emit(SimEventType.EnemyDied, u.Id, killerUnit?.Id ?? 0, u.Pos);
            if (u.Group != null && u.Group.AllDead && u.Group.ClearedAt < 0f)
            {
                u.Group.ClearedAt = w.Time;
                if (u.Group.IsCamp && !u.Group.IsBossGroup) w.Alert($"{u.Group.Name} cleared.", u.Group.Home, 0);
            }
            if (u.Kind == UnitKind.Rustmaw) w.Boss?.OnDeath(w);
        }

        public static void GiveLoot(Squad squad, LootEntry[] loot)
        {
            foreach (var l in loot)
            {
                float left = l.Amount;
                foreach (var m in squad.Members)
                {
                    if (!m.IsActive || m.Carry == null) continue;
                    float space = m.CarryCapacity - m.CarryLoad;
                    if (space <= 0f) continue;
                    float take = Math.Min(space, left);
                    m.Carry.Add(l.Kind, take);
                    left -= take;
                    if (left <= 0f) break;
                }
            }
        }

        public static void DestroyStructure(World w, Structure s)
        {
            if (s.Removed) return;
            s.Removed = true;
            s.Hp = 0f;
            if (s.Cells != null && (s.BlocksMove || s.BlocksSight)) w.Nav.Stamp(s.Cells, s.BlocksMove, s.BlocksSight, s.Id, -1);
            ForgetTarget(w, s.Id);
            w.Emit(SimEventType.StructureDestroyed, s.Id, 0, s.Pos, 0f);
        }
    }
}
