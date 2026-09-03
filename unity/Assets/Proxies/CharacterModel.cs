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
            { Pose.Idle, "idle" }, { Pose.Run, "running_glb_url" }, { Pose.Sprint, "running_glb_url" }, { Pose.Fly, "jump" },
            { Pose.Slash, "slash" }, { Pose.Land, "land" }, { Pose.Stagger, "hit" },
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
            Current = pose;
            int idx = Port(Map.TryGetValue(pose, out var n) ? n : "idle");
            if (idx < 0) return;
            previous = active; active = idx; fade = 0f; phase = 0f;
            ports[idx].SetTime(0); ports[idx].SetDone(false);
            Paused = pose == Pose.Fly; // Fly = jump clip frozen at its apex
            if (pose == Pose.Fly) { phase = 0.55f; ApplyPhase(); }
            Speed = pose == Pose.Sprint ? 1.35f : 1f;
        }

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
                ApplyPhase();
            }
            fade = Mathf.Min(1f, fade + (dt > 0f ? dt / FadeTime : 1f));
            for (int i = 0; i < ports.Count; i++)
                mixer.SetInputWeight(i, i == active ? fade : (i == previous ? 1f - fade : 0f));
            graph.Evaluate(0f);
        }

        int groundFixFrames = 3;
        void LateUpdate()
        {
            if (!Paused || fade < 1f) Tick(Time.deltaTime);
            // After the rig has evaluated, put the rendered feet exactly on the host origin (the bounds used at
            // dress time come from an unevaluated skinned mesh and can float the model).
            if (groundFixFrames > 0 && --groundFixFrames == 0)
            {
                var rs = GetComponentsInChildren<SkinnedMeshRenderer>(); if (rs.Length == 0) return;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float delta = b.min.y - transform.parent.position.y;
                if (Mathf.Abs(delta) > 0.02f) transform.position += Vector3.down * delta;
            }
        }

        /// <summary>Twin ODM blades on the hands (thin steel boxes until a modelled blade exists).</summary>
        void AddBlades(float height)
        {
            foreach (var (bone, nm) in new[] { (HumanBodyBones.RightHand, "Blade_R"), (HumanBodyBones.LeftHand, "Blade_L") })
            {
                var hand = animator.GetBoneTransform(bone); if (hand == null) continue;
                var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = nm; Destroy(g.GetComponent<Collider>());
                g.transform.SetParent(hand, false);
                float L = height * 0.55f;
                g.transform.localScale = new Vector3(0.012f, 0.05f, L) / transform.localScale.x;
                g.transform.localPosition = new Vector3(0f, 0f, L * 0.45f) / transform.localScale.x;
                g.GetComponent<Renderer>().sharedMaterial = Shared.Mats.Lit(new Color(0.82f, 0.85f, 0.9f), 0.9f, 1f);
            }
        }
        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}
