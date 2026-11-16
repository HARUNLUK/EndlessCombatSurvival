using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using EndlessSurvival.Save;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// 5.1 main menu (overlay on the game scene): the character slots. A slot holds one character that lives on
    /// between journeys (stats, read notes, records) and at most one journey on the road. Picking a slot opens its
    /// screen: continue the journey, start a new one (optional seed), give the journey up, delete the character.
    /// 5.2 pause menu ([Esc]: continue, settings, save and leave). Saving is automatic, there is no manual save.
    /// </summary>
    public class GameMenus : MonoBehaviour
    {
        private static bool _skipMenuOnce;
        private static bool _continueAfterReload;

        private enum Screen
        {
            None,
            Main,
            Pause,
            Settings,
            Controls,
            Character
        }

        private enum Confirm
        {
            None,
            AbandonJourney,
            DeleteCharacter
        }

        private int _slot;
        private string _nameText = "";
        private Confirm _confirm;
        private readonly CharacterProfile[] _slots = new CharacterProfile[CharacterProfiles.SlotCount];
        private readonly bool[] _slotHasRun = new bool[CharacterProfiles.SlotCount];

        private Screen _controlsReturn = Screen.Main;
        private Vector2 _controlsScroll;

        private static readonly (string head, (string key, string what)[] rows)[] ControlGroups =
        {
            ("Hareket", new[]
            {
                ("Sol çubuk · WASD", "Yürü"), ("L3 · Shift", "Koş (gamepad: bir kez bas)"), ("A · Space", "Zıpla"), ("R3 · C", "Çömel (gizlilik)"),
                ("X · E", "Etkileşim: aç, al, topla, doldur, konuş"), ("X · F", "Araca bin (aracın yanında)"),
            }),
            ("Savaş", new[]
            {
                ("LT · Sağ tık", "Nişan / blok"), ("RT · Sol tık", "Ateş (nişanda) / ağır vuruş"),
                ("RB · Sol tık", "Hafif vuruş (kombo)"), ("B · Sol Alt", "Kaçın (yön ile)"), ("X · R", "Şarjör değiştir"),
                ("Yön ↑ ← → · 1 2 3", "Uzun namlu / tabanca / yakın dövüş"), ("Y · V", "Tek el / çift el (basılı: silahı as)"),
                ("LB · T / G", "Fırlat: bomba varsa bomba, yoksa taş"),
            }),
            ("Hayatta kalma", new[]
            {
                ("View · Tab", "Karakter, çanta, üretim, kamp, araç, durum"), ("Yön ↓ · 4", "Hızlı eşyayı kullan"),
                ("Yön ↓ basılı · Q", "Eylem menüsü: dürbün, tuvalet, sigara, olta, kamp ateşi, kayıt"),
                ("P / J / H / B / Z", "Klavye kısayolları: tuvalet, sigara, olta, ateş, dürbün"),
                ("RB · Sol tık (ağaç)", "Odun kes"),
            }),
            ("Araç", new[]
            {
                ("RT / LT · W / S", "Gaz / fren-geri"), ("Sol çubuk · A / D", "Direksiyon"), ("B · Space", "El freni"),
                ("Y · F", "Araçtan in"), ("X · R", "Motoru çalıştır"), ("Yön ↑ · H", "Farlar"), ("Yön → · T", "Çeki halatı"),
                ("Yön ← · L", "Tavan projektörü"), ("Sağ çubuk · Fare", "Kamera"),
            }),
            ("Oyun", new[]
            {
                ("Start · Esc", "Duraklat / menüyü kapat"),
                ("Kayıt", "Otomatik: uyuyunca, yeni bölgede, birkaç dakikada bir, çıkarken"),
                ("Ölüm", "Yolculuk biter; karakterin statları ve öğrendikleri kalır"),
            }),
        };

        private Screen _screen = Screen.None;
        private Screen _settingsReturn = Screen.Pause;
        private string _seedText = "";
        private string _message;
        private bool _otherOpenLastFrame;
        private float _savedTimeScale = 1f;

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat("settings.volume", 1f);
            set
            {
                PlayerPrefs.SetFloat("settings.volume", Mathf.Clamp01(value));
                AudioListener.volume = Mathf.Clamp01(value);
            }
        }

        public static float MouseSensitivity
        {
            get => PlayerPrefs.GetFloat("settings.mouse", 1f);
            set
            {
                float v = Mathf.Clamp(value, 0.2f, 3f);
                PlayerPrefs.SetFloat("settings.mouse", v);
                EndlessCombat.Character.PlayerInputState.LookSensitivity = v;
            }
        }

        private void Start()
        {
            AudioListener.volume = MasterVolume;
            EndlessCombat.Character.PlayerInputState.LookSensitivity = MouseSensitivity;

            // Test scenes start straight in the arena, without the main menu
            if (EndlessSurvival.Testing.TestArena.Active) return;

            if (_continueAfterReload)
            {
                _continueAfterReload = false;
                StartCoroutine(LoadAfterReload());
                return;
            }
            if (_skipMenuOnce)
            {
                _skipMenuOnce = false;
                return;
            }
            Open(Screen.Main);
        }

        private IEnumerator LoadAfterReload()
        {
            // Let the world build its first chunks before applying the save
            yield return null;
            yield return null;
            var save = SaveManager.Instance;
            if (save != null) save.LoadGame();
        }

        private void Update()
        {
            bool otherOpen = SurvivalUIState.OtherOpen || SurvivalUIState.PanelOpen || SurvivalUIState.TradeOpen;
            // Esc / gamepad Start opens and closes the pause menu; Esc / gamepad B goes one screen back
            bool pause = EndlessCombat.Combat.GameInput.PausePressed;
            bool back = EndlessCombat.Combat.GameInput.BackPressed;
            if (_screen == Screen.None)
            {
                if (pause && !otherOpen && !_otherOpenLastFrame && !ActionMenu.IsOpen && !SurvivalUIState.JourneyOpen) Open(Screen.Pause);
            }
            else if (pause || back)
            {
                if (_screen == Screen.Pause) Close();
                else if (_screen == Screen.Settings)
                {
                    PlayerPrefs.Save();
                    _screen = _settingsReturn;
                    SurvivalUIState.PauseOpen = _screen == Screen.Pause;
                    SurvivalUIState.MenuOpen = _screen == Screen.Main;
                }
                else if (_screen == Screen.Controls) CloseControls();
                else if (_screen == Screen.Character) BackToMain();
            }
            _otherOpenLastFrame = otherOpen;

            // Controls screen: the stick scrolls the list
            if (_screen == Screen.Controls)
            {
                float y = EndlessCombat.Combat.GameInput.PadLeftStick.y + EndlessCombat.Combat.GameInput.PadRightStick.y;
                if (Mathf.Abs(y) > 0.2f) _controlsScroll.y -= y * 600f * Time.unscaledDeltaTime;
            }
        }

        private void Open(Screen screen)
        {
            if (_screen == Screen.None)
            {
                _savedTimeScale = Time.timeScale > 0.001f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
                SurvivalUIState.AcquireCursor();
            }
            _screen = screen;
            _message = null;
            SurvivalUIState.PauseOpen = screen == Screen.Pause || screen == Screen.Settings;
            SurvivalUIState.MenuOpen = screen == Screen.Main || screen == Screen.Character;
            if (screen == Screen.Main) RefreshSlots();
        }

        /// <summary>The slot files are read once when the list opens or changes, not on every GUI event.</summary>
        private void RefreshSlots()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = CharacterProfiles.Peek(i);
                _slotHasRun[i] = _slots[i] != null && CharacterProfiles.HasRun(i);
            }
        }

        private void OpenCharacter(int slot)
        {
            _slot = slot;
            _confirm = Confirm.None;
            _message = null;
            _nameText = "Yolcu " + (slot + 1);
            _seedText = "";
            _screen = Screen.Character;
            SurvivalUIState.MenuOpen = true;
            PadGUI.Focus(this, 0);
        }

        private void BackToMain()
        {
            _confirm = Confirm.None;
            _message = null;
            _screen = Screen.Main;
            SurvivalUIState.MenuOpen = true;
            RefreshSlots();
            PadGUI.Focus(this, _slot);
        }

        /// <summary>Closes the menu (used by the play mode smoke test).</summary>
        public void CloseMenu() => Close();

        /// <summary>Test hooks for the UI capture tool.</summary>
        public void DebugOpenPause() { Close(); Open(Screen.Pause); }
        public void DebugOpenControls() => OpenControls(_screen == Screen.None ? Screen.Main : _screen);
        public void DebugOpenSettings() { _settingsReturn = _screen; _screen = Screen.Settings; }

        private void Close()
        {
            if (_screen == Screen.None) return;
            _screen = Screen.None;
            SurvivalUIState.PauseOpen = false;
            SurvivalUIState.MenuOpen = false;
            Time.timeScale = _savedTimeScale;
            EndlessCombat.Combat.GameInput.BlockGameplayBriefly();
            SurvivalUIState.ReleaseCursor();
        }

        private void OnDestroy()
        {
            if (_screen != Screen.None) Time.timeScale = 1f;
        }

        // ---------------- actions ----------------

        /// <summary>New journey for the selected slot's character (a journey still on the road is dropped).</summary>
        private void NewJourney()
        {
            if (!CharacterProfiles.Select(_slot))
            {
                _message = "Karakter okunamadı";
                return;
            }
            CharacterProfiles.DeleteRun(_slot);
            NoteJourneyStarted();
            Preparation.Commit();
            DifficultyCurve.SnapshotCharacter();
            string seed = _seedText != null ? _seedText.Trim() : "";
            ChunkManager.PendingSeed = string.IsNullOrEmpty(seed) ? RandomSeed() : seed;
            _skipMenuOnce = true;
            Reload();
        }

        /// <summary>
        /// Back onto the road with the selected slot's character. The scene is always reloaded: the world is built
        /// from the journey's seed and the character's stats are read when the survival systems start.
        /// </summary>
        private void ContinueJourney()
        {
            if (!CharacterProfiles.Select(_slot))
            {
                _message = "Karakter okunamadı";
                return;
            }
            var save = SaveManager.Instance;
            string savedSeed = save != null && save.HasSaveFile() ? save.PeekSavedSeed() : null;
            if (string.IsNullOrEmpty(savedSeed))
            {
                _message = "Yolculuk kaydı okunamadı";
                return;
            }
            ChunkManager.PendingSeed = savedSeed;
            DifficultyCurve.SnapshotCharacter();
            _continueAfterReload = true;
            Reload();
        }

        private static void NoteJourneyStarted()
        {
            var p = CharacterProfiles.Current;
            if (p == null) return;
            p.journeys++;
            CharacterProfiles.SaveCurrent();
        }

        /// <summary>Pause menu: the journey is saved, then the scene reloads onto the main menu.</summary>
        private void SaveAndLeave(bool quit)
        {
            var save = SaveManager.Instance;
            if (save != null && save.CanSave) save.SaveGame(isAutoSave: true, silent: true);
            if (quit)
            {
                Quit();
                return;
            }
            Reload();
        }

        private void Reload()
        {
            Time.timeScale = 1f;
            SurvivalUIState.ResetAll();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }

        /// <summary>The next scene load starts a new run on a random world and skips the main menu (game over "Yeni Koşu").</summary>
        public static void PrepareNewRun()
        {
            // Same character, new journey. After a death the journey is already closed and this does nothing;
            // from the arrival screen it drops the journey that just reached the Safe Zone.
            var save = SaveManager.Instance;
            if (save != null) save.EndRun(false);
            NoteJourneyStarted();
            Preparation.Commit();
            DifficultyCurve.SnapshotCharacter();
            ChunkManager.PendingSeed = RandomSeed();
            _skipMenuOnce = true;
            _continueAfterReload = false;
            SurvivalUIState.ResetAll();
        }

        private static string RandomSeed()
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var c = new char[10];
            for (int i = 0; i < c.Length; i++) c[i] = chars[Random.Range(0, chars.Length)];
            return new string(c);
        }

        // ---------------- GUI ----------------

        private void OnGUI()
        {
            if (_screen == Screen.None) return;
            SurvivalGUI.Init();
            GUI.depth = -100;
            bool front = _screen == Screen.Main || _screen == Screen.Character; // the title screens hide the world
            if (!PadGUI.Begin(this))
            {
                // A gamepad press changed the screen this frame: keep the dark backdrop, draw the rest next frame
                SurvivalGUI.Fill(new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height), new Color(0f, 0f, 0f, front ? 0.9f : 0.65f));
                return;
            }
            var full = new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height);
            SurvivalGUI.Fill(full, new Color(0f, 0f, 0f, front ? 0.9f : 0.65f));
            if (front) DrawMainBackdrop();

            float w = _screen == Screen.Controls ? 960f : front ? 480f : 380f;
            float h = _screen == Screen.Settings ? 350f : front ? MainHeight : _screen == Screen.Controls ? 650f : 310f;
            w = Mathf.Min(w, UnityEngine.Screen.width - 32f);
            h = Mathf.Min(h, UnityEngine.Screen.height - 32f);
            var rect = new Rect((UnityEngine.Screen.width - w) * 0.5f, (UnityEngine.Screen.height - h) * 0.5f, w, h);
            GUI.Box(rect, GUIContent.none, SurvivalGUI.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 24, rect.y + 18, w - 48, h - 36));

            switch (_screen)
            {
                case Screen.Main: DrawMain(); break;
                case Screen.Pause: DrawPause(); break;
                case Screen.Settings: DrawSettings(); break;
                case Screen.Controls: DrawControls(); break;
                case Screen.Character: DrawCharacter(); break;
            }

            if (!string.IsNullOrEmpty(_message))
            {
                GUILayout.Space(6);
                GUILayout.Label(_message, SurvivalGUI.Small);
            }
            GUILayout.EndArea();
            PadGUI.End();
            if (PadGUI.Show)
                GUI.Label(new Rect(rect.x, rect.yMax + 6f, rect.width, 22f), "Yön tuşu / sol çubuk: seç    A: onayla    B: geri", SurvivalGUI.SmallRight);
        }

        private void OpenControls(Screen from)
        {
            _controlsReturn = from;
            _screen = Screen.Controls;
            _controlsScroll = Vector2.zero;
            // Keep MenuOpen while it was reached from the main menu, so the HUD stays hidden behind it
            SurvivalUIState.MenuOpen = from == Screen.Main;
            SurvivalUIState.PauseOpen = from != Screen.Main;
        }

        private void CloseControls()
        {
            _screen = _controlsReturn;
            SurvivalUIState.PauseOpen = _screen == Screen.Pause;
            SurvivalUIState.MenuOpen = _screen == Screen.Main;
        }

        /// <summary>Big faded title and a one-line pitch behind the main menu box.</summary>
        private static void DrawMainBackdrop()
        {
            float sw = UnityEngine.Screen.width, sh = UnityEngine.Screen.height;
            if (_bigTitle == null)
            {
                _bigTitle = new GUIStyle(GUI.skin.label) { fontSize = 46, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.95f, 0.82f, 0.55f, 0.95f) } };
                _subTitle = new GUIStyle(GUI.skin.label) { fontSize = 15, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(1f, 1f, 1f, 0.6f) } };
            }
            float top = Mathf.Max(8f, (sh - MainHeight) * 0.5f - 104f);
            GUI.Label(new Rect(0, top, sw, 60f), "ENDLESS COMBAT SURVIVAL", _bigTitle);
            GUI.Label(new Rect(0, top + 58f, sw, 24f), "Yol bitmez. Yakıtını, suyunu ve aklını koru.", _subTitle);
            SurvivalGUI.Fill(new Rect(sw * 0.5f - 160f, top + 88f, 320f, 2f), new Color(0.95f, 0.8f, 0.5f, 0.5f));
        }

        private static GUIStyle _bigTitle, _subTitle;

        private void DrawControls()
        {
            GUILayout.Label("KONTROLLER", SurvivalGUI.Title);
            GUILayout.Space(6);
            _controlsScroll = GUILayout.BeginScrollView(_controlsScroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            GUILayout.BeginHorizontal();
            for (int col = 0; col < 2; col++)
            {
                GUILayout.BeginVertical(GUILayout.Width(430f));
                for (int g = col; g < ControlGroups.Length; g += 2)
                {
                    GUILayout.Space(8);
                    GUILayout.Label(ControlGroups[g].head, SurvivalGUI.Header);
                    foreach (var row in ControlGroups[g].rows)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(row.key, SurvivalGUI.Tag, GUILayout.Width(170f));
                        GUILayout.Label(row.what, SurvivalGUI.Small);
                        GUILayout.EndHorizontal();
                    }
                }
                GUILayout.EndVertical();
                GUILayout.Space(14);
            }
            GUILayout.EndHorizontal();
            GUILayout.Space(8);
            GUILayout.Label("İpucu: Ekrandaki sol üst kart can, tokluk, su ve vücut durumunu gösterir. Kırmızı yanıp sönen çubuk acil ihtiyaçtır.", SurvivalGUI.Small);
            GUILayout.EndScrollView();
            if (PadGUI.Button("Geri", GUILayout.Height(32))) CloseControls();
        }

        private const float MainHeight = 620f;

        /// <summary>The character slots: an empty slot makes a new character, a filled one opens its screen.</summary>
        private void DrawMain()
        {
            GUILayout.Label("KARAKTERLER", SurvivalGUI.Title);
            GUILayout.Space(8);
            for (int i = 0; i < _slots.Length; i++)
            {
                var p = _slots[i];
                string label = p == null ? $"{i + 1}.  Boş yuva  ·  yeni karakter" : $"{i + 1}.  {p.name}";
                if (PadGUI.Button(label, GUILayout.Height(36))) OpenCharacter(i);
                GUILayout.Label(p == null ? " " : SlotSummary(p, _slotHasRun[i]), SurvivalGUI.Small);
            }
            GUILayout.Space(6);
            if (PadGUI.Button("Kontroller", GUILayout.Height(30))) OpenControls(Screen.Main);
            if (PadGUI.Button("Ayarlar", GUILayout.Height(30)))
            {
                _settingsReturn = Screen.Main;
                _screen = Screen.Settings;
            }
            if (PadGUI.Button("Çıkış", GUILayout.Height(30))) Quit();
        }

        private static string SlotSummary(CharacterProfile p, bool hasRun)
        {
            string road = hasRun ? $"Yolda: gün {Mathf.Max(1, p.runDay)}, {p.runKm:0.0} km" : "Yolculuk yok";
            return $"     {road}   ·   en uzak {p.bestKm:0.0} km   ·   {p.deaths} ölüm";
        }

        private static string StatsText(CharacterProfile p)
        {
            if (p.stats == null || p.stats.Length == 0) return "Statlar başlangıç değerlerinde.";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < p.stats.Length; i++)
            {
                if (i > 0) sb.Append(i % 4 == 0 ? "\n" : "   ·   ");
                sb.Append(StatSettings.StatName((StatType)i)).Append(' ').Append(Mathf.FloorToInt(p.stats[i]));
            }
            return sb.ToString();
        }

        /// <summary>One slot: make the character, or continue / start / give up its journey, or delete it.</summary>
        private void DrawCharacter()
        {
            var p = _slots[_slot];
            if (p == null)
            {
                GUILayout.Label("YENİ KARAKTER", SurvivalGUI.Title);
                GUILayout.Space(12);
                GUILayout.Label("İsim", SurvivalGUI.Small);
                _nameText = GUILayout.TextField(_nameText ?? "", 18);
                GUILayout.Space(8);
                GUILayout.Label("Karakter yolculuklar arasında yaşar: statları, öğrendikleri ve okuduğu notlar kalır. Ölünce sadece yolculuk biter; çanta, araç ve dünya sıfırlanır.", SurvivalGUI.Small);
                GUILayout.Space(10);
                if (PadGUI.Button("Oluştur", GUILayout.Height(38)))
                {
                    CharacterProfiles.Create(_slot, _nameText);
                    RefreshSlots();
                    PadGUI.Focus(this, 0);
                }
                if (PadGUI.Button("Geri", GUILayout.Height(32))) BackToMain();
                return;
            }

            bool hasRun = _slotHasRun[_slot];
            GUILayout.Label(p.name.ToUpperInvariant(), SurvivalGUI.Title);
            GUILayout.Space(4);
            GUILayout.Label($"{p.journeys} yolculuk   ·   {p.deaths} ölüm   ·   en uzak {p.bestKm:0.0} km   ·   Güvenli Bölge {p.arrivals} kez", SurvivalGUI.Small);
            GUILayout.Space(6);
            GUILayout.Label(StatsText(p), SurvivalGUI.Small);
            GUILayout.Label($"Öğrenilen plan: {(p.knownPlans != null ? p.knownPlans.Count : 0)} / {Plans.Total}", SurvivalGUI.Small);
            GUILayout.Space(10);

            if (hasRun)
            {
                if (PadGUI.Button($"Devam et   ·   gün {Mathf.Max(1, p.runDay)}, {p.runKm:0.0} km", GUILayout.Height(40))) ContinueJourney();
                string abandon = _confirm == Confirm.AbandonJourney ? "Emin misin? Yolculuk silinir, karakter kalır" : "Yolculuğu bırak";
                if (PadGUI.Button(abandon, GUILayout.Height(32)))
                {
                    if (_confirm == Confirm.AbandonJourney)
                    {
                        CharacterProfiles.DeleteRun(_slot);
                        _confirm = Confirm.None;
                        RefreshSlots();
                    }
                    else _confirm = Confirm.AbandonJourney;
                }
            }
            else
            {
                GUILayout.Label("Seed (boş = rastgele)", SurvivalGUI.Small);
                _seedText = GUILayout.TextField(_seedText ?? "", 24);
                GUILayout.Space(4);
                int chosen = Preparation.ChosenCost;
                if (PadGUI.Button(chosen > 0 ? $"Yeni yolculuk   ·   hazırlık {Mathf.Min(chosen, p.prepPoints)} puan" : "Yeni yolculuk", GUILayout.Height(40))) NewJourney();

                // R5: starting gear for this journey, paid with the points earned on the road
                GUILayout.Space(4);
                GUILayout.Label($"Hazırlık puanı: {p.prepPoints}   (yolda kazanılır: her bölge +{SurvivalSettings.Instance.prepPointsPerZone}, her Güvenli Bölge +{SurvivalSettings.Instance.prepPointsPerSafeZone}; sadece başlangıç eşyası alır)", SurvivalGUI.Small);
                int left = p.prepPoints - chosen;
                foreach (var option in Preparation.Options)
                {
                    bool on = Preparation.Chosen.Contains(option.id);
                    GUI.enabled = on || option.cost <= left;
                    bool now = PadGUI.Toggle(on, $"  {option.label}  ({option.cost})");
                    GUI.enabled = true;
                    if (now == on) continue;
                    if (now) Preparation.Chosen.Add(option.id);
                    else Preparation.Chosen.Remove(option.id);
                }
            }

            string delete = _confirm == Confirm.DeleteCharacter ? "Emin misin? Karakter ve her şeyi silinir" : "Karakteri sil";
            if (PadGUI.Button(delete, GUILayout.Height(32)))
            {
                if (_confirm == Confirm.DeleteCharacter)
                {
                    CharacterProfiles.Delete(_slot);
                    BackToMain();
                }
                else _confirm = Confirm.DeleteCharacter;
            }
            if (PadGUI.Button("Geri", GUILayout.Height(32))) BackToMain();
        }

        private void DrawPause()
        {
            GUILayout.Label("DURAKLATILDI", SurvivalGUI.Title);
            GUILayout.Space(14);
            if (PadGUI.Button("Devam", GUILayout.Height(38))) Close();
            if (PadGUI.Button("Kontroller", GUILayout.Height(34))) OpenControls(Screen.Pause);
            if (PadGUI.Button("Ayarlar", GUILayout.Height(34)))
            {
                _settingsReturn = Screen.Pause;
                _screen = Screen.Settings;
            }
            if (PadGUI.Button("Kaydet ve ana menüye dön", GUILayout.Height(34))) SaveAndLeave(false);
            if (PadGUI.Button("Kaydet ve oyundan çık", GUILayout.Height(34))) SaveAndLeave(true);
        }

        private void DrawSettings()
        {
            GUILayout.Label("AYARLAR", SurvivalGUI.Title);
            GUILayout.Space(12);
            GUILayout.Label($"Ses: %{Mathf.RoundToInt(MasterVolume * 100f)}", SurvivalGUI.Text);
            float vol = PadGUI.Slider(MasterVolume, 0f, 1f);
            if (!Mathf.Approximately(vol, MasterVolume)) MasterVolume = vol;
            GUILayout.Space(10);
            GUILayout.Label($"Fare hassasiyeti: {MouseSensitivity:0.00}", SurvivalGUI.Text);
            float mouse = PadGUI.Slider(MouseSensitivity, 0.2f, 3f);
            if (!Mathf.Approximately(mouse, MouseSensitivity)) MouseSensitivity = mouse;
            GUILayout.Space(12);
            bool tips = PadGUI.Toggle(SurvivalTips.Enabled, "  Oyun içi ipuçları");
            if (tips != SurvivalTips.Enabled) SurvivalTips.Enabled = tips;
            GUILayout.Space(12);
            if (PadGUI.Button("Geri", GUILayout.Height(34)))
            {
                PlayerPrefs.Save();
                _screen = _settingsReturn;
                SurvivalUIState.PauseOpen = _screen == Screen.Pause;
                SurvivalUIState.MenuOpen = _screen == Screen.Main;
            }
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
