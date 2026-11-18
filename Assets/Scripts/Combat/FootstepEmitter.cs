using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Distance based footstep sounds for the player and the enemies (crouch / walk / run).
    /// A step plays every stride length of horizontal travel while grounded. Footsteps are focus sounds:
    /// their volume is multiplied by HearingFocus.FocusGain, so they stand out while the player crouches.
    /// Sits on the Animator object and also receives the OnFootstep / OnLand animation events of the
    /// locomotion clips (ignored: the enemies have no EndlessCombat.Character.PlayerMotor to receive them otherwise).
    /// </summary>
    [DisallowMultipleComponent]
    public class FootstepEmitter : MonoBehaviour
    {
        [Tooltip("Player steps are 2D-ish and use crouch state from PlayerStealthController")]
        public bool isPlayer;

        private CharacterController _controller;
        private AudioSource _source;
        private Vector3 _lastPosition;
        private float _distance;
        private bool _wasGrounded = true;
        private float _airTime;
        private int _lastClip = -1;

        private void Start()
        {
            _controller = GetComponentInParent<CharacterController>();
            _lastPosition = transform.position;

            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = isPlayer ? 0.6f : 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 1f;
            _source.maxDistance = CombatContent.Instance.footstepMaxDistance;
            _source.dopplerLevel = 0f;

            if (isPlayer)
            {
                HearingFocus.Ensure();
            }
        }

        private void OnEnable()
        {
            _lastPosition = transform.position;
            _distance = 0f;
        }

        private void Update()
        {
            var content = CombatContent.Instance;
            if (content == null) return;
            // Steps are counted even without clips: GameSound gets every step for the future sound player

            Vector3 pos = transform.position;
            Vector3 delta = pos - _lastPosition;
            _lastPosition = pos;
            delta.y = 0f;

            float dt = Mathf.Max(Time.deltaTime, 0.0001f);
            float speed = delta.magnitude / dt;
            if (speed > 20f) return; // teleport / respawn

            bool grounded = _controller == null || _controller.isGrounded;

            // Landing after a fall or a jump
            if (!grounded) _airTime += dt;
            else
            {
                if (!_wasGrounded && _airTime > 0.35f)
                {
                    EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.Footstep, pos, 1f, "land", isPlayer);
                    if (content.landingClip != null) Play(content.landingClip, content.footstepVolume * 1.2f);
                }
                _airTime = 0f;
            }
            _wasGrounded = grounded;

            if (!grounded || speed < 0.3f)
            {
                _distance = Mathf.Min(_distance, StrideFor(content, 1) * 0.5f);
                return;
            }

            int gait = Gait(speed);
            _distance += delta.magnitude;
            float stride = StrideFor(content, gait);
            if (_distance < stride) return;
            _distance = 0f;

            float gaitVolume = gait == 0 ? content.gaitVolume.x : gait == 1 ? content.gaitVolume.y : content.gaitVolume.z;
            EndlessCombat.Audio.GameSound.Play(EndlessCombat.Audio.SoundId.Footstep, pos, gaitVolume,
                gait == 0 ? "walk" : gait == 1 ? "run" : "sprint", isPlayer);
            if (content.footstepClips != null && content.footstepClips.Length > 0) Play(NextClip(content), content.footstepVolume * gaitVolume);
        }

        private int Gait(float speed)
        {
            if (isPlayer)
            {
                var stealth = PlayerStealthController.Instance;
                if (stealth != null && stealth.IsCrouching) return 0;
            }
            if (speed < 2.6f) return 0;
            if (speed < 5f) return 1;
            return 2;
        }

        private static float StrideFor(CombatContent c, int gait)
        {
            return gait == 0 ? c.strideLength.x : gait == 1 ? c.strideLength.y : c.strideLength.z;
        }

        private AudioClip NextClip(CombatContent c)
        {
            int count = c.footstepClips.Length;
            int index = Random.Range(0, count);
            if (count > 1 && index == _lastClip) index = (index + 1) % count;
            _lastClip = index;
            return c.footstepClips[index];
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip == null || _source == null) return;
            _source.pitch = Random.Range(0.92f, 1.08f);
            _source.PlayOneShot(clip, Mathf.Clamp01(volume * HearingFocus.FocusGain));
        }

        // Animation event receivers (locomotion clips call these); steps are distance based instead.
        private void OnFootstep(AnimationEvent animationEvent) { }
        private void OnLand(AnimationEvent animationEvent) { }
    }
}
