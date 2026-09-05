using UnityEngine;

namespace Shared
{
    /// <summary>Procedural looping noise beds (no asset needed): wind for the camera, gas hiss for the player, steam for the Titan.</summary>
    public static class NoiseLoop
    {
        static AudioClip brown, white;
        public static AudioClip Brown() => brown != null ? brown : (brown = Make("brownNoise", true));
        public static AudioClip White() => white != null ? white : (white = Make("whiteNoise", false));

        static AudioClip Make(string name, bool isBrown)
        {
            const int sr = 22050; int n = sr * 4;
            var data = new float[n]; var rng = new System.Random(7); float last = 0f;
            for (int i = 0; i < n; i++)
            {
                float w = (float)(rng.NextDouble() * 2.0 - 1.0);
                if (isBrown) { last = (last + 0.03f * w) / 1.03f; data[i] = Mathf.Clamp(last * 6f, -1f, 1f); }
                else data[i] = w * 0.6f;
            }
            int fade = 2048; // seamless loop: crossfade the tail into the head
            for (int i = 0; i < fade; i++) { float f = i / (float)fade; data[i] = data[i] * f + data[n - fade + i] * (1f - f); }
            var clip = AudioClip.Create(name, n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>A silent, playing, looping source. Drive .volume (and the filter cutoff) every frame.</summary>
        public static AudioSource Source(GameObject host, AudioClip clip, float spatial, float maxDist, out AudioLowPassFilter lp)
        {
            var src = host.AddComponent<AudioSource>();
            src.clip = clip; src.loop = true; src.playOnAwake = false; src.volume = 0f; src.spatialBlend = spatial;
            src.rolloffMode = AudioRolloffMode.Linear; src.maxDistance = maxDist; src.minDistance = maxDist * 0.2f;
            lp = host.AddComponent<AudioLowPassFilter>(); lp.cutoffFrequency = 800f;
            if (!Application.isBatchMode) src.Play();
            return src;
        }
    }
}
