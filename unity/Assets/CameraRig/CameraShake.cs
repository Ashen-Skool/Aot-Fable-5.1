using UnityEngine;

namespace AotCamera
{
    /// <summary>Trauma-based shake: impacts add trauma, offsets scale with trauma squared, Perlin driven so it never allocates.</summary>
    public class CameraShake
    {
        public float Trauma { get; private set; }
        public Vector3 PosOffset { get; private set; }
        public Vector3 RotOffset { get; private set; }
        public float decayPerSec = 1.2f;
        public float maxPos = 0.45f;
        public float maxRotDeg = 7f;
        public float frequency = 24f;
        float t;

        public void Add(float amount) => Trauma = Mathf.Clamp01(Trauma + amount);

        public void Update(float dt)
        {
            if (Trauma <= 0f) { PosOffset = Vector3.zero; RotOffset = Vector3.zero; return; }
            t += dt * frequency;
            float s = Trauma * Trauma;
            PosOffset = new Vector3(N(0) * maxPos * s, N(1) * maxPos * s, N(2) * maxPos * 0.3f * s);
            RotOffset = new Vector3(N(3) * maxRotDeg * s, N(4) * maxRotDeg * s, N(5) * maxRotDeg * 1.6f * s);
            Trauma = Mathf.Max(0f, Trauma - decayPerSec * dt);
        }

        float N(int i) => Mathf.PerlinNoise(i * 17.31f + t, i * 7.13f + 0.5f) * 2f - 1f;
    }
}
