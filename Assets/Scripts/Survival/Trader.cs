using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using EndlessSurvival.Inventory;
using EndlessSurvival.World;

namespace EndlessSurvival.Survival
{
    /// <summary>4.5: roadside trader with a cart. [E] opens the trade window (buy with money, sell backpack items).</summary>
    public class Trader : MonoBehaviour, IInteractable
    {
        public readonly List<ItemStack> stock = new List<ItemStack>();

        public Vector3 InteractPosition => transform.position + Vector3.up;
        public bool CanInteract => !TradeUI.IsOpen;

        public static Trader Create(Vector3 position, Quaternion rotation, Transform parent, int seed)
        {
            var root = new GameObject("Trader").transform;
            root.SetParent(parent, true);
            root.position = position;
            root.rotation = rotation;

            var body = SurvivalPrimitives.Part(PrimitiveType.Capsule, root, new Vector3(0f, 1f, 0f), new Vector3(0.7f, 1f, 0.7f), new Color(0.35f, 0.4f, 0.3f));
            body.name = "TraderBody";
            SurvivalPrimitives.Part(PrimitiveType.Sphere, root, new Vector3(0f, 1.85f, 0f), Vector3.one * 0.38f, new Color(0.85f, 0.7f, 0.55f), false);
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, root, new Vector3(0f, 2.05f, 0f), new Vector3(0.55f, 0.04f, 0.55f), new Color(0.3f, 0.22f, 0.12f), false);

            // cart
            var cart = new GameObject("Cart").transform;
            cart.SetParent(root, false);
            cart.localPosition = new Vector3(1.6f, 0f, 0f);
            Color wood = new Color(0.45f, 0.32f, 0.2f);
            SurvivalPrimitives.Part(PrimitiveType.Cube, cart, new Vector3(0f, 0.8f, 0f), new Vector3(1.4f, 0.5f, 2f), wood);
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, cart, new Vector3(0.75f, 0.4f, 0f), new Vector3(0.8f, 0.05f, 0.8f), new Color(0.2f, 0.2f, 0.2f), false)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            SurvivalPrimitives.Part(PrimitiveType.Cylinder, cart, new Vector3(-0.75f, 0.4f, 0f), new Vector3(0.8f, 0.05f, 0.8f), new Color(0.2f, 0.2f, 0.2f), false)
                .transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            SurvivalPrimitives.Part(PrimitiveType.Cube, cart, new Vector3(0f, 1.25f, 0f), new Vector3(1.2f, 0.4f, 1.6f), new Color(0.6f, 0.55f, 0.4f), false);

            var trader = root.gameObject.AddComponent<Trader>();
            trader.RollStock(new SeededRandom(seed));
            return trader;
        }

        private void RollStock(SeededRandom rng)
        {
            var s = SurvivalSettings.Instance;
            var pool = new List<ItemDefinition>();
            if (s.traderStock != null) foreach (var it in s.traderStock) if (it != null) pool.Add(it);
            if (pool.Count == 0) return;
            int kinds = Mathf.Min(pool.Count, rng.Range(5, 9));
            for (int i = 0; i < kinds; i++)
            {
                int idx = rng.Range(0, pool.Count);
                var item = pool[idx];
                pool.RemoveAt(idx);
                stock.Add(new ItemStack(item, item.category == ItemCategory.Ammo ? rng.Range(2, 6) : rng.Range(1, 4)));
            }
        }

        public string GetPrompt() => "Tüccarla takas yap";

        public string Interact(PlayerInventory user)
        {
            TradeUI.Open(this, user);
            return null;
        }

        public static int BuyPrice(ItemDefinition item) => Mathf.Max(1, Mathf.CeilToInt(item.value * SurvivalSettings.Instance.traderMarkup));
        public static int SellPrice(ItemDefinition item) => Mathf.Max(0, Mathf.FloorToInt(item.value * SurvivalSettings.Instance.traderSellBack));

        public string Buy(int index, PlayerInventory user)
        {
            if (index < 0 || index >= stock.Count) return null;
            var st = stock[index];
            int price = BuyPrice(st.item);
            if (user.Money < price) return "Paran yetmiyor";
            if (!st.item.IsDirectPickup && user.Storage.MaxAddable(st.item) <= 0) return "Çantada yer yok";
            if (!user.SpendMoney(price)) return "Paran yetmiyor";
            user.AddItem(st.item, 1);
            st.amount--;
            if (st.amount <= 0) stock.RemoveAt(index);
            else stock[index] = st;
            return $"{st.item.DisplayName} alındı (-{price})";
        }

        public string Sell(ItemDefinition item, PlayerInventory user)
        {
            if (item == null || user.Storage.GetCount(item) <= 0) return null;
            int price = SellPrice(item);
            if (price <= 0) return "Tüccar bunu almıyor";
            user.Storage.Remove(item, 1);
            user.AddMoney(price);
            return $"{item.DisplayName} satıldı (+{price})";
        }
    }

    /// <summary>Simple OnGUI trade window.</summary>
    public class TradeUI : MonoBehaviour
    {
        private static TradeUI _instance;
        public static bool IsOpen => _instance != null && _instance._trader != null;

        private Trader _trader;
        private PlayerInventory _user;
        private Vector2 _scrollBuy, _scrollSell;
        private string _message;

        public static void Open(Trader trader, PlayerInventory user)
        {
            if (_instance == null)
            {
                var go = new GameObject("TradeUI");
                _instance = go.AddComponent<TradeUI>();
            }
            if (_instance._trader != null) return;
            _instance._trader = trader;
            _instance._user = user;
            _instance._message = null;
            SurvivalUIState.TradeOpen = true;
            SurvivalUIState.AcquireCursor();
        }

        public static void Close()
        {
            if (_instance == null || _instance._trader == null) return;
            _instance._trader = null;
            SurvivalUIState.TradeOpen = false;
            SurvivalUIState.ReleaseCursor();
        }

        private void Update()
        {
            if (_trader == null) return;
            if (SKeys.Down(Key.Escape) || _user == null ||
                Vector3.Distance(_user.transform.position, _trader.transform.position) > 5f)
                Close();
        }

        private void OnDestroy()
        {
            if (_instance == this && _trader != null)
            {
                SurvivalUIState.TradeOpen = false;
                SurvivalUIState.ReleaseCursor();
            }
        }

        private void OnGUI()
        {
            if (_trader == null || _user == null) return;
            SurvivalGUI.Init();
            float w = 720f, h = 460f;
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(rect, GUIContent.none, SurvivalGUI.Panel);
            GUILayout.BeginArea(new Rect(rect.x + 16, rect.y + 12, w - 32, h - 24));
            GUILayout.Label($"TÜCCAR     Paran: {_user.Money}", SurvivalGUI.Title);
            GUILayout.BeginHorizontal();

            GUILayout.BeginVertical(GUILayout.Width(w * 0.5f - 24));
            GUILayout.Label("Satın al", SurvivalGUI.Header);
            _scrollBuy = GUILayout.BeginScrollView(_scrollBuy, GUILayout.Height(300));
            int buy = -1;
            for (int i = 0; i < _trader.stock.Count; i++)
            {
                var st = _trader.stock[i];
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{st.item.DisplayName} x{st.amount}", SurvivalGUI.Text, GUILayout.Width(200));
                if (GUILayout.Button($"Al ({Trader.BuyPrice(st.item)})", GUILayout.Width(100))) buy = i;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            if (buy >= 0) _message = _trader.Buy(buy, _user);
            GUILayout.EndVertical();

            GUILayout.BeginVertical(GUILayout.Width(w * 0.5f - 24));
            GUILayout.Label("Sat (çantadan)", SurvivalGUI.Header);
            _scrollSell = GUILayout.BeginScrollView(_scrollSell, GUILayout.Height(300));
            var items = new List<KeyValuePair<ItemDefinition, int>>(_user.Storage.Items);
            foreach (var pair in items)
            {
                if (pair.Key == null) continue;
                GUILayout.BeginHorizontal();
                GUILayout.Label($"{pair.Key.DisplayName} x{pair.Value}", SurvivalGUI.Text, GUILayout.Width(200));
                int price = Trader.SellPrice(pair.Key);
                GUI.enabled = price > 0;
                if (GUILayout.Button($"Sat ({price})", GUILayout.Width(100))) _message = _trader.Sell(pair.Key, _user);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_message, SurvivalGUI.Text);
            if (GUILayout.Button("Kapat [Esc]", GUILayout.Width(140))) Close();
            GUILayout.EndArea();
        }
    }
}
