using UnityEngine;
using StarterAssets;
using EndlessSurvival.Inventory;
using EndlessSurvival.Camping;

namespace EndlessSurvival.Survival
{
    /// <summary>Values of the SurvAction int on the SurvivalFullBody layer (built by Setup Survival Animations).</summary>
    public enum SurvAnim
    {
        None = 0,
        Toilet = 1,
        Drink = 2,
        FishCast = 3,
        FishIdle = 4,
        FishReel = 5,
        Gather = 6,
        KneelFire = 7,
        Sleep = 8,
        Breath = 9,
        HeavyWalk = 10,
        Smoke = 11,
        Death = 20
    }

    /// <summary>Clip names made from the user's Mixamo files (Assets/Animations/Survival).</summary>
    public static class SurvivalAnimClips
    {
        public const string Toilet = "Surv_Toilet";
        public const string Drink = "Surv_Drink";
        public const string FishCast = "Surv_FishCast";
        public const string FishIdle = "Surv_FishIdle";
        public const string FishReel = "Surv_FishReel";
        public const string Gather = "Surv_Gather";
        public const string KneelFire = "Surv_KneelFire";
        public const string Sleep = "Surv_Sleep";
        public const string Breath = "Surv_Breath";
        public const string HeavyWalk = "Surv_HeavyWalk";
        public const string Smoke = "Surv_Smoke";
        public const string NpcHit = "Npc_Hit";
        public const string NpcDeath = "Npc_Death";
        public const string NpcStand = "Npc_Stand";
        public const string NpcTalk1 = "Npc_Talk1";
        public const string NpcTalk2 = "Npc_Talk2";
    }

    /// <summary>
    /// Drives the full body SurvivalFullBody layer of one Animator (player or enemy): sets SurvAction and fades the
    /// layer weight. When the action ends the weight fades out first, then the state goes back to Empty.
    /// </summary>
    public class SurvivalAnimLayer : MonoBehaviour
    {
        public const string LayerName = "SurvivalFullBody";
        public const string Param = "SurvAction";

        public float fadeSpeed = 5f;

        private Animator _animator;
        private int _layer = -1;
        private bool _hasParam;
        private SurvAnim _target;
        private SurvAnim _shown;
        private float _weight;

        public bool Available => _layer >= 0 && _hasParam;
        public SurvAnim Current => _target;

        public static SurvivalAnimLayer GetOrAdd(Animator animator)
        {
            if (animator == null) return null;
            var l = animator.GetComponent<SurvivalAnimLayer>();
            if (l == null) l = animator.gameObject.AddComponent<SurvivalAnimLayer>();
            l.Init();
            return l;
        }

        private void Awake() => Init();

        private void Init()
        {
            if (_animator != null) return;
            _animator = GetComponent<Animator>();
            if (_animator == null || _animator.runtimeAnimatorController == null) return;
            _layer = _animator.GetLayerIndex(LayerName);
            foreach (var p in _animator.parameters)
                if (p.name == Param && p.type == AnimatorControllerParameterType.Int) _hasParam = true;
        }

        public void Set(SurvAnim action)
        {
            Init();
            _target = action;
            if (!Available) return;
            if (action != SurvAnim.None)
            {
                _shown = action;
                _animator.SetInteger(Param, (int)action);
            }
        }

        /// <summary>Death: jump straight to full weight (no fade) and stay.</summary>
        public void SetInstant(SurvAnim action)
        {
            Set(action);
            if (!Available) return;
            _weight = 1f;
            _animator.SetLayerWeight(_layer, 1f);
        }

        /// <summary>Length of a clip in this animator's controller (0 if missing).</summary>
        public float ClipLength(string clipName)
        {
            Init();
            if (_animator == null || _animator.runtimeAnimatorController == null) return 0f;
            foreach (var c in _animator.runtimeAnimatorController.animationClips)
                if (c != null && c.name == clipName) return c.length;
            return 0f;
        }

        private void Update()
        {
            if (!Available || !_animator.isActiveAndEnabled) return;
            float targetWeight = _target != SurvAnim.None ? 1f : 0f;
            _weight = Mathf.MoveTowards(_weight, targetWeight, Time.deltaTime * fadeSpeed);
            _animator.SetLayerWeight(_layer, _weight);
            if (_target == SurvAnim.None && _weight <= 0.001f && _shown != SurvAnim.None)
            {
                _shown = SurvAnim.None;
                _animator.SetInteger(Param, 0);
            }
        }
    }

    /// <summary>
    /// Picks the player's survival animation each frame from the game state (toilet, fishing, campfire work, sleep,
    /// exhaustion, heavy load) plus short one-shots (drink, gather, kneel at the fire). Actions other than breathing
    /// and heavy walking hold the player still. No animation is authored here: only the user's clips are played.
    /// </summary>
    public class PlayerSurvivalAnimator : MonoBehaviour
    {
        public static PlayerSurvivalAnimator Instance { get; private set; }

        private GameObject _player;
        private SurvivalAnimLayer _layer;
        private StarterAssetsInputs _inputs;
        private Animator _animator;
        private bool _hasAimParam;
        private SurvAnim _oneShot;
        private float _oneShotUntil;
        private SurvAnim _current;

        /// <summary>True while an action that keeps the player in place is playing.</summary>
        public static bool LocksMovement
        {
            get
            {
                if (Instance == null) return false;
                var c = Instance._current;
                return c != SurvAnim.None && c != SurvAnim.Breath && c != SurvAnim.HeavyWalk;
            }
        }

        public static SurvAnim CurrentAction => Instance != null ? Instance._current : SurvAnim.None;

        /// <summary>Plays a short action once (its clip length, capped). Ignored when the layer is missing.</summary>
        public static void PlayOneShot(SurvAnim action, float maxSeconds = 4f)
        {
            if (Instance == null) return;
            Instance.EnsureLayer();
            if (Instance._layer == null || !Instance._layer.Available) return;
            float len = Instance._layer.ClipLength(ClipFor(action));
            if (len <= 0f) len = 1.5f;
            Instance._oneShot = action;
            Instance._oneShotUntil = Time.time + Mathf.Min(len, maxSeconds);
        }

        /// <summary>Length of one of the player's survival clips (0 when the layer or clip is missing).</summary>
        public float ClipLength(string clipName)
        {
            EnsureLayer();
            return _layer != null ? _layer.ClipLength(clipName) : 0f;
        }

        public static void CancelOneShot()
        {
            if (Instance != null) Instance._oneShot = SurvAnim.None;
        }

        private static string ClipFor(SurvAnim a)
        {
            switch (a)
            {
                case SurvAnim.Toilet: return SurvivalAnimClips.Toilet;
                case SurvAnim.Drink: return SurvivalAnimClips.Drink;
                case SurvAnim.FishCast: return SurvivalAnimClips.FishCast;
                case SurvAnim.FishIdle: return SurvivalAnimClips.FishIdle;
                case SurvAnim.FishReel: return SurvivalAnimClips.FishReel;
                case SurvAnim.Gather: return SurvivalAnimClips.Gather;
                case SurvAnim.KneelFire: return SurvivalAnimClips.KneelFire;
                case SurvAnim.Sleep: return SurvivalAnimClips.Sleep;
                case SurvAnim.Breath: return SurvivalAnimClips.Breath;
                case SurvAnim.HeavyWalk: return SurvivalAnimClips.HeavyWalk;
                case SurvAnim.Smoke: return SurvivalAnimClips.Smoke;
                case SurvAnim.Death: return SurvivalAnimClips.NpcDeath;
            }
            return null;
        }

        private void Awake() => Instance = this;

        private void OnEnable() => PlayerInventory.ItemConsumed += OnItemConsumed;

        private void OnDisable()
        {
            PlayerInventory.ItemConsumed -= OnItemConsumed;
            if (_layer != null) _layer.Set(SurvAnim.None);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnItemConsumed(ItemDefinition item, PlayerInventory user)
        {
            if (item != null && item.category == ItemCategory.Drink) PlayOneShot(SurvAnim.Drink);
        }

        private void EnsureLayer()
        {
            var player = SurvivalDirector.Player;
            if (player == _player && _layer != null) return;
            _player = player;
            _layer = null;
            _inputs = null;
            if (player == null) return;
            _inputs = player.GetComponent<StarterAssetsInputs>();
            _animator = player.GetComponentInChildren<Animator>();
            _hasAimParam = false;
            if (_animator != null)
                foreach (var p in _animator.parameters)
                    if (p.name == "IsAiming" && p.type == AnimatorControllerParameterType.Bool) _hasAimParam = true;
            _layer = SurvivalAnimLayer.GetOrAdd(_animator);
        }

        private void Update()
        {
            EnsureLayer();
            if (_layer == null || !_layer.Available) return;

            SurvAnim want = Pick();
            if (want != _current)
            {
                _current = want;
                _layer.Set(want);
            }
        }

        private SurvAnim Pick()
        {
            if (_player == null || !_player.activeInHierarchy) { _oneShot = SurvAnim.None; return SurvAnim.None; }
            var health = _player.GetComponent<PlayerHealth>();
            if (health != null && health.IsDead) return SurvAnim.None;

            if (_oneShot != SurvAnim.None)
            {
                if (Time.time < _oneShotUntil) return _oneShot;
                _oneShot = SurvAnim.None;
            }

            var vitals = PlayerVitals.Instance;
            if (CampfireUI.IsResting) return SurvAnim.Sleep;
            if (vitals != null && vitals.ToiletActive) return SurvAnim.Toilet;
            if (SmokingAction.Active) return SurvAnim.Smoke;
            var fishing = FishingController.Instance;
            if (fishing != null && fishing.State != FishingController.FishState.Idle) return SurvAnim.FishIdle;
            var craft = CraftingSystem.Instance;
            if (craft != null && craft.CurrentJob != null && craft.CurrentJob.station == CraftStation.Campfire) return SurvAnim.KneelFire;

            bool moving = _inputs != null && _inputs.move.sqrMagnitude > 0.01f;
            bool aiming = _hasAimParam && _animator != null && _animator.GetBool("IsAiming");
            if (vitals != null && !aiming)
            {
                if (vitals.Exhausted && !moving) return SurvAnim.Breath;
                if (vitals.Overloaded && moving && (_inputs == null || !_inputs.sprint)) return SurvAnim.HeavyWalk;
            }
            return SurvAnim.None;
        }
    }

    /// <summary>Trader NPC: stands, talks while the trade window is open with him (two talking clips in turn).</summary>
    public class TraderAnimator : MonoBehaviour
    {
        public const string TalkParam = "Talk";

        public Trader trader;
        private Animator _animator;
        private float _nextSwitch;
        private int _talk;

        private void Awake() => _animator = GetComponentInChildren<Animator>();

        private void Update()
        {
            if (_animator == null || trader == null) return;
            bool talking = TradeUI.IsTrading(trader);
            if (talking)
            {
                if (_talk == 0 || Time.time >= _nextSwitch)
                {
                    _talk = _talk == 1 ? 2 : 1;
                    _nextSwitch = Time.time + Random.Range(4f, 7f);
                    _animator.SetInteger(TalkParam, _talk);
                }
                // Face the player while talking
                var p = SurvivalDirector.PlayerPosition - trader.transform.position;
                p.y = 0f;
                if (p.sqrMagnitude > 0.01f)
                    trader.transform.rotation = Quaternion.Slerp(trader.transform.rotation, Quaternion.LookRotation(p), Time.unscaledDeltaTime * 4f);
            }
            else if (_talk != 0)
            {
                _talk = 0;
                _animator.SetInteger(TalkParam, 0);
            }
        }
    }
}
