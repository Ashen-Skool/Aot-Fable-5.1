using System;
using UnityEngine;
using Shared;
using Shared.Rigs;
using Pose = Shared.Rigs.Pose;

namespace Proxies
{
    /// <summary>
    /// Hooks the proxies into Bootstrap: Mikasa (1.7 m), a 7 m Titan and the 15 m boss replace
    /// the placeholder capsules. Registered in Ctx: mikasa, titan, boss (GameObject),
    /// mikasaProxy (MikasaProxy), titanProxy, bossProxy (TitanProxy), mikasaPoser, titanPoser,
    /// bossPoser (IPoser). With -piece proxies (the capture rig) it also builds a pose lineup
    /// of every pose for every proxy, frozen at its most readable phase, in Ctx "proxyLineup".
    /// </summary>
    public static class ProxyBootstrap
    {
        public static readonly Vector3 MikasaPos = new Vector3(0f, 0f, -20f);
        public static readonly Vector3 TitanPos = new Vector3(-6f, 0f, 50f);
        public static readonly Vector3 BossPos = new Vector3(0f, 0f, 98f);   // just inside the gate; he comes for you when the title lifts

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Register()
        {
            Bootstrap.CharacterFactory = Spawn;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void AfterScene()
        {
            Reboot.Register(20, AfterScene);
            var b = Bootstrap.Ensure();
            if (Ctx.Get<MikasaProxy>("mikasaProxy") == null) Spawn(b); // Bootstrap ran before our hook was set
            if (Bootstrap.Arg("-piece") == "proxies" || Bootstrap.Arg("-lineup") != null) BuildLineup();
        }

        /// <summary>Bootstrap.CharacterFactory: build the three proxies and register them.</summary>
        public static void Spawn(Bootstrap b)
        {
            var old = Ctx.Get<GameObject>("mikasa");
            if (old != null && old.GetComponent<MikasaProxy>() == null) HumanoidProxy.Kill(old);
            old = Ctx.Get<GameObject>("titan");
            if (old != null && old.GetComponent<TitanProxy>() == null) HumanoidProxy.Kill(old);

            var mikasa = MikasaProxy.Build("Mikasa", MikasaPos, 0f);
            var titan = TitanProxy.Build("Titan", TitanProxy.SmallHeight, TitanPos, 180f);
            var boss = TitanProxy.Build("Boss", TitanProxy.BossHeight, BossPos, 180f);
            titan.rig.SetPose(Pose.Idle);
            boss.rig.SetPose(Pose.Idle);

            b.mikasa = mikasa.gameObject;
            b.titan = titan.gameObject;
            Ctx.Set("mikasa", mikasa.gameObject);
            Ctx.Set("titan", titan.gameObject);
            Ctx.Set("boss", boss.gameObject);
            Ctx.Set("mikasaProxy", mikasa);
            Ctx.Set("titanProxy", titan);
            Ctx.Set("bossProxy", boss);
            Ctx.Set("mikasaPoser", (IPoser)mikasa.rig);
            Ctx.Set("titanPoser", (IPoser)titan.rig);
            Ctx.Set("bossPoser", (IPoser)boss.rig);
            // Real rigged model, if the FBX is in Resources/Characters (made with the user; proxy otherwise).
            var mikasaModel = Characters.CharacterModel.TryDress(mikasa.gameObject, "Characters/Mikasa", MikasaProxy.Height);
            if (mikasaModel != null) { Ctx.Set("mikasaPoser", (IPoser)mikasaModel); Ctx.Set("mikasaModel", mikasaModel); }
            var bossModel = Characters.CharacterModel.TryDress(boss.gameObject, "Characters/Titan", TitanProxy.BossHeight);
            if (bossModel != null) { Ctx.Set("bossPoser", (IPoser)bossModel); Ctx.Set("bossModel", bossModel); bossModel.SetPose(Pose.Idle); }
            Ctx.Set("bossBrain", TitanBrain.Attach(boss.gameObject, TitanProxy.BossHeight));
            CannonPlacer.Ensure(); // the town registers its rooftops after this runs; the placer waits for them
            var orbit = Ctx.Get<OrbitCamera>("orbit");
            if (orbit != null) orbit.target = mikasa.rig.Bone(BoneId.Chest);
        }

        /// <summary>Waits for the town's rooftops, then places the cannons once.</summary>
        class CannonPlacer : MonoBehaviour
        {
            public static void Ensure() { if (FindFirstObjectByType<CannonPlacer>() == null) new GameObject("CannonPlacer").AddComponent<CannonPlacer>(); }
            float waited;
            void Update()
            {
                waited += Time.deltaTime;
                bool ready = Ctx.Get<Vector3[]>("town.rooftops") != null && Ctx.Has("odmGrid");
                if (ready || waited > 5f) { DressTowers(); PlaceCannons(); Destroy(gameObject); }
            }
        }

        static Transform GridRoot()
        {
            var g = Ctx.Get<object>("odmGrid");
            return g is Component c ? c.transform : g is GameObject go ? go.transform : null;
        }

        /// <summary>The flight-test towers become part of the district: town stone bodies, dark stone flat tops.</summary>
        static void DressTowers()
        {
            var root = GridRoot(); if (root == null) return;
            var stone = Ctx.Get<Material>("town.stoneMat"); var top = Ctx.Get<Material>("town.roofMat");
            if (stone == null) return;
            var mpb = new MaterialPropertyBlock();
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                bool isTop = r.transform.localScale.y < 1.5f && r.transform.parent != root;
                r.sharedMaterial = isTop ? top : stone;
                // a unit cube's UVs span 0..1 per face: tile the stone per 3 m so a 30 m tower is not one stretched brick
                var sc = r.transform.lossyScale; float tx = Mathf.Max(sc.x, sc.z) / 3f, ty = (isTop ? Mathf.Max(sc.x, sc.z) : sc.y) / 3f;
                r.GetPropertyBlock(mpb); mpb.SetVector("_BaseMap_ST", new Vector4(tx, ty, 0f, 0f)); mpb.SetVector("_BumpMap_ST", new Vector4(tx, ty, 0f, 0f)); r.SetPropertyBlock(mpb);
            }
        }

        /// <summary>Flat tops of the tallest towers, then a point on top of each.</summary>
        static System.Collections.Generic.List<Vector3> TowerTops()
        {
            var tops = new System.Collections.Generic.List<Vector3>();
            var root = GridRoot(); if (root == null) return tops;
            foreach (var col in root.GetComponentsInChildren<BoxCollider>())
            {
                var b = col.bounds; if (b.size.y < 10f) continue;
                tops.Add(new Vector3(b.center.x, b.max.y, b.center.z));
            }
            tops.Sort((a, b) => b.y.CompareTo(a.y));
            return tops;
        }

        /// <summary>Cannons: the three tallest tower tops, spread out, plus one on the Titan wall's walkway.</summary>
        public static void PlaceCannons()
        {
            var picked = new System.Collections.Generic.List<Vector3>();
            foreach (var r in TowerTops())
            {
                bool far = true; foreach (var p in picked) if (Vector3.Distance(p, r) < 30f) { far = false; break; }
                if (!far) continue;
                picked.Add(r); if (picked.Count == 3) break;
            }
            if (Ctx.Has("town.wallTop")) picked.Add(Ctx.Get<Vector3>("town.wallTop"));
            if (picked.Count == 0) { var roofs = Ctx.Get<Vector3[]>("town.rooftops"); if (roofs != null && roofs.Length > 0) picked.Add(roofs[0]); }
            var list = new System.Collections.Generic.List<Cannon>();
            foreach (var r in picked) list.Add(Cannon.Place(r, 0f));
            Ctx.Set("cannons", list.ToArray());
            Debug.Log("[Cannons] placed " + list.Count + " on roofs up to " + (picked.Count > 0 ? picked[0].y.ToString("0.0") : "-") + " m");
        }

        // ---------------- pose lineup for the capture rig ----------------

        /// <summary>The phase at which each pose reads best in a still frame.</summary>
        public static float BestPhase(Pose p)
        {
            switch (p)
            {
                case Pose.Idle: return 0.9f;
                case Pose.Run: return 0.10f;
                case Pose.Sprint: return 0.083f;
                case Pose.Fly: return 0.5f;
                case Pose.Slash: return 0.29f;
                case Pose.Land: return 0.1f;
                case Pose.Stagger: return 0.3f;
                case Pose.Kneel: return 0.5f;
                case Pose.Swipe: return 0.52f;
                case Pose.Grab: return 0.6f;
                case Pose.Stomp: return 0.6f;
            }
            return 0f;
        }

        public static readonly Pose[] AllPoses = (Pose[])Enum.GetValues(typeof(Pose));

        public static GameObject BuildLineup()
        {
            var existing = Ctx.Get<GameObject>("proxyLineup");
            if (existing != null) return existing;
            var root = new GameObject("ProxyLineup");
            var director = root.AddComponent<LineupDirector>();
            Font font = null;
            try { font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (Exception) { }

            // Every row faces +Z and is photographed looking -Z, so the rows never stack up
            // behind each other; LineupDirector shows only the row the camera is standing in front of.
            var mikasaRow = Group(root, "Row_Mikasa");
            var titanRow = Group(root, "Row_Titan");
            var bossRow = Group(root, "Row_Boss");
            for (int i = 0; i < AllPoses.Length; i++)
            {
                var pose = AllPoses[i];
                float fly = pose == Pose.Fly ? 1.4f : 0f;
                var m = MikasaProxy.Build("Lineup_Mikasa_" + pose, new Vector3(MikasaRowX0 + i * MikasaRowStep, fly, MikasaRowZ), 0f);
                m.transform.SetParent(mikasaRow, true);
                m.rig.Snap(pose, BestPhase(pose)); m.rig.Paused = true;
                Label(m.transform, pose.ToString(), MikasaProxy.Height, font);

                // titans stand three-quarter on (right side toward the camera) so the acting limb reads in profile
                var t = TitanProxy.Build("Lineup_Titan_" + pose, TitanProxy.SmallHeight, new Vector3(TitanRowX0 + i * TitanRowStep, fly * 3f, TitanRowZ), TitanLineupYaw);
                t.transform.SetParent(titanRow, true);
                t.rig.Snap(pose, BestPhase(pose)); t.rig.Paused = true;
                Label(t.transform, pose.ToString(), TitanProxy.SmallHeight, font);

                var bs = TitanProxy.Build("Lineup_Boss_" + pose, TitanProxy.BossHeight, new Vector3(BossRowX0 + i * BossRowStep, fly * 6f, BossRowZ), TitanLineupYaw);
                bs.transform.SetParent(bossRow, true);
                bs.rig.Snap(pose, BestPhase(pose)); bs.rig.Paused = true;
                Label(bs.transform, pose.ToString(), TitanProxy.BossHeight, font);
            }

            // scale trio: boss, titan, Mikasa side by side, idle
            var trio = Group(root, "ScaleTrio");
            var b3 = TitanProxy.Build("Scale_Boss", TitanProxy.BossHeight, new Vector3(-60f, 0f, -40f), 0f);
            var t3 = TitanProxy.Build("Scale_Titan", TitanProxy.SmallHeight, new Vector3(-72f, 0f, -40f), 0f);
            var m3 = MikasaProxy.Build("Scale_Mikasa", new Vector3(-77.5f, 0f, -40f), 0f);
            b3.transform.SetParent(trio, true); t3.transform.SetParent(trio, true); m3.transform.SetParent(trio, true);
            b3.rig.Snap(Pose.Idle, 0.9f); t3.rig.Snap(Pose.Idle, 2.2f); m3.rig.Snap(Pose.Idle, 1.5f);
            Label(b3.transform, "15 m", TitanProxy.BossHeight, font);
            Label(t3.transform, "7 m", TitanProxy.SmallHeight, font);
            Label(m3.transform, "1.7 m", MikasaProxy.Height, font);

            director.mikasaRow = mikasaRow.gameObject;
            director.titanRow = titanRow.gameObject;
            director.bossRow = bossRow.gameObject;
            director.trio = trio.gameObject;
            director.Apply();
            Ctx.Set("proxyLineup", root);
            return root;
        }

        public const float MikasaRowZ = 60f, MikasaRowX0 = 40f, MikasaRowStep = 3.5f;
        public const float TitanRowZ = -10f, TitanRowX0 = 40f, TitanRowStep = 9f;
        public const float BossRowZ = -100f, BossRowX0 = 40f, BossRowStep = 15f;
        public const float TitanLineupYaw = -65f;

        static Transform Group(GameObject root, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root.transform, false);
            return g.transform;
        }

        static void Label(Transform who, string text, float height, Font font)
        {
            if (font == null) return;
            var go = new GameObject("Label");
            go.transform.SetParent(who, false);
            go.transform.localPosition = new Vector3(0, height * 1.12f, 0);
            go.transform.rotation = Quaternion.Euler(0, 180f, 0); // world-facing: rows are photographed from +Z whatever the figure's yaw
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.text = text;
            tm.fontSize = 48;
            tm.characterSize = height * 0.025f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = new Color(0.08f, 0.08f, 0.1f);
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = font.material;
        }
    }
}
