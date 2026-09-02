using UnityEngine;

namespace Shared.Rigs
{
    /// <summary>
    /// Body proportions as fractions of total height (heel to crown). Two presets:
    /// Human (7.5 heads, Mikasa) and Titan (big head, long arms, thick limbs).
    /// </summary>
    [System.Serializable]
    public class Proportions
    {
        // vertical chain
        public float hipsY = 0.53f;       // hip joint height
        public float hipHalf = 0.055f;    // hip joint half-spacing
        public float upperLeg = 0.245f;
        public float lowerLeg = 0.225f;
        public float ankle = 0.05f;       // ankle joint above ground
        public float spineRise = 0.02f;   // hips joint -> spine joint
        public float spine = 0.10f;
        public float chest = 0.17f;
        public float neck = 0.04f;
        public float headR = 0.07f;       // head half-height; head fills the rest to 1.0
        // arms
        public float shoulderHalf = 0.115f;
        public float upperArm = 0.17f;
        public float lowerArm = 0.15f;
        public float hand = 0.08f;
        // thickness
        public float torsoW = 0.20f;
        public float torsoD = 0.11f;
        public float pelvisW = 0.18f;
        public float thighR = 0.045f;
        public float shinR = 0.035f;
        public float upperArmR = 0.03f;
        public float lowerArmR = 0.025f;
        public float neckR = 0.025f;
        public float footLen = 0.13f;
        public float footW = 0.05f;
        public float belly = 0f;          // 0 = none; radius fraction of a pot belly on the spine
        public float tempo = 1f;          // cycle-rate multiplier baked from height (set by HumanoidProxy)

        public static Proportions Human() => new Proportions();

        public static Proportions Titan()
        {
            return new Proportions
            {
                hipsY = 0.50f, hipHalf = 0.075f, upperLeg = 0.23f, lowerLeg = 0.21f, ankle = 0.06f,
                spineRise = 0.02f, spine = 0.10f, chest = 0.19f, neck = 0.035f, headR = 0.0925f,
                shoulderHalf = 0.16f, upperArm = 0.20f, lowerArm = 0.19f, hand = 0.10f,
                torsoW = 0.28f, torsoD = 0.16f, pelvisW = 0.24f,
                thighR = 0.065f, shinR = 0.05f, upperArmR = 0.045f, lowerArmR = 0.04f, neckR = 0.045f,
                footLen = 0.16f, footW = 0.07f, belly = 0.10f,
            };
        }

        /// <summary>The Abnormal: athletic, no belly, broader shoulders, longer legs.</summary>
        public static Proportions Boss()
        {
            var p = Titan();
            p.belly = 0f;
            p.hipsY = 0.52f; p.upperLeg = 0.24f; p.lowerLeg = 0.22f;
            p.spine = 0.09f; p.chest = 0.20f; p.headR = 0.085f;
            p.shoulderHalf = 0.17f; p.torsoW = 0.30f; p.torsoD = 0.17f;
            p.thighR = 0.07f; p.upperArmR = 0.05f; p.lowerArmR = 0.045f;
            return p;
        }
    }

    /// <summary>Materials per body region. Any null falls back to skin.</summary>
    public class Palette
    {
        public Material skin, torso, pelvis, arms, hands, legs, feet, head, hair;

        public static Palette Solid(Color c, float smooth = 0.25f)
        {
            var m = Mats.Lit(c, smooth);
            return new Palette { skin = m };
        }

        public Material Torso => torso ?? skin;
        public Material Pelvis => pelvis ?? Torso;
        public Material Arms => arms ?? Torso;
        public Material Hands => hands ?? skin;
        public Material Legs => legs ?? Pelvis;
        public Material Feet => feet ?? Legs;
        public Material Head => head ?? skin;
    }
}
