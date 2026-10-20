using System;
using System.Collections.Generic;
using System.Text;

namespace Kinforge.Sim
{
    public sealed class Cost
    {
        public readonly List<LootEntry> Items = new List<LootEntry>();

        public static Cost Of(params object[] pairs)
        {
            var c = new Cost();
            for (int i = 0; i + 1 < pairs.Length; i += 2)
            {
                c.Items.Add(new LootEntry((ResourceKind)pairs[i], (int)pairs[i + 1]));
            }
            return c;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            foreach (var it in Items)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(it.Amount).Append(' ').Append(Stockpile.Name(it.Kind));
            }
            return sb.ToString();
        }
    }

    public sealed class Stockpile
    {
        public const int KindCount = 6;
        public readonly float[] Amounts = new float[KindCount];

        public static string Name(ResourceKind k)
        {
            switch (k)
            {
                case ResourceKind.Scrap: return "Scrap";
                case ResourceKind.Ferrite: return "Ferrite";
                case ResourceKind.Stone: return "Crater Stone";
                case ResourceKind.Fiber: return "Grass Fiber";
                case ResourceKind.Hide: return "Hide";
                case ResourceKind.Alloy: return "Ferrite Alloy";
            }
            return k.ToString();
        }

        public float this[ResourceKind k]
        {
            get => Amounts[(int)k];
            set => Amounts[(int)k] = value;
        }

        public int Whole(ResourceKind k) => (int)MathF.Floor(Amounts[(int)k] + 0.0001f);

        public float Total
        {
            get
            {
                float t = 0f;
                foreach (float a in Amounts) t += a;
                return t;
            }
        }

        public void Add(ResourceKind k, float amount) => Amounts[(int)k] += amount;

        public bool Has(Cost cost)
        {
            if (cost == null) return true;
            foreach (var it in cost.Items)
            {
                if (Amounts[(int)it.Kind] + 0.0001f < it.Amount) return false;
            }
            return true;
        }

        public bool TrySpend(Cost cost)
        {
            if (!Has(cost)) return false;
            foreach (var it in cost.Items) Amounts[(int)it.Kind] -= it.Amount;
            return true;
        }

        public void Refund(Cost cost)
        {
            foreach (var it in cost.Items) Amounts[(int)it.Kind] += it.Amount;
        }

        public void MoveAllTo(Stockpile other)
        {
            for (int i = 0; i < KindCount; i++)
            {
                other.Amounts[i] += Amounts[i];
                Amounts[i] = 0f;
            }
        }

        public void Clear()
        {
            for (int i = 0; i < KindCount; i++) Amounts[i] = 0f;
        }
    }
}
