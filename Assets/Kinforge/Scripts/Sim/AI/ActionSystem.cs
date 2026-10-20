using System;

namespace Kinforge.Sim
{
    /// <summary>
    /// Turns each unit's chosen intent into motion and actions: wind-ups, casts, channels,
    /// gathering and resting. Attacks resolve at the end of the wind-up, so a target that moves
    /// out of reach in time makes the swing miss.
    /// </summary>
    public static class ActionSystem
    {
        public static void Update(World w)
        {
            float dt = Balance.Dt;
            for (int i = 0; i < w.Units.Count; i++)
            {
                var u = w.Units[i];
                if (!u.IsActive) continue;
                if (u.Status.Disabled)
                {
                    u.HasMoveGoal = false;
                    continue;
                }

                switch (u.Action)
                {
                    case UnitAction.Windup:
                        FaceTarget(w, u, dt);
                        u.ActionTimer -= dt;
                        if (u.ActionTimer <= 0f) ResolveAttack(w, u);
                        break;
                    case UnitAction.Casting:
                        FaceTarget(w, u, dt);
                        u.ActionTimer -= dt;
                        if (u.ActionTimer <= 0f) FinishCast(w, u);
                        break;
                    case UnitAction.Channel:
                        FaceTarget(w, u, dt);
                        UpdateChannel(w, u, dt);
                        break;
                    case UnitAction.Gathering:
                        UpdateGather(w, u, dt);
                        break;
                }

                if (u.IsActive) ExecuteIntent(w, u);
            }
        }

        private static void FaceTarget(World w, Unit u, float dt)
        {
            Vec2 at;
            var t = w.FindEntity(u.ActionTargetId);
            if (t != null) at = t.Pos;
            else if (u.ActionPoint != Vec2.Zero) at = u.ActionPoint;
            else return;
            Vec2 d = at - u.Pos;
            if (d.LengthSq < 0.01f) return;
            u.Facing = KMath.RotateTowards(u.Facing, d.ToAngle(), UnitMotor.TurnRate(u) * dt);
        }

        // -------------------------------------------------------------------------------------
        // Intent execution
        // -------------------------------------------------------------------------------------

        private static void ExecuteIntent(World w, Unit u)
        {
            ref var it = ref u.Intent;
            u.HasMoveGoal = false;
            u.MoveSpeedMul = 1f;
            u.MoveArriveDist = 0.35f;

            switch (it.Kind)
            {
                case IntentKind.Idle:
                    break;

                case IntentKind.Move:
                case IntentKind.Dodge:
                    if (u.Action == UnitAction.Resting || u.Action == UnitAction.Gathering) u.Action = UnitAction.None;
                    // Getting out of a telegraph (or away from a hunter) beats finishing a heal.
                    if (u.Action == UnitAction.Channel && (it.Kind == IntentKind.Dodge || u.ActionAbility == AbilityId.MendBeam || u.ActionAbility == AbilityId.WeldBeam))
                        u.Action = UnitAction.None;
                    else if (it.Kind == IntentKind.Dodge && (u.Action == UnitAction.Windup || u.Action == UnitAction.Casting))
                        u.Action = UnitAction.None;
                    u.HasMoveGoal = true;
                    u.MoveGoal = it.Point;
                    if (it.Kind == IntentKind.Dodge) u.MoveSpeedMul = 1.15f;
                    if (u.IsPlayer && u.Squad != null && u.Squad.Stance == CommandType.Charge && u.Squad.InCombat) u.MoveSpeedMul = 1.15f;
                    break;

                case IntentKind.Attack:
                    ExecuteAttack(w, u, ref it);
                    break;

                case IntentKind.Ability:
                    ExecuteAbility(w, u, ref it);
                    break;

                case IntentKind.Gather:
                {
                    var node = w.FindNode(it.TargetId);
                    if (node == null || node.Amount <= 0f || u.LoadFraction >= 0.999f)
                    {
                        if (u.Action == UnitAction.Gathering) u.Action = UnitAction.None;
                        break;
                    }
                    u.GatherNodeId = node.Id;
                    float edge = Vec2.Distance(u.Pos, node.Pos) - node.Radius - u.Radius;
                    if (edge <= 1.3f)
                    {
                        if (!u.IsBusy && u.Action != UnitAction.Gathering) u.Action = UnitAction.Gathering;
                        Vec2 d = node.Pos - u.Pos;
                        if (d.LengthSq > 0.01f) u.Facing = d.ToAngle();
                    }
                    else
                    {
                        if (u.Action == UnitAction.Gathering) u.Action = UnitAction.None;
                        u.HasMoveGoal = true;
                        u.MoveGoal = it.Point;
                    }
                    break;
                }

                case IntentKind.Rest:
                    if (Vec2.Distance(u.Pos, it.Point) > 1.2f)
                    {
                        if (u.Action == UnitAction.Resting) u.Action = UnitAction.None;
                        u.HasMoveGoal = true;
                        u.MoveGoal = it.Point;
                    }
                    else if (!u.IsBusy && u.Action != UnitAction.Resting)
                    {
                        u.Action = UnitAction.Resting;
                    }
                    break;
            }
        }

        public static float AttackReach(Unit u, Entity target)
        {
            var ad = u.Def.Attack;
            return ad.IsMelee ? u.ReachTo(target, ad.Range) : ad.Range + (target?.Radius ?? 0f);
        }

        private static void ExecuteAttack(World w, Unit u, ref Intent it)
        {
            var target = w.FindEntity(it.TargetId);
            if (target == null || !target.IsTargetable)
            {
                it.Kind = IntentKind.Idle;
                return;
            }
            if (u.Action == UnitAction.Resting || u.Action == UnitAction.Gathering) u.Action = UnitAction.None;
            var ad = u.Def.Attack;
            float reach = AttackReach(u, target);
            float d = Vec2.Distance(u.Pos, target.Pos);
            bool los = ad.IsMelee || w.Nav.SightClear(u.Pos, target.Pos);
            bool inRange = d <= reach && los;
            bool standOk = !it.HasStand || Vec2.Distance(u.Pos, it.Stand) <= it.StandTolerance;

            if (!u.IsBusy && u.AttackCooldown <= 0f && inRange && (standOk || it.FireOnMove || ad.IsMelee))
            {
                StartAttack(w, u, target);
            }

            if (it.HasStand && !standOk)
            {
                u.HasMoveGoal = true;
                u.MoveGoal = it.Stand;
            }
            else if (!inRange)
            {
                u.HasMoveGoal = true;
                if (!los && d <= reach)
                {
                    // In range but blocked: walk around toward the target until a lane opens.
                    u.MoveGoal = target.Pos;
                    u.MoveArriveDist = Math.Max(0.4f, d - 2f);
                }
                else
                {
                    u.MoveGoal = target.Pos;
                    u.MoveArriveDist = ad.IsMelee ? Math.Max(0.2f, reach * 0.85f) : reach * 0.85f;
                }
            }
        }

        private static void ExecuteAbility(World w, Unit u, ref Intent it)
        {
            var slot = u.Slot(it.Ability);
            if (slot == null || !slot.Unlocked)
            {
                it.Kind = IntentKind.Idle;
                return;
            }
            if (u.IsBusy) return;
            if (!slot.Ready)
            {
                it.Kind = IntentKind.Idle;
                return;
            }
            if (u.Action == UnitAction.Resting || u.Action == UnitAction.Gathering) u.Action = UnitAction.None;
            var def = slot.Def;
            switch (def.Target)
            {
                case AbilityTarget.Self:
                    StartAbility(w, u, def.Id, 0, u.Pos, it.Point - u.Pos);
                    break;
                case AbilityTarget.Ground:
                {
                    float d = Vec2.Distance(u.Pos, it.Point);
                    if (d <= def.Range) StartAbility(w, u, def.Id, it.TargetId, it.Point, it.Dir);
                    else
                    {
                        u.HasMoveGoal = true;
                        u.MoveGoal = it.Point;
                        u.MoveArriveDist = def.Range * 0.8f;
                    }
                    break;
                }
                default:
                {
                    var target = w.FindEntity(it.TargetId) as Unit;
                    bool valid = target != null && !target.Removed && (def.Target == AbilityTarget.DownedAlly
                        ? target.Life == LifeState.Downed
                        : target.Life == LifeState.Active && !target.Untargetable);
                    if (!valid)
                    {
                        it.Kind = IntentKind.Idle;
                        return;
                    }
                    float reach = def.MeleeReach ? u.ReachTo(target, def.Range) : def.Range + target.Radius;
                    float d = Vec2.Distance(u.Pos, target.Pos);
                    bool needsLos = !def.MeleeReach && def.Id != AbilityId.TauntRoar;
                    bool los = !needsLos || w.Nav.SightClear(u.Pos, target.Pos);
                    if (d <= reach && los) StartAbility(w, u, def.Id, target.Id, target.Pos, target.Pos - u.Pos);
                    else
                    {
                        u.HasMoveGoal = true;
                        u.MoveGoal = target.Pos;
                        u.MoveArriveDist = los ? reach * 0.8f : Math.Max(0.4f, d - 2f);
                    }
                    break;
                }
            }
        }

        // -------------------------------------------------------------------------------------
        // Attacks
        // -------------------------------------------------------------------------------------

        public static void StartAttack(World w, Unit u, Entity target)
        {
            u.Action = UnitAction.Windup;
            u.ActionTimer = u.Def.Attack.Windup;
            u.ActionTotal = u.Def.Attack.Windup;
            u.ActionTargetId = target.Id;
            u.ActionPoint = target.Pos;
            u.ActionAbility = AbilityId.None;
            Vec2 d = target.Pos - u.Pos;
            if (d.LengthSq > 0.01f && u.Def.Attack.IsMelee) u.Facing = KMath.RotateTowards(u.Facing, d.ToAngle(), 1.2f);
            w.Emit(SimEventType.Swing, u.Id, target.Id, u.Pos);
        }

        private static void ResolveAttack(World w, Unit u)
        {
            var ad = u.Def.Attack;
            u.Action = UnitAction.None;
            float slow = u.IsPlayer ? 1f + Balance.LoadAttackPenalty * u.LoadFraction : 1f;
            u.AttackCooldown = ad.Cooldown * slow;
            u.LastAttackTime = w.Time;
            var target = w.FindEntity(u.ActionTargetId);
            if (target == null || !target.IsTargetable) return;

            float dmg = ad.Damage;
            if (u.Kind == UnitKind.Rustmaw && w.Boss != null) dmg *= w.Boss.DamageDealtMultiplier;

            if (ad.IsMelee)
            {
                if (Vec2.Distance(u.Pos, target.Pos) <= u.ReachTo(target, ad.Range) + 0.6f)
                    Combat.Deal(w, u, target, dmg, ad.Type, HitFlags.Melee);
                else
                    w.Emit(SimEventType.Miss, u.Id, target.Id, target.Pos);
            }
            else
            {
                ProjectileSystem.Fire(w, u, u.IsPlayer ? u.Id : 0, target, Vec2.Zero, false, dmg, ad.Type,
                    ad.ProjectileSpeed, ad.Range, ad.SpreadDeg, VisualFor(u), HitFlags.None);
            }
        }

        private static ProjectileVisual VisualFor(Unit u)
        {
            switch (u.Kind)
            {
                case UnitKind.Mender: return ProjectileVisual.SearBolt;
                case UnitKind.SalvageDrone: return ProjectileVisual.Rivet;
                case UnitKind.ScrapMender: return ProjectileVisual.Spark;
                default: return ProjectileVisual.RifleBolt;
            }
        }

        // -------------------------------------------------------------------------------------
        // Abilities
        // -------------------------------------------------------------------------------------

        public static bool StartAbility(World w, Unit u, AbilityId id, int targetId, Vec2 point, Vec2 dir)
        {
            var slot = u.Slot(id);
            if (slot == null || !slot.Ready) return false;
            var def = slot.Def;
            u.Action = UnitAction.Casting;
            u.ActionTimer = def.CastTime;
            u.ActionTotal = def.CastTime;
            u.ActionAbility = id;
            u.ActionTargetId = targetId;
            u.ActionPoint = point;
            u.ActionDir = dir.LengthSq > 0.0001f ? dir.Normalized : Vec2.FromAngle(u.Facing);
            u.ActionAccum = 0f;
            u.ActionShots = 0;
            slot.Cooldown = def.Cooldown;
            u.LastCastTime = w.Time;
            if (id == AbilityId.TailSlam || id == AbilityId.RustmawSwipe)
            {
                var target = w.FindEntity(targetId);
                Vec2 aim = target != null ? (target.Pos - u.Pos) : u.ActionDir;
                u.ActionDir = aim.LengthSq > 0.0001f ? aim.Normalized : Vec2.FromAngle(u.Facing);
                float radius = id == AbilityId.TailSlam ? def.Radius : u.Radius + def.Range + 0.6f;
                var tg = w.AddTelegraph(new Telegraph
                {
                    Shape = TelegraphShape.Cone, Pos = u.Pos, Dir = u.ActionDir, Radius = radius, ArcDeg = def.ArcDeg,
                    Duration = def.CastTime, Threatens = Faction.Player, OwnerId = u.Id, Label = def.Name,
                });
                u.ActionTelegraphId = tg.Id;
            }
            w.Emit(SimEventType.AbilityCast, u.Id, targetId, point, 0f, def.Type, id);
            return true;
        }

        private static void FinishCast(World w, Unit u)
        {
            var def = AbilityDefs.Get(u.ActionAbility);
            if (def.IsChannel)
            {
                u.Action = UnitAction.Channel;
                u.ActionTimer = def.ChannelTime;
                u.ActionTotal = def.ChannelTime;
                return;
            }
            switch (def.Id)
            {
                case AbilityId.BurstVolley:
                    u.Action = UnitAction.Channel;
                    u.ActionShots = 3;
                    u.ActionSubTimer = 0f;
                    u.ActionTimer = 0.6f;
                    u.ActionTotal = 0.6f;
                    return;
                case AbilityId.Pounce:
                {
                    var target = w.FindUnit(u.ActionTargetId);
                    if (target == null || !target.IsTargetable)
                    {
                        u.Action = UnitAction.None;
                        return;
                    }
                    Vec2 to = target.Pos - u.Pos;
                    float dist = Math.Max(0f, to.Length - (u.Radius + target.Radius + 0.1f));
                    u.ForcedVel = to.Normalized * (dist / 0.45f);
                    u.ForcedTime = 0.45f;
                    u.Action = UnitAction.Channel;
                    u.ActionTimer = 0.45f;
                    u.ActionTotal = 0.45f;
                    return;
                }
            }
            u.Action = UnitAction.None;
            AbilitySystem.Resolve(w, u, def.Id, u.ActionTargetId, u.ActionPoint, u.ActionDir);
        }

        private static void UpdateChannel(World w, Unit u, float dt)
        {
            u.ActionTimer -= dt;
            var def = AbilityDefs.Get(u.ActionAbility);
            bool end = u.ActionTimer <= 0f;
            switch (u.ActionAbility)
            {
                case AbilityId.MendBeam:
                case AbilityId.WeldBeam:
                {
                    var t = w.FindUnit(u.ActionTargetId);
                    bool ok = t != null && t.Life == LifeState.Active && !t.Garrisoned
                              && Vec2.Distance(u.Pos, t.Pos) <= def.Range + t.Radius + 2f
                              && w.Nav.SightClear(u.Pos, t.Pos);
                    if (!ok)
                    {
                        end = true;
                        break;
                    }
                    float rate = def.Power;
                    if (u.ActionAbility == AbilityId.WeldBeam && t.Kind == UnitKind.Rustmaw && w.Boss != null) rate = w.Boss.WeldRate;
                    u.ActionAccum += Combat.Heal(w, u.IsPlayer ? u : null, t, rate * dt, false);
                    if (end && u.ActionAccum >= 1f) w.Emit(SimEventType.Heal, u.Id, t.Id, t.Pos, u.ActionAccum);
                    break;
                }
                case AbilityId.Stabilize:
                {
                    var t = w.FindUnitAny(u.ActionTargetId);
                    if (t == null || t.Life != LifeState.Downed || Vec2.Distance(u.Pos, t.Pos) > def.Range + 1.5f)
                    {
                        end = true;
                        break;
                    }
                    if (end) Combat.Revive(w, t, Balance.StabilizeHpFraction, u);
                    break;
                }
                case AbilityId.BurstVolley:
                {
                    u.ActionSubTimer -= dt;
                    if (u.ActionShots > 0 && u.ActionSubTimer <= 0f)
                    {
                        var t = w.FindEntity(u.ActionTargetId);
                        if (t == null || !t.IsTargetable)
                        {
                            end = true;
                            break;
                        }
                        ProjectileSystem.Fire(w, u, u.Id, t, Vec2.Zero, false, def.Power, DamageType.Kinetic, 46f, def.Range, 1.6f,
                            ProjectileVisual.RifleBolt, HitFlags.Ability);
                        u.ActionShots--;
                        u.ActionSubTimer = 0.18f;
                    }
                    if (u.ActionShots <= 0) end = true;
                    break;
                }
                case AbilityId.Pounce:
                    if (end)
                    {
                        var t = w.FindUnit(u.ActionTargetId);
                        if (t != null && t.IsTargetable && Vec2.Distance(u.Pos, t.Pos) <= u.Radius + t.Radius + 1.4f)
                        {
                            Combat.Deal(w, u, t, def.Power, def.Type, HitFlags.Ability | HitFlags.Melee);
                            Combat.Stun(w, t, def.Duration);
                            w.Emit(SimEventType.AbilityImpact, u.Id, t.Id, t.Pos, 0f, def.Type, def.Id);
                        }
                    }
                    break;
            }
            if (end)
            {
                u.Action = UnitAction.None;
                u.ActionShots = 0;
            }
        }

        private static void UpdateGather(World w, Unit u, float dt)
        {
            var node = w.FindNode(u.GatherNodeId);
            if (node == null || node.Amount <= 0f || u.Carry == null || u.LoadFraction >= 0.999f)
            {
                u.Action = UnitAction.None;
                return;
            }
            float rate = Balance.GatherRate;
            if (u.Role == Role.Warwright && (node.Kind == ResourceKind.Stone || node.Kind == ResourceKind.Ferrite)) rate *= Balance.WarwrightQuarryBonus;
            float take = Math.Min(rate * dt, Math.Min(node.Amount, u.CarryCapacity - u.CarryLoad));
            node.Amount -= take;
            u.Carry.Add(node.Kind, take);
            if (node.Amount <= 0.001f)
            {
                node.Amount = 0f;
                node.Removed = true;
                if (node.Cells != null) w.Nav.Stamp(node.Cells, true, false, 0, -1);
                w.Alert($"{node.Label} exhausted.", node.Pos, 0);
            }
        }
    }
}
