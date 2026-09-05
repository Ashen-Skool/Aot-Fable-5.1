using UnityEngine;

namespace Shared
{
    /// <summary>Synthesized one-shots the asset kit lacks: a blade/arm whoosh and a Titan roar.</summary>
    public static class Synth
    {
        static AudioClip whoosh, roar;
        const int SR = 22050;

        public static AudioClip Whoosh()
        {
            if (whoosh != null) return whoosh;
            int n = (int)(SR * 0.55f); var d = new float[n]; var rng = new System.Random(3); float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Sin(t * Mathf.PI); env *= env;
                float cutoff = Mathf.Lerp(0.02f, 0.35f, Mathf.Sin(t * Mathf.PI));   // the sweep: dull, bright, dull
                float w = (float)(rng.NextDouble() * 2 - 1);
                lp += (w - lp) * cutoff;
                d[i] = Mathf.Clamp(lp * env * 2.2f, -1f, 1f);
            }
            whoosh = AudioClip.Create("whoosh", n, 1, SR, false); whoosh.SetData(d, 0); return whoosh;
        }

        public static AudioClip Roar()
        {
            if (roar != null) return roar;
            float dur = 2.2f; int n = (int)(SR * dur); var d = new float[n]; var rng = new System.Random(5); float lp = 0f, phase = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SR, u = t / dur;
                float env = Mathf.Clamp01(t * 6f) * Mathf.Clamp01((dur - t) * 1.2f);
                float f = 58f + 22f * Mathf.Sin(u * 5.5f) - 18f * u + 6f * Mathf.Sin(t * 37f);   // a falling growl with vibrato
                phase += f / SR; if (phase > 1f) phase -= 1f;
                float saw = phase * 2f - 1f; float sq = Mathf.Sign(Mathf.Sin(phase * 6.283f * 2f));
                float voice = saw * 0.6f + sq * 0.2f + Mathf.Sin(phase * 6.283f * 3f) * 0.3f;
                float w = (float)(rng.NextDouble() * 2 - 1); lp += (w - lp) * 0.12f;
                float v = (voice * 0.7f + lp * 0.8f) * env;
                d[i] = Mathf.Clamp(v * 1.4f / (1f + Mathf.Abs(v)), -1f, 1f);   // soft clip: grit
            }
            roar = AudioClip.Create("roar", n, 1, SR, false); roar.SetData(d, 0); return roar;
        }
    }
}
