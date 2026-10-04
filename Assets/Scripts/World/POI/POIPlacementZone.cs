namespace EndlessSurvival.World.POI
{
    /// <summary>
    /// Nesnelerin (POI, Kamp, Mağara, Barikat) yol ile olan konumsal bölgesini tanımlar.
    /// </summary>
    public enum POIPlacementZone
    {
        /// <summary>
        /// Kesinlikle yol dışında, yoldan güvenli mesafede (Doğada / Dağda). Yol koridorunda ve yakınında asla bulunamaz.
        /// </summary>
        OffRoad,

        /// <summary>
        /// Yol kenarında (mağara gibi girişi yola bakan ancak yapısı yoldan uzağa uzanan yapılar).
        /// </summary>
        Roadside,

        /// <summary>
        /// Yalnızca yolun tam üstünde özel olarak tanımlanmış noktalarda (barikat, pusu, kaza, kontrol noktası).
        /// Yol dışı nesneler asla bu bölgede oluşamaz.
        /// </summary>
        OnRoad
    }
}
