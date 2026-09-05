using UnityEngine;

namespace Shared
{
    /// <summary>Looping music over Resources/Audio/Music/{title,battle,ending}. Music.Set("battle") is idempotent and crossfades.
    /// Missing clips are silently skipped, so the game runs with no music files present.</summary>
    public static class Music
    {
        const float FadeSeconds = 2.5f, Volume = 0.55f;
        static AudioSource a, b; static string current; static MusicTicker ticker;

        public static void Set(string name)
        {
            if (Application.isBatchMode || name == current) return;
            current = name;
            var clip = Resources.Load<AudioClip>("Audio/Music/" + name);
            EnsureSources();
            var incoming = b; b = a; a = incoming;             // a = now playing, b = fading out
            a.clip = clip; a.volume = 0f;
            if (clip != null) a.Play(); else a.Stop();
        }

        static void EnsureSources()
        {
            if (a != null) return;
            var go = new GameObject("Music"); Object.DontDestroyOnLoad(go);
            a = go.AddComponent<AudioSource>(); b = go.AddComponent<AudioSource>();
            foreach (var s in new[] { a, b }) { s.loop = true; s.spatialBlend = 0f; s.playOnAwake = false; s.ignoreListenerPause = true; }
            ticker = go.AddComponent<MusicTicker>();
        }

        class MusicTicker : MonoBehaviour
        {
            void Update()
            {
                float step = Time.unscaledDeltaTime / FadeSeconds;   // keeps fading while the title pauses the clock
                if (a != null && a.clip != null) a.volume = Mathf.MoveTowards(a.volume, Volume, step);
                if (b != null && b.isPlaying) { b.volume = Mathf.MoveTowards(b.volume, 0f, step); if (b.volume <= 0f) b.Stop(); }
            }
        }
    }
}
