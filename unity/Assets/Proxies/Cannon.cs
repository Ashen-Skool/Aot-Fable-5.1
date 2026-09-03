using UnityEngine;
using Shared;

namespace Proxies
{
    /// <summary>A rooftop cannon. Walk up, press E: it swings onto the Titan, fires a shell with flash and recoil, big damage.</summary>
    public class Cannon : MonoBehaviour
    {
        public float useRadius = 5f, damage = 40f, reload = 5f, shellSpeed = 70f;
        Transform barrel; float cooldown, recoil; Light flash; Transform shell; Vector3 shellVel; bool shellLive; float shellT;
        static GameObject prefab;
        public static Cannon Place(Vector3 roofPoint, float yaw)
        {
            if (prefab == null) prefab = Resources.Load<GameObject>("Props/Cannon");
            var host = new GameObject("Cannon"); host.transform.position = roofPoint; host.transform.rotation = Quaternion.Euler(0, yaw, 0);
            var c = host.AddComponent<Cannon>();
            var mount = new GameObject("Mount").transform; mount.SetParent(host.transform, false); c.barrel = mount;
            if (prefab != null)
            {
                var inst = Instantiate(prefab, mount); inst.name = "Model";
                foreach (var col in inst.GetComponentsInChildren<Collider>()) Destroy(col);
                PropAlign.Align(inst.transform, tipSmaller: true, length: 3.4f, pivotFrac: 0.35f);
                // stand on the roof: lift so the lowest point of the model sits on the roof point
                var rs = inst.GetComponentsInChildren<Renderer>(); var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                inst.transform.position += Vector3.up * (roofPoint.y - b.min.y);
                var mat = PropAlign.TexturedLit("Props/CannonTex", 0.5f, 0.7f);
                foreach (var r in rs) r.sharedMaterial = mat;
            }
            else
            {
                var body = GameObject.CreatePrimitive(PrimitiveType.Cylinder); body.transform.SetParent(mount, false); body.transform.localRotation = Quaternion.Euler(90, 0, 0); body.transform.localScale = new Vector3(0.5f, 1.6f, 0.5f); body.transform.localPosition = new Vector3(0, 1.2f, 1.2f); Destroy(body.GetComponent<Collider>());
            }
            var lgo = new GameObject("Flash"); lgo.transform.SetParent(host.transform, false); lgo.transform.localPosition = new Vector3(0, 1.6f, 3.2f);
            c.flash = lgo.AddComponent<Light>(); c.flash.type = LightType.Point; c.flash.range = 25f; c.flash.color = new Color(1f, 0.75f, 0.4f); c.flash.intensity = 0f;
            var sh = GameObject.CreatePrimitive(PrimitiveType.Sphere); sh.name = "Shell"; sh.transform.localScale = Vector3.one * 0.5f; Destroy(sh.GetComponent<Collider>()); sh.GetComponent<Renderer>().sharedMaterial = Mats.Lit(new Color(0.15f, 0.15f, 0.16f), 0.4f, 0.8f); sh.SetActive(false); c.shell = sh.transform;
            return c;
        }

        void Update()
        {
            float dt = Time.deltaTime; cooldown -= dt;
            var boss = Ctx.Get<GameObject>("boss");
            var player = Ctx.Get<Component>("player");
            // track the Titan whenever he is around
            if (boss != null)
            {
                Vector3 to = boss.transform.position + Vector3.up * 8f - transform.position; to.y = 0f;
                if (to.sqrMagnitude > 1f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(to.normalized, Vector3.up), 60f * dt);
            }
            recoil = Mathf.MoveTowards(recoil, 0f, dt * 2f);
            barrel.localPosition = Vector3.back * recoil * 0.6f;
            flash.intensity = Mathf.MoveTowards(flash.intensity, 0f, dt * 40f);
            if (player != null && Vector3.Distance(player.transform.position, transform.position) < useRadius)
            {
                Ctx.Set("cannonPrompt", cooldown > 0f ? "cannon reloading " + cooldown.ToString("0.0") + " s" : "<b>E</b> fire the cannon");
                if (cooldown <= 0f && UnityEngine.Input.GetKeyDown(KeyCode.E) && boss != null) Fire(boss);
            }
            if (shellLive)
            {
                shellT += dt; shellVel += Physics.gravity * 0.35f * dt; shell.position += shellVel * dt;
                var brain = boss != null ? boss.GetComponent<TitanBrain>() : null;
                if (boss != null && Vector3.Distance(shell.position, boss.transform.position + Vector3.up * 7.5f) < 5f) { brain?.Hit("cannon", shell.position); Impact(shell.position); }
                else if (shellT > 4f || shell.position.y < -5f) { shellLive = false; shell.gameObject.SetActive(false); }
            }
        }

        void Fire(GameObject boss)
        {
            cooldown = reload; recoil = 1f; flash.intensity = 60f;
            Vector3 muzzle = transform.position + transform.forward * 3.0f + Vector3.up * 1.6f;
            Vector3 target = boss.transform.position + Vector3.up * 8f;
            shell.gameObject.SetActive(true); shell.position = muzzle; shellVel = (target - muzzle).normalized * shellSpeed; shellLive = true; shellT = 0f;
            var cam = Ctx.Get<Camera>("camera"); var camT = Ctx.Get<Component>("cameraRig"); // shake if the rig exposes it
            Ctx.Set("cannonFired", Time.time);
        }

        void Impact(Vector3 at)
        {
            shellLive = false; shell.gameObject.SetActive(false);
            var burst = GameObject.CreatePrimitive(PrimitiveType.Sphere); burst.transform.position = at; burst.transform.localScale = Vector3.one * 4f; Destroy(burst.GetComponent<Collider>());
            burst.GetComponent<Renderer>().sharedMaterial = Mats.Unlit(new Color(1f, 0.85f, 0.6f)); Destroy(burst, 0.18f);
        }
    }
}
