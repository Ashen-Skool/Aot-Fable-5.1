using System;
using System.Collections.Generic;

namespace Shared
{
    /// <summary>
    /// Static service locator. Every piece registers what it exposes here and
    /// reads what it needs from here. Cross-piece access goes through Ctx only.
    /// </summary>
    public static class Ctx
    {
        static readonly Dictionary<string, object> map = new Dictionary<string, object>(64);

        public static void Set(string name, object value)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Ctx.Set: empty name");
            map[name] = value;
        }

        /// <summary>Returns the registered value or default(T) when missing or of the wrong type.</summary>
        public static T Get<T>(string name)
        {
            if (map.TryGetValue(name, out var v) && v is T t) return t;
            return default;
        }

        /// <summary>Like Get but throws when missing. Use for hard dependencies.</summary>
        public static T Require<T>(string name)
        {
            if (map.TryGetValue(name, out var v) && v is T t) return t;
            throw new KeyNotFoundException("Ctx.Require: '" + name + "' as " + typeof(T).Name + " is not registered");
        }

        public static bool Has(string name) => map.ContainsKey(name);
        public static bool Remove(string name) => map.Remove(name);
        public static void Clear() => map.Clear();
        public static int Count => map.Count;
        public static IEnumerable<string> Names => map.Keys;
    }
}
