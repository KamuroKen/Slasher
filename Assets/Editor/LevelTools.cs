using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class LevelTools
{
    // After a tile asset changes type (e.g. a static Tile becomes an AnimatedTile), tilemaps keep the old
    // cached data until refreshed. This refreshes every tilemap in the open scene; save the scene afterwards.
    [MenuItem("Tools/Level/Refresh Tilemaps")]
    public static void RefreshTilemaps()
    {
        var tilemaps = Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var tilemap in tilemaps)
        {
            Undo.RecordObject(tilemap, "Refresh Tilemaps");
            tilemap.RefreshAllTiles();
            EditorUtility.SetDirty(tilemap);
        }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log($"Refreshed {tilemaps.Length} tilemaps. Save the scene to keep animated tiles.");
    }
}
