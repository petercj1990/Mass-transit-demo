using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>A kin-band: five Vhorn under one command. The squad is the unit of command.</summary>
    public sealed class Squad
    {
        public int Id;
        public string Name;
        public int ColorIndex;
        public readonly List<Unit> Members = new List<Unit>(5);

        public CommandType Stance = CommandType.DefensiveFormation;
        public CommandType PrevStance = CommandType.DefensiveFormation;
        public float StanceSince;

        public SquadTask Task;
        public int TaskTargetId;
        public Vec2 TaskPos;
        public bool Forced;
        public SquadTask ResumeTask;
        public int ResumeTargetId;

        public Vec2 Anchor;
        public Vec2 CombatAnchor;
        public Vec2 Forward = Vec2.Forward;
        public Vec2 RallyPoint;
        public int FocusTargetId;
        public int AiFocusId;
        public float AiFocusChosenAt;

        public bool InCombat;
        public float LastEngagedTime = -999f;
        public float CombatStartTime;
        public readonly List<Unit> Engaged = new List<Unit>();
        public FightTracker Fight;
        public BattleReport LastReport;

        public bool Garrisoned;
        public bool Wiped;

        // Formation march
        public Vec2 MarchPoint;
        public Vec2 MarchHeading = Vec2.Forward;
        public readonly List<Vec2> MarchPath = new List<Vec2>();
        public int MarchIndex;
        public Vec2 MarchGoal;
        public int MarchNavVersion = -1;
        /// <summary>Reached the job site of the current task (stays true until a new order).</summary>
        public bool Arrived;

        public int WeaponTier = 1;
        public int ArmorTier = 1;
        public float LastUnderAttackAlert = -999f;

        public Unit Member(Role role)
        {
            foreach (var u in Members)
            {
                if (u.Role == role) return u;
            }
            return null;
        }

        public Unit ActiveMember(Role role)
        {
            foreach (var u in Members)
            {
                if (u.Role == role && u.IsActive) return u;
            }
            return null;
        }

        public int ActiveCount
        {
            get
            {
                int n = 0;
                foreach (var u in Members)
                {
                    if (u.IsActive) n++;
                }
                return n;
            }
        }

        public Vec2 Centroid
        {
            get
            {
                Vec2 sum = Vec2.Zero;
                int n = 0;
                foreach (var u in Members)
                {
                    if (!u.IsActive) continue;
                    sum += u.Pos;
                    n++;
                }
                return n > 0 ? sum / n : Anchor;
            }
        }

        public float MarchSpeed
        {
            get
            {
                float s = float.MaxValue;
                foreach (var u in Members)
                {
                    if (!u.IsActive) continue;
                    float us = UnitMotor.BaseSpeed(u);
                    if (us < s) s = us;
                }
                return s == float.MaxValue ? 4f : s;
            }
        }

        public float CarryLoad
        {
            get
            {
                float t = 0f;
                foreach (var u in Members) t += u.CarryLoad;
                return t;
            }
        }

        public float CarryCapacity
        {
            get
            {
                float t = 0f;
                foreach (var u in Members)
                {
                    if (u.Life != LifeState.Dead) t += u.CarryCapacity;
                }
                return t;
            }
        }

        public int CoresCarried
        {
            get
            {
                int n = 0;
                foreach (var u in Members) n += u.CarriedCores.Count;
                return n;
            }
        }

        public override string ToString() => Name;
    }

    public enum GroupState : byte
    {
        Idle,
        Engaged,
        Returning,
        Hunting,
    }

    /// <summary>A camp, pack or war party of enemies that aggro, leash and respawn together.</summary>
    public sealed class EnemyGroup
    {
        public int Id;
        public string Name;
        public Vec2 Home;
        public float AggroRadius = 12f;
        public float LeashRadius = 40f;
        public GroupState State;
        public readonly List<Unit> Members = new List<Unit>();
        public readonly List<UnitKind> Spec = new List<UnitKind>();
        public bool IsCamp;
        public bool IsBossGroup;
        public bool IsWarParty;
        public int Tier;
        public float ClearedAt = -1f;
        public float EngagedAt;
        public int HuntSquadId;
        public float SpawnedAt;

        public bool AllDead
        {
            get
            {
                foreach (var m in Members)
                {
                    if (m.Life == LifeState.Active) return false;
                }
                return true;
            }
        }
    }
}
