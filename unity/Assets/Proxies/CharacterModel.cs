using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Shared.Rigs;
using Pose = Shared.Rigs.Pose;

namespace Characters
{
    /// <summary>
    /// A real rigged character (FBX in Resources/Characters) dressed over a proxy host. Hides the proxy's
    /// primitive geometry, keeps its collider and sockets, and implements IPoser through the Playables API so
    /// no AnimatorController asset is needed.
    /// </summary>
    public class CharacterModel : MonoBehaviour, IPoser
    {
        public Animator animator;
        public Pose Current { get; private set; } = Pose.Idle;
        public float Phase { get => phase; set { phase = value; ApplyPhase(); } }
        public float Speed { get; set; } = 1f;
        public bool Paused { get; set; }

        PlayableGraph graph;
        AnimationMixerPlayable mixer;
        readonly Dictionary<string, AnimationClip> clips = new Dictionary<string, AnimationClip>();
        readonly List<AnimationClipPlayable> ports = new List<AnimationClipPlayable>();
        int active = -1, previous = -1; float fade; const float FadeTime = 0.15f; float phase;
        static readonly Dictionary<Pose, string> Map = new Dictionary<Pose, string>
        {
            { Pose.Idle, "combatidle" }, { Pose.Run, "runfast" }, { Pose.Sprint, "runfast" }, { Pose.Fly, "jump" }, { Pose.Swing, "ropehang" },
            { Pose.Slash, "weaponcombo" }, { Pose.Land, "land" }, { Pose.Stagger, "hit" },
            { Pose.Kneel, "kneel" }, { Pose.Swipe, "swipe" }, { Pose.Grab, "grab" }, { Pose.Stomp, "stomp" },
        };

        /// <summary>Dress a proxy host with the model; returns null (host untouched) if the resource is missing.</summary>
        public static CharacterModel TryDress(GameObject host, string resource, float height)
        {
            var prefab = Resources.Load<GameObject>(resource);
            if (prefab == null) { Debug.Log("[CharacterModel] no resource " + resource + ", keeping proxy"); return null; }
            var inst = Instantiate(prefab, host.transform);
            inst.name = "Model";
            inst.transform.localPosition = Vector3.zero; inst.transform.localRotation = Quaternion.identity;
            var b = Bounds(inst); float h = b.size.y > 0.01f ? b.size.y : height;
            inst.transform.localScale = Vector3.one * (height / h);
            inst.transform.localPosition = new Vector3(0, -b.min.y * (height / h), 0); // feet on the host origin
            foreach (var r in host.GetComponentsInChildren<MeshRenderer>(true))
                if (r.gameObject.name.StartsWith("Geo_") || r.transform.parent == host.transform && r.gameObject != inst) r.enabled = false;
            var m = inst.AddComponent<CharacterModel>();
            m.animator = inst.GetComponent<Animator>() ?? inst.AddComponent<Animator>();
            m.animator.applyRootMotion = false;
            m.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            m.ApplyTextures(resource + "Tex");
            if (resource.EndsWith("Mikasa")) m.AddBlades(height);
            m.BuildGraph(resource);
            m.SetPose(Pose.Idle);
            return m;
        }

        /// <summary>Unity does not unpack FBX-embedded textures; build a URP Lit material from the PBR maps in Resources.</summary>
        void ApplyTextures(string texFolder)
        {
            var baseMap = Resources.Load<Texture2D>(texFolder + "/base_color");
            if (baseMap == null) { Debug.Log("[CharacterModel] no textures at " + texFolder); return; }
            var mat = Shared.Mats.Lit(Color.white, 0.15f);
            mat.name = name + "_Skin";
            mat.SetTexture("_BaseMap", baseMap); mat.mainTexture = baseMap;
            var nrm = Resources.Load<Texture2D>(texFolder + "/normal");
            if (nrm != null) { mat.EnableKeyword("_NORMALMAP"); mat.SetTexture("_BumpMap", nrm); mat.SetFloat("_BumpScale", 0.6f); }
            mat.SetFloat("_Metallic", 0f);
            foreach (var r in GetComponentsInChildren<Renderer>(true)) { var ms = r.sharedMaterials; for (int i = 0; i < ms.Length; i++) ms[i] = mat; r.sharedMaterials = ms; }
        }

        static UnityEngine.Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) return new UnityEngine.Bounds(go.transform.position, Vector3.zero);
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return new UnityEngine.Bounds(b.center - go.transform.position, b.size);
        }

        void BuildGraph(string resource)
        {
            var own = Resources.LoadAll<AnimationClip>(resource);
            foreach (var c in own) clips[c.name] = c;
            graph = PlayableGraph.Create("Character:" + name);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            mixer = AnimationMixerPlayable.Create(graph, 0);
            var output = AnimationPlayableOutput.Create(graph, "anim", animator);
            output.SetSourcePlayable(mixer);
            graph.Play();
        }

        static readonly Dictionary<string, string[]> Alternates = new Dictionary<string, string[]>
        {
            { "hit", new[] { "stagger" } }, { "stagger", new[] { "hit" } }, { "running_glb_url", new[] { "walking_glb_url" } }, { "jump", new[] { "sprint", "running_glb_url" } },
            { "combatidle", new[] { "idle" } }, { "swordrun", new[] { "running_glb_url", "sprint" } }, { "spinjump", new[] { "jump", "sprint" } }, { "weaponcombo", new[] { "slash", "swipe" } },
            { "runfast", new[] { "running_glb_url", "sprint" } }, { "ropehang", new[] { "spinjump", "jump" } },
            { "swipe", new[] { "bladespin", "weaponcombo", "slash" } },
        };
        int Port(string clipName)
        {
            if (!clips.TryGetValue(clipName, out var clip))
            {
                if (Alternates.TryGetValue(clipName, out var alts)) foreach (var a in alts) if (clips.TryGetValue(a, out clip)) break;
                if (clip == null && !clips.TryGetValue("idle", out clip)) return -1;
            }
            for (int i = 0; i < ports.Count; i++) if (ports[i].GetAnimationClip() == clip) return i;
            var p = AnimationClipPlayable.Create(graph, clip);
            p.SetApplyFootIK(false);
            int idx = ports.Count; ports.Add(p);
            mixer.SetInputCount(ports.Count);
            graph.Connect(p, 0, mixer, idx);
            mixer.SetInputWeight(idx, 0f);
            return idx;
        }

        public void SetPose(Pose pose)
        {
            if (pose == Current && active >= 0) return;
            Current = pose; holdEnd = false;
            int idx = Port(Map.TryGetValue(pose, out var n) ? n : "idle");
            if (idx < 0) return;
            previous = active; active = idx; fade = 0f; phase = 0f;
            ports[idx].SetTime(0); ports[idx].SetDone(false);
            Paused = pose == Pose.Fly || pose == Pose.Swing; // airborne poses hold a frame: jump apex, or the hang
            if (pose == Pose.Fly) { phase = 0.5f; ApplyPhase(); }
            if (pose == Pose.Swing) { phase = 0.3f; ApplyPhase(); }
            Speed = pose == Pose.Sprint ? 1.35f : 1f;
        }

        /// <summary>Play an arbitrary clip once (e.g. death) outside the pose set.</summary>
        public void PlayClip(string clipName)
        {
            int idx = Port(clipName); if (idx < 0) return;
            previous = active; active = idx; fade = 0f; phase = 0f; Paused = false; Speed = 1f; Current = Pose.Stagger;
            ports[idx].SetTime(0); ports[idx].SetDone(false);
            holdEnd = true;
        }
        bool holdEnd;

        public void Snap(Pose pose, float ph) { SetPose(pose); phase = ph; ApplyPhase(); Tick(0f); }

        void ApplyPhase()
        {
            if (active < 0) return;
            var clip = ports[active].GetAnimationClip();
            ports[active].SetTime(phase * clip.length);
        }

        public void Tick(float dt)
        {
            if (!graph.IsValid() || active < 0) return;
            if (!Paused && dt > 0f)
            {
                var clip = ports[active].GetAnimationClip();
                phase += dt * Speed / Mathf.Max(0.01f, clip.length);
                if (clip.isLooping) phase %= 1f; else phase = Mathf.Min(1f, phase);
                if (holdEnd && phase >= 1f) Paused = true;
                ApplyPhase();
            }
            fade = Mathf.Min(1f, fade + (dt > 0f ? dt / FadeTime : 1f));
            for (int i = 0; i < ports.Count; i++)
                mixer.SetInputWeight(i, i == active ? fade : (i == previous ? 1f - fade : 0f));
            graph.Evaluate(0f);
        }

        float plantY; bool plantInit;
        void LateUpdate()
        {
            if (!Paused || fade < 1f) Tick(Time.deltaTime);
            PlantFeet();
            TrackBlades();
        }

        /// <summary>
        /// Keep the rendered feet on the host origin every frame. The Meshy clips carry a hip height that does not match
        /// the rescaled rig, so the body would otherwise ride up to 5 m above the ground. Airborne poses are left alone.
        /// </summary>
        void PlantFeet()
        {
            if (Current == Pose.Fly || Current == Pose.Swing) return;
            float feet = float.MaxValue;
            foreach (var b in new[] { HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot, HumanBodyBones.LeftToes, HumanBodyBones.RightToes })
            { var t = animator.GetBoneTransform(b); if (t != null) feet = Mathf.Min(feet, t.position.y); }
            if (feet == float.MaxValue) return;
            float sole = 0.03f * transform.lossyScale.x * 100f; // toe bone sits a little above the sole (FBX unit scale ~0.01)
            float wantLocalY = transform.localPosition.y - (feet - sole - transform.parent.position.y);
            plantY = plantInit ? Mathf.Lerp(plantY, wantLocalY, 0.5f) : wantLocalY; plantInit = true;
            var lp = transform.localPosition; lp.y = plantY; transform.localPosition = lp;
        }

        /// <summary>
        /// Twin ODM blades gripped in the palms: the blade runs from the palm along the finger direction (hand -> middle
        /// finger), with a short grip behind the hand, so it reads as held rather than growing out of the wrist.
        /// </summary>
        readonly System.Collections.Generic.List<(Transform root, Transform hand, Transform arm)> blades = new System.Collections.Generic.List<(Transform, Transform, Transform)>();
        /// <summary>Twin ODM blades: the modelled prop (Resources/Props/Blade, pivot in the grip, +Z along the blade) in each fist.</summary>
        void AddBlades(float height)
        {
            var prefab = Resources.Load<GameObject>("Props/Blade");
            var pairs = new[] { (HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, "Blade_R"), (HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, "Blade_L") };
            foreach (var (handB, armB, nm) in pairs)
            {
                var hand = animator.GetBoneTransform(handB); var arm = animator.GetBoneTransform(armB);
                if (hand == null || arm == null) continue;
                var root = new GameObject(nm); root.transform.SetParent(transform.parent, true);
                if (prefab != null)
                {
                    var inst = Instantiate(prefab, root.transform); inst.name = "Blade";
                    foreach (var c in inst.GetComponentsInChildren<Collider>()) Destroy(c);
                    AlignPropToZ(inst.transform);
                    inst.transform.localScale = Vector3.one * (height / 1.7f) * inst.transform.localScale.x;
                    var mat = Shared.Mats.Lit(Color.white, 0.6f, 0.9f);
                    var bc = Resources.Load<Texture2D>("Props/BladeTex/base_color"); if (bc != null) { mat.SetTexture("_BaseMap", bc); mat.mainTexture = bc; }
                    var nr = Resources.Load<Texture2D>("Props/BladeTex/normal"); if (nr != null) { mat.EnableKeyword("_NORMALMAP"); mat.SetTexture("_BumpMap", nr); }
                    foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.sharedMaterial = mat;
                }
                else
                {
                    var blade = GameObject.CreatePrimitive(PrimitiveType.Cube); blade.name = "Edge"; Destroy(blade.GetComponent<Collider>());
                    blade.transform.SetParent(root.transform, false); blade.transform.localScale = new Vector3(0.012f, 0.05f, 0.85f); blade.transform.localPosition = new Vector3(0, 0, 0.4f);
                }
                blades.Add((root.transform, hand, arm));
            }
        }

        /// <summary>The FBX axis mapping is not worth trusting: measure the mesh, put its long axis on +Z (tip forward, pivot stays).</summary>
        static void AlignPropToZ(Transform t)
        {
            var mf = t.GetComponentInChildren<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return;
            var b = mf.sharedMesh.bounds; var e = b.size; int axis = e.x >= e.y && e.x >= e.z ? 0 : (e.y >= e.z ? 1 : 2);
            Vector3 dir = Vector3.zero; dir[axis] = b.center[axis] >= 0f ? 1f : -1f; // the tip is the far side of the pivot
            dir = -dir; // measured in-game: the grip ended up outboard, so the far side of the pivot is the grip on this export
            dir = mf.transform.TransformDirection(dir);
            Vector3 upHint = Vector3.up;
            t.rotation = Quaternion.FromToRotation(dir, Vector3.forward) * t.rotation;
        }

        void TrackBlades()
        {
            for (int i = 0; i < blades.Count; i++)
            {
                var (root, hand, arm) = blades[i];
                Vector3 axis = hand.position - arm.position; if (axis.sqrMagnitude < 1e-6f) continue;
                axis.Normalize();
                Vector3 up = Vector3.Cross(axis, transform.parent.right); if (up.sqrMagnitude < 1e-4f) up = Vector3.up;
                root.position = hand.position + axis * 0.02f;
                root.rotation = Quaternion.LookRotation(axis, up);
            }
        }
        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
