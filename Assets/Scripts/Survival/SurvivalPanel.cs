using UnityEngine;
using UnityEngine.InputSystem;
using EndlessSurvival.Inventory;
using EndlessSurvival.Vehicle;
using EndlessSurvival.Camping;

namespace EndlessSurvival.Survival
{
    public enum HubTab
    {
        Inventory,
        Craft,
        Camp,
        Vehicle,
        Status,
        Stats
    }

    /// <summary>
    /// The one Tab screen: a tab bar on top of the inventory window. Envanter shows the inventory (scene UI);
    /// the other tabs (crafting 2.1, campfire + camp placing, vehicle workshop + vehicle home 2.19, body status, stats 4.4)
    /// are drawn in the same rectangle. [E] on a campfire opens the Kamp tab, [U] near the vehicle opens the Araç tab.
    /// Tab / Esc closes everything (the inventory owns open/close and the cursor).
    /// </summary>
    public class SurvivalPanel : MonoBehaviour
    {
        public static SurvivalPanel Instance { get; private set; }

        private static readonly HubTab[] Order = { HubTab.Inventory, HubTab.Craft, HubTab.Camp, HubTab.Vehicle, HubTab.Status, HubTab.Stats };

        private HubTab _tab;
        private bool _wasOpen;
        private bool _ownOpen; // only used when the scene has no InventoryUI
        private HubTab? _pending;
        private Vector2 _scroll;
        private string _message;
        private Rect _lastPanelRect;
        private PlayerCampfire _nearFire;
        private float _nextFireSearch;

        public static bool IsHubOpen
        {
            get
            {
                if (Instance == null) return false;
                return InventoryUI.Instance != null ? InventoryUI.IsOpen : Instance._ownOpen;
            }
        }

        /// <summary>True while the Tab screen is open on this tab.</summary>
        public static bool Showing(HubTab tab) => IsHubOpen && Instance._tab == tab;

        /// <summary>Opens the Tab screen on a tab (or switches to it). False when there is no Tab screen.</summary>
        public static bool OpenTab(HubTab tab)
        {
            if (Instance == null) return false;
            var self = Instance;
            if (!IsHubOpen)
            {
                self._pending = tab;
                var inv = InventoryUI.Instance;
                if (inv != null) inv.Open();
                else self.SetOwnOpen(true);
            }
            self.Select(tab);
            self._wasOpen = IsHubOpen;
            self._pending = null;
            return true;
        }

        public static void CloseHub()
        {
            if (Instance == null || !IsHubOpen) return;
            var inv = InventoryUI.Instance;
            if (inv != null) inv.Close();
            else Instance.SetOwnOpen(false);
        }

        private void Awake() => Instance = this;

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDisable()
        {
            if (_ownOpen) SetOwnOpen(false);
        }

        private void Select(HubTab tab)
        {
            if (_tab != tab) _scroll = Vector2.zero;
            _tab = tab;
            _message = null;
            ApplyInventoryVisibility();
        }

        private void SetOwnOpen(bool open)
        {
            if (_ownOpen == open) return;
            _ownOpen = open;
            SurvivalUIState.PanelOpen = open;
            if (open) SurvivalUIState.AcquireCursor();
            else SurvivalUIState.ReleaseCursor();
        }

        private void ApplyInventoryVisibility()
        {
            var inv = InventoryUI.Instance;
            if (inv != null && InventoryUI.IsOpen) inv.SetPanelVisible(_tab == HubTab.Inventory);
        }

        private void Update()
        {
            // Fallback without a scene inventory: the panel handles Tab itself
            if (InventoryUI.Instance == null)
            {
                if (_ownOpen && (SKeys.Down(Key.Tab) || SKeys.Down(Key.Escape))) SetOwnOpen(false);
                else if (!_ownOpen && SKeys.Down(Key.Tab) && !SurvivalUIState.AnyOpen) SetOwnOpen(true);
            }

            bool open = IsHubOpen;
            if (open && !_wasOpen)
            {
                // Opened with Tab or a chest: start on the inventory
                Select(_pending ?? HubTab.Inventory);
                _pending = null;
            }
            _wasOpen = open;
            if (open) ApplyInventoryVisibility();
        }

        // ---------------- layout ----------------

        private Rect PanelScreenRect()
        {
            var inv = InventoryUI.Instance;
            var rt = inv != null ? inv.PanelRect : null;
            if (rt != null)
            {
                var corners = new Vector3[4];
                rt.GetWorldCorners(corners);
                var canvas = rt.GetComponentInParent<Canvas>(true);
                Camera cam = canvas != null && canvas.rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.rootCanvas.worldCamera : null;
                Vector2 bl = RectTransformUtility.WorldToScreenPoint(cam, corners[0]);
                Vector2 tr = RectTransformUtility.WorldToScreenPoint(cam, corners[2]);
                var r = new Rect(bl.x, Screen.height - tr.y, tr.x - bl.x, tr.y - bl.y);
                if (r.width > 100f && r.height > 100f)
                {
                    _lastPanelRect = r;
                    return r;
                }
            }
            if (_lastPanelRect.width > 100f) return _lastPanelRect;
            float w = Mathf.Min(1360f, Screen.width - 40f), h = Mathf.Min(780f, Screen.height - 110f);
            return new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f + 20f, w, h);
        }

        private static string TabName(HubTab t)
        {
            switch (t)
            {
                case HubTab.Inventory: return "Envanter";
                case HubTab.Craft: return "Üretim";
                case HubTab.Camp: return "Kamp";
                case HubTab.Vehicle: return "Araç";
                case HubTab.Status: return "Durum";
                default: return "Statlar";
            }
        }

        private void OnGUI()
        {
            if (!IsHubOpen) return;
            SurvivalGUI.Init();
            Rect panel = PanelScreenRect();

            // Tab bar right above the window (inside it when there is no room)
            const float barH = 40f;
            var bar = new Rect(panel.x, panel.y - barH - 4f, panel.width, barH);
            if (bar.y < 4f)
            {
                bar.y = panel.y;
                panel.yMin += barH + 4f;
            }
            SurvivalGUI.Fill(bar, new Color(0.05f, 0.06f, 0.08f, 0.94f));
            float tabW = Mathf.Min(150f, (bar.width - 12f) / Order.Length - 6f);
            for (int i = 0; i < Order.Length; i++)
            {
                var t = Order[i];
                var r = new Rect(bar.x + 6f + i * (tabW + 6f), bar.y + 5f, tabW, barH - 10f);
                bool selected = t == _tab;
                if (selected) SurvivalGUI.Fill(new Rect(r.x - 2, r.y - 2, r.width + 4, r.height + 4), new Color(1f, 0.75f, 0.3f, 0.9f));
                var old = GUI.color;
                if (selected) GUI.color = new Color(1f, 0.92f, 0.7f);
                if (GUI.Button(r, TabName(t)) && !selected) Select(t);
                GUI.color = old;
            }
            GUI.Label(new Rect(bar.xMax - 220f, bar.y, 210f, barH), "TAB / ESC: kapat", SurvivalGUI.Small);

            if (_tab == HubTab.Inventory) return; // the scene inventory window draws itself

            SurvivalGUI.Fill(panel, new Color(0.05f, 0.06f, 0.08f, 0.94f));
            GUILayout.BeginArea(new Rect(panel.x + 20f, panel.y + 14f, panel.width - 40f, panel.height - 28f));
            GUILayout.Label(TabName(_tab).ToUpperInvariant(), SurvivalGUI.Title);
            GUILayout.Space(4);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.Height(panel.height - 110f));
            switch (_tab)
            {
                case HubTab.Craft: DrawCraft(); break;
                case HubTab.Camp: DrawCamp(); break;
                case HubTab.Vehicle: DrawVehicle(); break;
                case HubTab.Status: DrawStatus(); break;
                case HubTab.Stats: DrawStats(); break;
            }
            GUILayout.EndScrollView();
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message, SurvivalGUI.Text);
            GUILayout.EndArea();
        }

        private static void Line(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, SurvivalGUI.Header, GUILayout.Width(170));
            GUILayout.Label(value, SurvivalGUI.Text);
            GUILayout.EndHorizontal();
        }

        // ---------------- craft ----------------

        private void DrawCraft()
        {
            var craft = CraftingSystem.Instance;
            if (craft == null) return;
            var recipes = craft.Recipes;
            if (recipes == null || recipes.Count == 0)
            {
                GUILayout.Label("Tarif yok. Unity menüsünden 'Endless Survival > Setup Survival Packages 2-5' çalıştırılmalı.", SurvivalGUI.Text);
                return;
            }

            if (craft.CurrentJob != null)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Yapılıyor: {craft.CurrentJob.DisplayName}  %{Mathf.RoundToInt(craft.JobProgress * 100f)}", SurvivalGUI.Header);
                if (GUILayout.Button("İptal", GUILayout.Width(80))) craft.CancelJob();
                GUILayout.EndHorizontal();
            }

            foreach (CraftStation station in System.Enum.GetValues(typeof(CraftStation)))
            {
                bool available = craft.StationAvailable(station);
                GUILayout.Space(4);
                GUILayout.Label($"{RecipeDefinition.StationName(station)}{(available ? "" : "  (yakında değil)")}", SurvivalGUI.Header);
                foreach (var r in recipes)
                {
                    if (r == null || r.station != station) continue;
                    bool can = craft.CanCraft(r, out string reason);
                    GUILayout.BeginHorizontal();
                    GUI.enabled = can;
                    if (GUILayout.Button("Yap", GUILayout.Width(60)))
                    {
                        craft.StartCraft(r);
                        _message = null;
                    }
                    GUI.enabled = true;
                    GUILayout.BeginVertical();
                    var oldColor = GUI.color;
                    if (!can) GUI.color = new Color(1f, 1f, 1f, 0.55f);
                    GUILayout.Label($"{r.DisplayName}  ->  {craft.Results(r)}", SurvivalGUI.Text);
                    GUILayout.Label($"Gerekli: {craft.Requirements(r)}{(can || reason == null ? "" : "   [" + reason + "]")}", SurvivalGUI.Small);
                    GUI.color = oldColor;
                    GUILayout.EndVertical();
                    GUILayout.EndHorizontal();
                }
            }
        }

        // ---------------- camp ----------------

        private PlayerCampfire NearbyFire()
        {
            const float range = 6f;
            Vector3 p = SurvivalDirector.PlayerPosition;
            var bound = CampfireUI.BoundFire;
            if (bound != null && (bound.transform.position - p).sqrMagnitude < range * range) return bound;
            if (Time.unscaledTime >= _nextFireSearch)
            {
                _nextFireSearch = Time.unscaledTime + 0.5f;
                _nearFire = null;
                float best = range * range;
                foreach (var f in Object.FindObjectsByType<PlayerCampfire>())
                {
                    float d = (f.transform.position - p).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        _nearFire = f;
                    }
                }
            }
            return _nearFire;
        }

        private void DrawCamp()
        {
            var player = SurvivalDirector.Player;
            var inv = SurvivalDirector.Inventory;

            GUILayout.Label("Kamp kur", SurvivalGUI.Header);
            GUILayout.Label("Kamp ateşini önüne yerleştirir: sol tık koyar, sağ tık iptal eder. Bir odun harcar. Kısayol: [B].", SurvivalGUI.Small);
            var placer = player != null ? player.GetComponent<PlayerCampPlacer>() : null;
            GUI.enabled = placer != null && !SurvivalDirector.PlayerInVehicle;
            if (GUILayout.Button("Kamp ateşi yerleştir", GUILayout.Width(220)))
            {
                CloseHub();
                placer.BeginPlacement();
            }
            GUI.enabled = true;

            GUILayout.Space(12);
            GUILayout.Label("Kamp ateşi", SurvivalGUI.Header);
            var fire = NearbyFire();
            if (fire == null || inv == null)
            {
                GUILayout.Label("Yakında kamp ateşi yok. Ateşin yanındayken [E] de bu sekmeyi açar.", SurvivalGUI.Text);
                return;
            }

            float left = fire.RemainingTime;
            GUILayout.Label(fire.IsBurning ? $"Ateş yanıyor, {Mathf.FloorToInt(left / 60f):00}:{Mathf.FloorToInt(left % 60f):00} kaldı" : "Ateş söndü, yakacak ekle", SurvivalGUI.Text);
            GUILayout.Space(4);

            CampButton("Yakacak ekle (odun)", CampfireUI.CampAction.AddFuel, fire, inv);
            CampButton("Çiğ et pişir", CampfireUI.CampAction.CookMeat, fire, inv);
            CampButton("Konserve fasulyeyi ısıt (sıcak güveç)", CampfireUI.CampAction.CookStew, fire, inv);
            CampButton("Kirli suyu kaynat", CampfireUI.CampAction.BoilWater, fire, inv);
            CampButton("Yara sar (bandaj)", CampfireUI.CampAction.Bandage, fire, inv);
            GUILayout.Space(6);
            CampButton("Ateşin başına otur", CampfireUI.CampAction.Sit, fire, inv);
            CampButton("Ateşin başında uyu (zaman ilerler)", CampfireUI.CampAction.Rest, fire, inv);

            if (!string.IsNullOrEmpty(CampfireUI.LastFeedback))
            {
                GUILayout.Space(6);
                var old = GUI.color;
                GUI.color = CampfireUI.LastFeedbackColor;
                GUILayout.Label(CampfireUI.LastFeedback, SurvivalGUI.Text);
                GUI.color = old;
            }
            GUILayout.Space(6);
            GUILayout.Label("Tütsüleme, tuzlama, balık pişirme ve diğer ateş tarifleri: Üretim sekmesi > Kamp ateşi.", SurvivalGUI.Small);
        }

        private static void CampButton(string label, CampfireUI.CampAction action, PlayerCampfire fire, PlayerInventory inv)
        {
            if (GUILayout.Button(label, GUILayout.Width(340))) CampfireUI.Do(action, fire, inv);
        }

        // ---------------- vehicle ----------------

        private void DrawVehicle()
        {
            var home = VehicleHome.Instance;
            var vehicle = SurvivalDirector.Vehicle;
            if (home == null || vehicle == null)
            {
                GUILayout.Label("Araç bulunamadı.", SurvivalGUI.Text);
                return;
            }
            bool near = home.CanUseVehicle;
            Line("Araç", $"Can {Mathf.RoundToInt(vehicle.currentHealth)} / {Mathf.RoundToInt(vehicle.maxHealth)}, yakıt {Mathf.RoundToInt(vehicle.currentFuel)} / {Mathf.RoundToInt(vehicle.maxFuel)}" + (near ? "" : "  (uzaktasın)"));
            if (VehicleHome.Has(VehicleModType.WaterTank)) Line("Su deposu", $"{home.TankWater:0} / {VehicleHome.TankCapacity:0} şişe");

            GUILayout.Space(6);
            GUI.enabled = near;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Araçta uyu", GUILayout.Width(160))) _message = home.SleepInVehicle();
            GUI.enabled = near && VehicleHome.Has(VehicleModType.WaterTank);
            if (GUILayout.Button("Temiz suyu depoya boşalt", GUILayout.Width(200))) _message = home.PourIntoTank();
            if (GUILayout.Button("Depodan şişe doldur", GUILayout.Width(180))) _message = home.FillFromTank();
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            GUILayout.Label("Araçta uyumak kamp kurmadan olur; daha az dinlendirir ama baskına karşı daha güvenlidir. Yakıt, tamir ve bagaj: Envanter sekmesi. Tamir kiti: Üretim sekmesi > Araç.", SurvivalGUI.Small);

            GUILayout.Space(10);
            GUILayout.Label("Modifiyeler" + (near ? "" : "  (takmak için aracın yanına git)"), SurvivalGUI.Header);
            var mods = VehicleModifications.Instance;
            if (mods == null) mods = vehicle.GetComponent<VehicleModifications>();
            if (mods == null || mods.modifications == null)
            {
                GUILayout.Label("Modifiye sistemi yok.", SurvivalGUI.Text);
                return;
            }
            var inv = SurvivalDirector.Inventory;
            var stash = vehicle.GetComponent<VehicleStorage>();
            VehicleModType? install = null, installFree = null, uninstall = null;
            foreach (var mod in mods.modifications)
            {
                if (mod == null) continue;
                string cost = $"{mod.scrapCost}x Hurda Metal" + (string.IsNullOrEmpty(mod.secondaryItemKeyword) ? "" : $" + {mod.secondaryItemCost}x {mod.secondaryItemKeyword}");
                GUILayout.BeginHorizontal();
                GUILayout.BeginVertical();
                GUILayout.Label($"{(mod.isInstalled ? "[X]" : "[ ]")} {mod.displayName}", SurvivalGUI.Text);
                GUILayout.Label($"Etki: {mod.statBenefit}", SurvivalGUI.Small);
                GUILayout.Label($"Maliyet: {cost}. {mod.description}", SurvivalGUI.Small);
                GUILayout.EndVertical();
                GUILayout.FlexibleSpace();
                GUI.enabled = near;
                if (mod.isInstalled)
                {
                    if (GUILayout.Button("Sök", GUILayout.Width(150))) uninstall = mod.type;
                }
                else
                {
                    bool afford = mods.HasRequiredMaterials(mod, inv, stash);
                    GUI.enabled = near && afford;
                    if (GUILayout.Button(afford ? "Monte et" : "Malzeme yetersiz", GUILayout.Width(150))) install = mod.type;
                    GUI.enabled = near;
                    if (GUILayout.Button("Test (bedava)", GUILayout.Width(110))) installFree = mod.type;
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                GUILayout.Space(6);
            }
            // Change the list after drawing it
            if (install.HasValue) mods.InstallModification(install.Value, freeCost: false);
            if (installFree.HasValue) mods.InstallModification(installFree.Value, freeCost: true);
            if (uninstall.HasValue) mods.UninstallModification(uninstall.Value);
        }

        // ---------------- status ----------------

        private void DrawStatus()
        {
            var v = PlayerVitals.Instance;
            if (v == null) return;
            var player = SurvivalDirector.Player;
            var survival = player != null ? player.GetComponent<PlayerSurvival>() : null;
            var inv = SurvivalDirector.Inventory;

            Line("Stamina", $"{Mathf.RoundToInt(v.Stamina)} / {Mathf.RoundToInt(v.MaxStamina)}");
            Line("Yorgunluk", $"{Mathf.RoundToInt(v.Fatigue)} / 100" + (v.Fatigue > 60f ? "  (stamina tavanı düşük)" : ""));
            if (survival != null)
            {
                Line("Açlık / Susuzluk", $"{Mathf.RoundToInt(survival.Hunger)} / {Mathf.RoundToInt(survival.Thirst)}");
                Line("Tuvalet", $"{Mathf.RoundToInt(survival.Bladder)} / {Mathf.RoundToInt(survival.maxBladder)}" + (v.ToiletNeglected ? "  (ihmal: yavaşlama)" : "") + "   [P] ile git");
            }
            Line("Vücut ısısı", $"{Mathf.RoundToInt(v.BodyHeat)} / 100   Hava {Mathf.RoundToInt(v.AmbientTemperature)}°C, hissedilen {Mathf.RoundToInt(v.FeltTemperature)}°C");
            Line("Islaklık", $"{Mathf.RoundToInt(v.Wetness)} / 100");
            Line("Kıyafet", $"Sıcaklık +{v.ClothingInsulation:0.#}°C, su geçirmezlik %{Mathf.RoundToInt(v.ClothingWaterproof * 100f)}, ses +%{Mathf.RoundToInt(v.ClothingNoise * 100f)}, yıpranma %{Mathf.RoundToInt(100f - v.WornAverageDurability)}");
            if (inv != null) Line("Yük", $"{inv.Storage.TotalWeight:0.0} kg / rahat {v.ComfortWeight:0.0} kg (en fazla {inv.Storage.MaxWeight:0.0})");
            Line("Stres", $"{Mathf.RoundToInt(v.Stress)} / 100" + (v.Panicking ? "  PANİK" : ""));
            int anx = v.AnxietyLevel;
            Line("Anksiyete", anx == 0 ? (v.AnxietyHint ? "Gergin (dinlenmelisin)" : "Yok") : anx == 1 ? "Hafif" : anx == 2 ? "Orta" : "Ağır");
            string health = "";
            if (v.IsPoisoned) health += "Zehirlenme  ";
            if (v.IsInfected) health += "Enfeksiyon (merhem)  ";
            else if (v.WoundDirty) health += "Kirli yara (kaynamış su/alkol ile temizle)  ";
            Line("Sağlık", health.Length > 0 ? health : "Temiz");

            var spoil = FoodSpoilage.Instance;
            if (spoil != null && inv != null)
            {
                GUILayout.Space(8);
                GUILayout.Label("Bozulabilir yiyecekler (çanta)", SurvivalGUI.Header);
                foreach (var pair in inv.Storage.Items)
                {
                    if (pair.Key == null || !pair.Key.IsPerishable) continue;
                    float left = spoil.HoursLeft(pair.Key);
                    GUILayout.Label($"{pair.Key.DisplayName} x{pair.Value}: en eskisi ~{Mathf.CeilToInt(left)} oyun saati sonra bozulur", SurvivalGUI.Small);
                }
            }

            GUILayout.Space(8);
            GUILayout.Label("Tuşlar: [Tab] bu ekran, [P] tuvalet, [H] balık tut (olta + su), [E] topla / doldur / kes, [B] kamp ateşi, sol tık: ağaçtan odun", SurvivalGUI.Small);
        }

        // ---------------- stats ----------------

        private void DrawStats()
        {
            var stats = CharacterStats.Instance;
            if (stats == null) return;
            GUILayout.Label("Kullandıkça artar. Kötü koşullar geçici düşürür (etkin / temel).", SurvivalGUI.Small);
            foreach (StatType s in System.Enum.GetValues(typeof(StatType)))
            {
                var r = GUILayoutUtility.GetRect(400, 20);
                float f = stats.Effective(s) / StatSettings.Instance.maxValue;
                float b = stats.Base(s) / StatSettings.Instance.maxValue;
                SurvivalGUI.Fill(new Rect(r.x, r.y + 2, 260 * b, 16), new Color(0.35f, 0.35f, 0.35f, 0.8f));
                SurvivalGUI.Fill(new Rect(r.x, r.y + 2, 260 * f, 16), stats.Reasons(s).Count > 0 ? new Color(0.85f, 0.5f, 0.25f, 0.9f) : new Color(0.4f, 0.75f, 0.4f, 0.9f));
                GUI.Label(new Rect(r.x + 270, r.y, r.width + 300, 20), stats.Describe(s), SurvivalGUI.Text);
            }
        }
    }
}
