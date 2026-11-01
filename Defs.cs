using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace TomYum
{
    public enum ItemType
    {
        Shrimp = 0, Mushroom = 1, Lemongrass = 2, Galangal = 3, KaffirLeaf = 4,
        Chili = 5, Lime = 6, Tomato = 7, FishSauce = 8, Bowl = 9
    }

    public class SoupEntry
    {
        public ItemType type;
        public bool chopped;
        public int count;
        public float cook; // seconds spent boiling

        public SoupEntry Clone()
        {
            return new SoupEntry { type = type, chopped = chopped, count = count, cook = cook };
        }
    }

    public static class Defs
    {
        public const float CookTime = 8f;    // boiling seconds until "cooked"
        public const float BurntTime = 45f;  // boiling/dry-heat seconds until "burnt"
        public const int ChopsNeeded = 5;

        public static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static readonly string[] Names =
            { "Shrimp", "Mushroom", "Lemongrass", "Galangal", "Kaffir leaf", "Chili", "Lime", "Tomato", "Fish sauce", "Bowl" };

        // file names (without extension) of the Blender models in Assets/Resources/Models
        public static readonly string[] Models =
            { "shrimp", "mushroom", "lemongrass", "galangal", "kaffir_leaf", "chili", "lime", "tomato", "fish_sauce", "bowl" };

        // longest side in metres (models are rescaled to this at runtime)
        public static readonly float[] Sizes =
            { 0.30f, 0.18f, 0.45f, 0.22f, 0.16f, 0.24f, 0.14f, 0.16f, 0.32f, 0.38f };

        public static readonly Color[] Colors =
        {
            new Color(1f, 0.5f, 0.3f), new Color(0.85f, 0.78f, 0.65f), new Color(0.7f, 0.85f, 0.4f),
            new Color(0.9f, 0.8f, 0.55f), new Color(0.15f, 0.5f, 0.15f), new Color(0.9f, 0.1f, 0.1f),
            new Color(0.5f, 0.8f, 0.15f), new Color(0.9f, 0.15f, 0.1f), new Color(0.5f, 0.25f, 0.08f),
            new Color(0.95f, 0.95f, 0.9f)
        };

        public static bool Choppable(ItemType t)
        {
            return t == ItemType.Mushroom || t == ItemType.Lemongrass || t == ItemType.Galangal
                || t == ItemType.Chili || t == ItemType.Tomato;
        }

        public static string F(float v) { return v.ToString("0.###", Inv); }

        public static float P(string s)
        {
            float r;
            float.TryParse(s, NumberStyles.Float, Inv, out r);
            return r;
        }

        public static int PI(string s)
        {
            int r;
            int.TryParse(s, NumberStyles.Integer, Inv, out r);
            return r;
        }

        // remove characters used as separators in network messages
        public static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace('|', ' ').Replace(',', ' ').Replace(';', ' ').Replace('/', ' ').Replace(':', ' ').Trim();
        }

        public static string Pack(List<SoupEntry> l)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < l.Count; i++)
            {
                if (i > 0) sb.Append('/');
                var e = l[i];
                sb.Append((int)e.type).Append(':').Append(e.chopped ? 1 : 0).Append(':')
                  .Append(e.count).Append(':').Append(F(e.cook));
            }
            return sb.ToString();
        }

        public static List<SoupEntry> Unpack(string s)
        {
            var r = new List<SoupEntry>();
            if (string.IsNullOrEmpty(s)) return r;
            foreach (var part in s.Split('/'))
            {
                var p = part.Split(':');
                if (p.Length < 4) continue;
                r.Add(new SoupEntry { type = (ItemType)PI(p[0]), chopped = p[1] == "1", count = PI(p[2]), cook = P(p[3]) });
            }
            return r;
        }

        public static string Describe(List<SoupEntry> l)
        {
            if (l.Count == 0) return "empty";
            var sb = new StringBuilder();
            foreach (var e in l)
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(e.count).Append("x ").Append(Names[(int)e.type]);
                if (e.chopped) sb.Append(" (chopped)");
                if (e.cook >= BurntTime) sb.Append(" [BURNT]");
                else if (e.cook >= CookTime) sb.Append(" [cooked]");
                else if (e.cook > 0.5f) sb.Append(" [cooking]");
            }
            return sb.ToString();
        }
    }
}
