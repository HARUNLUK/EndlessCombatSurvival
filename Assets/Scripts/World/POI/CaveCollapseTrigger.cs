using System.Collections;
using UnityEngine;

namespace EndlessSurvival.World.POI
{
    /// <summary>
    /// Oyuncu magaranin icine girdiginde girisi buyuk kayalarla kapatan ve uyari veren tetikleyici.
    /// </summary>
    public class CaveCollapseTrigger : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Girişi kapatacak kaya engeli objesi")]
        public GameObject bouldersBlocker;

        [Tooltip("Çöküntü sırasında düşecek toz/duman efekti")]
        public ParticleSystem dustParticles;

        [Tooltip("Çöküntü ses kaynağı")]
        public AudioSource collapseAudio;

        [Header("State")]
        private bool _hasTriggered = false;

        private void Start()
        {
            if (bouldersBlocker != null)
            {
                bouldersBlocker.SetActive(false);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_hasTriggered) return;

            if (other.CompareTag("Player"))
            {
                _hasTriggered = true;
                StartCoroutine(CollapseRoutine());
            }
        }

        private IEnumerator CollapseRoutine()
        {
            Debug.Log("<color=yellow>[CaveCollapse] Mağara girişi çöküyor!</color>");

            if (collapseAudio != null)
            {
                collapseAudio.Play();
            }

            if (dustParticles != null)
            {
                dustParticles.Play();
            }

            // Hafif bir bekleme ve ardından kayaların yolu kapatması
            yield return new WaitForSeconds(0.2f);

            if (bouldersBlocker != null)
            {
                bouldersBlocker.SetActive(true);
            }

            // Ekran uyarısı veya feedback
            Debug.LogWarning("<color=red>[CaveCollapse] GİRİŞ ÇÖKTÜ! İçeride mahsur kaldın. İlerleyip başka bir çıkış bulmalısın!</color>");
        }
    }
}
