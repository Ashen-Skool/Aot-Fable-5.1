using Shared;
using UnityEngine;
using UnityEngine.Video;

namespace ODM
{
    /// <summary>
    /// The in-game HUD, one typeface family (Bebas Neue for numbers and titles, Oswald for labels): crosshair with the
    /// hook target, HP and gas at the bottom left, speed at the bottom right, the Titan bar top centre with the
    /// hamstring chips and the NAPE OPEN flash, floating damage numbers, cannon markers, prompts, hit vignette,
    /// the title and ending screens.
    /// </summary>
    public static class Hud
    {
        static Font bebas, oswald; static bool fontsTried;
        static GUIStyle sTitle, sBig, sNum, sLabel, sSmall, sPrompt, sPop;
        static Texture2D vignette, ring;
        static float playStart = -1f, helpFade = 1f, fpsAcc, fpsShown; static int fpsN; static bool orbitStarted;
        static VideoPlayer titleVideo; static RenderTexture titleRt; static string videoFile;
        /// <summary>A StreamingAssets video looping into a render texture (title.mp4 behind the title, nape.mp4 for the kill cutscene).
        /// Returns null while it prepares or when the file is missing.</summary>
        static Texture Video(string file, bool loop)
        {
            if (videoFile != file)
            {
                StopVideo(); videoFile = file;
                var path = System.IO.Path.Combine(Application.streamingAssetsPath, file);
                if (System.IO.File.Exists(path))
                {
                    var go = new GameObject("HudVideo"); Object.DontDestroyOnLoad(go);
                    if (titleRt == null) titleRt = new RenderTexture(1920, 1080, 0);
                    titleVideo = go.AddComponent<VideoPlayer>();
                    titleVideo.playOnAwake = false; titleVideo.isLooping = loop; titleVideo.source = VideoSource.Url; titleVideo.url = path;
                    titleVideo.renderMode = VideoRenderMode.RenderTexture; titleVideo.targetTexture = titleRt; titleVideo.audioOutputMode = VideoAudioOutputMode.None;
                    titleVideo.aspectRatio = VideoAspectRatio.FitOutside; titleVideo.Play();
                }
            }
            if (titleVideo != null && loop && !titleVideo.isPlaying && titleVideo.isPrepared) titleVideo.Play();
            return titleVideo != null && titleVideo.isPrepared ? titleRt : null;
        }
        static bool VideoLoaded => titleVideo != null;
        static bool VideoDone => titleVideo != null && titleVideo.isPrepared && !titleVideo.isLooping && titleVideo.frameCount > 0 && titleVideo.frame >= 0 && (ulong)titleVideo.frame >= titleVideo.frameCount - 1;
        static void StopVideo() { if (titleVideo != null) { titleVideo.Stop(); Object.Destroy(titleVideo.gameObject); titleVideo = null; } videoFile = null; }
        static void DrawVideoCover(Texture vid, float W, float H)
        {
            // cover the screen, keep the clip's own aspect (title 2912x1280, nape 2544x1456)
            float va = titleVideo != null && titleVideo.width > 0 && titleVideo.height > 0 ? (float)titleVideo.width / titleVideo.height : 2912f / 1280f;
            float sa = W / H; float vw = sa > va ? W : H * va, vh = sa > va ? W / va : H;
            GUI.DrawTexture(new Rect((W - vw) * 0.5f, (H - vh) * 0.5f, vw, vh), vid, ScaleMode.StretchToFill);
        }

        static void Init()
        {
            if (sTitle != null) return;
            if (!fontsTried) { fontsTried = true; bebas = Resources.Load<Font>("Fonts/BebasNeue-Regular"); oswald = Resources.Load<Font>("Fonts/Oswald"); }
            var baseFont = bebas != null ? bebas : GUI.skin.font;
            var labelFont = oswald != null ? oswald : GUI.skin.font;
            GUIStyle Mk(Font f, int size, TextAnchor a, FontStyle fs = FontStyle.Normal) { var st = new GUIStyle(GUI.skin.label) { font = f, fontSize = size, alignment = a, richText = true, fontStyle = fs, wordWrap = false }; st.normal.textColor = Color.white; st.clipping = TextClipping.Overflow; return st; }
            sTitle = Mk(baseFont, 110, TextAnchor.MiddleCenter);
            sBig = Mk(baseFont, 44, TextAnchor.MiddleLeft);
            sNum = Mk(baseFont, 30, TextAnchor.MiddleLeft);
            sLabel = Mk(labelFont, 15, TextAnchor.MiddleLeft, FontStyle.Bold);
            sSmall = Mk(labelFont, 13, TextAnchor.MiddleLeft);
            sPrompt = Mk(baseFont, 34, TextAnchor.MiddleCenter);
            sPop = Mk(baseFont, 34, TextAnchor.MiddleCenter);
            vignette = new Texture2D(128, 128, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 128; y++) for (int x = 0; x < 128; x++)
            {
                float dx = (x + 0.5f) / 128f - 0.5f, dy = (y + 0.5f) / 128f - 0.5f;
                float d = Mathf.Sqrt(dx * dx * 1.3f + dy * dy) * 2f;
                float a = Mathf.Clamp01((d - 0.55f) / 0.5f); a = a * a;
                vignette.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            vignette.Apply();
            ring = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(32f, 32f)) / 32f;
                float a = Mathf.Clamp01(1f - Mathf.Abs(d - 0.8f) / 0.12f);
                ring.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            ring.Apply();
        }

        static float S => Screen.height / 1080f;
        static void Box(float x, float y, float w, float h, Color c) { GUI.color = c; GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture); GUI.color = Color.white; }
        static void Text(Rect r, string t, GUIStyle st, Color c, float shadow = 2f)
        {
            var keep = st.normal.textColor;
            st.normal.textColor = new Color(0f, 0f, 0f, c.a * 0.8f); GUI.Label(new Rect(r.x + shadow, r.y + shadow, r.width, r.height), t, st);
            st.normal.textColor = c; GUI.Label(r, t, st);
            st.normal.textColor = keep;
        }
        static GUIStyle Sized(GUIStyle st, float size) { st.fontSize = Mathf.RoundToInt(size * S); return st; }

        public static void Draw(OdmController c, Camera cam)
        {
            Init();
            float W = Screen.width, H = Screen.height, s = S;
            if (!OdmController.TitleDone) { Title(c, W, H, s); return; }
            float introLeft = Ctx.Get<float>("introUntil") - Time.unscaledTime;
            if (introLeft > 0f)
            {
                // the dive: a dark frame that opens as the camera arrives, and the objective
                Box(0, 0, W, H, new Color(0f, 0f, 0f, Mathf.Clamp01(introLeft / 2.6f) * 0.5f));
                var so = Sized(sPrompt, 34f); Text(new Rect(0, H * 0.78f, W, 40f * s), "CUT THE NAPE", so, new Color(1f, 0.85f, 0.4f, Mathf.Clamp01(introLeft * 1.5f)));
                return;
            }
            if (playStart < 0f) playStart = Time.unscaledTime;
            if (Ctx.Get<bool>("napeCutscene")) { Cutscene(c, W, H, s); return; }
            var over = Ctx.Get<string>("gameOver");
            if (!string.IsNullOrEmpty(over)) { Music.Set("ending"); Ending(c, over, W, H, s); return; }   // the card alone, nothing bleeding through
            var brain = Ctx.Get<Proxies.TitanBrain>("bossBrain");
            Music.Set(brain != null && brain.Current == Proxies.TitanBrain.State.Dead ? "ending" : "battle");
            HudEvents.Prune();

            // hit / low-HP vignette
            float hp01 = c.Health / c.HealthMax;
            float vig = c.HitFlash > 0f ? c.HitFlash * 2.2f : 0f;
            if (hp01 < 0.3f && c.Health > 0f) vig = Mathf.Max(vig, 0.35f + 0.25f * Mathf.Sin(Time.unscaledTime * 5f));
            if (vig > 0f) { GUI.color = new Color(0.8f, 0.05f, 0.02f, Mathf.Clamp01(vig)); GUI.DrawTexture(new Rect(0, 0, W, H), vignette); GUI.color = Color.white; }

            // crosshair + hook target
            float cx = W * 0.5f, cy = H * 0.5f;
            bool hooked = c.Hook != HookState.None;
            var xc = hooked ? new Color(1f, 0.6f, 0.2f) : c.AimHasHit ? Color.white : new Color(1f, 1f, 1f, 0.6f);
            float gap = 9f * s, len = 9f * s, th = 2f * s;
            Box(cx - th * 0.5f, cy - gap - len, th, len, xc); Box(cx - th * 0.5f, cy + gap, th, len, xc);
            Box(cx - gap - len, cy - th * 0.5f, len, th, xc); Box(cx + gap, cy - th * 0.5f, len, th, xc);
            Box(cx - th * 0.5f, cy - th * 0.5f, th, th, xc);
            if (c.AimHasHit && !hooked && cam != null)
            {
                float rr = 22f * s; GUI.color = new Color(1f, 1f, 1f, 0.9f); GUI.DrawTexture(new Rect(cx - rr, cy - rr, rr * 2f, rr * 2f), ring); GUI.color = Color.white;
                Text(new Rect(cx + rr + 6f * s, cy - 10f * s, 120f * s, 20f * s), c.AimHitDist.ToString("0") + " m", Sized(sSmall, 14f), new Color(1f, 1f, 1f, 0.85f), 1f);
            }
            if (hooked && cam != null)
            {
                var sp = cam.WorldToScreenPoint(c.Anchor);
                if (sp.z > 0f) { float rr = 14f * s; GUI.color = new Color(1f, 0.6f, 0.2f, 0.9f); GUI.DrawTexture(new Rect(sp.x - rr, H - sp.y - rr, rr * 2f, rr * 2f), ring); GUI.color = Color.white; }
            }

            // bottom left: HP + gas
            float px = 36f * s, py = H - 120f * s, bw = 300f * s;
            Text(new Rect(px, py - 22f * s, 200f * s, 20f * s), "HP", Sized(sLabel, 15f), new Color(1f, 1f, 1f, 0.8f), 1f);
            Box(px - 2f * s, py, bw + 4f * s, 12f * s, new Color(0f, 0f, 0f, 0.6f));
            Box(px, py + 2f * s, bw * hp01, 8f * s, c.HitFlash > 0f ? Color.white : hp01 < 0.3f ? new Color(1f, 0.25f, 0.2f) : new Color(0.86f, 0.18f, 0.14f));
            Text(new Rect(px + bw + 12f * s, py - 12f * s, 120f * s, 36f * s), c.Health <= 0f ? "DOWN" : c.Health.ToString("0"), Sized(sNum, 30f), Color.white);
            float gy = py + 30f * s; float g01 = c.Gas / c.GasMax;
            Text(new Rect(px, gy - 2f * s, 200f * s, 20f * s), "GAS", Sized(sLabel, 15f), new Color(1f, 1f, 1f, 0.8f), 1f);
            const int cells = 12; float cw = (bw - (cells - 1) * 4f * s) / cells;
            for (int i = 0; i < cells; i++)
            {
                float fill = Mathf.Clamp01(g01 * cells - i);
                Box(px + i * (cw + 4f * s), gy + 18f * s, cw, 8f * s, new Color(0f, 0f, 0f, 0.55f));
                if (fill > 0f) Box(px + i * (cw + 4f * s), gy + 18f * s, cw * fill, 8f * s, c.Boosting ? new Color(1f, 1f, 1f) : g01 < 0.2f ? new Color(1f, 0.5f, 0.2f) : new Color(0.72f, 0.9f, 1f));
            }

            // bottom right: speed
            float spd = c.Speed; float k = Mathf.Clamp01(spd / 50f);
            var sc = Color.Lerp(new Color(1f, 1f, 1f, 0.75f), new Color(1f, 0.85f, 0.55f), k);
            var sSpeed = Sized(sBig, 64f + 18f * k); sSpeed.alignment = TextAnchor.LowerRight;
            Text(new Rect(W - 300f * s, H - 150f * s, 220f * s, 80f * s), spd.ToString("0"), sSpeed, sc);
            var sUnit = Sized(sLabel, 15f); sUnit.alignment = TextAnchor.LowerLeft;
            Text(new Rect(W - 76f * s, H - 92f * s, 60f * s, 20f * s), "M/S", sUnit, new Color(1f, 1f, 1f, 0.7f), 1f);
            string state = hooked ? "HOOKED" : c.Boosting ? "GAS" : c.Grounded ? "GROUND" : "AIR";
            var sState = Sized(sLabel, 15f); sState.alignment = TextAnchor.LowerRight;
            Text(new Rect(W - 300f * s, H - 70f * s, 264f * s, 20f * s), state, sState, hooked ? new Color(1f, 0.6f, 0.2f) : new Color(1f, 1f, 1f, 0.6f), 1f);
            sBig.alignment = TextAnchor.MiddleLeft; sLabel.alignment = TextAnchor.MiddleLeft;

            // top centre: the Titan
            if (brain != null && brain.Current != Proxies.TitanBrain.State.Idle)
            {
                float tw = Mathf.Min(560f * s, W * 0.42f), tx = (W - tw) * 0.5f, ty = 34f * s;
                var sName = Sized(sNum, 28f); sName.alignment = TextAnchor.MiddleCenter;
                Text(new Rect(0, ty, W, 30f * s), "ATTACK TITAN", sName, brain.Current == Proxies.TitanBrain.State.Dead ? new Color(0.6f, 0.6f, 0.6f) : Color.white);
                sNum.alignment = TextAnchor.MiddleLeft;
                Box(tx - 2f * s, ty + 34f * s, tw + 4f * s, 14f * s, new Color(0f, 0f, 0f, 0.6f));
                float hp = Mathf.Clamp01(brain.HP / brain.HPMax);
                Box(tx, ty + 36f * s, tw * hp, 10f * s, brain.Current == Proxies.TitanBrain.State.Dead ? new Color(0.4f, 0.4f, 0.4f) : new Color(0.78f, 0.16f, 0.32f));
                // hamstring chips
                float chipW = 70f * s, chipY = ty + 56f * s;
                Chip(tx, chipY, chipW, "L HAM", brain.HamL, s); Chip(tx + tw - chipW, chipY, chipW, "R HAM", brain.HamR, s);
                // the nape line on the bar: the last quarter only falls to the nape cut
                Box(tx + tw * brain.napePhaseAt - 1f * s, ty + 33f * s, 2f * s, 16f * s, new Color(1f, 0.85f, 0.3f, 0.9f));
                if (c.Riding)
                {
                    float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 7f);
                    var dots = ""; for (int i = 0; i < c.StabsToKill; i++) dots += i < c.Stabs ? "\u25CF " : "\u25CB ";
                    var sNape = Sized(sPrompt, 40f);
                    Text(new Rect(0, chipY - 6f * s, W, 40f * s), c.FinalBlow ? "THE NAPE" : "STAB   " + dots.TrimEnd(), sNape, new Color(1f, 0.8f, 0.2f, pulse), 4f);
                    var sh = Sized(sLabel, 18f); sh.alignment = TextAnchor.MiddleCenter; Text(new Rect(0, chipY + 34f * s, W, 26f * s), "LMB  STAB      SPACE  JUMP OFF", sh, new Color(1f, 1f, 1f, 0.8f)); sLabel.alignment = TextAnchor.MiddleLeft;
                }
                else if (brain.NapePhase)
                {
                    float since = Time.unscaledTime - Ctx.Get<float>("napePhaseAt");
                    float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 6f);
                    float big = since < 3f ? Mathf.Lerp(72f, 36f, Mathf.SmoothStep(0f, 1f, (since - 1.5f) / 1.5f)) : 36f;
                    var sNape = Sized(sPrompt, big);
                    Text(new Rect(0, since < 3f ? H * 0.36f : chipY - 4f * s, W, big * 1.2f * s), "GO FOR THE NAPE", sNape, new Color(1f, 0.8f, 0.2f, pulse), 4f);
                    if (since < 3f) { var sh = Sized(sLabel, 20f); sh.alignment = TextAnchor.MiddleCenter; Text(new Rect(0, H * 0.36f + big * 1.25f * s, W, 30f * s), "HOOK HIGH   ·   HIT THE BACK OF HIS NECK FROM THE AIR", sh, new Color(1f, 1f, 1f, 0.85f)); sLabel.alignment = TextAnchor.MiddleLeft; }
                }
                else if (brain.Current == Proxies.TitanBrain.State.Kneel)
                {
                    float pulse = 0.7f + 0.3f * Mathf.Sin(Time.unscaledTime * 8f);
                    var sNape = Sized(sPrompt, 36f);
                    Text(new Rect(0, chipY - 4f * s, W, 34f * s), "NAPE OPEN", sNape, new Color(1f, 0.8f, 0.2f, pulse));
                }
            }

            if (c.Perched) { var sp = Sized(sLabel, 18f); sp.alignment = TextAnchor.MiddleCenter; Text(new Rect(0, H * 0.80f, W, 26f * s), "LMB  LEAP      SHIFT  LAUNCH      SPACE  DROP", sp, new Color(1f, 1f, 1f, 0.75f)); sLabel.alignment = TextAnchor.MiddleLeft; }
            // floating damage numbers
            if (cam != null)
            {
                for (int i = 0; i < HudEvents.Pops.Count; i++)
                {
                    var p = HudEvents.Pops[i]; float age = (Time.unscaledTime - p.t0) / p.life;
                    var sp = cam.WorldToScreenPoint(p.pos); if (sp.z < 0f) continue;
                    float rise = 70f * s * (1f - (1f - age) * (1f - age));
                    float alpha = age < 0.7f ? 1f : 1f - (age - 0.7f) / 0.3f;
                    float size = (34f + 10f * Mathf.Exp(-age * 9f)) * p.size;
                    var st = Sized(sPop, size);
                    Text(new Rect(sp.x - 150f * s, H - sp.y - rise - 24f * s, 300f * s, 48f * s), p.text, st, new Color(p.color.r, p.color.g, p.color.b, alpha));
                }
            }

            // cannon markers
            var cannons = Ctx.Get<Proxies.Cannon[]>("cannons");
            if (cannons != null && cam != null)
            {
                var sm = Sized(sSmall, 13f); sm.alignment = TextAnchor.MiddleCenter;
                // only the two nearest: five labels at once was clutter
                var near = new System.Collections.Generic.List<Proxies.Cannon>();
                foreach (var cn in cannons) if (cn != null) near.Add(cn);
                near.Sort((p, q) => Vector3.Distance(c.transform.position, p.transform.position).CompareTo(Vector3.Distance(c.transform.position, q.transform.position)));
                for (int ci = 0; ci < Mathf.Min(2, near.Count); ci++)
                {
                    var cn = near[ci];
                    Vector3 wp = cn.transform.position + Vector3.up * 2.5f; Vector3 sp = cam.WorldToScreenPoint(wp);
                    bool behind = sp.z < 0f; if (behind) { sp.x = W - sp.x; sp.y = H - sp.y; }
                    float sx = Mathf.Clamp(sp.x, 60f * s, W - 60f * s), sy = Mathf.Clamp(H - sp.y, 120f * s, H - 160f * s);   // below the help and the Titan bar
                    float dist = Vector3.Distance(c.transform.position, cn.transform.position);
                    var col = new Color(1f, 0.8f, 0.3f, behind ? 0.45f : 0.9f);
                    var keep = GUI.matrix; GUIUtility.RotateAroundPivot(45f, new Vector2(sx, sy));
                    Box(sx - 5f * s, sy - 5f * s, 10f * s, 10f * s, col); GUI.matrix = keep;
                    Text(new Rect(sx - 70f * s, sy + 9f * s, 140f * s, 18f * s), "CANNON  " + dist.ToString("0") + " M", sm, col, 1f);
                }
                sSmall.alignment = TextAnchor.MiddleLeft;
            }
            // nape marker: where to hook and cut once he is at a quarter
            if (brain != null && brain.NapePhase && !c.Riding && cam != null && brain.Current != Proxies.TitanBrain.State.Dead)
            {
                Vector3 wp = brain.NapeWorld(); Vector3 sp = cam.WorldToScreenPoint(wp);
                bool behind = sp.z < 0f; if (behind) { sp.x = W - sp.x; sp.y = H - sp.y; }
                float sx = Mathf.Clamp(sp.x, 60f * s, W - 60f * s), sy = Mathf.Clamp(H - sp.y, 120f * s, H - 160f * s);
                float pulse = 0.75f + 0.25f * Mathf.Sin(Time.unscaledTime * 6f);
                float ring = (18f + 6f * Mathf.Sin(Time.unscaledTime * 6f)) * s;
                var col = new Color(1f, 0.82f, 0.25f, (behind ? 0.5f : 1f) * pulse);
                GUI.color = col; GUI.DrawTexture(new Rect(sx - ring, sy - ring, ring * 2f, ring * 2f), RingTex()); GUI.color = Color.white;
                var sm = Sized(sSmall, 14f); sm.alignment = TextAnchor.MiddleCenter;
                float dist = Vector3.Distance(c.transform.position, wp);
                Text(new Rect(sx - 80f * s, sy + ring + 2f * s, 160f * s, 18f * s), "NAPE  " + dist.ToString("0") + " M", sm, col, 1f);
                sSmall.alignment = TextAnchor.MiddleLeft;
            }
            var prompt = Ctx.Get<string>("cannonPrompt");
            if (!string.IsNullOrEmpty(prompt)) { Text(new Rect(0, cy + 80f * s, W, 40f * s), prompt.ToUpperInvariant(), Sized(sPrompt, 30f), new Color(1f, 0.9f, 0.6f)); Ctx.Set("cannonPrompt", ""); }

            // controls, fading out after the first quarter minute
            float played = Time.unscaledTime - playStart;
            helpFade = played < 14f ? 1f : Mathf.Clamp01(1f - (played - 14f) / 2f);
            if (helpFade > 0.01f)
            {
                var sh = Sized(sSmall, 13f); sh.alignment = TextAnchor.UpperLeft;
                Text(new Rect(36f * s, 30f * s, 520f * s, 90f * s),
                    "WASD move · MOUSE aim · SPACE hook / release · SHIFT gas\nLMB slash · E cannon · ESC frees the mouse\nfist roll [ ]  " + Characters.CharacterModel.FistRollDeg.ToString("0") + "°   titan wrist ; '  " + Characters.CharacterModel.TitanHandRollDeg.ToString("0") + "°",
                    sh, new Color(1f, 1f, 1f, 0.7f * helpFade), 1f);
                sSmall.alignment = TextAnchor.MiddleLeft;
            }
            if (OdmController.Paused)
            {
                Box(0, 0, W, H, new Color(0f, 0f, 0f, 0.55f));
                Text(new Rect(0, cy - 70f * s, W, 80f * s), "PAUSED", Sized(sTitle, 72f), Color.white, 3f);
                Text(new Rect(0, cy + 10f * s, W, 34f * s), "CLICK OR ESC  RESUME      R  RESTART      CMD-Q  QUIT", Sized(sPrompt, 26f), new Color(1f, 1f, 1f, 0.85f));
                if (UnityEngine.Input.GetKeyDown(KeyCode.R) && !Reboot.Restarting) { OdmController.Paused = false; playStart = -1f; orbitStarted = false; OdmController.TitleDone = false;  Reboot.Now(); }
            }
            else if (!GameInput.CursorCaptured) Text(new Rect(0, H - 40f * s, W, 22f * s), "CLICK TO CAPTURE THE MOUSE  ·  ESC PAUSES", Sized(sSmall, 14f), new Color(1f, 1f, 1f, 0.7f), 1f);
            // frame rate, tiny, top right, so a build's cost is always visible
            fpsAcc += Time.unscaledDeltaTime; fpsN++;
            if (fpsAcc > 0.5f) { fpsShown = fpsN / fpsAcc; fpsAcc = 0f; fpsN = 0; }
            var sf = Sized(sSmall, 12f); sf.alignment = TextAnchor.UpperRight;
            Text(new Rect(W - 120f * s, 10f * s, 100f * s, 16f * s), fpsShown.ToString("0") + " FPS", sf, new Color(1f, 1f, 1f, fpsShown < 50f ? 0.9f : 0.35f), 1f);
            sSmall.alignment = TextAnchor.MiddleLeft;

        }

        static void Chip(float x, float y, float w, string label, bool on, float s)
        {
            Box(x, y, w, 20f * s, on ? new Color(1f, 0.55f, 0.3f, 0.95f) : new Color(0f, 0f, 0f, 0.5f));
            var st = Sized(sSmall, 13f); st.alignment = TextAnchor.MiddleCenter;
            Text(new Rect(x, y, w, 20f * s), label, st, on ? Color.black : new Color(1f, 1f, 1f, 0.6f), on ? 0f : 1f);
            sSmall.alignment = TextAnchor.MiddleLeft;
        }

        static void Title(OdmController c, float W, float H, float s)
        {
            Music.Set("title");
            Time.timeScale = 0f;   // nothing moves until a key is pressed (the video runs on its own clock)
            Ctx.Set("titleHold", true);
            if (!orbitStarted) { orbitStarted = true; var rig = Ctx.Get<Component>("cameraRig"); if (rig != null) rig.SendMessage("TitleOrbit", SendMessageOptions.DontRequireReceiver); }
            var vid = Video("title.mp4", true);
            if (vid != null) DrawVideoCover(vid, W, H);
            Box(0, 0, W, H, new Color(0.02f, 0.02f, 0.03f, vid != null ? 0.3f : 0.42f));
            Text(new Rect(0, H * 0.30f, W, 120f * s), "AOT FABLE 5.1", Sized(sTitle, 110f), Color.white, 4f);
            var sub = Sized(sLabel, 18f); sub.alignment = TextAnchor.MiddleCenter;
            Text(new Rect(0, H * 0.30f + 122f * s, W, 30f * s), "SHIGANSHINA DISTRICT   ·   ONE TITAN   ·   CUT THE NAPE", sub, new Color(1f, 0.85f, 0.55f));
            Text(new Rect(0, H * 0.30f + 170f * s, W, 80f * s), "WASD move   ·   Mouse aim   ·   Space hook / release   ·   Shift gas   ·   LMB slash   ·   E fire a cannon\nHook a tower, get above him, cut both hamstrings, then the nape.", sub, new Color(1f, 1f, 1f, 0.75f));
            sLabel.alignment = TextAnchor.MiddleLeft;
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 3f);
            Text(new Rect(0, H * 0.30f + 280f * s, W, 40f * s), "PRESS ANY KEY", Sized(sPrompt, 34f), new Color(1f, 1f, 1f, pulse));
            if (UnityEngine.Input.anyKeyDown || Ctx.Get<bool>("autoStart"))
            {
                Ctx.Set("autoStart", false); StopVideo();
                OdmController.TitleDone = true; Time.timeScale = 1f; Sfx.Play("ui", c.transform.position, 1f, 0.6f);
                Ctx.Set("titleHold", false); Ctx.Set("introUntil", Time.unscaledTime + 2.6f);
                var rig = Ctx.Get<Component>("cameraRig"); if (rig != null) rig.SendMessage("BeginIntroDive", SendMessageOptions.DontRequireReceiver);
            }
        }

        /// <summary>The nape kill: the game freezes, StreamingAssets/nape.mp4 plays once (7 s), then the Titan drops and the
        /// win card follows. Without the file: a 1.6 s black beat with the words.</summary>
        public const float CutsceneSeconds = 7f;
        static void Cutscene(OdmController c, float W, float H, float s)
        {
            Time.timeScale = 0f; Ctx.Set("titleHold", true);
            float since = Time.unscaledTime - Ctx.Get<float>("napeCutsceneAt");
            var vid = Video("nape.mp4", false);
            bool has = VideoLoaded;
            Box(0, 0, W, H, Color.black);
            if (vid != null) DrawVideoCover(vid, W, H);
            else if (!has) { var st = Sized(sTitle, 96f); Text(new Rect(0, H * 0.42f, W, 110f * s), "THE NAPE", st, new Color(1f, 0.9f, 0.7f, Mathf.Clamp01(since * 2f)), 4f); }
            // letterbox + fade in/out
            float fade = Mathf.Clamp01(since * 3f); float end = has ? CutsceneSeconds : 1.6f;
            fade = Mathf.Min(fade, Mathf.Clamp01((end - since) * 2.5f));
            Box(0, 0, W, H, new Color(0f, 0f, 0f, 1f - fade));
            bool skip = UnityEngine.Input.GetKeyDown(KeyCode.Space) || UnityEngine.Input.GetKeyDown(KeyCode.Escape);
            if (since >= end || (has && VideoDone) || skip || Application.isBatchMode)
            {
                StopVideo(); Ctx.Set("napeCutscene", false); Ctx.Set("titleHold", false); Time.timeScale = 1f;
                var brain = Ctx.Get<Proxies.TitanBrain>("bossBrain"); if (brain != null) brain.FinishNapeKill();
            }
        }

        static void Ending(OdmController c, string over, float W, float H, float s)
        {
            float cy = H * 0.5f;
            Box(0, 0, W, H, new Color(0f, 0f, 0f, 0.72f));
            Text(new Rect(0, cy - 110f * s, W, 100f * s), over, Sized(sTitle, 96f), new Color(1f, 0.9f, 0.7f), 4f);
            var sub = Sized(sLabel, 18f); sub.alignment = TextAnchor.MiddleCenter;
            Text(new Rect(0, cy, W, 30f * s), "THE DISTRICT IS CLEAR   ·   TIME " + Time.timeSinceLevelLoad.ToString("0") + " S   ·   HP LEFT " + c.Health.ToString("0"), sub, Color.white);
            sLabel.alignment = TextAnchor.MiddleLeft;
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.unscaledTime * 3f);
            Text(new Rect(0, cy + 44f * s, W, 40f * s), "R  PLAY AGAIN      CMD-Q  QUIT", Sized(sPrompt, 30f), new Color(1f, 1f, 1f, pulse));
            if (UnityEngine.Input.GetKeyDown(KeyCode.R) && !Reboot.Restarting) { playStart = -1f; orbitStarted = false; OdmController.TitleDone = false;  Reboot.Now(); }
        }
    }
}
