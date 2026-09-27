using UnityEditor;
using UnityEngine;

namespace EndlessSurvival.Inventory.Editor
{
    /// <summary>
    /// After scripts compile, rebuilds the inventory UI once per editor session when the scene still contains
    /// an outdated layout (the layout is versioned by InventoryUI.CurrentUiVersion).
    /// </summary>
    [InitializeOnLoad]
    public static class InventoryUiAutoUpdate
    {
        private const string TriedKey = "EndlessSurvival.InventoryUiAutoUpdateTried";

        static InventoryUiAutoUpdate()
        {
            EditorApplication.delayCall += Check;
        }

        private static void Check()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            if (SessionState.GetBool(TriedKey, false)) return;

            var ui = Object.FindFirstObjectByType<InventoryUI>(FindObjectsInactive.Include);
            if (ui == null || ui.uiVersion == InventoryUI.CurrentUiVersion) return;

            SessionState.SetBool(TriedKey, true);
            Debug.Log("[InventoryUiAutoUpdate] Inventory UI in the scene is outdated. Running the inventory setup. Save the scene afterwards (Ctrl+S).");
            InventorySetupTool.Run();
        }
    }
}
