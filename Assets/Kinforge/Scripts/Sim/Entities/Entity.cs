namespace Kinforge.Sim
{
    /// <summary>Anything that can be hit: units and structures share ids, positions and health.</summary>
    public abstract class Entity
    {
        public int Id;
        public Faction Faction;
        public Vec2 Pos;
        public Vec2 PrevPos;
        public float Radius;
        public float Hp;
        public bool Removed;

        public abstract float MaxHp { get; }
        public virtual float Armor => 0f;
        public virtual bool IsTargetable => !Removed && Hp > 0f;
        public float HpFraction => MaxHp > 0f ? Hp / MaxHp : 0f;

        public bool IsHostileTo(Entity other) =>
            other != null && Faction != Faction.Neutral && other.Faction != Faction.Neutral && Faction != other.Faction;
    }
}
