using UnityEngine;
using StarterAssets;

namespace EndlessSurvival.Camping
{
    public class PlayerSittingController : MonoBehaviour
    {
        [Header("Settings")]
        [Tooltip("Yere oturup kalkma kısayol tuşu")]
        public KeyCode sitToggleKey = KeyCode.C;

        private Animator _animator;
        private ThirdPersonController _controller;
        private StarterAssetsInputs _inputs;
        private bool _isSitting = false;

        public bool IsSitting => _isSitting;

        private void Start()
        {
            _animator = GetComponentInChildren<Animator>();
            _controller = GetComponent<ThirdPersonController>();
            _inputs = GetComponent<StarterAssetsInputs>();
        }

        private void Update()
        {
            var keyboard = UnityEngine.InputSystem.Keyboard.current;
            if (keyboard != null && keyboard.cKey.wasPressedThisFrame)
            {
                // UI açıkken veya araçtayken oturulmaz
                if (CampfireUI.IsOpen || !gameObject.activeInHierarchy) return;
                ToggleSit();
            }

            if (_isSitting)
            {
                // Oyuncu yürümeye (WASD) veya zıplamaya kalkarsa otomatik ayağa kalksın
                if (_inputs != null)
                {
                    if (_inputs.move != Vector2.zero || _inputs.jump)
                    {
                        StandUp();
                    }
                }
            }
        }

        public void SitDown(Vector3? faceDirection = null)
        {
            if (_isSitting) return;
            _isSitting = true;

            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                _animator.SetBool("IsSitting", true);
            }

            if (faceDirection.HasValue && faceDirection.Value != Vector3.zero)
            {
                Vector3 dir = faceDirection.Value;
                dir.y = 0;
                if (dir != Vector3.zero)
                {
                    transform.rotation = Quaternion.LookRotation(dir);
                }
            }

            Debug.Log("<color=cyan>[Camping] Kamp ateşi başında yere oturdun. Kalkmak için WASD veya Boşluk tuşuna bas.</color>");
        }

        public void StandUp()
        {
            if (!_isSitting) return;
            _isSitting = false;

            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_animator != null)
            {
                _animator.SetBool("IsSitting", false);
            }
        }

        public void ToggleSit()
        {
            if (_isSitting) StandUp();
            else SitDown();
        }
    }
}
