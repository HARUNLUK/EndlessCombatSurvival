using UnityEngine;
using EndlessSurvival.Story;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Readable notes lying beside the road (2026-11-14). The text is made at run time (no assets), the paper is a
    /// thin primitive with an InteractableNote on it. Used by WorldPopulator (start chunk note, roadside notes)
    /// and by Corpses (a note next to some bodies).
    /// </summary>
    public static class RoadNotes
    {
        private static readonly string[][] Texts =
        {
            new[] { "Buzdolabı Notu", "Selin",
                "Emre, üç gün bekledim. Su bitti, komşular gitti. Arabayı alıp güneye iniyorum, yol boyunca işaret bırakacağım. Kapıyı kilitleme, anahtar saksının altında." },
            new[] { "Benzinlik Defteri", "Bilinmiyor",
                "Pompalar kuru. Arka depoda yarım bidon vardı, onu da dün gece biri almış. Buradan sonra dokuz on kilometre hiçbir şey yok derler. Yola çıkmadan depoyu doldur." },
            new[] { "Yırtık Sayfa", "Bilinmiyor",
                "Dereden içtim, iki gün kıvrandım. Kaynatmadan içme. Sabahları otların üstündeki çiy bile daha temiz." },
            new[] { "Uyarı", "Bir yolcu",
                "İleride yolu kapatmışlar. Durup kapıyı açmaya kalkma, bekliyor olabilirler. Hızını al, vur geç. Araba dayanır." },
            new[] { "Kamp Ateşi", "Bilinmiyor",
                "Gece uzakta tek bir ateş gördüm. Yanında biri oturuyordu, yalnız. Yaklaşmadım. Herkes düşman değil ama herkes dost da değil." },
            new[] { "Son Liste", "Hasan",
                "Tabanca, dokuz mermi, bıçak, üç şişe su, iki konserve. Bu kadarla çıkıyorum. Gerisini yol verir." },
        };

        private static NoteDefinition[] _notes;

        public static int Count => Texts.Length;

        /// <summary>The note definitions (created once; noteId road_note_1 ...).</summary>
        public static NoteDefinition Get(int index)
        {
            if (_notes == null || _notes.Length != Texts.Length) _notes = new NoteDefinition[Texts.Length];
            index = Mathf.Clamp(index, 0, Texts.Length - 1);
            if (_notes[index] == null)
            {
                var note = ScriptableObject.CreateInstance<NoteDefinition>();
                note.hideFlags = HideFlags.HideAndDontSave;
                note.name = "RoadNote_" + (index + 1);
                note.title = Texts[index][0];
                note.author = Texts[index][1];
                note.bodyText = Texts[index][2];
                note.noteId = "road_note_" + (index + 1);
                _notes[index] = note;
            }
            return _notes[index];
        }

        /// <summary>Puts a random note on the ground at pos (pos is the ground point). Returns the paper object.</summary>
        public static GameObject Spawn(Transform parent, Vector3 pos, SeededRandom rng)
        {
            var note = Get(rng.Range(0, Texts.Length));
            var paper = SurvivalPrimitives.Part(PrimitiveType.Cube, parent, Vector3.zero, new Vector3(0.32f, 0.02f, 0.22f), new Color(0.88f, 0.85f, 0.72f));
            paper.name = "RoadNote";
            paper.transform.position = pos + Vector3.up * 0.02f;
            paper.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);
            var interactable = paper.AddComponent<InteractableNote>();
            interactable.noteDefinition = note;
            return paper;
        }
    }
}
