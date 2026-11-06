using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using StarterAssets;
using EndlessSurvival.Save;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// 5.1 main menu (overlay on the game scene: new game with optional seed, continue, quit) and
    /// 5.2 pause menu ([Esc]: continue, save, settings with volume / mouse sensitivity, main menu).
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
            Settings
        }

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
                StarterAssetsInputs.LookSensitivity = v;
            }
        }

        private void Start()
        {
            AudioListener.volume = MasterVolume;
            StarterAssetsInputs.LookSensitivity = MouseSensitivity;

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
            if (SKeys.Down(Key.Escape))
            {
                if (_screen == Screen.Pause) Close();
                else if (_screen == Screen.Settings) _screen = _settingsReturn;
                else if (_screen == Screen.None && !otherOpen && !_otherOpenLastFrame) Open(Screen.Pause);
            }
            _otherOpenLastFrame = otherOpen;
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
            SurvivalUIState.MenuOpen = screen == Screen.Main;
        }

        /// <summary>Closes the menu (used by the play mode smoke test).</summary>
        public void CloseMenu() => Close();

        private void Close()
        {
            if (_screen == Screen.None) return;
            _screen = Screen.None;
            SurvivalUIState.PauseOpen = false;
            SurvivalUIState.MenuOpen = false;
            Time.timeScale = _savedTimeScale;
            SurvivalUIState.ReleaseCursor();
        }

        private void OnDestroy()
        {
            if (_screen != Screen.None) Time.timeScale = 1f;
        }

        // ---------------- actions ----------------

        private void NewGame()
        {
            string seed = _seedText != null ? _seedText.Trim() : "";
            ChunkManager.PendingSeed = string.IsNullOrEmpty(seed) ? RandomSeed() : seed;
            _skipMenuOnce = true;
            Reload();
        }

        private void Continue()
        {
            var save = SaveManager.Instance;
            if (save == null || !save.HasSaveFile())
            {
                _message = "Kayıt bulunamadı";
                return;
            }
            string savedSeed = save.PeekSavedSeed();
            var manager = ChunkManager.Instance;
            if (!string.IsNullOrEmpty(savedSeed) && manager != null && manager.masterSeed != savedSeed)
            {
                // The world must be built from the saved seed first
                ChunkManager.PendingSeed = savedSeed;
                _continueAfterReload = true;
                Reload();
                return;
            }
            Close();
            save.LoadGame();
        }

        private void Reload()
        {
            Time.timeScale = 1f;
            SurvivalUIState.ResetAll();
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
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
            var full = new Rect(0, 0, UnityEngine.Screen.width, UnityEngine.Screen.height);
            SurvivalGUI.Fill(full, new Color(0f, 0f, 0f, _screen == Screen.Main ? 0.82f : 0.6f));

            float w = 380f, h = _screen == Screen.Settings ? 300f : _screen == Screen.Main ? 380f : 330f;
            var rect = new Rect((UnityEngine.Screen.width - w) * 0.5f, (UnityEngine.Screen.height - h) * 0.5f, w, h);
            GUI.Box(rect, GUIContent.none, SurvivalGUI.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 24, rect.y + 18, w - 48, h - 36));

            switch (_screen)
            {
                case Screen.Main: DrawMain(); break;
                case Screen.Pause: DrawPause(); break;
                case Screen.Settings: DrawSettings(); break;
            }

            if (!string.IsNullOrEmpty(_message))
            {
                GUILayout.Space(6);
                GUILayout.Label(_message, SurvivalGUI.Small);
            }
            GUILayout.EndArea();
        }

        private void DrawMain()
        {
            GUILayout.Label("ENDLESS COMBAT SURVIVAL", SurvivalGUI.Title);
            GUILayout.Space(14);
            GUILayout.Label("Seed (boş = rastgele)", SurvivalGUI.Small);
            _seedText = GUILayout.TextField(_seedText ?? "", 24);
            GUILayout.Space(6);
            if (GUILayout.Button("Yeni oyun", GUILayout.Height(38))) NewGame();
            var save = SaveManager.Instance;
            GUI.enabled = save != null && save.HasSaveFile();
            if (GUILayout.Button("Devam et", GUILayout.Height(38))) Continue();
            GUI.enabled = true;
            if (GUILayout.Button("Ayarlar", GUILayout.Height(32)))
            {
                _settingsReturn = Screen.Main;
                _screen = Screen.Settings;
                SurvivalUIState.MenuOpen = false;
                SurvivalUIState.PauseOpen = true;
            }
            if (GUILayout.Button("Çıkış", GUILayout.Height(32))) Quit();
            GUILayout.Space(8);
            var manager = ChunkManager.Instance;
            if (manager != null) GUILayout.Label($"Şu anki dünya seed'i: {manager.masterSeed}", SurvivalGUI.Small);
        }

        private void DrawPause()
        {
            GUILayout.Label("DURAKLATILDI", SurvivalGUI.Title);
            GUILayout.Space(14);
            if (GUILayout.Button("Devam", GUILayout.Height(38))) Close();
            if (GUILayout.Button("Kaydet", GUILayout.Height(34)))
            {
                var save = SaveManager.Instance;
                _message = save != null && save.SaveGame() ? "Oyun kaydedildi" : "Kayıt alınamadı";
            }
            if (GUILayout.Button("Ayarlar", GUILayout.Height(34)))
            {
                _settingsReturn = Screen.Pause;
                _screen = Screen.Settings;
            }
            if (GUILayout.Button("Ana menü", GUILayout.Height(34)))
            {
                _screen = Screen.Main;
                SurvivalUIState.PauseOpen = false;
                SurvivalUIState.MenuOpen = true;
            }
        }

        private void DrawSettings()
        {
            GUILayout.Label("AYARLAR", SurvivalGUI.Title);
            GUILayout.Space(12);
            GUILayout.Label($"Ses: %{Mathf.RoundToInt(MasterVolume * 100f)}", SurvivalGUI.Text);
            float vol = GUILayout.HorizontalSlider(MasterVolume, 0f, 1f);
            if (!Mathf.Approximately(vol, MasterVolume)) MasterVolume = vol;
            GUILayout.Space(10);
            GUILayout.Label($"Fare hassasiyeti: {MouseSensitivity:0.00}", SurvivalGUI.Text);
            float mouse = GUILayout.HorizontalSlider(MouseSensitivity, 0.2f, 3f);
            if (!Mathf.Approximately(mouse, MouseSensitivity)) MouseSensitivity = mouse;
            GUILayout.Space(16);
            if (GUILayout.Button("Geri", GUILayout.Height(34)))
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
