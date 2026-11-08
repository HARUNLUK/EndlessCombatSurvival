using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.Survival
{
    /// <summary>Shared OnGUI styles and textures.</summary>
    public static class SurvivalGUI
    {
        public static GUIStyle Panel, Title, Header, Text, Small, Bar, Center, Tag;
        public static Texture2D White, Vignette;
        private static bool _ready;

        public static void Init()
        {
            if (_ready && White != null) return;
            _ready = true;
            White = Texture2D.whiteTexture;
            Vignette = MakeVignette();

            Panel = new GUIStyle(GUI.skin.box) { normal = { background = Solid(new Color(0.06f, 0.06f, 0.07f, 0.92f)) } };
            Title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.95f, 0.85f, 0.6f) } };
            Header = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.85f, 0.85f, 0.85f) } };
            Text = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, normal = { textColor = Color.white } };
            Small = new GUIStyle(GUI.skin.label) { fontSize = 12, wordWrap = true, normal = { textColor = new Color(0.8f, 0.8f, 0.8f) } };
            Bar = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft, normal = { textColor = Color.white } };
            Center = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            Tag = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
        }

        private static Texture2D Solid(Color c)
        {
            var t = new Texture2D(1, 1) { hideFlags = HideFlags.DontSave };
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        private static Texture2D MakeVignette()
        {
            const int size = 128;
            var t = new Texture2D(size, size, TextureFormat.RGBA32, false) { hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f, dy = (y + 0.5f) / size * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy) / 1.41f;
                float a = Mathf.SmoothStep(0.35f, 1f, d);
                t.SetPixel(x, y, new Color(0f, 0f, 0f, a));
            }
            t.Apply();
            return t;
        }

        public static void Fill(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, White);
            GUI.color = old;
        }

        public static void DrawBar(Rect r, string label, float value01, Color color)
        {
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(r.x + 1, r.y + 1, (r.width - 2) * Mathf.Clamp01(value01), r.height - 2), color);
            GUI.Label(new Rect(r.x + 5, r.y, r.width - 6, r.height), label, Bar);
        }
    }

    /// <summary>
    /// Survival HUD (OnGUI): stamina, fatigue, body heat, wetness, stress bars; condition tags; temperature and
    /// weather; notifications; action progress (toilet, craft, fishing); compass (5.4) and screen effects.
    /// </summary>
    public class SurvivalHUD : MonoBehaviour
    {
        private void OnGUI()
        {
            if (SurvivalUIState.MenuOpen) return;
            if (GameOverUI.IsGameOver) return;
            SurvivalGUI.Init();

            var v = PlayerVitals.Instance;
            if (v != null)
            {
                DrawEffects(v);
                DrawBars(v);
                DrawTags(v);
                DrawProgress(v);
            }
            if (!SurvivalPanel.IsHubOpen) DrawCompass();
            if (!SurvivalPanel.IsHubOpen && VehicleParts.Instance != null) VehicleParts.Instance.DrawDrivingHud();
            DrawNotifications();
        }

        // ---------------- screen effects ----------------

        private static void DrawEffects(PlayerVitals v)
        {
            var full = new Rect(0, 0, Screen.width, Screen.height);
            var old = GUI.color;
            float vignette = 0f;
            if (v.Panicking) vignette = 0.6f + 0.35f * Mathf.Abs(Mathf.Sin(Time.time * 5.5f)); // heartbeat pulse
            if (v.Freezing) vignette = Mathf.Max(vignette, 0.45f);
            if (vignette > 0f)
            {
                GUI.color = new Color(v.Freezing && !v.Panicking ? 0.6f : 1f, v.Freezing && !v.Panicking ? 0.8f : 1f, 1f, vignette);
                GUI.DrawTexture(full, SurvivalGUI.Vignette, ScaleMode.StretchToFill);
            }

            // "blur": soft tinted wash with a slow wobble (no post-processing on the camera)
            float wash = 0f;
            Color tint = Color.black;
            if (v.IsPoisoned || v.IsInfected)
            {
                wash = 0.12f + 0.06f * Mathf.Sin(Time.time * 1.3f);
                tint = v.IsPoisoned ? new Color(0.35f, 0.45f, 0.2f) : new Color(0.5f, 0.25f, 0.2f);
            }
            if (v.Fatigue > 75f)
            {
                // heavy eyelids now and then
                float blink = Mathf.Clamp01(Mathf.Sin(Time.time * 0.7f) * 4f - 3f) * Mathf.InverseLerp(75f, 100f, v.Fatigue);
                wash = Mathf.Max(wash, blink * 0.7f);
                if (blink > 0.05f) tint = Color.black;
            }
            if (wash > 0.01f) SurvivalGUI.Fill(full, new Color(tint.r, tint.g, tint.b, wash));
            GUI.color = old;
        }

        // ---------------- bars ----------------

        private static void DrawBars(PlayerVitals v)
        {
            float w = 190f, h = 16f, x = 20f, y = 96f;
            SurvivalGUI.DrawBar(new Rect(x, y, w, h), $"Stamina {Mathf.RoundToInt(v.Stamina)}", v.Stamina / Mathf.Max(1f, v.MaxStamina), new Color(0.85f, 0.75f, 0.2f, 0.9f));
            y += h + 3;
            SurvivalGUI.DrawBar(new Rect(x, y, w, h), $"Yorgunluk {Mathf.RoundToInt(v.Fatigue)}", v.Fatigue / 100f, new Color(0.45f, 0.4f, 0.7f, 0.9f));
            y += h + 3;
            SurvivalGUI.DrawBar(new Rect(x, y, w, h), $"Vücut ısısı {Mathf.RoundToInt(v.BodyHeat)}", v.BodyHeat / 100f, Color.Lerp(new Color(0.3f, 0.55f, 0.95f, 0.9f), new Color(0.9f, 0.5f, 0.25f, 0.9f), v.BodyHeat / 100f));
            y += h + 3;
            SurvivalGUI.DrawBar(new Rect(x, y, w, h), $"Islaklık {Mathf.RoundToInt(v.Wetness)}", v.Wetness / 100f, new Color(0.25f, 0.55f, 0.85f, 0.9f));
            y += h + 3;
            SurvivalGUI.DrawBar(new Rect(x, y, w, h), $"Stres {Mathf.RoundToInt(v.Stress)}", v.Stress / 100f, new Color(0.8f, 0.3f, 0.3f, 0.9f));
            y += h + 4;

            var w8 = WeatherSystem.Instance;
            string weather = w8 != null ? w8.DisplayName : "Açık";
            GUI.Label(new Rect(x, y, w + 10, 20), $"Hava {Mathf.RoundToInt(v.AmbientTemperature)}°C  Hissedilen {Mathf.RoundToInt(v.FeltTemperature)}°C  {weather}", SurvivalGUI.Small);
        }

        private static void DrawTags(PlayerVitals v)
        {
            float x = 20f, y = 222f;
            void Tag(string text, Color color)
            {
                var size = SurvivalGUI.Tag.CalcSize(new GUIContent(text));
                var r = new Rect(x, y, size.x + 8f, 18f);
                SurvivalGUI.Fill(r, color);
                GUI.Label(r, text, SurvivalGUI.Tag);
                y += 21f;
            }

            if (v.Panicking) Tag("PANİK", new Color(0.8f, 0.1f, 0.1f, 0.85f));
            if (v.IsPoisoned) Tag("ZEHİRLENME", new Color(0.35f, 0.55f, 0.15f, 0.85f));
            if (v.IsInfected) Tag("ENFEKSİYON / ATEŞ", new Color(0.65f, 0.25f, 0.15f, 0.85f));
            else if (v.WoundDirty) Tag("KİRLİ YARA (temizle)", new Color(0.55f, 0.4f, 0.2f, 0.85f));
            if (v.Freezing) Tag("DONUYORSUN", new Color(0.2f, 0.4f, 0.9f, 0.85f));
            else if (v.ColdSeverity > 0.05f) Tag("ÜŞÜYORSUN", new Color(0.3f, 0.5f, 0.85f, 0.85f));
            if (v.Wetness > 40f) Tag("ISLAK", new Color(0.2f, 0.45f, 0.75f, 0.85f));
            if (v.Overloaded) Tag("AŞIRI YÜK", new Color(0.6f, 0.45f, 0.2f, 0.85f));
            if (v.ToiletNeglected) Tag("TUVALET [P]", new Color(0.55f, 0.5f, 0.2f, 0.85f));
            if (v.Fatigue >= SurvivalSettings.Instance.fatigueWarning) Tag("YORGUN", new Color(0.4f, 0.35f, 0.6f, 0.85f));
            int anx = v.AnxietyLevel;
            if (anx > 0) Tag(anx == 1 ? "ANKSİYETE: HAFİF" : anx == 2 ? "ANKSİYETE: ORTA" : "ANKSİYETE: AĞIR", new Color(0.45f, 0.2f, 0.45f, 0.85f));
            else if (v.AnxietyHint) Tag("GERGİN", new Color(0.4f, 0.3f, 0.4f, 0.75f));
        }

        private static void DrawProgress(PlayerVitals v)
        {
            string label = null;
            float progress = -1f;
            if (v.ToiletActive)
            {
                label = "Tuvalet...";
                progress = v.ToiletProgress;
            }
            var craft = CraftingSystem.Instance;
            if (label == null && craft != null && craft.CurrentJob != null)
            {
                label = craft.CurrentJob.DisplayName + "...";
                progress = craft.JobProgress;
            }
            var parts = VehicleParts.Instance;
            if (label == null && parts != null && parts.CurrentJob != null)
            {
                label = parts.CurrentJob.label + "...";
                progress = parts.JobProgress;
            }
            var fishing = SurvivalDirector.Instance != null ? SurvivalDirector.Instance.GetComponent<FishingController>() : null;
            if (label == null && fishing != null && fishing.StatusText != null) label = fishing.StatusText;
            if (label == null) return;

            float w = 260f, x = (Screen.width - w) * 0.5f, y = Screen.height * 0.62f;
            GUI.Label(new Rect(x, y - 24, w, 22), label, SurvivalGUI.Center);
            if (progress >= 0f) SurvivalGUI.DrawBar(new Rect(x, y, w, 12), "", progress, new Color(0.9f, 0.8f, 0.4f, 0.9f));
        }

        // ---------------- compass (5.4) ----------------

        private static void DrawCompass()
        {
            var cam = Camera.main;
            if (cam == null) return;
            float w = Mathf.Min(520f, Screen.width * 0.5f), h = 24f, x = (Screen.width - w) * 0.5f, y = 64f;
            SurvivalGUI.Fill(new Rect(x, y, w, h), new Color(0f, 0f, 0f, 0.45f));

            Vector3 f = cam.transform.forward;
            float heading = Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg;
            const float fov = 180f; // degrees shown across the strip
            string[] names = { "K", "KD", "D", "GD", "G", "GB", "B", "KB" };
            for (int i = 0; i < 8; i++)
            {
                float delta = Mathf.DeltaAngle(heading, i * 45f);
                if (Mathf.Abs(delta) > fov * 0.5f) continue;
                float px = x + w * 0.5f + delta / fov * w;
                GUI.Label(new Rect(px - 15, y + 2, 30, 20), names[i], i % 2 == 0 ? SurvivalGUI.Tag : SurvivalGUI.Small);
            }

            // Road direction marker (forward along the road = next chunk)
            Vector3 roadDir;
            if (TryRoadDirection(out roadDir))
            {
                float roadHeading = Mathf.Atan2(roadDir.x, roadDir.z) * Mathf.Rad2Deg;
                float delta = Mathf.Clamp(Mathf.DeltaAngle(heading, roadHeading), -fov * 0.5f, fov * 0.5f);
                float px = x + w * 0.5f + delta / fov * w;
                SurvivalGUI.Fill(new Rect(px - 3, y + h - 6, 6, 6), new Color(1f, 0.7f, 0.2f, 1f));
                GUI.Label(new Rect(px - 20, y + h - 2, 40, 16), "YOL", SurvivalGUI.Small);
            }
            SurvivalGUI.Fill(new Rect(x + w * 0.5f - 1, y, 2, h), new Color(1f, 1f, 1f, 0.6f));
        }

        private static bool TryRoadDirection(out Vector3 dir)
        {
            dir = Vector3.forward;
            Vector3 pos = SurvivalDirector.PlayerPosition;
            var chunk = SurvivalDirector.ChunkAt(pos);
            if (chunk == null) return false;
            var spline = chunk.GetComponentInChildren<RoadSpline>();
            if (spline != null)
            {
                Vector3 local = spline.transform.InverseTransformPoint(pos);
                float z = Mathf.Clamp(local.z + 25f, 0f, chunk.chunkLength);
                Vector3 ahead = spline.transform.TransformPoint(spline.GetPointAtZ(z));
                dir = ahead - pos;
            }
            else if (chunk.exitSocket != null)
            {
                dir = chunk.exitSocket.position - pos;
            }
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = chunk.transform.forward;
            return true;
        }

        // ---------------- notifications ----------------

        private static void DrawNotifications()
        {
            // With the Tab screen open the messages go below it, centred
            bool hub = SurvivalPanel.IsHubOpen;
            float y = hub ? Screen.height - 140f : 120f;
            float w = 360f, x = hub ? (Screen.width - w) * 0.5f : Screen.width - w - 16f;
            foreach (var e in SurvivalNotify.Entries)
            {
                float alpha = Mathf.Clamp01((e.until - Time.unscaledTime) * 2f);
                var r = new Rect(x, y, w, 24f);
                SurvivalGUI.Fill(r, new Color(0f, 0f, 0f, 0.5f * alpha));
                var style = SurvivalGUI.Text;
                var old = style.normal.textColor;
                style.normal.textColor = new Color(e.color.r, e.color.g, e.color.b, alpha);
                GUI.Label(new Rect(x + 8, y + 2, w - 12, 22), e.text, style);
                style.normal.textColor = old;
                y += 27f;
            }
        }
    }
}
