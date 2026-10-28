using System;
using System.Collections.Generic;

namespace Kinforge.Sim
{
    /// <summary>Ramparts, turrets, fields, base buildings and the raid hatch.</summary>
    public sealed class Structure : Entity
    {
        public StructureKind Kind;
        public BuildingKind Building;
        public int OwnerUnitId;
        public Squad OwnerSquad;
        public float Life = -1f;       // seconds remaining; negative = permanent
        public float MaxLife = -1f;
        public Vec2 Dir = Vec2.Forward;
        public float HalfLen;
        public float HalfWidth;
        public float BaseMaxHp;
        public float AttackCooldown;
        public int TargetId;
        public List<int> Cells;
        public bool BlocksMove;
        public bool BlocksSight;
        public float Power;
        public float AreaRadius;
        public float Range;
        public DamageType Type;
        public float BuiltAt;
        public bool Indestructible;
        public float DamageAbsorbed;

        public override float MaxHp => BaseMaxHp;

        public override bool IsTargetable =>
            !Removed && Hp > 0f && !Indestructible && Kind != StructureKind.SpikeField && Kind != StructureKind.RegenField;

        public bool IsField => Kind == StructureKind.SpikeField || Kind == StructureKind.RegenField;
        public bool IsWall => Kind == StructureKind.Rampart || Kind == StructureKind.Hatch;
        public Vec2 EndA => Pos - Dir * HalfLen;
        public Vec2 EndB => Pos + Dir * HalfLen;
    }

    public enum ProjectileVisual : byte
    {
        RifleBolt,
        SearBolt,
        Rivet,
        Shell,
        TurretBolt,
        Spark,
    }

    public sealed class Projectile
    {
        public int Id;
        public Faction Faction;
        public int SourceId;
        public int OwnerUnitId;
        public Vec2 Pos;
        public Vec2 PrevPos;
        public Vec2 Start;
        public Vec2 Dir;
        public float Speed;
        public float MaxRange;
        public float Traveled;
        public float Damage;
        public DamageType Type;
        public HitFlags Flags;
        public bool Arcing;
        public Vec2 ArcTarget;
        public float ArcTime;
        public float ArcDuration;
        public float Splash;
        public float StartHeight;
        public ProjectileVisual Visual;
        public bool Removed;

        /// <summary>0..1 progress for arcing shells (the view lifts them on a parabola).</summary>
        public float ArcProgress => ArcDuration > 0f ? KMath.Clamp01(ArcTime / ArcDuration) : 0f;
    }

    public enum TelegraphShape : byte
    {
        Circle,
        Line,
        Cone,
    }

    /// <summary>
    /// A visible warning a few seconds before a big attack lands. Units read these too: how fast
    /// they react depends on Doctrine tier and Instinct.
    /// </summary>
    public sealed class Telegraph
    {
        public int Id;
        public TelegraphShape Shape;
        public Vec2 Pos;
        public Vec2 Dir = Vec2.Forward;
        public float Radius;
        public float Length;
        public float Width;
        public float ArcDeg;
        public float Duration;
        public float Remaining;
        public Faction Threatens;
        public int OwnerId;
        /// <summary>Walls between the origin and a unit protect it (fire waves).</summary>
        public bool CoverBlocks;
        /// <summary>A charge a grown Titan can stand in front of and stop.</summary>
        public bool Catchable;
        public float CreatedAt;
        public string Label;
        public bool Removed;

        public float Progress => Duration > 0f ? KMath.Clamp01(1f - Remaining / Duration) : 1f;

        public bool Contains(Vec2 p, float r)
        {
            Vec2 d = p - Pos;
            switch (Shape)
            {
                case TelegraphShape.Circle:
                    return d.LengthSq <= (Radius + r) * (Radius + r);
                case TelegraphShape.Line:
                {
                    float along = Vec2.Dot(d, Dir);
                    float lat = Vec2.Dot(d, Dir.Right);
                    return along >= -r && along <= Length + r && MathF.Abs(lat) <= Width * 0.5f + r;
                }
                case TelegraphShape.Cone:
                {
                    float dist = d.Length;
                    if (dist > Radius + r) return false;
                    if (dist < r + 0.5f) return true;
                    float ang = MathF.Abs(KMath.AngleDelta(Dir.ToAngle(), d.ToAngle()));
                    return ang <= ArcDeg * 0.5f * KMath.Deg2Rad + r / dist;
                }
            }
            return false;
        }
    }

    public sealed class ResourceNode
    {
        public int Id;
        public ResourceKind Kind;
        public Vec2 Pos;
        public float Radius;
        public float Amount;
        public float MaxAmount;
        public bool Removed;
        public List<int> Cells;
        public int Variant;
        public string Label;
    }

    /// <summary>Kin-cores dropped where the kin-band was wiped. The clan carries them home once nobody is left standing.</summary>
    public sealed class CoreCache
    {
        public int Id;
        public Vec2 Pos;
        public readonly List<int> UnitIds = new List<int>();
        public float CreatedAt;
        public bool Removed;
    }

    public enum PropKind : byte
    {
        Rock,
        Wreck,
        WreckSpire,
        Boulder,
    }

    public sealed class StaticProp
    {
        public PropKind Kind;
        public Vec2 Pos;
        public Vec2 Dir = Vec2.Forward;
        public float HalfLen;
        public float HalfWidth;
        public float Radius;
        public float Height;
        public int Variant;
    }

    /// <summary>WoW-style threat: enemies attack whoever has built the most threat on them.</summary>
    public sealed class ThreatTable
    {
        private readonly List<int> _ids = new List<int>(8);
        private readonly List<float> _vals = new List<float>(8);

        public int Count => _ids.Count;
        public int IdAt(int i) => _ids[i];
        public float ValueAt(int i) => _vals[i];

        public void Add(int id, float amount)
        {
            int i = _ids.IndexOf(id);
            if (i >= 0) _vals[i] += amount;
            else
            {
                _ids.Add(id);
                _vals.Add(amount);
            }
        }

        public float Get(int id)
        {
            int i = _ids.IndexOf(id);
            return i >= 0 ? _vals[i] : 0f;
        }

        public void Set(int id, float value)
        {
            int i = _ids.IndexOf(id);
            if (i >= 0) _vals[i] = value;
            else
            {
                _ids.Add(id);
                _vals.Add(value);
            }
        }

        public bool Contains(int id) => _ids.IndexOf(id) >= 0;

        public void Remove(int id)
        {
            int i = _ids.IndexOf(id);
            if (i < 0) return;
            _ids.RemoveAt(i);
            _vals.RemoveAt(i);
        }

        public void Clear()
        {
            _ids.Clear();
            _vals.Clear();
        }

        public float TopValue()
        {
            float best = 0f;
            foreach (float v in _vals)
            {
                if (v > best) best = v;
            }
            return best;
        }

        /// <summary>Highest-threat id that passes the filter; stale ids are pruned.</summary>
        public int Top(Func<int, bool> valid)
        {
            int best = 0;
            float bestV = float.MinValue;
            for (int i = _ids.Count - 1; i >= 0; i--)
            {
                if (!valid(_ids[i]))
                {
                    _ids.RemoveAt(i);
                    _vals.RemoveAt(i);
                    continue;
                }
                if (_vals[i] > bestV)
                {
                    bestV = _vals[i];
                    best = _ids[i];
                }
            }
            return best;
        }
    }
}
