using System.Collections.Generic;
using UnityEngine;
using EndlessCombat.Combat;
using EndlessSurvival.Camping;
using EndlessSurvival.Save;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// The frame of a run: where it starts, what the player is heading for and how it ends.
    ///   Start      : a short briefing on a new run and a "first steps" list (get in the car, drive, loot, camp, sleep, fight).
    ///   On the way : the road is cut into zones (every zoneKm). Entering a new zone is a milestone: a notice, an
    ///                automatic save when no enemy is near, a little stress off. The strip under the compass shows
    ///                the day, the distance and how far the destination is.
    ///   Stops      : a Safe Zone every safeZoneEveryKm (SafeZone.cs builds the place): a summary screen and a save;
    ///                the road has no last one and gets harder after each (DifficultyCurve). Dying ends the journey
    ///                on the game over screen as before. The farthest distance is kept as a record.
    /// Everything is saved with the game (ISurvivalSave).
    /// </summary>
    public class Journey : MonoBehaviour, ISurvivalSave
    {
        public static Journey Instance { get; private set; }

        [Header("Road")]
        [Tooltip("Length of one zone in km: entering the next one is a milestone (notice + auto save)")]
        public float zoneKm = 2f;
        /// <summary>Distance between two Safe Zones in km (SurvivalSettings.safeZoneEveryKm). The road has no last one.</summary>
        public float destinationKm => Mathf.Max(0.05f, SurvivalSettings.Instance.safeZoneEveryKm);

        public float MaxKm { get; private set; }
        public int Zone { get; private set; } = 1;
        /// <summary>Safe Zones reached on this journey.</summary>
        public int Stops { get; private set; }
        public bool Arrived => Stops > 0;
        public float NextStopKm => (Stops + 1) * destinationKm;

        private bool _showArrival;
        private float _savedTimeScale = 1f;
        private bool _briefed;
        private float _startedAt;
        private bool _slept;
        private readonly bool[] _stepDone = new bool[Steps.Length];
        private float _stepsHiddenAt = -1f;
        private GUIStyle _strip, _stepStyle, _stepHead, _big;

        private static readonly string[] Steps =
        {
            "Araca bin",
            "500 metre yol al",
            "Bir sandık, enkaz ya da ceset ara",
            "Bir düşmanı etkisiz hale getir",
            "Kamp ateşi kur",
            "Uyu (ateş başında ya da araçta)",
        };

        public string SaveKey => "journey";

        [System.Serializable]
        private class State
        {
            public float maxKm;
            public int zone = 1;
            public bool arrived; // saves from before the stops repeated
            public int stops;
            public bool briefed;
            public bool slept;
            public bool[] steps;
        }

        public string CaptureJson() => JsonUtility.ToJson(new State { maxKm = MaxKm, zone = Zone, arrived = Arrived, stops = Stops, briefed = _briefed, slept = _slept, steps = _stepDone });

        public void RestoreJson(string json)
        {
            var s = JsonUtility.FromJson<State>(json);
            if (s == null) return;
            MaxKm = s.maxKm;
            Zone = Mathf.Max(1, s.zone);
            Stops = Mathf.Max(s.stops, s.arrived ? 1 : 0);
            // Never behind the distance already driven (a stop is not announced twice after a load)
            Stops = Mathf.Max(Stops, Mathf.FloorToInt(MaxKm / destinationKm));
            _briefed = true; // a loaded run is not a fresh start
            _slept = s.slept;
            if (s.steps != null)
                for (int i = 0; i < _stepDone.Length && i < s.steps.Length; i++) _stepDone[i] = s.steps[i];
        }

        private void Awake()
        {
            Instance = this;
            _startedAt = Time.time;
            DayNightCycle.Slept += OnSlept;
        }

        private void Start()
        {
            if (SurvivalDirector.Instance != null) SurvivalDirector.Instance.Register(this);
        }

        private void OnDestroy()
        {
            DayNightCycle.Slept -= OnSlept;
            if (_showArrival) CloseArrival();
            if (Instance == this) Instance = null;
        }

        private void OnSlept(float hours, bool inVehicle) => _slept = true;

        public static int Day => Mathf.FloorToInt((float)(GameClock.TotalHours / 24.0)) + 1;

        private void Update()
        {
            if (SurvivalDirector.Player == null || GameOverUI.IsGameOver) return;
            if (SurvivalUIState.MenuOpen) { _startedAt = Time.time; return; } // main menu still up

            float km = DifficultyCurve.Km(SurvivalDirector.PlayerPosition);
            if (km > MaxKm) MaxKm = km;

            // Briefing a few seconds into a new run
            if (!_briefed && Time.time - _startedAt > 2.5f && !SurvivalUIState.AnyOpen)
            {
                _briefed = true;
                SurvivalNotify.Show($"Yol tek yön. İlk Güvenli Bölge {destinationKm:0} km ileride; her {destinationKm:0} km'de bir tane var.", new Color(1f, 0.85f, 0.5f), 9f);
                SurvivalNotify.Show("Depo dolu değil: yakıt, su, yiyecek ve mermi için durman gerekecek. Gece ateş yak ve dinlen.", new Color(1f, 0.85f, 0.5f), 10f);
            }

            // Zones
            int zone = Mathf.FloorToInt(MaxKm / Mathf.Max(0.2f, zoneKm)) + 1;
            if (zone > Zone)
            {
                Zone = zone;
                OnNewZone();
            }

            // The next Safe Zone
            if (!_showArrival && MaxKm >= NextStopKm)
            {
                Stops++;
                Preparation.Earn(SurvivalSettings.Instance.prepPointsPerSafeZone);
                PlayerPrefs.SetInt("best.arrivals", PlayerPrefs.GetInt("best.arrivals", 0) + 1);
                var vitalsNow = PlayerVitals.Instance;
                if (vitalsNow != null) vitalsNow.AddStressRelief(40f);
                var character = CharacterProfiles.Current;
                if (character != null) character.arrivals++; // written with the save OpenArrival makes
                OpenArrival();
            }

            if (MaxKm > PlayerPrefs.GetFloat("best.km", 0f) + 0.05f) PlayerPrefs.SetFloat("best.km", MaxKm);
            UpdateSteps();
            UpdateLeftVehicle();

            if (_showArrival && (GameInput.PadPressed(1) || SKeys.Down(UnityEngine.InputSystem.Key.Escape))) CloseArrival();
        }

        // ------------------------------------------------------------------ R6: the vehicle left behind

        private float _nextCarCheck;
        private bool _strandedTold;
        private Rigidbody _heldCar;

        /// <summary>
        /// Losing the vehicle does not end the journey: the road can be walked, and the tow at the next Safe Zone
        /// brings the vehicle back for money. A vehicle left far behind is held in place (its ground unloads).
        /// </summary>
        private void UpdateLeftVehicle()
        {
            if (Time.time < _nextCarCheck) return;
            _nextCarCheck = Time.time + 1f;
            var car = SurvivalDirector.Vehicle;
            var player = SurvivalDirector.Player;
            if (car == null || player == null) return;
            if (SurvivalDirector.PlayerInVehicle)
            {
                _strandedTold = false;
                return;
            }

            float dist = Vector3.Distance(car.transform.position, player.transform.position);
            var rb = car.GetComponent<Rigidbody>();
            if (rb != null)
            {
                if (dist > 350f && !rb.isKinematic)
                {
                    rb.isKinematic = true;
                    _heldCar = rb;
                }
                else if (dist < 250f && rb.isKinematic && _heldCar == rb)
                {
                    rb.isKinematic = false;
                    _heldCar = null;
                }
            }

            if (!_strandedTold && dist > 120f && (car.currentFuel <= 0.5f || car.IsBroken))
            {
                _strandedTold = true;
                SurvivalNotify.Warn($"Araç geride kaldı. Yaya devam edebilirsin: Güvenli Bölge'deki çekici onu {SurvivalSettings.Instance.safeZoneTowPrice} paraya getirir.", 9f);
            }
        }

        private void OnNewZone()
        {
            float left = NextStopKm - MaxKm;
            Preparation.Earn(SurvivalSettings.Instance.prepPointsPerZone);
            string where = left > 0.05f ? $"Güvenli Bölge {Stops + 1}'e {left:0.0} km" : "Güvenli Bölge";
            SurvivalNotify.Show($"BÖLGE {Zone}   ·   {MaxKm:0.0} km geride   ·   {where}", new Color(0.6f, 1f, 0.7f), 7f);
            if (Zone % 2 == 0) SurvivalNotify.Show("Buradan sonrası daha tehlikeli: düşmanlar daha kalabalık ve daha iyi silahlı, yakıt daha az.", new Color(1f, 0.75f, 0.35f), 7f);
            var vitals = PlayerVitals.Instance;
            if (vitals != null) vitals.AddStressRelief(8f);

            // Milestone = save point, unless a fight is going on
            var save = SaveManager.Instance;
            if (save != null && !SurvivalDirector.DangerNearby(SurvivalDirector.PlayerPosition, 60f)) save.SaveGame(true);
        }

        // ------------------------------------------------------------------ first steps

        private void UpdateSteps()
        {
            if (_stepsHiddenAt > 0f) return;
            var stats = GameStatsTracker.Instance;
            Check(0, SurvivalDirector.PlayerInVehicle);
            Check(1, MaxKm >= 0.5f);
            Check(2, stats != null && stats.itemsLooted > 0);
            Check(3, stats != null && stats.enemiesKilled > 0);
            Check(4, Time.frameCount % 30 == 0 && FindAnyObjectByType<PlayerCampfire>() != null);
            Check(5, _slept);

            bool all = true;
            foreach (bool d in _stepDone) all &= d;
            if (all)
            {
                _stepsHiddenAt = Time.time;
                SurvivalNotify.Show("İlk adımlar tamam. Gerisi sende: yolun sonuna kadar hayatta kal.", new Color(0.6f, 1f, 0.7f), 6f);
            }
        }

        private void Check(int i, bool done)
        {
            if (_stepDone[i] || !done) return;
            _stepDone[i] = true;
            SurvivalNotify.Show("Tamamlandı: " + Steps[i], new Color(0.6f, 1f, 0.7f), 3f);
        }

        // ------------------------------------------------------------------ arrival screen

        private void OpenArrival()
        {
            if (_showArrival) return;
            _showArrival = true;
            _savedTimeScale = Time.timeScale > 0.001f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            SurvivalUIState.JourneyOpen = true;
            SurvivalUIState.AcquireCursor();
            var save = SaveManager.Instance;
            if (save != null) save.SaveGame(true);
        }

        private void CloseArrival()
        {
            if (!_showArrival) return;
            _showArrival = false;
            Time.timeScale = _savedTimeScale;
            SurvivalUIState.JourneyOpen = false;
            SurvivalUIState.ReleaseCursor();
            GameInput.BlockGameplayBriefly();
            SurvivalNotify.Show("Buradayken: tüccarla takas yap, pompadan depoyu doldur, tamirciye aracı onart. Hepsi para ister.", new Color(1f, 0.85f, 0.5f), 9f);
            SurvivalNotify.Show($"Sonraki Güvenli Bölge {destinationKm:0} km ileride. Yol oraya kadar daha sert.", new Color(1f, 0.75f, 0.35f), 9f);
        }

        // ------------------------------------------------------------------ GUI

        private void Styles()
        {
            if (_strip != null) return;
            _strip = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = Color.white } };
            _stepHead = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.95f, 0.85f, 0.6f) } };
            _stepStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, normal = { textColor = Color.white } };
            _big = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, normal = { textColor = new Color(0.6f, 1f, 0.7f) } };
        }

        private void OnGUI()
        {
            if (SurvivalDirector.Player == null || GameOverUI.IsGameOver) return;
            SurvivalGUI.Init();
            Styles();
            if (_showArrival) { DrawArrival(); return; }
            if (SurvivalUIState.AnyOpen || BinocularsController.Active) return;

            // Strip under the compass: day, distance, destination
            float left = NextStopKm - MaxKm;
            string goal = $"Güvenli Bölge {Stops + 1}'e {Mathf.Max(0f, left):0.0} km";
            string text = $"Gün {Day}   ·   Bölge {Zone}   ·   {MaxKm:0.0} km   ·   {goal}";
            var r = new Rect(Screen.width * 0.5f - 260f, 134f, 520f, 22f); // under the compass and the stealth banner
            SurvivalGUI.Fill(new Rect(r.x + 60f, r.y, r.width - 120f, r.height), new Color(0f, 0f, 0f, 0.35f));
            SurvivalGUI.Shadowed(r, text, _strip);

            // First steps (top right, under the resource card) until all are done
            if (_stepsHiddenAt < 0f)
            {
                // Left side, under the vitals card and its tags (the top right is where the notices appear)
                float w = 250f, x = 14f, y = 330f;
                int open = 0;
                foreach (bool d in _stepDone) if (!d) open++;
                float h = 24f + open * 18f + 6f;
                SurvivalGUI.Fill(new Rect(x, y, w, h), new Color(0.05f, 0.06f, 0.08f, 0.6f));
                GUI.Label(new Rect(x + 8f, y + 3f, w - 16f, 18f), "İLK ADIMLAR", _stepHead);
                float yy = y + 22f;
                for (int i = 0; i < Steps.Length; i++)
                {
                    if (_stepDone[i]) continue;
                    GUI.Label(new Rect(x + 8f, yy, w - 16f, 18f), "·  " + Steps[i], _stepStyle);
                    yy += 18f;
                }
            }
        }

        private void DrawArrival()
        {
            GUI.depth = -90;
            if (!PadGUI.Begin(this))
            {
                SurvivalGUI.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.8f));
                return;
            }
            SurvivalGUI.Fill(new Rect(0, 0, Screen.width, Screen.height), new Color(0f, 0f, 0f, 0.8f));
            float w = 560f, h = 470f;
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(rect, GUIContent.none, SurvivalGUI.Panel);
            GUI.Label(new Rect(rect.x, rect.y + 14f, w, 46f), $"GÜVENLİ BÖLGE {Stops}", _big);
            GUILayout.BeginArea(new Rect(rect.x + 30f, rect.y + 70f, w - 60f, h - 90f));
            GUILayout.Label($"{Stops * destinationKm:0} kilometrelik yolu sağ çıkardın. Burada kimse sana saldırmaz: tüccar, yakıt pompası ve tamirci yolun kenarında. Yol bitmiyor; sonraki durak {destinationKm:0} km ileride ve oraya kadar düşmanlar daha dayanıklı, yakıt daha kıt.", SurvivalGUI.Text);
            GUILayout.Space(10);
            var stats = GameStatsTracker.Instance;
            Row("Geçen gün", Day.ToString());
            Row("Katedilen yol", $"{MaxKm:0.0} km");
            if (stats != null)
            {
                Row("Etkisiz düşman", stats.enemiesKilled.ToString());
                Row("Toplanan ganimet", stats.itemsLooted + " parça");
                Row("Skor", stats.CalculateSurvivalScore().ToString("N0"));
            }
            var who = CharacterProfiles.Current;
            Row("Vardığın Güvenli Bölge", (who != null ? Mathf.Max(1, who.arrivals) : PlayerPrefs.GetInt("best.arrivals", 1)) + ". (bu karakterle toplam)");
            GUILayout.Space(14);
            if (PadGUI.Button("Devam", GUILayout.Height(38))) CloseArrival();
            GUILayout.Label("Oyun kaydedildi.", SurvivalGUI.Small);
            GUILayout.EndArea();
            PadGUI.End();
        }

        private static void Row(string label, string value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, SurvivalGUI.Header, GUILayout.Width(220));
            GUILayout.Label(value, SurvivalGUI.Text);
            GUILayout.EndHorizontal();
        }
    }
}
