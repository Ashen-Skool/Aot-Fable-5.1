using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Shared;
using Characters;

/// Diagnostic: where do the dressed models' feet, toes and rendered bounds sit relative to their host origin?
public class GroundSnapProbe
{
    [UnityTest]
    public IEnumerator ReportFeet()
    {
        Bootstrap.Ensure();
        for (int i = 0; i < 8; i++) yield return null;
        foreach (var key in new[] { "mikasaModel", "bossModel" })
        {
            var m = Ctx.Get<CharacterModel>(key); if (m == null) { Debug.Log("[Probe] no " + key); continue; }
            var host = m.transform.parent;
            var rs = m.GetComponentsInChildren<SkinnedMeshRenderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            string s = "[Probe] " + key + " host.y=" + host.position.y.ToString("0.00") + " model.localY=" + m.transform.localPosition.y.ToString("0.00") + " lossyScale=" + m.transform.lossyScale.x.ToString("0.000");
            foreach (var hb in new[] { HumanBodyBones.Hips, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightFoot, HumanBodyBones.RightToes, HumanBodyBones.Head })
            { var t = m.animator.GetBoneTransform(hb); s += " " + hb + "=" + (t == null ? "null" : t.position.y.ToString("0.00")); }
            s += " boundsMinY=" + b.min.y.ToString("0.00") + " boundsMaxY=" + b.max.y.ToString("0.00") + " pose=" + m.Current;
            Debug.Log(s);
        }
        Assert.Pass();
    }
}
