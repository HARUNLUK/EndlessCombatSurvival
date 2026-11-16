using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Contextual tips: the first time something happens in a run (hungry, bleeding, first night, low fuel...) a
    /// short "İPUCU" box at the bottom of the screen says what to do about it. Each tip shows once per run, one at a
    /// time, never while a menu is open. Can be turned off in Ayarlar (PlayerPrefs "settings.tips").
    /// </summary>
    public class SurvivalTips : MonoBehaviour
    {
        public static bool Enabled
        {
            get => PlayerPrefs.GetInt("settings.tips", 1) == 1;
            set => PlayerPrefs.SetInt("settings.tips", value ? 1 : 0);
        }

        private struct Tip
        {
            public string id, title, text;
        }

        private readonly HashSet<string> _shown = new HashSet<string>();
        private readonly Queue<Tip> _queue = new Queue<Tip>();
        private Tip _current;
        private float _currentUntil;
        private float _nextCheck;
        private float _startTime = -1f;
        private const float ShowSeconds = 8f;

        private static GUIStyle _titleStyle, _textStyle;

        private void Update()
        {
            if (!Enabled) return;
            // The tip waits (timer paused) while the Tab screen or another panel covers the screen
            if (_current.id != null && SurvivalUIState.AnyOpen) _currentUntil += Time.unscaledDeltaTime;

            if (SurvivalUIState.MenuOpen || SurvivalUIState.PauseOpen) return;
            if (GameOverUI.IsGameOver) return;
            var player = SurvivalDirector.Player;
            if (player == null) return;
            if (_startTime < 0f) _startTime = Time.time;

            if (Time.time >= _nextCheck)
            {
                _nextCheck = Time.time + 0.5f;
                Check(player);
            }

            if (_current.id != null && Time.unscaledTime > _currentUntil) _current = default;
            if (_current.id == null && _queue.Count > 0 && !SurvivalUIState.AnyOpen)
            {
                _current = _queue.Dequeue();
                _currentUntil = Time.unscaledTime + ShowSeconds;
            }
        }

        private void Offer(string id, string title, string text)
        {
            if (_shown.Contains(id)) return;
            _shown.Add(id);
            _queue.Enqueue(new Tip { id = id, title = title, text = text });
        }

        private void Check(GameObject player)
        {
            float since = Time.time - _startTime;
            if (since > 2f)
                Offer("start", "Hoş geldin", EndlessCombat.Combat.GameInput.UsingPad ? "Sol üstteki kart hayati değerlerin. View envanteri açar, Start menüsünde tüm tuşlar var. Yön ↓ basılı: eylemler." : "Sol üstteki kart hayati değerlerin. [Tab] envanteri açar, [Esc] menüde tüm tuşlar var.");
            if (since > 14f)
                Offer("road", "Yol", "Pusuladaki turuncu işaret yolun devamını gösterir. Araçla ilerle, yakıt ve erzak topla.");

            var health = player.GetComponent<PlayerHealth>();
            var survival = player.GetComponent<PlayerSurvival>();
            var vitals = PlayerVitals.Instance;

            if (health != null && !health.IsDead)
            {
                if (health.isBleeding)
                    Offer("bleed", "Kanıyorsun", "Kanama canını eritir. Tab > Envanter'den sargı bezi ya da ilk yardım çantası kullan.");
                if (health.CurrentHealth < health.maxHealth * 0.4f)
                    Offer("lowhp", "Canın az", "Güvenli bir yere çekil. Sargı, ilk yardım çantası ve pişmiş yemek can verir.");
            }

            if (survival != null)
            {
                if (survival.Hunger < survival.maxHunger * 0.45f)
                    Offer("hunger", "Acıktın", "Tab > Envanter'den bir yiyecek ye. Çiğ eti kamp ateşinde pişir, bozulmadan önce tüket.");
                if (survival.Thirst < survival.maxThirst * 0.45f)
                    Offer("thirst", "Susadın", "Su şişesi iç. Boş şişeyi dere başında " + EndlessCombat.Combat.GameInput.Hint("[E]", "X") + " ile doldur; kirli suyu ateşte kaynat.");
                if (survival.IsBladderFull)
                    Offer("toilet", "Tuvalet", EndlessCombat.Combat.GameInput.Hint("[P]", "Yön ↓ basılı > Tuvalet") + " ile ihtiyacını gider. Uzun süre beklersen stamina yavaş dolar.");
            }

            if (vitals != null)
            {
                if (vitals.Fatigue >= SurvivalSettings.Instance.fatigueWarning)
                    Offer("tired", "Yorgunsun", "Kamp ateşinin başında ya da araçta uyu. Yorgunluk stamina tavanını düşürür.");
                if (vitals.ColdSeverity > 0.05f)
                    Offer("cold", "Üşüyorsun", "Ateşin yanına geç, araca bin ya da kalın kıyafet giy. Islaklık soğuğu artırır.");
                if (vitals.Overloaded)
                    Offer("heavy", "Çanta ağır", "Fazla yük seni yavaşlatır. Eşyaları araç bagajına koy (Tab > Envanter > Araca).");
                if (vitals.Stress > 60f)
                    Offer("stress", "Stres yükseliyor", "Ateş başında oturmak, uyku, yemek ve sigara stresi düşürür. Uzun süre yüksek kalırsa anksiyete başlar.");
            }

            var day = DayNightCycle.Instance;
            if (day != null && day.IsNight && since > 5f)
                Offer("night", "Gece", "Gece soğuk ve tehlikeli. " + EndlessCombat.Combat.GameInput.Hint("[B]", "Yön ↓ basılı > Kamp ateşi") + " ile kamp ateşi kur; ateşte pişir, ısın ve uyu.");

            var car = SurvivalDirector.Vehicle;
            if (car != null)
            {
                bool inside = car.isDriven;
                float dist = Vector3.Distance(car.transform.position, player.transform.position);
                if (!inside && dist < 6f && since > 8f)
                    Offer("car", "Araç", EndlessCombat.Combat.GameInput.Hint("[F]", "X") + " ile araca bin. Bagaj sınırsızdır; envanter ekranının Çanta sekmesinde sağda görünür.");
                if (inside)
                    Offer("drive", "Sürüş", EndlessCombat.Combat.GameInput.UsingPad ? "X motoru çalıştırır, Yön ↑ farlar, B el freni, Y araçtan iner. Araç sekmesinde (View) lastik, akü ve camları gör." : "[R] motoru çalıştırır, [H] farlar, [Space] el freni, [F] iner. Araç sekmesinde ([Tab]) lastik, akü ve camları gör.");
                if (car.maxFuel > 0f && car.currentFuel < car.maxFuel * 0.25f)
                    Offer("fuel", "Yakıt azalıyor", "Benzin istasyonlarında pompa, enkazlarda hortumla yakıt bulunur. Benzin bidonu ile depoyu doldur.");
                if (car.maxHealth > 0f && car.currentHealth < car.maxHealth * 0.5f)
                    Offer("carhp", "Araç hasarlı", "Araçtan in, Tab > Envanter'de hurda metal ile tamir et.");
            }

            if (SurvivalDirector.DangerNearby(player.transform.position, 30f))
                Offer("danger", "Tehlike", "Seni fark ettiler. [C] ile çömel ve saklan, ya da siper al. [Sol Alt] ile yana kaçın.");
        }

        private void OnGUI()
        {
            if (_current.id == null || !Enabled) return;
            if (SurvivalUIState.AnyOpen || GameOverUI.IsGameOver) return;
            SurvivalGUI.Init();
            if (_titleStyle == null)
            {
                _titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.95f, 0.8f, 0.45f) } };
                _textStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, wordWrap = true, normal = { textColor = Color.white } };
            }

            float remain = _currentUntil - Time.unscaledTime;
            float alpha = Mathf.Clamp01(remain * 1.5f) * Mathf.Clamp01((ShowSeconds - remain) * 4f);
            float w = Mathf.Min(560f, Screen.width - 40f);
            float textH = _textStyle.CalcHeight(new GUIContent(_current.text), w - 30f);
            float h = 30f + textH + 10f;
            float x = (Screen.width - w) * 0.5f;
            float y = Screen.height - h - (SurvivalDirector.PlayerInVehicle ? 170f : 110f);

            var old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            SurvivalGUI.Fill(new Rect(x, y, w, h), new Color(0.05f, 0.05f, 0.06f, 0.82f));
            SurvivalGUI.Fill(new Rect(x, y, 4f, h), new Color(0.95f, 0.8f, 0.45f, 0.9f));
            GUI.Label(new Rect(x + 16f, y + 6f, w - 30f, 20f), "İPUCU  ·  " + _current.title, _titleStyle);
            GUI.Label(new Rect(x + 16f, y + 26f, w - 30f, textH + 4f), _current.text, _textStyle);
            GUI.color = old;
        }
    }
}
