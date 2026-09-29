using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

// One-off: Tools > Altar > Make Altar Walkable. Delete this file afterwards.
// Lets the player climb the altar stairs and stand on the platform in front of the hand:
//  - Altar_Offering prefab: drawn under the player (sorting layer Walls), solid colliders only around the
//    platform edges, the stair sides and the pillar; the use area becomes the spot in front of the hand.
//  - Open scene: erases the 6x2 block of collision tiles that covered the whole altar base.
public static class AltarWalkable
{
    const string AltarPrefab = "Assets/Prefabs/Altar/Altar_Offering.prefab";
    const string BlocksName = "Blocks";

    // Local to the altar pivot (centre of its bottom-left cell). (centre, size) in world units.
    static readonly (Vector2 center, Vector2 size)[] Solid =
    {
        (new Vector2(0.6f, 0.1f), new Vector2(1.2f, 1.2f)),     // front face, left of the stairs
        (new Vector2(3.375f, 0.1f), new Vector2(1.25f, 1.2f)),  // front face, right of the stairs
        (new Vector2(0.15f, 1.55f), new Vector2(0.3f, 1.7f)),   // left edge of the platform
        (new Vector2(3.85f, 1.55f), new Vector2(0.3f, 1.7f)),   // right edge of the platform
        (new Vector2(2.0f, 2.25f), new Vector2(4.0f, 0.3f)),    // back edge of the platform
        (new Vector2(2.125f, 1.8f), new Vector2(0.75f, 0.6f)),  // the pillar holding the hand
    };
    static readonly Vector2 UseCenter = new Vector2(2.15f, 1.3f), UseSize = new Vector2(1.5f, 1f);

    [MenuItem("Tools/Altar/Make Altar Walkable")]
    public static void Run()
    {
        if (AssetDatabase.LoadAssetAtPath<GameObject>(AltarPrefab) == null) { Debug.LogError("Altar: " + AltarPrefab + " not found."); return; }
        var root = PrefabUtility.LoadPrefabContents(AltarPrefab);
        try
        {
            var renderer = root.GetComponent<SpriteRenderer>();
            renderer.sortingLayerName = "Walls"; // under the player, like the walls: the player can stand on it
            renderer.sortingOrder = 5;

            var altar = root.GetComponent<OfferingAltar>();
            var serialized = new SerializedObject(altar);
            var area = serialized.FindProperty("useArea").objectReferenceValue as BoxCollider2D;
            if (area != null) { area.isTrigger = true; area.offset = UseCenter; area.size = UseSize; }
            serialized.FindProperty("reach").floatValue = 0.2f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            var old = root.transform.Find(BlocksName);
            if (old != null) Object.DestroyImmediate(old.gameObject);
            var blocks = new GameObject(BlocksName);
            blocks.transform.SetParent(root.transform, false);
            foreach (var (center, size) in Solid)
            {
                var box = blocks.AddComponent<BoxCollider2D>();
                box.offset = center;
                box.size = size;
            }
            PrefabUtility.SaveAsPrefabAsset(root, AltarPrefab);
        }
        finally { PrefabUtility.UnloadPrefabContents(root); }

        int erased = EraseBaseCollision();
        Debug.Log($"Altar: prefab updated, {erased} collision tile(s) under the altar erased. Save the scene (Ctrl+S); this file can then be deleted.");
    }

    static int EraseBaseCollision()
    {
        var scene = EditorSceneManager.GetActiveScene();
        var altar = Object.FindObjectsByType<OfferingAltar>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(a => a.gameObject.scene == scene);
        var collision = Object.FindObjectsByType<Tilemap>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => t.gameObject.scene == scene && t.name == "L01_Collision");
        if (altar == null || collision == null) { Debug.LogWarning("Altar: open Ye_level with the altar to erase its old collision."); return 0; }
        var cell = new SerializedObject(altar).FindProperty("altarCell").vector3IntValue;
        Undo.RecordObject(collision, "Make Altar Walkable");
        int erased = 0;
        for (int x = cell.x; x <= cell.x + 5; x++)
            for (int y = cell.y; y <= cell.y + 1; y++)
            {
                var p = new Vector3Int(x, y, 0);
                if (collision.GetTile(p) == null) continue;
                collision.SetTile(p, null);
                erased++;
            }
        EditorSceneManager.MarkSceneDirty(scene);
        return erased;
    }
}
