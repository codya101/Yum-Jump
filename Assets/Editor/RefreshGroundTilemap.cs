using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class RefreshGroundTilemap
{
    [MenuItem("Tools/Refresh Ground Tilemap")]
    public static void Refresh()
    {
        var go = GameObject.Find("Ground");
        if (go == null)
        {
            Debug.LogWarning("RefreshGroundTilemap: no GameObject named 'Ground' in the active scene.");
            return;
        }
        var tilemap = go.GetComponent<Tilemap>();
        if (tilemap == null)
        {
            Debug.LogWarning("RefreshGroundTilemap: 'Ground' has no Tilemap component.");
            return;
        }
        tilemap.RefreshAllTiles();
        EditorUtility.SetDirty(tilemap);
        Debug.Log("RefreshGroundTilemap: refreshed " + tilemap.GetUsedTilesCount() + " tiles. Save the scene to persist sprite changes.");
    }
}
