using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools > Enemies > Setup Layers: creates the Walls / Player / Enemies physics layers, puts the level colliders,
// the player and the enemy prefabs on them, and stops enemies from pushing each other. Safe to run again.
public static class EnemyLayerSetup
{
    const string WallsLayer = "Walls";
    const string PlayerLayer = "Player";
    const string EnemiesLayer = "Enemies";
    const string EnemyPrefabsFolder = "Assets/Prefabs/Enemies";
    // Level objects whose colliders should block sight and movement. Pits stay on Default:
    // they block walking but not the view.
    static readonly string[] WallObjects = { "L01_Collision", "L01_Walls" };

    [MenuItem("Tools/Enemies/Setup Layers")]
    public static void Run()
    {
        int walls = EnsureLayer(WallsLayer), player = EnsureLayer(PlayerLayer), enemies = EnsureLayer(EnemiesLayer);
        if (walls < 0 || player < 0 || enemies < 0) return;

        int wallObjects = 0;
        foreach (var transform in Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (WallObjects.Contains(transform.name)) { SetLayer(transform.gameObject, walls); wallObjects++; }

        var playerHealth = Object.FindFirstObjectByType<PlayerHealth>(FindObjectsInactive.Include);
        if (playerHealth != null) SetLayer(playerHealth.gameObject, player);

        // Enemies placed directly in the scene (not from a prefab) get the layer too.
        foreach (var enemy in Object.FindObjectsByType<EnemyController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (!PrefabUtility.IsPartOfPrefabInstance(enemy)) { SetLayer(enemy.gameObject, enemies); SetMasks(enemy, walls, enemies); }

        int prefabs = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { EnemyPrefabsFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var enemy = root.GetComponent<EnemyController>();
                if (enemy == null) continue;
                foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = enemies;
                SetMasks(enemy, walls, enemies);
                PrefabUtility.SaveAsPrefabAsset(root, path);
                prefabs++;
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Enemies keep their distance through steering instead of shoving each other.
        Physics2D.IgnoreLayerCollision(enemies, enemies, true);
        var physicsSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/Physics2DSettings.asset");
        if (physicsSettings.Length > 0) EditorUtility.SetDirty(physicsSettings[0]);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Layers ready: {wallObjects} wall objects, player {(playerHealth != null ? "set" : "not found")}, {prefabs} enemy prefabs. Save the scene '{scene.name}'.");
    }

    static int EnsureLayer(string layerName)
    {
        int existing = LayerMask.NameToLayer(layerName);
        if (existing >= 0) return existing;
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        for (int i = 8; i < layers.arraySize; i++)
        {
            var slot = layers.GetArrayElementAtIndex(i);
            if (!string.IsNullOrEmpty(slot.stringValue)) continue;
            slot.stringValue = layerName;
            tagManager.ApplyModifiedProperties();
            return i;
        }
        Debug.LogError($"No free layer slot for '{layerName}'.");
        return -1;
    }

    static void SetLayer(GameObject root, int layer)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            Undo.RecordObject(t.gameObject, "Set Layer");
            t.gameObject.layer = layer;
        }
    }

    static void SetMasks(EnemyController enemy, int walls, int enemies)
    {
        var serialized = new SerializedObject(enemy);
        serialized.FindProperty("sightBlockers").intValue = 1 << walls;
        serialized.FindProperty("obstacles").intValue = (1 << walls) | 1; // Walls + Default (pits and other scenery)
        serialized.FindProperty("allies").intValue = 1 << enemies;
        serialized.ApplyModifiedProperties();
    }
}
