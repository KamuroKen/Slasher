using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-off: Tools > Characters > Add Shadows. Puts a "Shadow" child (soft ellipse from The Necromancer's Order,
// made darker: Assets/Sprites/Shadow_Character.png) under every enemy prefab and under the Player in the open
// scene(s). The shadow is drawn right under its owner (same sorting layer, order - 1), so it lies on the floor under
// characters and props. Running it again only updates the existing shadows. Undo with Ctrl+Z (scene part).
// Afterwards: save the scene (Ctrl+S) and delete this file. Size, position and strength (Color alpha) of each shadow
// can then be changed by hand on its Shadow object.
public static class CharacterShadowSetup
{
    const string ShadowSprite = "Assets/Sprites/Shadow_Character.png";
    const string ShadowName = "Shadow";

    // Prefab, shadow scale (relative to the character, which may itself be scaled), height of its centre in sprite
    // pixels above the character's pivot, strength (alpha).
    static readonly (string prefab, float scale, float lift, float alpha)[] Enemies =
    {
        ("Assets/Prefabs/Enemies/Skeleton_Common.prefab", 0.9f, 1f, 1f),
        ("Assets/Prefabs/Enemies/Skeleton_Common_Ghost.prefab", 0.9f, 1f, 1f),
        ("Assets/Prefabs/Enemies/Skeleton_Armored.prefab", 0.9f, 1f, 1f),
        ("Assets/Prefabs/Enemies/Skeleton_Armored_Ghost.prefab", 0.9f, 1f, 1f),
        ("Assets/Prefabs/Enemies/Skeleton_Captain.prefab", 1f, 1f, 1f),
        ("Assets/Prefabs/Enemies/Skeleton_Captain_Ghost.prefab", 1f, 1f, 1f),
        ("Assets/Prefabs/Enemies/Knight.prefab", 1.2f, 1f, 1f),
        ("Assets/Prefabs/Enemies/Bat.prefab", 0.45f, 0f, 0.7f), // flies: smaller, lighter shadow on the ground
    };
    const float PlayerScale = 0.7f, PlayerLift = 1f, PlayerAlpha = 1f;
    const float PixelsPerUnit = 16f;

    [MenuItem("Tools/Characters/Add Shadows")]
    public static void Run()
    {
        var sprite = AssetDatabase.LoadAllAssetsAtPath(ShadowSprite).OfType<Sprite>().FirstOrDefault();
        if (sprite == null) { Debug.LogError("Shadows: " + ShadowSprite + " not found."); return; }

        int prefabs = 0;
        foreach (var (path, scale, lift, alpha) in Enemies)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) { Debug.LogWarning("Shadows: prefab not found " + path); continue; }
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (Apply(root, sprite, scale, lift, alpha, false)) { PrefabUtility.SaveAsPrefabAsset(root, path); prefabs++; }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        int players = 0;
        foreach (var player in Object.FindObjectsByType<PlayerHealth>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!player.gameObject.scene.IsValid() || EditorUtility.IsPersistent(player)) continue;
            if (Apply(player.gameObject, sprite, PlayerScale, PlayerLift, PlayerAlpha, true))
            {
                EditorSceneManager.MarkSceneDirty(player.gameObject.scene);
                players++;
            }
        }
        AssetDatabase.SaveAssets();
        Debug.Log($"Shadows: {prefabs} enemy prefab(s) and {players} player(s) in open scenes. Save the scene (Ctrl+S); this file can then be deleted.");
    }

    static bool Apply(GameObject owner, Sprite sprite, float scale, float lift, float alpha, bool undo)
    {
        var body = owner.GetComponent<SpriteRenderer>();
        if (body == null) { Debug.LogWarning("Shadows: " + owner.name + " has no SpriteRenderer on its root, skipped.", owner); return false; }

        var existing = owner.transform.Find(ShadowName);
        GameObject shadow;
        if (existing != null)
        {
            shadow = existing.gameObject;
            if (undo) Undo.RecordObjects(new Object[] { shadow.transform, shadow.GetComponent<SpriteRenderer>() }, "Add Shadows");
        }
        else
        {
            shadow = new GameObject(ShadowName);
            shadow.transform.SetParent(owner.transform, false);
            shadow.transform.SetAsFirstSibling();
            shadow.AddComponent<SpriteRenderer>();
            if (undo) Undo.RegisterCreatedObjectUndo(shadow, "Add Shadows");
        }
        shadow.layer = owner.layer;
        shadow.transform.localPosition = new Vector3(0f, lift / PixelsPerUnit, 0f);
        shadow.transform.localRotation = Quaternion.identity;
        shadow.transform.localScale = new Vector3(scale, scale, 1f);

        var renderer = shadow.GetComponent<SpriteRenderer>();
        if (renderer == null) renderer = shadow.AddComponent<SpriteRenderer>();
        renderer.sprite = sprite;
        renderer.color = new Color(1f, 1f, 1f, alpha);
        renderer.sortingLayerID = body.sortingLayerID;
        renderer.sortingOrder = body.sortingOrder - 1; // under the character and the props, above the floor
        renderer.spriteSortPoint = SpriteSortPoint.Pivot;
        renderer.flipX = renderer.flipY = false;
        return true;
    }
}
