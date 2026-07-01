using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only convenience for testing the "brand-new player" flow. Wipes the
/// PlayerPrefs-backed save so the Main Menu shows a grayed-out Continue button
/// and New Game skips its confirmation dialog.
/// </summary>
public static class SaveDataMenu
{
    [MenuItem("Yum Jump/Erase Save Data")]
    private static void EraseSaveData()
    {
        // DeleteAll clears every PlayerPrefs key, guaranteeing a truly fresh
        // state (no leftover save, settings, etc.) for first-time testing.
        PlayerPrefs.DeleteAll();
        PlayerPrefs.Save();
        Debug.Log("[SaveDataMenu] Save data erased. Play the Main Menu to test the first-time flow.");
    }
}
