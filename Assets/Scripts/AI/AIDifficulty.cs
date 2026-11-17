using UnityEngine;

namespace EndlessCombat.AI
{
    /// <summary>
    /// Enemy intelligence chosen by the player (Enemy_AI_Deep_Plan.md section 4). Four presets and Custom.
    /// The base competence (cover, shield, reload in cover, no friendly fire, acting on belief) is on at every tier;
    /// the tiers change dials (numbers) and locks (extra behaviours). Behaviour code reads AIDifficulty.P (or
    /// For(squad) when the squad's leader died: one tier lower). Never "knows everything": no tier cheats.
    /// </summary>
    public static class AIDifficulty
    {
        public enum Tier { Easy, Normal, Hard, Brutal, Custom }

        [System.Serializable]
        public class Profile
        {
            // ---- dials ----
            public float reactionTime = 0.65f;
            public float noticeMul = 1f;
            public float sightMul = 1f;
            public float hearMul = 1f;
            public float spreadMul = 1.2f;
            public float firstShotMissChance = 0.7f;
            public bool leadTarget;
            public int maxAttackers = 2;
            public float decideMin = 0.25f, decideMax = 0.45f;
            public float beliefDecay = 0.08f;
            public float predictSeconds = 0.5f;
            public float shoutDelay = 1.2f;
            public float shoutRadius = 25f;
            public float searchTime = 60f;
            public float nerveShift;
            public float peekUnderAimChance = 0.35f;
            public float throwWindup = 1.2f;
            /// <summary>Grenades per squad: a flat count plus this many per gunner (rounded up).</summary>
            public int grenadesFlat = 1;
            public float grenadesPerGunner;
            public float mistakeChance = 0.08f;
            public float vehicleFloor = 0.15f;

            // ---- locks ----
            public bool flank = true;
            public bool flankDouble;
            public bool bounding = true;
            public bool staggerReload = true;
            public bool suppressFire = true;
            public bool grenadeFlush = true;
            public bool grenadeCoverRetreat;
            public float grenadeThrowBack;
            public bool shieldBash = true;
            public bool shieldSideFire = true;
            public bool exploitReload = true;
            public bool exploitDraw;
            public bool exploitDrop;
            public bool countShots;
            /// <summary>0 only when out of ammo, 1 when ammo is low, 2 when a better gun is seen, 3 also to disarm the player.</summary>
            public int scavenge = 1;
            /// <summary>0 shoot while fleeing, 1 cut off the way, 2 tyres and grenade, 3 ambush at the parked vehicle.</summary>
            public int vehicleDeny = 1;
            public bool pairSearch = true;
            /// <summary>Seconds until a missing squad mate is noticed (0 = never).</summary>
            public float missingCheck = 120f;
            /// <summary>0 off, 1 at the second rock, 2 at the first rock.</summary>
            public int rockBacktrace;
            public bool encounterMemory = true;
            /// <summary>Faction habit learning rate: 0 off, 1 normal, 2 double.</summary>
            public float factionMemory;
            public bool signatureMoves = true;

            // ---- what the player is shown (4.3) ----
            /// <summary>Ghost marker range (m): 0 none, large = always.</summary>
            public float ghostRange = 9999f;
            public float barkRange = 35f;
            public bool edgeIndicator = true;
            public bool flankerBark = true;
            public bool grenadeLine = true;

            public Profile Clone() => (Profile)MemberwiseClone();
        }

        public static Tier Current { get; private set; } = Tier.Normal;
        public static Profile P { get; private set; } = Preset(Tier.Normal);

        private static readonly Profile[] Cache = new Profile[4];

        public static string[] TierNames = { "Kolay", "Normal", "Zor", "Acımasız", "Özel" };
        public static string Name(Tier t) => TierNames[(int)t];

        public static void Set(Tier tier)
        {
            if (tier == Tier.Custom)
            {
                Current = Tier.Custom;
                return;
            }
            Current = tier;
            P = Preset(tier);
        }

        /// <summary>Custom profile from the save (tier Custom).</summary>
        public static void SetCustom(Profile p)
        {
            if (p == null) return;
            Current = Tier.Custom;
            P = p.Clone();
        }

        /// <summary>The profile a squad plays with: one tier lower after its leader died (12.9).</summary>
        public static Profile For(EnemySquad squad)
        {
            if (squad == null || squad.TierPenalty <= 0 || Current == Tier.Custom || Current == Tier.Easy) return P;
            int t = Mathf.Max(0, (int)Current - squad.TierPenalty);
            if (Cache[t] == null) Cache[t] = Preset((Tier)t);
            return Cache[t];
        }

        public static Profile Preset(Tier tier)
        {
            var p = new Profile();
            switch (tier)
            {
                case Tier.Easy:
                    p.reactionTime = 1f; p.noticeMul = 0.7f; p.sightMul = 0.9f; p.hearMul = 0.85f; p.spreadMul = 1.6f;
                    p.firstShotMissChance = 1f; p.leadTarget = false; p.maxAttackers = 2; p.decideMin = 0.35f; p.decideMax = 0.6f;
                    p.beliefDecay = 0.12f; p.predictSeconds = 0f; p.shoutDelay = 1.6f; p.shoutRadius = 20f; p.searchTime = 30f;
                    p.nerveShift = -10f; p.peekUnderAimChance = 0.6f; p.throwWindup = 1.4f; p.grenadesFlat = 0; p.grenadesPerGunner = 0f;
                    p.mistakeChance = 0.15f; p.vehicleFloor = 0.15f;
                    p.flank = false; p.flankDouble = false; p.bounding = false; p.staggerReload = false; p.suppressFire = false;
                    p.grenadeFlush = false; p.grenadeCoverRetreat = false; p.grenadeThrowBack = 0f; p.shieldBash = false; p.shieldSideFire = false;
                    p.exploitReload = false; p.exploitDraw = false; p.exploitDrop = false; p.countShots = false; p.scavenge = 0; p.vehicleDeny = 0;
                    p.pairSearch = false; p.missingCheck = 0f; p.rockBacktrace = 0; p.encounterMemory = false; p.factionMemory = 0f; p.signatureMoves = false;
                    p.ghostRange = 9999f; p.barkRange = 45f; p.edgeIndicator = true; p.flankerBark = true; p.grenadeLine = true;
                    break;
                case Tier.Hard:
                    p.reactionTime = 0.4f; p.noticeMul = 1.25f; p.sightMul = 1.1f; p.hearMul = 1.1f; p.spreadMul = 1f;
                    p.firstShotMissChance = 0.3f; p.leadTarget = true; p.maxAttackers = 3; p.decideMin = 0.2f; p.decideMax = 0.35f;
                    p.beliefDecay = 0.06f; p.predictSeconds = 1f; p.shoutDelay = 0.9f; p.shoutRadius = 32f; p.searchTime = 90f;
                    p.nerveShift = 10f; p.peekUnderAimChance = 0.15f; p.throwWindup = 1f; p.grenadesFlat = 0; p.grenadesPerGunner = 0.5f;
                    p.mistakeChance = 0.03f; p.vehicleFloor = 0.15f;
                    p.flank = true; p.flankDouble = false; p.bounding = true; p.staggerReload = true; p.suppressFire = true;
                    p.grenadeFlush = true; p.grenadeCoverRetreat = true; p.grenadeThrowBack = 0f; p.shieldBash = true; p.shieldSideFire = true;
                    p.exploitReload = true; p.exploitDraw = true; p.exploitDrop = true; p.countShots = true; p.scavenge = 2; p.vehicleDeny = 2;
                    p.pairSearch = true; p.missingCheck = 60f; p.rockBacktrace = 1; p.encounterMemory = true; p.factionMemory = 1f; p.signatureMoves = true;
                    p.ghostRange = 30f; p.barkRange = 28f; p.edgeIndicator = true; p.flankerBark = false; p.grenadeLine = true;
                    break;
                case Tier.Brutal:
                    p.reactionTime = 0.25f; p.noticeMul = 1.5f; p.sightMul = 1.2f; p.hearMul = 1.2f; p.spreadMul = 0.85f;
                    p.firstShotMissChance = 0f; p.leadTarget = true; p.maxAttackers = 4; p.decideMin = 0.15f; p.decideMax = 0.3f;
                    p.beliefDecay = 0.05f; p.predictSeconds = 1.5f; p.shoutDelay = 0.7f; p.shoutRadius = 38f; p.searchTime = 120f;
                    p.nerveShift = 20f; p.peekUnderAimChance = 0.05f; p.throwWindup = 0.8f; p.grenadesFlat = 0; p.grenadesPerGunner = 1f;
                    p.mistakeChance = 0f; p.vehicleFloor = 0f;
                    p.flank = true; p.flankDouble = true; p.bounding = true; p.staggerReload = true; p.suppressFire = true;
                    p.grenadeFlush = true; p.grenadeCoverRetreat = true; p.grenadeThrowBack = 0.3f; p.shieldBash = true; p.shieldSideFire = true;
                    p.exploitReload = true; p.exploitDraw = true; p.exploitDrop = true; p.countShots = true; p.scavenge = 3; p.vehicleDeny = 3;
                    p.pairSearch = true; p.missingCheck = 30f; p.rockBacktrace = 2; p.encounterMemory = true; p.factionMemory = 2f; p.signatureMoves = true;
                    p.ghostRange = 0f; p.barkRange = 20f; p.edgeIndicator = false; p.flankerBark = false; p.grenadeLine = false;
                    break;
                default: // Normal: the field defaults above
                    break;
            }
            return p;
        }

        /// <summary>Base delay before an enemy reacts (±20 %).</summary>
        public static float Reaction(Profile p) => p.reactionTime * Random.Range(0.8f, 1.2f);

        // ------------------------------------------------------------------ tuning panel, page "AI Zorluk"

        private static bool s_registered;

        private static void Changed()
        {
            if (Current != Tier.Custom)
            {
                P = P.Clone();
                Current = Tier.Custom;
            }
        }

        public static void RegisterTuning()
        {
            if (s_registered) return;
            s_registered = true;
            const string G = "AI Zorluk";
            Tuning.TuningPanel.RegisterChoice(G, "Düşman zekası", TierNames, () => (int)Current, v =>
            {
                if (v == (int)Tier.Custom) Changed();
                else Set((Tier)v);
            });
            void F(string n, System.Func<float> get, System.Action<float> set, float min, float max, float step) =>
                Tuning.TuningPanel.Register(G, n, get, v => { Changed(); set(v); }, min, max, step);
            void B(string n, System.Func<bool> get, System.Action<bool> set) =>
                Tuning.TuningPanel.RegisterToggle(G, n, get, v => { Changed(); set(v); });

            F("Tepki süresi (sn)", () => P.reactionTime, v => P.reactionTime = v, 0.05f, 2f, 0.05f);
            F("Fark etme hızı çarpanı", () => P.noticeMul, v => P.noticeMul = v, 0.3f, 2f, 0.05f);
            F("Görüş mesafesi çarpanı", () => P.sightMul, v => P.sightMul = v, 0.5f, 2f, 0.05f);
            F("Duyma çarpanı", () => P.hearMul, v => P.hearMul = v, 0.5f, 2f, 0.05f);
            F("Atış sapması çarpanı", () => P.spreadMul, v => P.spreadMul = v, 0.3f, 3f, 0.05f);
            F("İlk mermi ıska olasılığı", () => P.firstShotMissChance, v => P.firstShotMissChance = v, 0f, 1f, 0.05f);
            F("Aynı anda saldıran (takım)", () => P.maxAttackers, v => P.maxAttackers = Mathf.RoundToInt(v), 1f, 6f, 1f);
            F("Karar aralığı en az (sn)", () => P.decideMin, v => { P.decideMin = v; P.decideMax = Mathf.Max(P.decideMax, v); }, 0.05f, 1.5f, 0.05f);
            F("Karar aralığı en çok (sn)", () => P.decideMax, v => P.decideMax = Mathf.Max(v, P.decideMin), 0.05f, 2f, 0.05f);
            F("İnanç kaybı (güven / sn)", () => P.beliefDecay, v => P.beliefDecay = v, 0.01f, 0.5f, 0.01f);
            F("Kaybedince ileri tahmin (sn)", () => P.predictSeconds, v => P.predictSeconds = v, 0f, 4f, 0.1f);
            F("Bağırma gecikmesi (sn)", () => P.shoutDelay, v => P.shoutDelay = v, 0.1f, 4f, 0.1f);
            F("Bağırma menzili (m)", () => P.shoutRadius, v => P.shoutRadius = v, 5f, 80f, 1f);
            F("Arama süresi (sn)", () => P.searchTime, v => P.searchTime = v, 10f, 300f, 5f);
            F("Sinir eşik kayması", () => P.nerveShift, v => P.nerveShift = v, -40f, 40f, 1f);
            F("Nişan altında çıkma olasılığı", () => P.peekUnderAimChance, v => P.peekUnderAimChance = v, 0f, 1f, 0.05f);
            F("Bomba hazırlık süresi (sn)", () => P.throwWindup, v => P.throwWindup = v, 0.3f, 3f, 0.05f);
            F("Takım başına bomba (sabit)", () => P.grenadesFlat, v => P.grenadesFlat = Mathf.RoundToInt(v), 0f, 6f, 1f);
            F("Silahlı başına bomba", () => P.grenadesPerGunner, v => P.grenadesPerGunner = v, 0f, 2f, 0.25f);
            F("Hata olasılığı (karar başına)", () => P.mistakeChance, v => P.mistakeChance = v, 0f, 0.5f, 0.01f);
            F("Araç canı tabanı (oran)", () => P.vehicleFloor, v => P.vehicleFloor = v, 0f, 0.6f, 0.05f);
            B("Koşan hedefin önüne nişan", () => P.leadTarget, v => P.leadTarget = v);
            B("Kanat", () => P.flank, v => P.flank = v);
            B("İki yönden kanat", () => P.flankDouble, v => P.flankDouble = v);
            B("Siperden sipere sırayla ilerleme", () => P.bounding, v => P.bounding = v);
            B("Sırayla şarjör", () => P.staggerReload, v => P.staggerReload = v);
            B("Baskı ateşi", () => P.suppressFire, v => P.suppressFire = v);
            B("Bombayla siperden çıkarma", () => P.grenadeFlush, v => P.grenadeFlush = v);
            B("Çekilirken kendini koruma bombası", () => P.grenadeCoverRetreat, v => P.grenadeCoverRetreat = v);
            F("Bombayı geri atma olasılığı", () => P.grenadeThrowBack, v => P.grenadeThrowBack = v, 0f, 1f, 0.05f);
            B("Kalkan darbesi", () => P.shieldBash, v => P.shieldBash = v);
            B("Kalkan yanından ateş", () => P.shieldSideFire, v => P.shieldSideFire = v);
            B("Fırsat: oyuncu dolduruyor", () => P.exploitReload, v => P.exploitReload = v);
            B("Fırsat: silah kınında", () => P.exploitDraw, v => P.exploitDraw = v);
            B("Fırsat: düşen silah", () => P.exploitDrop, v => P.exploitDrop = v);
            B("Mermi sayma", () => P.countShots, v => P.countShots = v);
            Tuning.TuningPanel.RegisterChoice(G, "Yerden silah alma", new[] { "mermi bitince", "mermi azalınca", "daha iyisini görünce", "silahsız bırakmak için de" },
                () => P.scavenge, v => { Changed(); P.scavenge = v; });
            Tuning.TuningPanel.RegisterChoice(G, "Araca karşı", new[] { "kaçarken ateş", "yol kesme", "lastik + bomba", "park yerinde pusu" },
                () => P.vehicleDeny, v => { Changed(); P.vehicleDeny = v; });
            B("İkili arama", () => P.pairSearch, v => P.pairSearch = v);
            F("Eksik arkadaşı fark etme (sn, 0 kapalı)", () => P.missingCheck, v => P.missingCheck = v, 0f, 300f, 5f);
            Tuning.TuningPanel.RegisterChoice(G, "Taşın geldiği yöne bakma", new[] { "kapalı", "ikinci taşta", "ilk taşta" },
                () => P.rockBacktrace, v => { Changed(); P.rockBacktrace = v; });
            B("Karşılaşma hafızası", () => P.encounterMemory, v => P.encounterMemory = v);
            F("Fraksiyon öğrenme hızı", () => P.factionMemory, v => P.factionMemory = v, 0f, 2f, 0.5f);
            B("İmza anlar", () => P.signatureMoves, v => P.signatureMoves = v);
            F("Hayalet işaret menzili (m, 0 yok)", () => Mathf.Min(P.ghostRange, 200f), v => P.ghostRange = v >= 200f ? 9999f : v, 0f, 200f, 5f);
            F("Balon menzili (m)", () => P.barkRange, v => P.barkRange = v, 5f, 80f, 1f);
            B("Ekran kenarı göstergesi", () => P.edgeIndicator, v => P.edgeIndicator = v);
            B("Kanatçı balonu", () => P.flankerBark, v => P.flankerBark = v);
            B("Bomba çizgisi", () => P.grenadeLine, v => P.grenadeLine = v);
        }
    }
}
