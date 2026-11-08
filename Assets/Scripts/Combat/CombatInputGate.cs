using UnityEngine;

namespace EndlessCombat.Combat
{
    /// <summary>True while a panel or mode owns the mouse / keyboard, so combat input must be ignored.</summary>
    public static class CombatInputGate
    {
        public static bool Blocked(GameObject player)
        {
            if (Time.timeScale <= 0.001f) return true;
            if (EndlessSurvival.Inventory.InventoryUI.IsOpen) return true;
            if (EndlessSurvival.Camping.CampfireUI.IsOpen) return true;
            if (EndlessSurvival.Vehicle.VehicleModificationUI.IsOpen) return true;
            if (EndlessSurvival.World.GameOverUI.IsGameOver) return true;
            if (EndlessSurvival.Survival.SurvivalUIState.AnyOwnOpen) return true;
            var vitals = EndlessSurvival.Survival.PlayerVitals.Instance;
            if (vitals != null && vitals.ToiletActive) return true;
            if (EndlessSurvival.Survival.SmokingAction.Active) return true;
            var note = EndlessSurvival.Story.NoteReaderUI.Instance;
            if (note != null && note.IsOpen) return true;

            if (player != null)
            {
                var sitting = player.GetComponent<EndlessSurvival.Camping.PlayerSittingController>();
                if (sitting != null && sitting.IsSitting) return true;
                var placer = player.GetComponent<EndlessSurvival.Camping.PlayerCampPlacer>();
                if (placer != null && placer.IsPlacing) return true;
            }
            return false;
        }
    }
}
