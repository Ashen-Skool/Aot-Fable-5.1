using System.Collections.Generic;
using UnityEngine;

namespace Shared
{
    /// <summary>Tiny pooled one-shot player over Resources/Audio. Sfx.Play("hook_fire", pos, pitch, volume).</summary>
    public static class Sfx
    {
        static readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        static readonly List<AudioSource> pool = new List<AudioSource>();
        static Transform root;
        public static void Play(string name, Vector3 pos, float pitch = 1f, float volume = 1f, float maxDist = 60f)
        {
            if (Application.isBatchMode) return;
            if (!clips.TryGetValue(name, out var clip)) { clip = Resources.Load<AudioClip>("Audio/" + name); clips[name] = clip; }
            if (clip == null) return;
            if (root == null) { root = new GameObject("Sfx").transform; Object.DontDestroyOnLoad(root.gameObject); }
            AudioSource src = null;
            foreach (var s in pool) if (!s.isPlaying) { src = s; break; }
            if (src == null) { if (pool.Count >= 24) return; var go = new GameObject("src"); go.transform.SetParent(root); src = go.AddComponent<AudioSource>(); src.spatialBlend = 1f; src.rolloffMode = AudioRolloffMode.Linear; pool.Add(src); }
            src.transform.position = pos; src.pitch = pitch; src.volume = volume; src.maxDistance = maxDist; src.minDistance = maxDist * 0.15f;
            src.PlayOneShot(clip);
        }
    }
}
