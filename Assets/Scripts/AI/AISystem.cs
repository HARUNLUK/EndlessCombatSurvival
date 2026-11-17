using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using EndlessCombat.Combat;
using EndlessSurvival.Survival;

namespace EndlessCombat.AI
{
    /// <summary>
    /// The host of the enemy AI's shared work (Enemy_AI_Deep_Plan.md 3.3, 23): targets on the player and the
    /// vehicle, the player read (10 Hz), target velocities, persistent stimuli (1 Hz), footsteps, the tactical map,
    /// the vehicle stimuli, far groups, signature moves, the save, and what the player is shown (ghost marker,
    /// screen edge markers, fallback bubbles, grenade circles, debug lines). Created on its own at scene load.
    /// </summary>
    public class AISystem : MonoBehaviour
    {
        public static AISystem Instance { get; private set; }
        public static bool Running => Instance != null && Instance.isActiveAndEnabled;
        public static float LastMs { get; private set; }

        public static float FootstepInterval = 0.3f;

        private float _nextRead, _nextPersistent, _nextFootstep;
        private SurvivalDirector _registeredWith;
        private readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();
        private bool _anySeen;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoCreate()
        {
            if (Instance != null) return;
            var go = new GameObject("AISystem");
            DontDestroyOnLoad(go);
            go.AddComponent<AISystem>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            CoverMap.Hook();
            SceneManager.sceneLoaded += OnSceneLoaded;
            RenderPipelineManager.endCameraRendering += OnEndCamera;
            AIDifficulty.RegisterTuning();
            AIDebug.RegisterTuning();
        }

        private void OnDestroy()
        {
            if (Instance != this) return;
            Instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            RenderPipelineManager.endCameraRendering -= OnEndCamera;
        }

        private void OnSceneLoaded(Scene s, LoadSceneMode mode)
        {
            if (mode != LoadSceneMode.Single) return;
            AIStimuli.Clear();
            CoverMap.Clear();
            EnemySquad.ResetAll();
            VehicleThreat.ClearPersistent();
            EnemyPerception.ClearCaches();
            _registeredWith = null;
            if (_ghost != null) Destroy(_ghost);
        }

        private void Update()
        {
            if (Time.timeScale <= 0f) return; // U10: nothing moves while time stands still
            _watch.Restart();
            EnsureTargets();
            RegisterSave();

            if (Time.time >= _nextRead)
            {
                _nextRead = Time.time + 0.1f;
                PlayerRead.Tick();
                foreach (var t in AITarget.All) if (t != null) t.SampleVelocity();
                _anySeen = false;
                foreach (var e in EnemyController.Active)
                {
                    if (e == null || e.IsDead || e.Belief == null) continue;
                    var b = e.Belief.Of(AITarget.Player);
                    if (b != null && b.SeenNow) { _anySeen = true; break; }
                }
                // The player in the vehicle is seen through the vehicle
                PlayerRead.TickIntent(_anySeen);
                if (PlayerRead.Current.shieldRaised && AnyFight()) AIMemory.ShieldSeconds += 0.1f;
            }
            if (Time.time >= _nextPersistent)
            {
                _nextPersistent = Time.time + 1f;
                AIStimuli.ScanPersistent();
            }
            TickFootsteps();
            CoverMap.Tick();
            VehicleThreat.Tick();
            AbstractGroups.Tick();
            AISignatures.Tick();
            _watch.Stop();
            LastMs = Mathf.Lerp(LastMs, (float)_watch.Elapsed.TotalMilliseconds, 0.1f);
        }

        private static bool AnyFight()
        {
            foreach (var s in EnemySquad.Squads) if (s.Phase == AlertPhase.Combat) return true;
            return false;
        }

        private void EnsureTargets()
        {
            if (AITarget.Player == null)
            {
                var p = SurvivalDirector.Player != null ? SurvivalDirector.Player : GameObject.FindGameObjectWithTag("Player");
                if (p != null) AITarget.GetOrAdd(p, AITargetKind.Player, CombatTeam.Player).bodyHeight = 1.2f;
            }
            if (AITarget.PlayerVehicle == null)
            {
                var v = SurvivalDirector.Vehicle;
                if (v != null) AITarget.GetOrAdd(v.gameObject, AITargetKind.Vehicle, CombatTeam.Player).bodyHeight = 1f;
            }
        }

        private void RegisterSave()
        {
            var d = SurvivalDirector.Instance;
            if (d == null || d == _registeredWith) return;
            _registeredWith = d;
            d.Register(AIMemory.Save);
        }

        private void TickFootsteps()
        {
            if (Time.time < _nextFootstep) return;
            _nextFootstep = Time.time + FootstepInterval;
            if (PlayerRead.Current.inVehicle || AITarget.Player == null) return;
            var stealth = PlayerStealthController.Instance;
            if (stealth == null) return;
            float r = stealth.GetCurrentNoiseRadius();
            if (r <= 0.01f) return;
            var w = WeatherSystem.Instance;
            if (w != null) r *= 1f - 0.4f * w.RainIntensity;
            AIStimuli.Raise(new Stimulus
            {
                kind = StimulusKind.Footstep, position = AITarget.Player.Position, radius = r, source = AITarget.Player, certainty = 0.6f
            });
        }

        // ------------------------------------------------------------------ ghost marker (24.1)

        private GameObject _ghost;
        private Material _ghostMat;
        private TextMesh _ghostText;

        private void LateUpdate()
        {
            UpdateGhost();
            AIDebug.Collect();
            CollectGrenadeCircles();
        }

        private void UpdateGhost()
        {
            var p = AIDifficulty.P;
            EnemySquad best = null;
            float bestD = float.MaxValue;
            if (AITarget.Player != null && (!_anySeen || AIDebug.ForceGhost))
            {
                Vector3 pp = AITarget.Player.Position;
                foreach (var s in EnemySquad.Squads)
                {
                    if (s.IsAbstract || !s.Shared.Valid) continue;
                    bool phase = s.Phase == AlertPhase.Combat || s.Phase == AlertPhase.Lost || s.Phase == AlertPhase.Searching;
                    if (!phase && !AIDebug.ForceGhost) continue;
                    if (s.Shared.confidence < 0.3f && !AIDebug.ForceGhost) continue;
                    float d = (s.Shared.position - pp).sqrMagnitude;
                    if (d > p.ghostRange * p.ghostRange && !AIDebug.ForceGhost) continue;
                    if (d < bestD) { bestD = d; best = s; }
                }
            }
            if (best == null)
            {
                if (_ghost != null && _ghost.activeSelf) _ghost.SetActive(false);
                return;
            }
            if (_ghost == null) CreateGhost();
            if (!_ghost.activeSelf) _ghost.SetActive(true);
            Vector3 pos = best.Shared.position;
            if (Physics.Raycast(pos + Vector3.up * 3f, Vector3.down, out RaycastHit hit, 8f, ~0, QueryTriggerInteraction.Ignore)) pos.y = hit.point.y;
            _ghost.transform.position = pos + Vector3.up * 0.9f;
            float a = Mathf.Lerp(0.08f, 0.35f, Mathf.InverseLerp(0.3f, 1f, best.Shared.confidence));
            if (_ghostMat != null) _ghostMat.color = new Color(0.9f, 0.9f, 1f, a);
            if (_ghostText != null)
            {
                _ghostText.color = new Color(1f, 1f, 1f, a + 0.3f);
                var cam = Camera.main;
                if (cam != null) _ghostText.transform.rotation = Quaternion.LookRotation(_ghostText.transform.position - cam.transform.position);
            }
        }

        private void CreateGhost()
        {
            _ghost = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _ghost.name = "AIGhostMarker";
            var col = _ghost.GetComponent<Collider>();
            if (col != null) Destroy(col);
            _ghost.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
            var r = _ghost.GetComponent<Renderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            if (r != null && sh != null)
            {
                _ghostMat = new Material(sh);
                // URP unlit: transparent surface
                _ghostMat.SetFloat("_Surface", 1f);
                _ghostMat.SetFloat("_Blend", 0f);
                _ghostMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _ghostMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _ghostMat.SetInt("_ZWrite", 0);
                _ghostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                _ghostMat.renderQueue = (int)RenderQueue.Transparent;
                r.sharedMaterial = _ghostMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            var t = new GameObject("Q");
            t.transform.SetParent(_ghost.transform, false);
            t.transform.localPosition = new Vector3(0f, 1.5f, 0f);
            t.transform.localScale = new Vector3(1f / 0.6f, 1f / 0.9f, 1f / 0.6f);
            _ghostText = t.AddComponent<TextMesh>();
            _ghostText.text = "?";
            _ghostText.anchor = TextAnchor.MiddleCenter;
            _ghostText.alignment = TextAlignment.Center;
            _ghostText.fontSize = 40;
            _ghostText.characterSize = 0.06f;
            DontDestroyOnLoad(_ghost);
        }

        // ------------------------------------------------------------------ grenade circles (4.3 grenadeLine)

        private static void CollectGrenadeCircles()
        {
            if (!AIDifficulty.P.grenadeLine) return;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead) continue;
                var t = e.PlannedThrowTarget;
                if (!t.HasValue || e.grenades <= 0) continue;
                AIDebug.Circle(t.Value + Vector3.up * 0.1f, EnemyThrower.BlastRadius, new Color(1f, 0.3f, 0.2f), 32);
                AIDebug.Add(EnemyThrower.Hand(e), t.Value + Vector3.up * 0.1f, new Color(1f, 0.3f, 0.2f, 0.6f));
            }
        }

        // ------------------------------------------------------------------ GL lines

        private static Material _lineMat;

        private static void OnEndCamera(ScriptableRenderContext ctx, Camera cam)
        {
            if (AIDebug.Lines.Count == 0 || cam == null || cam.cameraType != CameraType.Game) return;
            if (_lineMat == null)
            {
                Shader sh = Shader.Find("Hidden/Internal-Colored");
                if (sh == null) return;
                _lineMat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
                _lineMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                _lineMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                _lineMat.SetInt("_Cull", (int)CullMode.Off);
                _lineMat.SetInt("_ZWrite", 0);
            }
            GL.PushMatrix();
            GL.LoadProjectionMatrix(cam.projectionMatrix);
            GL.modelview = cam.worldToCameraMatrix;
            _lineMat.SetPass(0);
            GL.Begin(GL.LINES);
            foreach (var l in AIDebug.Lines)
            {
                GL.Color(l.color);
                GL.Vertex(l.a);
                GL.Vertex(l.b);
            }
            GL.End();
            GL.PopMatrix();
        }

        // ------------------------------------------------------------------ overlay: edge markers, fallback bubbles, robbery panel

        private GUIStyle _style;

        private void OnGUI()
        {
            AISignatures.OnGUI();
            var cam = Camera.main;
            if (cam == null || AITarget.Player == null) return;
            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold, fontSize = 15 };
            }
            var p = AIDifficulty.P;
            Vector3 pp = AITarget.Player.Position;

            // Fallback bubbles (no SpeechBubble prefab in Resources)
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead) continue;
                if (!e.BubbleFallback(out string text, out Color color)) continue;
                Vector3 sp = cam.WorldToScreenPoint(e.transform.position + Vector3.up * 2.3f);
                if (sp.z <= 0f) continue;
                DrawLabel(new Rect(sp.x - 120f, Screen.height - sp.y - 14f, 240f, 28f), text, color);
            }

            // Shouts said off screen: a marker at the screen edge (11.4)
            if (p.edgeIndicator)
            {
                for (int i = EnemyBarks.Visible.Count - 1; i >= 0; i--)
                {
                    var s = EnemyBarks.Visible[i];
                    if (s.speaker == null || Time.time > s.until) continue;
                    Vector3 wp = s.speaker.transform.position + Vector3.up * 1.8f;
                    if ((wp - pp).sqrMagnitude > p.barkRange * p.barkRange) continue;
                    Vector3 sp = cam.WorldToScreenPoint(wp);
                    bool onScreen = sp.z > 0f && sp.x > 0f && sp.x < Screen.width && sp.y > 0f && sp.y < Screen.height;
                    if (onScreen) continue;
                    Vector2 c = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                    Vector2 d = new Vector2(sp.x, sp.y) - c;
                    if (sp.z < 0f) d = -d;
                    if (d.sqrMagnitude < 1f) d = Vector2.down;
                    d.Normalize();
                    float sx = Mathf.Abs(d.x) > 0.001f ? (Screen.width * 0.5f - 60f) / Mathf.Abs(d.x) : float.MaxValue;
                    float sy = Mathf.Abs(d.y) > 0.001f ? (Screen.height * 0.5f - 30f) / Mathf.Abs(d.y) : float.MaxValue;
                    Vector2 at = c + d * Mathf.Min(sx, sy);
                    DrawLabel(new Rect(at.x - 110f, Screen.height - at.y - 14f, 220f, 28f), "> " + s.text, s.color);
                }
            }
        }

        private void DrawLabel(Rect r, string text, Color color)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.Label(new Rect(r.x + 1f, r.y + 1f, r.width, r.height), text, _style);
            GUI.color = color;
            GUI.Label(r, text, _style);
            GUI.color = old;
        }

        private void Start()
        {
            // Lines collected in LateUpdate are drawn by the cameras of the same frame and cleared next frame
            StartCoroutine(ClearLinesEachFrame());
        }

        private System.Collections.IEnumerator ClearLinesEachFrame()
        {
            var wait = new WaitForEndOfFrame();
            while (true)
            {
                yield return wait;
                AIDebug.Clear();
            }
        }
    }
}
