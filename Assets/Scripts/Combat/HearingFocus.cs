using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>
    /// Crouch "focus mode": while the player crouches, everything is ducked (AudioListener volume) and
    /// footsteps / nearby creature sounds are boosted on top of it, so they stand out clearly.
    /// Blends in and out over CombatContent.focusBlendTime. Values are in CombatContent (Inspector).
    /// Sounds that should stand out multiply their volume by <see cref="FocusGain"/>.
    /// </summary>
    public class HearingFocus : MonoBehaviour
    {
        private static HearingFocus _instance;
        private float _blend;
        private float _baseListenerVolume = 1f;

        /// <summary>0 = normal hearing, 1 = full focus.</summary>
        public static float Blend => _instance != null ? _instance._blend : 0f;

        /// <summary>
        /// Volume multiplier for focus sounds: cancels the duck and adds the boost
        /// (1 when not crouched). Footstep base volumes are kept low so the result stays below 1.
        /// </summary>
        public static float FocusGain
        {
            get
            {
                var c = CombatContent.Instance;
                return DbToLinear((c.focusBoostDb - c.ambientDuckDb) * Blend);
            }
        }

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("HearingFocus");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<HearingFocus>();
        }

        private void Awake()
        {
            _baseListenerVolume = AudioListener.volume;
        }

        private void Update()
        {
            var c = CombatContent.Instance;
            var stealth = PlayerStealthController.Instance;
            bool focus = stealth != null && stealth.isActiveAndEnabled && stealth.IsCrouching;

            float speed = c.focusBlendTime > 0.01f ? Time.unscaledDeltaTime / c.focusBlendTime : 1f;
            _blend = Mathf.MoveTowards(_blend, focus ? 1f : 0f, speed);

            AudioListener.volume = _baseListenerVolume * DbToLinear(c.ambientDuckDb * _blend);
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                AudioListener.volume = _baseListenerVolume;
                _instance = null;
            }
        }

        public static float DbToLinear(float db)
        {
            return Mathf.Pow(10f, db / 20f);
        }
    }
}
