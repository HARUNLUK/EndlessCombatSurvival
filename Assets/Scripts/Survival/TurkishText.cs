using System.Collections.Generic;
using UnityEngine;
using EndlessSurvival.Inventory;

namespace EndlessSurvival.Survival
{
    /// <summary>
    /// Turkish names and descriptions for the older items that were created with English text, and Turkish words
    /// for enums the UI prints (equipment slots). Applied at runtime on game start, so re-running the editor setup
    /// menus cannot bring the English names back. Only displayName/description change; asset names stay the same
    /// (keyword lookups and saves use the asset name).
    /// </summary>
    public static class TurkishText
    {
        private static readonly Dictionary<string, (string name, string desc)> Items = new Dictionary<string, (string, string)>
        {
            { "Item_ArmGuards", ("Kol Koruyucu", "Ön kollar için dolgulu koruma.") },
            { "Item_AssaultRifle", ("Piyade Tüfeği", "Otomatik tüfek. Dengeli ve güvenilir.") },
            { "Item_Bandage", ("Sargı Bezi", "Biraz can verir, kanamayı durdurur.") },
            { "Item_BaseballBat", ("Beyzbol Sopası", "Yavaş savrulur, sert vurur.") },
            { "Item_BrokenWatch", ("Kırık Saat", "Üçü çeyrek geçe durmuş.") },
            { "Item_CannedBeans", ("Konserve Fasulye", "Soğuk ama doyurucu.") },
            { "Item_Cap", ("Şapka", "Güneşi gözünden uzak tutar.") },
            { "Item_CarBattery", ("Araç Aküsü", "Ağır ama hâlâ şarjlı. Aracın elektriği için.") },
            { "Item_CargoPants", ("Kargo Pantolon", "Bol cepli.") },
            { "Item_Coins", ("Bozuk Para", "Para. Doğrudan cüzdana eklenir.") },
            { "Item_CombatBoots", ("Asker Botu", "Sağlam deri bot.") },
            { "Item_CombatHelmet", ("Askeri Miğfer", "Ordu fazlası miğfer.") },
            { "Item_CrackedPhone", ("Çatlak Telefon", "Ekranı paramparça.") },
            { "Item_DriedMeat", ("Kuru Et", "Aylarca bozulmaz.") },
            { "Item_DuctTape", ("Koli Bandı", "Neredeyse her şeyi tamir eder.") },
            { "Item_Electronics", ("Elektronik Parça", "Sökülmüş devre kartları ve kablolar.") },
            { "Item_EmptyBottle", ("Boş Şişe", "Su bulursan doldurabilirsin.") },
            { "Item_EnergyBar", ("Enerji Barı", "Küçük bir atıştırmalık.") },
            { "Item_EnginePart", ("Motor Parçası", "Motor yükseltmesi için kullanılır.") },
            { "Item_Flashlight", ("El Feneri", "Pili hâlâ çalışıyor.") },
            { "Item_FuelCan", ("Benzin Bidonu", "Aracın deposunu doldurur.") },
            { "Item_Grenade", ("El Bombası", "[G] ile fırlat. Üç saniye sonra patlar.") },
            { "Item_HuntingRifle", ("Av Tüfeği", "Yavaş, isabetli ve sert vurur.") },
            { "Item_KevlarVest", ("Kevlar Yelek", "Küçük kalibreli mermilerin çoğunu durdurur.") },
            { "Item_Knife", ("Bıçak", "Hızlı, kısa menzilli. Leş kesmek için de gerekir.") },
            { "Item_Medkit", ("İlk Yardım Çantası", "Çok can verir.") },
            { "Item_OldBoot", ("Eski Bot", "Çiftin sadece biri.") },
            { "Item_Pistol", ("Tabanca", "Hafif tabanca. Tüfekten zayıf ama hızlı çekilir.") },
            { "Item_ReinforcedPants", ("Takviyeli Pantolon", "Diz ve kaval kemiğinde sert plakalar.") },
            { "Item_RifleAmmo", ("Tüfek Mermisi", "Piyade tüfeği mermisi. Tüfek mermisi cebine eklenir.") },
            { "Item_PistolAmmo", ("Tabanca Mermisi", "Tabanca ve hafif makinalı mermisi. Tabanca mermisi cebine eklenir.") },
            { "Item_ShotgunShells", ("Av Fişeği", "Av tüfeği fişeği. Fişek cebine eklenir.") },
            { "Item_Rock", ("Taş", "LB (klavye T) ile fırlat, başka yerde ses çıkarsın.") },
            { "Item_RubberHose", ("Lastik Hortum", "Soğutma hortumu. Bir ucu çatlak.") },
            { "Item_RustyCan", ("Paslı Kutu", "Boş ve çürümüş.") },
            { "Item_ScrapMetal", ("Hurda Metal", "Aracı tamir ve yükseltmek için.") },
            { "Item_Smg", ("Hafif Makineli", "Küçük ve hızlı, ama tek atışı zayıf.") },
            { "Item_Sneakers", ("Spor Ayakkabı", "Eskimiş ama rahat.") },
            { "Item_SodaCan", ("Gazoz Kutusu", "Gazı kaçmış ama ıslak.") },
            { "Item_SparkPlug", ("Buji", "Ölü bir motordan söküldü. Hâlâ sağlam.") },
            { "Item_TeddyBear", ("Oyuncak Ayı", "Biri geride bırakmış.") },
            { "Item_ToolKit", ("Alet Çantası", "Eksiksiz bir tamirci takımı.") },
            { "Item_WaterBottle", ("Su Şişesi", "Temiz içme suyu.") },
            { "Item_WaterFilter", ("Su Filtresi", "Kirli suyu içilir hale getirir.") },
            { "Item_WoodenClub", ("Tahta Sopa", "Ağır bir sopa. Yumruktan iyidir.") },
            { "Item_WorkJacket", ("İş Ceketi", "Kalın kanvas ceket.") },
            { "DummyItem", ("Hurda Metal", "Aracı tamir ve yükseltmek için.") },
        };

        private static bool _applied;

        /// <summary>Renames every loaded item that still has its English text. Safe to call more than once.</summary>
        public static void ApplyToItems()
        {
            foreach (var item in Resources.FindObjectsOfTypeAll<ItemDefinition>())
            {
                if (item == null || !Items.TryGetValue(item.name, out var t)) continue;
                item.displayName = t.name;
                item.description = t.desc;
            }
            _applied = true;
        }

        public static void EnsureApplied()
        {
            if (!_applied) ApplyToItems();
        }

        public static string Slot(EquipSlot slot)
        {
            switch (slot)
            {
                case EquipSlot.Head: return "Baş";
                case EquipSlot.Torso: return "Gövde";
                case EquipSlot.Arms: return "Kollar";
                case EquipSlot.Legs: return "Bacaklar";
                case EquipSlot.Feet: return "Ayaklar";
                case EquipSlot.Weapon: return "Silah";
                case EquipSlot.Shield: return "Kalkan";
                case EquipSlot.Bow: return "Yay";
                case EquipSlot.LongGun: return "Uzun namlu";
                case EquipSlot.Pistol: return "Tabanca";
                case EquipSlot.Melee: return "Yakın dövüş";
                default: return slot.ToString();
            }
        }
    }
}
