using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;

// Tools > Enemies > Build Skeletons: builds the Common, Armored and Captain skeletons (and their Ghost versions)
// from "Vol2-The Necromancers Order" on top of the Knight: grid-slices the sheets with a feet pivot, creates
// Action_Direction clips, an Animator Override Controller of Knight.controller and a standalone prefab copied
// from the Knight (not a Variant, so later Knight edits do not change the skeletons).
// Safe to run again: existing clips, controllers and prefabs are updated in place, so references stay valid.
// Skeletons made by the first version of this tool as Prefab Variants are turned into standalone prefabs.
public static class SkeletonEnemyBuilder
{
    const string PackRoot = "Assets/Vol2-The Necromancers Order";
    const string KnightPrefabPath = "Assets/Prefabs/Enemies/Knight.prefab";
    const string PrefabFolder = "Assets/Prefabs/Enemies";
    const string ClipRoot = "Assets/Animations/Enemies";
    const string ItemsFolder = "Assets/Items";
    const float Scale = 0.75f;              // Same as the Knight.
    const float GhostSpeedMultiplier = 1.15f;
    const int SwingFrame = 4;               // Sheet frame where the weapon connects (same in every direction).
    const string CellSuffix = "_cell";

    // Pack file names look like "Common-AttackNE_60x57_Sheet", "Captain_WalkE_62x68_Sheet" or "CommonB-IdleS_60x57_Sheet" (B = Ghost).
    static readonly Regex SheetName = new Regex(
        @"^(?<char>[A-Za-z]+?)(?<ghost>B)?[-_](?<action>Idle|Walk|Attack|Damaged|Death)-?(?<dir>NE|SE|N|S|E)_(?<w>\d+)x(?<h>\d+)_Sheet$");

    static readonly Dictionary<string, string> Facing = new Dictionary<string, string>
    {
        { "E", "Right" }, { "NE", "UpRight" }, { "N", "Up" }, { "S", "Down" }, { "SE", "DownRight" },
    };

    struct ActionInfo
    {
        public string state; public float fps; public bool loop, dropFirst;
        public ActionInfo(string state, float fps, bool loop, bool dropFirst) { this.state = state; this.fps = fps; this.loop = loop; this.dropFirst = dropFirst; }
    }

    // The pack notes: in Attack, Damaged and Death the first frame is only an alignment reference.
    static readonly Dictionary<string, ActionInfo> Actions = new Dictionary<string, ActionInfo>
    {
        { "Idle", new ActionInfo("Idle", 10f, true, false) },
        { "Walk", new ActionInfo("Walk", 10f, true, false) },
        { "Attack", new ActionInfo("Attack", 10f, false, true) },
        { "Damaged", new ActionInfo("Hit", 12f, false, true) },
        { "Death", new ActionInfo("Death", 10f, false, true) },
    };

    struct LootSpec
    {
        public string item; public float chance; public int min, max;
        public LootSpec(string item, float chance, int min = 1, int max = 1) { this.item = item; this.chance = chance; this.min = min; this.max = max; }
    }

    sealed class EnemySpec
    {
        public string packName, prefabName;
        public int health, damage, poise;
        public float speed, knockback, cooldown;
        public Vector2 colliderSize;
        public LootSpec[] loot;
    }

    // Starting values; tune them later in the prefab Inspector (a rebuild writes these values again).
    static readonly EnemySpec[] Enemies =
    {
        new EnemySpec
        {
            packName = "Common", prefabName = "Skeleton_Common",
            health = 40, damage = 10, poise = 0, speed = 2.2f, knockback = 3f, cooldown = 1f,
            colliderSize = new Vector2(0.9f, 0.6f),
            loot = new[] { new LootSpec("food_apple", 0.25f), new LootSpec("potion_heal_common", 0.1f) },
        },
        new EnemySpec
        {
            packName = "Armored", prefabName = "Skeleton_Armored",
            health = 100, damage = 20, poise = 2, speed = 1.6f, knockback = 1.5f, cooldown = 1.2f,
            colliderSize = new Vector2(1f, 0.6f),
            loot = new[] { new LootSpec("food_apple", 0.25f), new LootSpec("potion_heal_common", 0.3f) },
        },
        new EnemySpec
        {
            packName = "Captain", prefabName = "Skeleton_Captain",
            health = 150, damage = 25, poise = 3, speed = 1.8f, knockback = 1f, cooldown = 1.4f,
            colliderSize = new Vector2(1.2f, 0.7f),
            loot = new[] { new LootSpec("potion_heal_common", 0.5f), new LootSpec("potion_heal_rare", 0.5f) },
        },
    };

    sealed class Sheet
    {
        public string path, action, dir;
        public int cellWidth, cellHeight;
    }

    [MenuItem("Tools/Enemies/Build Skeletons")]
    public static void Run()
    {
        var knight = AssetDatabase.LoadAssetAtPath<GameObject>(KnightPrefabPath);
        var baseController = knight != null ? knight.GetComponent<Animator>().runtimeAnimatorController : null;
        if (knight == null || baseController == null)
        {
            Debug.LogError("Skeletons: Knight prefab or its Animator Controller not found at " + KnightPrefabPath);
            return;
        }

        var built = new List<string>();
        try
        {
            foreach (var spec in Enemies)
            {
                GameObject standard = Build(spec, false, knight, baseController);
                if (standard == null) continue;
                built.Add(spec.prefabName);
                if (Build(spec, true, standard, baseController) != null) built.Add(spec.prefabName + "_Ghost");
            }
        }
        finally { EditorUtility.ClearProgressBar(); }

        AssetDatabase.SaveAssets();
        Debug.Log($"Skeletons built: {string.Join(", ", built)}. Prefabs are in {PrefabFolder}; drag them into the scene.");
    }

    static GameObject Build(EnemySpec spec, bool ghost, GameObject basePrefab, RuntimeAnimatorController baseController)
    {
        string name = spec.prefabName + (ghost ? "_Ghost" : "");
        string livery = ghost ? "Ghost Livery" : "Standard Livery";
        var sheets = FindSheets($"{PackRoot}/{livery}/{spec.packName}", spec.packName, ghost);
        if (sheets.Count != Facing.Count * Actions.Count)
            Debug.LogWarning($"{name}: found {sheets.Count} of {Facing.Count * Actions.Count} sheets, missing states will fall back to Idle.");
        if (sheets.Count == 0) { Debug.LogError($"{name}: no sheets in {PackRoot}/{livery}/{spec.packName}."); return null; }

        EditorUtility.DisplayProgressBar("Build Skeletons", $"{name}: slicing sheets", 0.1f);
        float pivotY = FeetPivot(sheets);
        SliceSheets(sheets, pivotY);

        EditorUtility.DisplayProgressBar("Build Skeletons", $"{name}: animation clips", 0.5f);
        string clipFolder = EnsureFolder(ClipRoot, name);
        var clips = new Dictionary<string, AnimationClip>();
        Sprite firstIdle = null;
        foreach (var sheet in sheets)
        {
            var info = Actions[sheet.action];
            var frames = GridSprites(sheet.path);
            if (info.dropFirst && frames.Count > 1) frames.RemoveAt(0);
            if (frames.Count == 0) { Debug.LogWarning($"{name}: no frames in {sheet.path}"); continue; }
            string state = info.state + "_" + Facing[sheet.dir];
            int swing = SwingFrame - (info.dropFirst ? 1 : 0);
            float? hitTime = sheet.action == "Attack" && swing >= 0 && swing < frames.Count ? swing / info.fps : (float?)null;
            clips[state] = SaveClip($"{clipFolder}/{state}.anim", frames, info.fps, info.loop, hitTime);
            if (state == "Idle_Down") firstIdle = frames[0];
        }

        var controller = SaveOverrideController($"{clipFolder}/{name}.overrideController", baseController, clips, name);

        EditorUtility.DisplayProgressBar("Build Skeletons", $"{name}: prefab", 0.9f);
        return SavePrefab($"{PrefabFolder}/{name}.prefab", basePrefab, root => Configure(root, spec, ghost, controller, firstIdle));
    }

    static List<Sheet> FindSheets(string folder, string packName, bool ghost)
    {
        var result = new List<Sheet>();
        if (!AssetDatabase.IsValidFolder(folder)) return result;
        foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var match = SheetName.Match(Path.GetFileNameWithoutExtension(path));
            if (!match.Success || match.Groups["char"].Value != packName || match.Groups["ghost"].Success != ghost) continue;
            string action = match.Groups["action"].Value, dir = match.Groups["dir"].Value;
            if (result.Any(s => s.action == action && s.dir == dir)) { Debug.LogWarning("Skeletons: duplicate sheet ignored: " + path); continue; }
            result.Add(new Sheet
            {
                path = path, action = action, dir = dir,
                cellWidth = int.Parse(match.Groups["w"].Value), cellHeight = int.Parse(match.Groups["h"].Value),
            });
        }
        return result;
    }

    // The lowest opaque row of the Idle frames is where the feet touch the ground; the pivot goes there,
    // so the sprite stands on the object's position like the Knight does.
    static float FeetPivot(List<Sheet> sheets)
    {
        int feet = int.MaxValue, cellHeight = 0;
        foreach (var sheet in sheets.Where(s => s.action == "Idle"))
        {
            var texture = new Texture2D(2, 2);
            texture.LoadImage(File.ReadAllBytes(sheet.path));
            var pixels = texture.GetPixels32();
            int width = texture.width;
            Object.DestroyImmediate(texture);
            cellHeight = sheet.cellHeight;
            for (int y = 0; y < sheet.cellHeight && y < feet; y++)
                for (int x = 0; x < width; x++)
                    if (pixels[y * width + x].a > 0) { feet = y; break; }
        }
        return feet == int.MaxValue || cellHeight == 0 ? 0.25f : (float)feet / cellHeight;
    }

    // Adds (or updates) one full-cell sprite per frame next to the pack's own trimmed sprites, which stay untouched.
    static void SliceSheets(List<Sheet> sheets, float pivotY)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var sheet in sheets)
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(sheet.path);
                bool changed = false;
                if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; changed = true; }
                if (importer.spriteImportMode != SpriteImportMode.Multiple) { importer.spriteImportMode = SpriteImportMode.Multiple; changed = true; }
                if (!Mathf.Approximately(importer.spritePixelsPerUnit, 16f)) { importer.spritePixelsPerUnit = 16f; changed = true; }
                if (importer.filterMode != FilterMode.Point) { importer.filterMode = FilterMode.Point; changed = true; }
                if (importer.mipmapEnabled) { importer.mipmapEnabled = false; changed = true; }

                importer.GetSourceTextureWidthAndHeight(out int width, out int height);
                int count = width / sheet.cellWidth;
                var pivot = new Vector2(0.5f, pivotY);

                var provider = factory.GetSpriteEditorDataProviderFromObject(importer);
                provider.InitSpriteEditorDataProvider();
                var rects = provider.GetSpriteRects().ToList();
                var names = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
                var pairs = names != null ? names.GetNameFileIdPairs().ToList() : null;
                string baseName = Path.GetFileNameWithoutExtension(sheet.path) + CellSuffix;
                for (int i = 0; i < count; i++)
                {
                    string spriteName = $"{baseName}{i:00}";
                    var rect = new Rect(i * sheet.cellWidth, height - sheet.cellHeight, sheet.cellWidth, sheet.cellHeight);
                    var existing = rects.FirstOrDefault(r => r.name == spriteName);
                    if (existing == null)
                    {
                        var added = new SpriteRect
                        {
                            name = spriteName, rect = rect, alignment = SpriteAlignment.Custom, pivot = pivot, spriteID = GUID.Generate(),
                        };
                        rects.Add(added);
                        pairs?.Add(new SpriteNameFileIdPair(added.name, added.spriteID));
                        changed = true;
                    }
                    else if (existing.rect != rect || existing.alignment != SpriteAlignment.Custom || existing.pivot != pivot)
                    {
                        existing.rect = rect; existing.alignment = SpriteAlignment.Custom; existing.pivot = pivot;
                        changed = true;
                    }
                }
                if (!changed) continue;
                provider.SetSpriteRects(rects.ToArray());
                if (names != null) names.SetNameFileIdPairs(pairs);
                provider.Apply();
                importer.SaveAndReimport();
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
    }

    static List<Sprite> GridSprites(string sheetPath)
    {
        string prefix = Path.GetFileNameWithoutExtension(sheetPath) + CellSuffix;
        return AssetDatabase.LoadAllAssetsAtPath(sheetPath).OfType<Sprite>()
            .Where(s => s.name.StartsWith(prefix))
            .OrderBy(s => int.Parse(s.name.Substring(prefix.Length)))
            .ToList();
    }

    static AnimationClip SaveClip(string path, List<Sprite> frames, float fps, bool loop, float? hitTime)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        bool isNew = clip == null;
        if (isNew) clip = new AnimationClip();
        clip.frameRate = fps;

        // The extra key at the end keeps the last frame on screen for a full frame.
        var keys = new ObjectReferenceKeyframe[frames.Count + 1];
        for (int i = 0; i < frames.Count; i++) keys[i] = new ObjectReferenceKeyframe { time = i / fps, value = frames[i] };
        keys[frames.Count] = new ObjectReferenceKeyframe { time = frames.Count / fps, value = frames[frames.Count - 1] };
        var binding = EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite");
        AnimationUtility.SetObjectReferenceCurve(clip, binding, keys);

        var settings = AnimationUtility.GetAnimationClipSettings(clip);
        settings.loopTime = loop;
        settings.startTime = 0f;
        settings.stopTime = frames.Count / fps;
        AnimationUtility.SetAnimationClipSettings(clip, settings);

        // EnemyController lands the blow on this event instead of guessing from Hit Moment.
        var events = hitTime.HasValue
            ? new[] { new AnimationEvent { functionName = EnemyController.AttackHitEvent, time = hitTime.Value } }
            : new AnimationEvent[0];
        AnimationUtility.SetAnimationEvents(clip, events);

        if (isNew) AssetDatabase.CreateAsset(clip, path);
        else EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimatorOverrideController SaveOverrideController(string path, RuntimeAnimatorController baseController,
        Dictionary<string, AnimationClip> clips, string enemyName)
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorOverrideController>(path);
        if (controller == null)
        {
            controller = new AnimatorOverrideController(baseController);
            AssetDatabase.CreateAsset(controller, path);
        }
        else controller.runtimeAnimatorController = baseController;

        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>(controller.overridesCount);
        controller.GetOverrides(overrides);
        for (int i = 0; i < overrides.Count; i++)
        {
            var original = overrides[i].Key;
            if (clips.TryGetValue(original.name, out var replacement))
                overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(original, replacement);
            else
                Debug.LogWarning($"{enemyName}: no clip for '{original.name}', it keeps the Knight animation.");
        }
        controller.ApplyOverrides(overrides);
        EditorUtility.SetDirty(controller);
        return controller;
    }

    // A new prefab is a standalone copy of basePrefab; an existing standalone prefab is edited in place.
    static GameObject SavePrefab(string path, GameObject basePrefab, System.Action<GameObject> configure)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && PrefabUtility.GetPrefabAssetType(existing) != PrefabAssetType.Variant)
        {
            var contents = PrefabUtility.LoadPrefabContents(path);
            try
            {
                configure(contents);
                return PrefabUtility.SaveAsPrefabAsset(contents, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }

        // New prefab, or an old Variant turned into a standalone prefab. The file is overwritten, so its GUID stays.
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(existing != null ? existing : basePrefab, scene);
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = Path.GetFileNameWithoutExtension(path);
            configure(instance);
            return PrefabUtility.SaveAsPrefabAsset(instance, path);
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static void Configure(GameObject root, EnemySpec spec, bool ghost, RuntimeAnimatorController controller, Sprite firstIdle)
    {
        root.transform.localScale = Vector3.one * Scale;
        root.GetComponent<Animator>().runtimeAnimatorController = controller;
        if (firstIdle != null) root.GetComponent<SpriteRenderer>().sprite = firstIdle;

        var enemy = new SerializedObject(root.GetComponent<EnemyController>());
        // The Ghost has the same stats as the normal skeleton, only faster.
        enemy.FindProperty("moveSpeed").floatValue = spec.speed * (ghost ? GhostSpeedMultiplier : 1f);
        enemy.FindProperty("attackDamage").intValue = spec.damage;
        enemy.FindProperty("poise").intValue = spec.poise;
        enemy.FindProperty("knockbackSpeed").floatValue = spec.knockback;
        enemy.FindProperty("attackCooldown").floatValue = spec.cooldown;
        enemy.ApplyModifiedPropertiesWithoutUndo();

        var health = new SerializedObject(root.GetComponent<Damageable>());
        health.FindProperty("maxHealth").intValue = spec.health;
        health.ApplyModifiedPropertiesWithoutUndo();

        // Feet collider, like the Knight's: a flat capsule sitting on the pivot.
        var capsule = root.GetComponent<CapsuleCollider2D>();
        if (capsule != null)
        {
            capsule.size = spec.colliderSize;
            capsule.offset = new Vector2(0f, spec.colliderSize.y * 0.5f);
        }

        var lootComponent = root.GetComponent<EnemyLoot>();
        if (lootComponent == null) lootComponent = root.AddComponent<EnemyLoot>();
        var loot = new SerializedObject(lootComponent);
        var drops = loot.FindProperty("loot.drops");
        var valid = spec.loot.Where(l => AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/{l.item}.asset") != null).ToArray();
        drops.arraySize = valid.Length;
        for (int i = 0; i < valid.Length; i++)
        {
            var drop = drops.GetArrayElementAtIndex(i);
            drop.FindPropertyRelative("item").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ItemDefinition>($"{ItemsFolder}/{valid[i].item}.asset");
            drop.FindPropertyRelative("chance").floatValue = valid[i].chance;
            drop.FindPropertyRelative("min").intValue = valid[i].min;
            drop.FindPropertyRelative("max").intValue = valid[i].max;
        }
        loot.ApplyModifiedPropertiesWithoutUndo();
    }

    static string EnsureFolder(string parent, string child)
    {
        string path = parent + "/" + child;
        if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, child);
        return path;
    }
}
