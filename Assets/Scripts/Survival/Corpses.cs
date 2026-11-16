using UnityEngine;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Corpses beside the road (2026-11-14): a body made of primitives lying on the ground that can be searched
    /// (LootChest), sometimes with a note next to it. No animation, no ragdoll. WorldPopulator decides where.
    /// </summary>
    public static class Corpses
    {
        private static LootTable _editorTable;

        private static LootTable DefaultTable()
        {
            var s = SurvivalSettings.Instance;
            if (s.corpseLoot != null) return s.corpseLoot;
#if UNITY_EDITOR
            if (_editorTable == null)
                _editorTable = UnityEditor.AssetDatabase.LoadAssetAtPath<LootTable>("Assets/Data/Loot/Loot_EnemyDrop.asset")
                               ?? UnityEditor.AssetDatabase.LoadAssetAtPath<LootTable>("Assets/Data/Loot/Loot_Roadside.asset");
            if (_editorTable != null) return _editorTable;
#endif
            return s.wreckLoot != null ? s.wreckLoot : s.houseLoot;
        }

        /// <summary>pos is a ground point. All randomness comes from rng, so the same seed gives the same body.</summary>
        public static GameObject Spawn(Transform parent, Vector3 pos, SeededRandom rng)
        {
            var root = new GameObject("Corpse");
            root.transform.SetParent(parent, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.Euler(0f, rng.Range(0f, 360f), 0f);

            Color cloth = Color.Lerp(new Color(0.22f, 0.24f, 0.2f), new Color(0.35f, 0.3f, 0.24f), rng.Value);
            Color trousers = Color.Lerp(new Color(0.16f, 0.17f, 0.2f), new Color(0.3f, 0.27f, 0.22f), rng.Value);
            Color skin = Color.Lerp(new Color(0.62f, 0.5f, 0.42f), new Color(0.75f, 0.62f, 0.52f), rng.Value);
            Color pale = Color.Lerp(skin, new Color(0.6f, 0.6f, 0.55f), 0.35f);

            var torso = SurvivalPrimitives.Part(PrimitiveType.Capsule, root.transform, new Vector3(0f, 0.17f, 0.1f), new Vector3(0.34f, 0.36f, 0.3f), cloth, false);
            torso.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            torso.name = "Torso";
            var head = SurvivalPrimitives.Part(PrimitiveType.Sphere, root.transform, new Vector3(0f, 0.12f, 0.58f), new Vector3(0.22f, 0.22f, 0.22f), pale, false);
            head.name = "Head";
            float spread = rng.Range(0.08f, 0.16f);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var leg = SurvivalPrimitives.Part(PrimitiveType.Capsule, root.transform, new Vector3(side * spread, 0.08f, -0.68f), new Vector3(0.13f, 0.4f, 0.13f), trousers, false);
                leg.transform.localRotation = Quaternion.Euler(90f, side * rng.Range(0f, 9f), 0f);
                leg.name = "Leg";
            }

            var box = root.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.15f, -0.22f);
            box.size = new Vector3(0.55f, 0.3f, 1.75f);

            var chest = root.AddComponent<LootChest>();
            chest.openPrompt = "Cesedi ara";
            chest.emptyPrompt = "Ceset (boş)";
            chest.containerTitle = "Ceset";
            var table = DefaultTable();
            // 30% of the bodies carry nothing
            if (table != null && !rng.Chance(0.3f)) chest.FillFrom(table, rng);
            else chest.FillFrom(null, rng);

            // Sometimes a note lies next to it
            if (rng.Chance(0.3f))
            {
                Vector3 beside = root.transform.right * 0.6f;
                Vector3 p = SurvivalPrimitives.Ground(pos + beside);
                RoadNotes.Spawn(parent, p, rng);
            }
            return root;
        }
    }
}
