using System.Collections.Generic;
using UnityEngine;

namespace Shared
{
    /// <summary>World-anchored floating texts (damage numbers, zone names) that the HUD draws and ages out.</summary>
    public static class HudEvents
    {
        public struct Pop { public Vector3 pos; public string text; public Color color; public float t0; public float size; public float life; }
        public static readonly List<Pop> Pops = new List<Pop>(32);
        public const float Life = 1.1f;
        public static void Add(Vector3 pos, string text, Color color, float size = 1f, float life = Life)
        {
            Pops.Add(new Pop { pos = pos, text = text, color = color, t0 = Time.unscaledTime, size = size, life = life });
            if (Pops.Count > 24) Pops.RemoveAt(0);
        }
        public static void Prune() { for (int i = Pops.Count - 1; i >= 0; i--) if (Time.unscaledTime - Pops[i].t0 > Pops[i].life) Pops.RemoveAt(i); }
    }
}
