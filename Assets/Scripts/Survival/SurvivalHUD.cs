using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;
using EndlessSurvival.World.Road;

namespace EndlessSurvival.Survival
{
    /// <summary>Shared OnGUI styles and textures.</summary>
    public static class SurvivalGUI
    {
        public static GUIStyle Panel, Title, Header, Text, Small, Bar, BarBig, Center, Tag, Right, SmallRight;
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
            BarBig = new GUIStyle(Bar) { fontSize = 13, fontStyle = FontStyle.Bold };
            Right = new GUIStyle(GUI.skin.label) { fontSize = 15, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };
            SmallRight = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleRight, normal = { textColor = new Color(1f, 1f, 1f, 0.65f) } };
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

        public static void DrawBar(Rect r, string label, float value01, Color color) => DrawBar(r, label, value01, color, Bar);

        public static void DrawBar(Rect r, string label, float value01, Color color, GUIStyle style)
        {
            Fill(r, new Color(0f, 0f, 0f, 0.55f));
            Fill(new Rect(r.x + 1, r.y + 1, (r.width - 2) * Mathf.Clamp01(value01), r.height - 2), color);
            if (string.IsNullOrEmpty(label)) return;
            Shadowed(new Rect(r.x + 5, r.y, r.width - 6, r.height), label, style);
        }

        /// <summary>Label with a 1 px dark shadow so it reads on bright bars and sky.</summary>
        public static void Shadowed(Rect r, string text, GUIStyle style)
        {
            var c = style.normal.textColor;
            style.normal.textColor = new Color(0f, 0f, 0f, 0.85f * c.a);
            GUI.Label(new Rect(r.x + 1, r.y + 1, r.width, r.height), text, style);
            style.normal.textColor = c;
            GUI.Label(r, text, style);
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
                DrawVitalsCard(v);
                DrawTags(v);
                DrawProgress(v);
            }
            if (!SurvivalPanel.IsHubOpen) DrawCompass();
            if (!SurvivalPanel.IsHubOpen) DrawResourceCard(v);
            if (!SurvivalPanel.IsHubOpen && !SurvivalDirector.PlayerInVehicle) DrawWeaponCard();
            if (!SurvivalPanel.IsHubOpen && SurvivalDirector.PlayerInVehicle) DrawDrivingCard();
            if (!SurvivalPanel.IsHubOpen && !SurvivalDirector.PlayerInVehicle) DrawVehiclePrompt();
            if (!SurvivalPanel.IsHubOpen && VehicleParts.Instance != null) VehicleParts.Instance.DrawDrivingHud();
            DrawNotifications();
        }

        // ---------------- legacy uGUI HUD ----------------

        private PlayerHealth _health;
        private PlayerSurvival _survival;
        private bool _legacyHidden;

        private void Update()
        {
            if (_legacyHidden && _health != null) return;
            var player = SurvivalDirector.Player;
            if (player == null) return;
            _health = player.GetComponent<PlayerHealth>();
            _survival = player.GetComponent<PlayerSurvival>();
            HideLegacyHud();
        }

        /// <summary>The old uGUI text HUD (HEALTH/HUNGER/MONEY/BACKPACK/hints) is replaced by the cards drawn here.</summary>
        private void HideLegacyHud()
        {
            if (_health != null && _health.healthText != null) _health.healthText.gameObject.SetActive(false);
            if (_survival != null && _survival.statusText != null) _survival.statusText.gameObject.SetActive(false);
            var inv = InventoryUI.Instance;
            if (inv != null)
            {
                if (inv.moneyText != null) inv.moneyText.gameObject.SetActive(false);
                if (inv.weightText != null) inv.weightText.gameObject.SetActive(false);
                var canvas = inv.GetComponentInParent<Canvas>();
                if (canvas != null)
                {
                    foreach (var t in canvas.rootCanvas.GetComponentsInChildren<UnityEngine.UI.Text>(true))
                        if (t.name == "HintText") t.gameObject.SetActive(false);
                }
            }
            _legacyHidden = _health != null;
        }

        // ---------------- vitals card (top left) ----------------

        private static readonly Color CardBg = new Color(0.04f, 0.045f, 0.05f, 0.62f);
        private static readonly Color Bad = new Color(0.9f, 0.22f, 0.18f, 0.95f);
        private float _tagsY = 222f;

        private void DrawVitalsCard(PlayerVitals v)
        {
            const float x = 14f, w = 236f, pad = 8f;
            float bw = w - pad * 2f;
            float y = 14f;
            bool pulse = Mathf.Repeat(Time.unscaledTime, 0.9f) < 0.45f;

            // health + 3 needs + gap + 5 body bars + weather line
            float h = pad + 22f + 3 * 17f + 6f + 5 * 15f + 20f + pad;
            SurvivalGUI.Fill(new Rect(x, y, w, h), CardBg);
            SurvivalGUI.Fill(new Rect(x, y, 3f, h), new Color(0.95f, 0.8f, 0.5f, 0.8f));
            float bx = x + pad, by = y + pad;

            if (_health != null)
            {
                float hp = _health.CurrentHealth, max = Mathf.Max(1f, _health.maxHealth);
                float armor = _health.equipment != null ? _health.equipment.TotalArmor : 0f;
                Color hc = hp / max < 0.3f && pulse ? new Color(1f, 0.25f, 0.2f, 1f) : new Color(0.78f, 0.2f, 0.2f, 0.95f);
                string label = $"Can {Mathf.CeilToInt(hp)} / {Mathf.CeilToInt(max)}" + (armor > 0.5f ? $"    Zırh {armor:0}" : "");
                SurvivalGUI.DrawBar(new Rect(bx, by, bw, 19f), label, hp / max, hc, SurvivalGUI.BarBig);
                if (_health.isBleeding)
                {
                    var r = new Rect(bx + bw - 70f, by + 2f, 66f, 15f);
                    SurvivalGUI.Fill(r, pulse ? new Color(0.9f, 0.05f, 0.05f, 0.95f) : new Color(0.55f, 0.05f, 0.05f, 0.95f));
                    GUI.Label(r, "KANAMA", SurvivalGUI.Tag);
                }
            }
            by += 22f;

            if (_survival != null)
            {
                NeedBar(bx, ref by, bw, "Tokluk", _survival.Hunger / Mathf.Max(1f, _survival.maxHunger), new Color(0.85f, 0.55f, 0.2f, 0.92f), false, pulse);
                NeedBar(bx, ref by, bw, "Su", _survival.Thirst / Mathf.Max(1f, _survival.maxThirst), new Color(0.25f, 0.6f, 0.9f, 0.92f), false, pulse);
                NeedBar(bx, ref by, bw, _survival.IsBladderFull ? "Tuvalet  " + EndlessCombat.Combat.GameInput.Hint("[P]", "(↓ basılı)") : "Tuvalet", _survival.Bladder / Mathf.Max(1f, _survival.maxBladder), new Color(0.75f, 0.7f, 0.3f, 0.92f), true, pulse);
            }
            else by += 3 * 17f;
            by += 6f;

            SmallBar(bx, ref by, bw, $"Kondisyon {Mathf.RoundToInt(v.Stamina)}", v.Stamina / Mathf.Max(1f, v.MaxStamina), new Color(0.85f, 0.75f, 0.2f, 0.9f));
            SmallBar(bx, ref by, bw, $"Yorgunluk {Mathf.RoundToInt(v.Fatigue)}", v.Fatigue / 100f, new Color(0.45f, 0.4f, 0.7f, 0.9f));
            SmallBar(bx, ref by, bw, $"Vücut ısısı {Mathf.RoundToInt(v.BodyHeat)}", v.BodyHeat / 100f, Color.Lerp(new Color(0.3f, 0.55f, 0.95f, 0.9f), new Color(0.9f, 0.5f, 0.25f, 0.9f), v.BodyHeat / 100f));
            SmallBar(bx, ref by, bw, $"Islaklık {Mathf.RoundToInt(v.Wetness)}", v.Wetness / 100f, new Color(0.25f, 0.55f, 0.85f, 0.9f));
            SmallBar(bx, ref by, bw, $"Stres {Mathf.RoundToInt(v.Stress)}", v.Stress / 100f, new Color(0.8f, 0.3f, 0.3f, 0.9f));

            var w8 = WeatherSystem.Instance;
            string weather = w8 != null ? w8.DisplayName : "Açık";
            GUI.Label(new Rect(bx, by + 1f, bw, 18f), $"{weather}   Hava {Mathf.RoundToInt(v.AmbientTemperature)}°C   Hissedilen {Mathf.RoundToInt(v.FeltTemperature)}°C", SurvivalGUI.Small);

            _tagsY = y + h + 6f;
        }

        private static void NeedBar(float x, ref float y, float w, string label, float value01, Color color, bool inverted, bool pulse)
        {
            float bad = inverted ? value01 : 1f - value01;
            Color c = bad >= 0.85f ? (pulse ? Bad : color) : bad >= 0.8f ? Color.Lerp(color, Bad, 0.5f) : color;
            SurvivalGUI.DrawBar(new Rect(x, y, w, 15f), $"{label}  {Mathf.RoundToInt(value01 * 100f)}", value01, c);
            y += 17f;
        }

        private static void SmallBar(float x, ref float y, float w, string label, float value01, Color color)
        {
            SurvivalGUI.DrawBar(new Rect(x, y, w, 13f), label, value01, color);
            y += 15f;
        }

        // ---------------- top right: money, backpack, key hints ----------------

        private static void DrawResourceCard(PlayerVitals v)
        {
            var inv = SurvivalDirector.Inventory;
            if (inv == null) return;
            float w = 230f, x = Screen.width - w - 14f, y = 14f;
            SurvivalGUI.Fill(new Rect(x, y, w, 46f), CardBg);
            var st = SurvivalGUI.Right;
            var old = st.normal.textColor;
            st.normal.textColor = new Color(1f, 0.85f, 0.35f);
            GUI.Label(new Rect(x + 8f, y + 3f, w - 16f, 20f), $"Para  {inv.Money}", st);
            bool over = v != null && v.Overloaded;
            st.normal.textColor = over ? new Color(1f, 0.45f, 0.35f) : Color.white;
            GUI.Label(new Rect(x + 8f, y + 23f, w - 16f, 20f), $"Çanta  {inv.Storage.TotalWeight:0.0} / {(inv.Storage.IsUnlimited ? "sınırsız" : inv.Storage.MaxWeight.ToString("0.#") + " kg")}", st);
            st.normal.textColor = old;
            GUI.Label(new Rect(x - 200f, y + 49f, w + 200f, 18f), EndlessCombat.Combat.GameInput.UsingPad
                ? (SurvivalDirector.PlayerInVehicle ? "View Envanter   Y İn   X Marş   Start Menü" : "View Envanter   X Etkileşim   ↓ basılı Eylemler   Start Menü")
                : "Tab Envanter   E Etkileşim   F Araç   Q Eylemler   Esc Menü", SurvivalGUI.SmallRight);
        }

        // ---------------- bottom right: weapon and ammo ----------------

        private EndlessCombat.Combat.PlayerShooter _shooter;
        private PlayerEquipment _equipment;
        private static GUIStyle _ammoBig;

        /// <summary>Held weapon name, magazine / reserve rounds and reload state (the scene has no ammo UI of its own).</summary>
        private void DrawWeaponCard()
        {
            var player = SurvivalDirector.Player;
            if (player == null) return;
            if (_shooter == null) _shooter = player.GetComponent<EndlessCombat.Combat.PlayerShooter>();
            if (_equipment == null) _equipment = player.GetComponent<PlayerEquipment>();
            if (_shooter == null) return;
            if (_ammoBig == null)
                _ammoBig = new GUIStyle(GUI.skin.label) { fontSize = 24, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };

            var weapon = _shooter.CurrentWeapon;
            var loadout = player.GetComponent<PlayerLoadout>();
            var item = loadout != null ? loadout.Item(loadout.Active) : (_equipment != null ? _equipment.Get(EquipSlot.Weapon) : null);
            string name = item != null ? item.DisplayName : (weapon != null ? weapon.WeaponName : "Yumruk");
            bool pad = EndlessCombat.Combat.GameInput.UsingPad;

            float w = 230f, h = 54f, x = Screen.width - w - 14f, y = Screen.height - 128f - h;
            if (loadout != null) DrawSlotStrip(loadout, x, y - 24f, w, pad);
            SurvivalGUI.Fill(new Rect(x, y, w, h), CardBg);
            SurvivalGUI.Fill(new Rect(x + w - 3f, y, 3f, h), new Color(0.95f, 0.8f, 0.5f, 0.8f));

            if (_shooter.IsHolstered || weapon == null)
            {
                bool stowed = _shooter.IsHolstered || loadout == null || loadout.Active == PlayerLoadout.Slot.None;
                string state = weapon == null ? (stowed ? "Eller boş" : "Yumruk") : name + "  (yerinde)";
                SurvivalGUI.Shadowed(new Rect(x + 8f, y + 4f, w - 18f, 20f), state, SurvivalGUI.Right);
                string hint = pad ? "LB bas: çıkar   LB basılı: seçim   LT: çek + nişan" : "1 / 2 / 3: silah seç   R basılı: çıkar / koy";
                GUI.Label(new Rect(x + 8f, y + 28f, w - 18f, 18f), hint, SurvivalGUI.SmallRight);
                return;
            }

            string grip = loadout != null && loadout.Active != PlayerLoadout.Slot.None ? (loadout.TwoHanded(loadout.Active) ? "  · çift el" : "  · tek el") : "";
            GUI.Label(new Rect(x + 8f, y + 3f, w - 18f, 18f), name + grip, SurvivalGUI.SmallRight);
            if (!weapon.IsFirearm)
            {
                SurvivalGUI.Shadowed(new Rect(x + 8f, y + 20f, w - 18f, 30f), "Yakın dövüş", SurvivalGUI.Right);
                return;
            }

            if (weapon.IsReloading)
            {
                SurvivalGUI.Shadowed(new Rect(x + 8f, y + 20f, w - 18f, 30f), "Şarjör değişiyor...", SurvivalGUI.Right);
                return;
            }

            int mag = weapon.CurrentAmmo, reserve = weapon.ReserveAmmo;
            string text = weapon.InfiniteAmmo ? $"{mag} / ∞" : $"{mag} / {reserve}";
            var old = _ammoBig.normal.textColor;
            _ammoBig.normal.textColor = mag <= 0 ? new Color(1f, 0.35f, 0.3f) : mag <= 5 ? new Color(1f, 0.75f, 0.35f) : Color.white;
            SurvivalGUI.Shadowed(new Rect(x + 8f, y + 18f, w - 18f, 32f), text, _ammoBig);
            _ammoBig.normal.textColor = old;
            if (mag <= 0) GUI.Label(new Rect(x + 8f, y + 22f, w - 120f, 24f), reserve > 0 ? (EndlessCombat.Combat.GameInput.UsingPad ? "X doldur" : "[R] doldur") : "Mermi yok", SurvivalGUI.Small);
        }

        /// <summary>Three small slot chips above the weapon card: long gun, pistol, melee (the one in the hand lit).</summary>
        private static void DrawSlotStrip(PlayerLoadout loadout, float x, float y, float w, bool pad)
        {
            string[] keys = pad ? new[] { "Y", "X", "RB" } : new[] { "1", "2", "3" };
            Color[] colors = { new Color(0.95f, 0.71f, 0.19f), new Color(0.37f, 0.71f, 0.94f), new Color(0.9f, 0.42f, 0.35f) };
            float cw = (w - 8f) / 3f;
            for (int i = 0; i < 3; i++)
            {
                var slot = (PlayerLoadout.Slot)i;
                var it = loadout.Item(slot);
                bool active = loadout.Active == slot && !loadout.IsStowed;
                var r = new Rect(x + i * (cw + 4f), y, cw, 20f);
                SurvivalGUI.Fill(r, active ? new Color(colors[i].r, colors[i].g, colors[i].b, 0.55f) : new Color(0f, 0f, 0f, 0.45f));
                SurvivalGUI.Fill(new Rect(r.x, r.yMax - 2f, r.width, 2f), new Color(colors[i].r, colors[i].g, colors[i].b, it != null || i == 2 ? 0.9f : 0.25f));
                string label = it != null ? it.DisplayName : (i == 2 ? "Yumruk" : "-");
                GUI.Label(new Rect(r.x + 4f, r.y + 2f, r.width - 6f, 18f), keys[i] + " " + label, SurvivalGUI.Small);
            }
        }

        // ---------------- bottom right: driving (speed, gear, fuel, hull) ----------------

        private static GUIStyle _speedBig;

        /// <summary>The scene's VehicleUI dashboard is not wired up, so speed, fuel and hull are drawn here.</summary>
        private static void DrawDrivingCard()
        {
            var car = SurvivalDirector.Vehicle;
            if (car == null) return;
            if (_speedBig == null)
                _speedBig = new GUIStyle(GUI.skin.label) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, normal = { textColor = Color.white } };

            float w = 250f, h = 102f, x = Screen.width - w - 14f, y = Screen.height - 128f - h;
            SurvivalGUI.Fill(new Rect(x, y, w, h), CardBg);
            SurvivalGUI.Fill(new Rect(x + w - 3f, y, 3f, h), new Color(0.95f, 0.8f, 0.5f, 0.8f));

            int speed = Mathf.RoundToInt(car.CurrentSpeedKmh);
            SurvivalGUI.Shadowed(new Rect(x + 8f, y + 4f, w - 70f, 36f), speed.ToString(), _speedBig);
            GUI.Label(new Rect(x + w - 62f, y + 10f, 54f, 16f), "km/s", SurvivalGUI.Small);
            GUI.Label(new Rect(x + w - 62f, y + 24f, 54f, 16f), "Vites " + car.GearLabel, SurvivalGUI.Small);

            float fuel = car.maxFuel > 0f ? car.currentFuel / car.maxFuel : 0f;
            float hull = car.maxHealth > 0f ? car.currentHealth / car.maxHealth : 0f;
            bool pulse = Mathf.Repeat(Time.unscaledTime, 0.9f) < 0.45f;
            Color fuelC = fuel < 0.15f && pulse ? Bad : new Color(0.85f, 0.6f, 0.2f, 0.92f);
            Color hullC = hull < 0.25f && pulse ? Bad : new Color(0.5f, 0.65f, 0.75f, 0.92f);
            SurvivalGUI.DrawBar(new Rect(x + 8f, y + 46f, w - 20f, 15f), $"Yakıt  %{Mathf.RoundToInt(fuel * 100f)}", fuel, fuelC);
            SurvivalGUI.DrawBar(new Rect(x + 8f, y + 64f, w - 20f, 15f), $"Gövde  %{Mathf.RoundToInt(hull * 100f)}", hull, hullC);

            string warn = car.IsBroken ? "MOTOR BOZUK: in, Tab > Envanter > Tamir et"
                : car.currentFuel <= 0f ? "YAKIT BİTTİ: benzin bidonu ile doldur"
                : fuel < 0.15f ? "Yakıt azalıyor" : null;
            if (warn != null)
            {
                var st = SurvivalGUI.SmallRight;
                var old = st.normal.textColor;
                st.normal.textColor = new Color(1f, 0.55f, 0.4f);
                GUI.Label(new Rect(x + 4f, y + 82f, w - 14f, 18f), warn, st);
                st.normal.textColor = old;
            }
        }

        // ---------------- vehicle enter prompt ----------------

        private EndlessSurvival.Vehicle.VehicleInteraction _carInteraction;

        /// <summary>"[F] Araca bin" next to the car (the VehicleUI prompt panel in the scene is not wired up).</summary>
        private void DrawVehiclePrompt()
        {
            if (_carInteraction == null)
            {
                var car = SurvivalDirector.Vehicle;
                if (car != null) _carInteraction = car.GetComponent<EndlessSurvival.Vehicle.VehicleInteraction>();
                if (_carInteraction == null) return;
            }
            if (!_carInteraction.IsPlayerNear || _carInteraction.IsPlayerInside) return;
            string text = EndlessCombat.Combat.GameInput.UsingPad ? "X  Araca bin        View  Bagaj ve araç sekmesi" : "[F] Araca bin     [U] Modifiye atölyesi     [Tab] Bagaj";
            var size = SurvivalGUI.Center.CalcSize(new GUIContent(text));
            float w = size.x + 30f, h = 30f, x = (Screen.width - w) * 0.5f, y = Screen.height * 0.70f;
            SurvivalGUI.Fill(new Rect(x, y, w, h), CardBg);
            SurvivalGUI.Shadowed(new Rect(x, y, w, h), text, SurvivalGUI.Center);
        }

        // ---------------- screen effects ----------------

        private static void DrawEffects(PlayerVitals v)
        {
            var full = new Rect(0, 0, Screen.width, Screen.height);
            var old = GUI.color;

            // Overcast wash: rain/snow/fog read as gloomy even where the sky shader ignores the weather
            var weather = WeatherSystem.Instance;
            if (weather != null && weather.Current != WeatherType.Clear && weather.Intensity > 0.02f)
            {
                Color overcast = weather.Current == WeatherType.Rain ? new Color(0.18f, 0.21f, 0.26f, 0.2f)
                    : weather.Current == WeatherType.Snow ? new Color(0.85f, 0.88f, 0.95f, 0.12f)
                    : new Color(0.6f, 0.62f, 0.65f, 0.18f);
                overcast.a *= weather.Intensity;
                SurvivalGUI.Fill(full, overcast);
            }
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
            // Starving or dehydrated: a pale, slow-pulsing haze, lighter than the poison wash
            var needs = v.GetComponent<PlayerSurvival>();
            if (needs != null && needs.IsCritical)
            {
                float haze = 0.07f + 0.04f * Mathf.Sin(Time.time * 0.9f);
                if (haze > wash)
                {
                    wash = haze;
                    tint = new Color(0.62f, 0.6f, 0.45f);
                }
            }
            if (wash > 0.01f) SurvivalGUI.Fill(full, new Color(tint.r, tint.g, tint.b, wash));
            GUI.color = old;
        }

        private void DrawTags(PlayerVitals v)
        {
            float x = 14f, y = _tagsY;
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
            if (v.ToiletNeglected) Tag("TUVALET " + EndlessCombat.Combat.GameInput.Hint("[P]", "(↓ basılı)"), new Color(0.55f, 0.5f, 0.2f, 0.85f));
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
