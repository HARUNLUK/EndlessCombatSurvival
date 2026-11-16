using UnityEngine;
using UnityEngine.InputSystem;
using EndlessCombat.Combat;
using EndlessSurvival.Camping;
using EndlessSurvival.Save;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Gamepad action menu: hold D-pad down (keyboard: Q). The survival actions that only had keyboard keys
    /// (binoculars Z, toilet P, cigarette J, fishing H, campfire B) in one short list.
    /// The game keeps running while it is open; B / Q / D-pad down closes it.
    /// The actions are started the same way the keys start them (SKeys.Inject), so their rules stay in one place.
    /// </summary>
    public class ActionMenu : MonoBehaviour
    {
        public static bool IsOpen { get; private set; }

        private float _openedAt;
        private bool _nearFresh;

        private void OnDisable() => IsOpen = false;

        private void Update()
        {
            var player = SurvivalDirector.Player;
            bool onFoot = player != null && player.activeInHierarchy && !SurvivalDirector.PlayerInVehicle;

            if (IsOpen)
            {
                // Cached once per frame so the Layout and Repaint passes of OnGUI agree on the button list
                var water = onFoot ? player.GetComponent<WaterFillInteractable>() : null;
                _nearFresh = water != null && water.NearFreshWater;
                bool close = GameInput.BackPressed || SKeys.Down(Key.Q);
                if (close || !onFoot || SurvivalUIState.AnyOpen || Time.timeScale <= 0.001f) Close();
                return;
            }

            if (!onFoot || SurvivalUIState.AnyOpen || Time.timeScale <= 0.001f) return;
            if (CombatBody.PlayerLocked) return;
            if (GameInput.ActionMenuPressed)
            {
                IsOpen = true;
                _openedAt = Time.unscaledTime;
                PadGUI.Focus(this, 0);
            }
        }

        private static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            GameInput.BlockGameplayBriefly();
        }

        private void OnGUI()
        {
            if (!IsOpen) return;
            SurvivalGUI.Init();
            GUI.depth = -60; // above the HUD cards and prompts
            if (!PadGUI.Begin(this)) return;

            float w = 330f, h = 336f + 38f + (_nearFresh ? 38f : 0f);
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            SurvivalGUI.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.35f));
            GUI.Box(rect, GUIContent.none, SurvivalGUI.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 16f, rect.y + 10f, w - 32f, h - 20f));
            GUILayout.Label("EYLEMLER", SurvivalGUI.Title);
            GUILayout.Space(4);

            if (PadGUI.Button("Dürbünle bak", GUILayout.Height(34))) Do(Binoculars);
            if (PadGUI.Button("Tuvalet", GUILayout.Height(34))) Do(() => SKeys.Inject(Key.P));
            if (PadGUI.Button("Sigara iç", GUILayout.Height(34))) Do(() => SKeys.Inject(Key.J));
            if (PadGUI.Button("Balık tut (su başında)", GUILayout.Height(34))) Do(() => SKeys.Inject(Key.H));
            if (PadGUI.Button("Kamp ateşi kur", GUILayout.Height(34))) Do(Campfire);
            if (_nearFresh && PadGUI.Button("Sudan iç", GUILayout.Height(34))) Do(DrinkFromWater);

            string dewBlock = DewCollecting.BlockReason();
            bool prevEnabled = GUI.enabled;
            GUI.enabled = prevEnabled && dewBlock == null;
            if (PadGUI.Button(dewBlock == null ? "Çiy topla" : $"Çiy topla ({dewBlock})", GUILayout.Height(34))) Do(Dew);
            GUI.enabled = prevEnabled;
            if (PadGUI.Button("Kapat", GUILayout.Height(30))) Close();

            GUILayout.Label(PadGUI.Show ? "Yön tuşu: seç   A: yap   B: kapat" : "Tıkla ya da Q ile kapat", SurvivalGUI.Small);
            GUILayout.EndArea();
            PadGUI.End();
        }

        private static void Do(System.Action action)
        {
            Close();
            action();
        }

        private static void Binoculars()
        {
            var bino = SurvivalDirector.Instance != null ? SurvivalDirector.Instance.GetComponent<BinocularsController>() : null;
            if (bino == null) return;
            string msg = bino.TryOpen(true);
            if (!string.IsNullOrEmpty(msg)) SurvivalNotify.Warn(msg);
        }

        private static void DrinkFromWater()
        {
            var player = SurvivalDirector.Player;
            var water = player != null ? player.GetComponent<WaterFillInteractable>() : null;
            if (water == null) return;
            string msg = water.TryDirectDrink(out bool started);
            if (!string.IsNullOrEmpty(msg) && !started) SurvivalNotify.Warn(msg);
        }

        private static void Dew()
        {
            string msg = DewCollecting.TryStart();
            if (!string.IsNullOrEmpty(msg)) SurvivalNotify.Warn(msg);
            else SurvivalNotify.Show("Çiy toplanıyor (yerinden oynama)");
        }

        private static void Campfire()
        {
            var player = SurvivalDirector.Player;
            var placer = player != null ? player.GetComponent<PlayerCampPlacer>() : null;
            if (placer != null) placer.BeginPlacement();
        }
    }
}
