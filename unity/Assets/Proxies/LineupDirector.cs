using UnityEngine;
using Shared;

namespace Proxies
{
    /// <summary>
    /// Shows only the lineup row the capture camera is standing in front of, keyed on the
    /// camera position, so wide shots of one row never have another row in the background.
    /// Rows: Mikasa z=60, Titan z=-10, Boss z=-100 (all x > 20); the scale trio lives at x < 0.
    /// </summary>
    public class LineupDirector : MonoBehaviour
    {
        public GameObject mikasaRow, titanRow, bossRow, trio;
        Vector3 last = new Vector3(float.NaN, 0, 0);

        void Update()
        {
            var cam = Ctx.Get<Camera>("camera") ?? Camera.main;
            if (cam == null) return;
            var p = cam.transform.position;
            if (p == last) return;
            last = p;
            Apply();
        }

        public void Apply()
        {
            var cam = Ctx.Get<Camera>("camera") ?? Camera.main;
            var p = cam != null ? cam.transform.position : Vector3.zero;
            // the visible row is the nearest one the camera stands in front of (rows face +Z)
            bool rows = p.x > 20f;
            bool mikasa = p.z > ProxyBootstrap.MikasaRowZ + 1f;
            bool titan = !mikasa && p.z > ProxyBootstrap.TitanRowZ + 1f;
            bool boss = !mikasa && !titan;
            Set(mikasaRow, rows && mikasa);
            Set(titanRow, rows && titan);
            Set(bossRow, rows && boss);
            Set(trio, p.x <= 0f);
        }

        static void Set(GameObject g, bool on)
        {
            if (g != null && g.activeSelf != on) g.SetActive(on);
        }
    }
}
