using UnityEngine;

namespace Shared
{
    /// <summary>The district's sound bed: a low wind with slow gusts, and a distant bell from the wall every so often.
    /// All synthesized, no clips. Lives on the camera so it follows the listener.</summary>
    public class AmbientBed : MonoBehaviour
    {
        AudioSource wind, bell; AudioLowPassFilter windLp; float gustSeed, nextBell;
        static AudioClip bellClip;

        public static void Ensure(Camera cam)
        {
            if (cam == null || Application.isBatchMode) return;
            if (cam.GetComponent<AmbientBed>() == null) cam.gameObject.AddComponent<AmbientBed>();
        }

        void Start()
        {
            wind = NoiseLoop.Source(gameObject, NoiseLoop.Brown(), 0f, 100f, out windLp);
            if (windLp != null) windLp.cutoffFrequency = 420f;
            gustSeed = Random.value * 100f;
            bell = gameObject.AddComponent<AudioSource>(); bell.spatialBlend = 0f; bell.playOnAwake = false; bell.volume = 0.22f;
            nextBell = Time.time + Random.Range(18f, 30f);
        }

        static AudioClip Bell()
        {
            if (bellClip != null) return bellClip;
            const int sr = 22050; float dur = 7f; int n = (int)(sr * dur); var d = new float[n];
            float f0 = 196f; float[] ratios = { 0.5f, 1f, 1.183f, 1.506f, 2f, 2.514f, 2.662f, 3.011f }; float[] amps = { 0.5f, 1f, 0.6f, 0.5f, 0.45f, 0.25f, 0.2f, 0.15f }; float[] decay = { 0.5f, 0.55f, 0.8f, 0.9f, 1.2f, 1.8f, 2f, 2.5f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)sr, v = 0f;
                for (int k = 0; k < ratios.Length; k++) v += amps[k] * Mathf.Sin(2f * Mathf.PI * f0 * ratios[k] * t) * Mathf.Exp(-decay[k] * t);
                float strike = Mathf.Exp(-t * 40f) * 0.3f * Mathf.Sin(2f * Mathf.PI * 1200f * t);
                d[i] = Mathf.Clamp((v * 0.28f + strike) * Mathf.Min(1f, t * 200f), -1f, 1f);
            }
            bellClip = AudioClip.Create("bell", n, 1, sr, false); bellClip.SetData(d, 0);
            return bellClip;
        }

        void Update()
        {
            if (wind != null)
            {
                float gust = Mathf.PerlinNoise(gustSeed, Time.time * 0.08f);
                wind.volume = 0.05f + 0.09f * gust;
                if (windLp != null) windLp.cutoffFrequency = 300f + 500f * gust;
            }
            if (bell != null && Time.time > nextBell)
            {
                nextBell = Time.time + Random.Range(38f, 75f);
                int strikes = Random.Range(2, 5);
                StartCoroutine(Toll(strikes));
            }
        }

        System.Collections.IEnumerator Toll(int strikes)
        {
            for (int i = 0; i < strikes; i++) { bell.PlayOneShot(Bell(), 1f); yield return new WaitForSeconds(2.6f); }
        }
    }
}
