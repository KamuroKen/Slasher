// Level 01 builder (editor-only tool, does not touch gameplay code).
// Menu: Tools > Level 01 > ...
// Reads Assets/Editor/Level01Builder/L01_LevelData.json and:
//   1) fixes import settings of the used sprite sheets (PPU 16, Point, no compression) and slices them 16x16,
//   2) creates Tile assets in Assets/Tiles/Level01/...,
//   3) creates Tile Palettes in Assets/Palettes/Level01/...,
//   4) adds sorting layers Floor / Walls (below Default) and Overhead (above Default), Y-sort on the 2D renderer,
//   5) builds L01_Castle_Grid (+ room / gate markers) in Assets/Scenes/Ye_level.unity.
// Everything it makes is plain tiles on tilemaps, so any tile can be repainted from your own palettes.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Tilemaps;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class L01_LevelBuilder
{
    const string DataPath = "Assets/Editor/Level01Builder/L01_LevelData.json";
    const string ScenePath = "Assets/Scenes/Ye_level.unity";
    const string PaletteFolder = "Assets/Palettes/Level01";
    const string GridName = "L01_Castle_Grid";
    const string RendererPath = "Assets/Settings/Renderer2D.asset";

    // ------------------------------------------------------------------ data
    [Serializable] class LevelData { public int mapHeight; public TexData[] textures; public TileData[] tiles; public PalData[] palettes; public LayerData[] layers; public MarkerData[] markers; public Vec2 playerStart; }
    [Serializable] class TexData { public string path; public bool keep; public SprData[] sprites; }
    [Serializable] class SprData { public string n; public int x, y, w, h, px, py; }
    [Serializable] class TileData { public string id, tex, sprite, asset; public int col; }
    [Serializable] class PalData { public string name; public CellData[] cells; }
    [Serializable] class CellData { public string t; public int x, y, f; }
    [Serializable] class LayerData { public string name, sortingLayer; public int order, individual, collider, renderer; public CellData[] cells; }
    [Serializable] class MarkerData { public string group, name, note; public float x, y, w, h; public int icon; }
    [Serializable] class Vec2 { public float x, y; }

    static LevelData Load()
    {
        if (!File.Exists(DataPath)) throw new Exception("Missing " + DataPath);
        return JsonUtility.FromJson<LevelData>(File.ReadAllText(DataPath));
    }

    // ------------------------------------------------------------------ menu
    [MenuItem("Tools/Level 01/Build everything (import + tiles + palettes + scene)", false, 1)]
    public static void BuildAll()
    {
        if (!EditorUtility.DisplayDialog("Level 01 builder",
            "Will fix import settings of the level sprite sheets, create tiles + palettes and rebuild '" + GridName +
            "' in Ye_level.\n\nManual edits inside " + GridName + " will be replaced. Continue?", "Build", "Cancel")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        AssetDatabase.Refresh();
        var d = Load();
        try
        {
            Step("Import settings + 16x16 slices", 0.05f); ImportTextures(d);
            Step("Tile assets", 0.45f); var tiles = BuildTiles(d);
            Step("Palettes", 0.7f); BuildPalettes(d, tiles);
            Step("Sorting layers + Y-sort", 0.8f); SetupSorting();
            Step("Scene", 0.85f); BuildScene(d, tiles);
        }
        finally { EditorUtility.ClearProgressBar(); }
        Debug.Log("[L01] Build finished: tiles, palettes (" + PaletteFolder + ") and " + GridName + " in Ye_level.");
    }

    [MenuItem("Tools/Level 01/Rebuild scene only (Ye_level)", false, 2)]
    public static void RebuildScene()
    {
        if (!EditorUtility.DisplayDialog("Level 01 builder", "Rebuild '" + GridName + "' in Ye_level from the layout data?\nManual edits inside it will be replaced.", "Rebuild", "Cancel")) return;
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var d = Load();
        var tiles = LoadTiles(d);
        SetupSorting();
        BuildScene(d, tiles);
    }

    static void Step(string s, float p) => EditorUtility.DisplayProgressBar("Level 01 builder", s, p);

    // ------------------------------------------------------------------ 1. textures
    static void ImportTextures(LevelData d)
    {
        var factory = new SpriteDataProviderFactories();
        factory.Init();
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var t in d.textures)
            {
                var ti = AssetImporter.GetAtPath(t.path) as TextureImporter;
                if (ti == null) { Debug.LogWarning("[L01] texture not found: " + t.path); continue; }
                ti.textureType = TextureImporterType.Sprite;
                ti.spritePixelsPerUnit = 16;
                ti.filterMode = FilterMode.Point;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.mipmapEnabled = false;
                ti.alphaIsTransparency = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 8192;
                foreach (var plat in new[] { "Standalone", "Android", "iPhone", "WebGL" })
                {
                    var ps = ti.GetPlatformTextureSettings(plat);
                    if (!ps.overridden) continue;
                    ps.textureCompression = TextureImporterCompression.Uncompressed;
                    ps.format = TextureImporterFormat.RGBA32;
                    ti.SetPlatformTextureSettings(ps);
                }
                if (!t.keep)
                {
                    ti.spriteImportMode = SpriteImportMode.Multiple;
                    var dp = factory.GetSpriteEditorDataProviderFromObject(ti);
                    dp.InitSpriteEditorDataProvider();
                    var oldRects = dp.GetSpriteRects();
                    var old = oldRects.GroupBy(r => r.name).ToDictionary(g => g.Key, g => g.First().spriteID);
                    var used = new HashSet<GUID>();
                    GUID Reuse(SprData s)
                    {   // keep existing sprite IDs (same name, or same rect, or the only sprite) so old references survive
                        if (old.TryGetValue(s.n, out var a) && used.Add(a)) return a;
                        var r = oldRects.FirstOrDefault(o => (int)o.rect.x == s.x && (int)o.rect.y == s.y && (int)o.rect.width == s.w && (int)o.rect.height == s.h);
                        if (r != null && used.Add(r.spriteID)) return r.spriteID;
                        if (oldRects.Length == 1 && t.sprites.Length == 1 && used.Add(oldRects[0].spriteID)) return oldRects[0].spriteID;
                        var g = GUID.Generate(); used.Add(g); return g;
                    }
                    var rects = new List<SpriteRect>();
                    foreach (var s in t.sprites)
                    {
                        rects.Add(new SpriteRect
                        {
                            name = s.n,
                            rect = new Rect(s.x, s.y, s.w, s.h),
                            alignment = SpriteAlignment.Custom,
                            pivot = new Vector2(s.px / (float)s.w, s.py / (float)s.h),
                            border = Vector4.zero,
                            spriteID = Reuse(s)
                        });
                    }
                    dp.SetSpriteRects(rects.ToArray());
                    var nf = dp.GetDataProvider<ISpriteNameFileIdDataProvider>();
                    if (nf != null) nf.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
                    dp.Apply();
                }
                ti.SaveAndReimport();
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.Refresh();
    }

    // ------------------------------------------------------------------ 2. tiles
    static Dictionary<string, TileBase> BuildTiles(LevelData d)
    {
        var sprites = new Dictionary<string, Dictionary<string, Sprite>>();
        Sprite FindSprite(string tex, string name)
        {
            if (!sprites.TryGetValue(tex, out var map))
            {
                map = new Dictionary<string, Sprite>();
                foreach (var s in AssetDatabase.LoadAllAssetRepresentationsAtPath(tex).OfType<Sprite>()) map[s.name] = s;
                sprites[tex] = map;
            }
            return map.TryGetValue(name, out var sp) ? sp : null;
        }
        var result = new Dictionary<string, TileBase>();
        int i = 0, missing = 0;
        foreach (var f in d.tiles.Select(x => Path.GetDirectoryName(x.asset).Replace('\\', '/')).Distinct()) EnsureFolder(f);
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var td in d.tiles)
            {
                if ((i++ & 63) == 0) EditorUtility.DisplayProgressBar("Level 01 builder", "Tiles " + i + "/" + d.tiles.Length, 0.45f + 0.25f * i / d.tiles.Length);
                var sp = FindSprite(td.tex, td.sprite);
                if (sp == null) { missing++; Debug.LogWarning("[L01] sprite missing: " + td.tex + " / " + td.sprite); continue; }
                var tile = AssetDatabase.LoadAssetAtPath<Tile>(td.asset);
                bool create = tile == null;
                if (create) tile = ScriptableObject.CreateInstance<Tile>();
                tile.sprite = sp;
                tile.color = Color.white;
                tile.flags = TileFlags.LockColor;
                tile.colliderType = td.col == 1 ? Tile.ColliderType.Grid : Tile.ColliderType.None;
                if (create) AssetDatabase.CreateAsset(tile, td.asset); else EditorUtility.SetDirty(tile);
                result[td.id] = tile;
            }
        }
        finally { AssetDatabase.StopAssetEditing(); }
        AssetDatabase.SaveAssets();
        if (missing > 0) Debug.LogWarning("[L01] " + missing + " sprites were not found (see warnings above).");
        return LoadTiles(d);
    }

    static Dictionary<string, TileBase> LoadTiles(LevelData d)
    {
        var result = new Dictionary<string, TileBase>();
        foreach (var td in d.tiles)
        {
            var t = AssetDatabase.LoadAssetAtPath<TileBase>(td.asset);
            if (t != null) result[td.id] = t;
        }
        return result;
    }

    // ------------------------------------------------------------------ 3. palettes
    static void BuildPalettes(LevelData d, Dictionary<string, TileBase> tiles)
    {
        EnsureFolder(PaletteFolder);
        foreach (var p in d.palettes)
        {
            string path = PaletteFolder + "/" + p.name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) AssetDatabase.DeleteAsset(path);
            var go = GridPaletteUtility.CreateNewPalette(PaletteFolder, p.name, GridLayout.CellLayout.Rectangle,
                GridPalette.CellSizing.Manual, Vector3.one, GridLayout.CellSwizzle.XYZ);
            if (go == null) { Debug.LogWarning("[L01] could not create palette " + p.name); continue; }
            var tm = go.GetComponentInChildren<Tilemap>();
            foreach (var c in p.cells)
                if (tiles.TryGetValue(c.t, out var tb)) tm.SetTile(new Vector3Int(c.x, c.y, 0), tb);
            tm.CompressBounds();
            EditorUtility.SetDirty(tm);
            PrefabUtility.SavePrefabAsset(go);
        }
        AssetDatabase.SaveAssets();
    }

    // ------------------------------------------------------------------ 4. sorting
    static void SetupSorting()
    {
        var tagAsset = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset").FirstOrDefault();
        if (tagAsset != null)
        {
            var so = new SerializedObject(tagAsset);
            var arr = so.FindProperty("m_SortingLayers");
            var list = new List<(string name, long id)>();
            for (int i = 0; i < arr.arraySize; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                list.Add((e.FindPropertyRelative("name").stringValue, e.FindPropertyRelative("uniqueID").longValue));
            }
            var rnd = new System.Random();
            long IdOf(string n)
            {
                var f = list.FirstOrDefault(l => l.name == n);
                if (f.name != null) return f.id;
                long id; do { id = rnd.Next(1, int.MaxValue); } while (list.Any(l => l.id == id));
                return id;
            }
            var floor = ("Floor", IdOf("Floor")); var walls = ("Walls", IdOf("Walls")); var over = ("Overhead", IdOf("Overhead"));
            list.RemoveAll(l => l.name == "Floor" || l.name == "Walls" || l.name == "Overhead");
            int di = list.FindIndex(l => l.name == "Default"); if (di < 0) di = 0;
            list.Insert(di, walls); list.Insert(di, floor);
            di = list.FindIndex(l => l.name == "Default");
            list.Insert(di + 1, over);
            arr.arraySize = list.Count;
            for (int i = 0; i < list.Count; i++)
            {
                var e = arr.GetArrayElementAtIndex(i);
                e.FindPropertyRelative("name").stringValue = list[i].name;
                e.FindPropertyRelative("uniqueID").longValue = list[i].id;
                var lk = e.FindPropertyRelative("locked"); if (lk != null) lk.boolValue = false;
            }
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }
        var r2d = AssetDatabase.LoadMainAssetAtPath(RendererPath);
        if (r2d != null)
        {
            var so = new SerializedObject(r2d);
            var mode = so.FindProperty("m_TransparencySortMode");
            var axis = so.FindProperty("m_TransparencySortAxis");
            if (mode != null) mode.intValue = (int)TransparencySortMode.CustomAxis;
            if (axis != null) axis.vector3Value = new Vector3(0f, 1f, 0f);
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(r2d);
            AssetDatabase.SaveAssets();
        }
        else Debug.LogWarning("[L01] " + RendererPath + " not found: set Transparency Sort Mode = Custom Axis (0,1,0) on your 2D renderer by hand.");
    }

    // ------------------------------------------------------------------ 5. scene
    static void BuildScene(LevelData d, Dictionary<string, TileBase> tiles)
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        foreach (var root in scene.GetRootGameObjects())
            if (root.name == GridName || root.name == "L01_ROOMS" || root.name == "L01_GATES_TODO" || root.name == "L01_PlayerStart")
                UnityEngine.Object.DestroyImmediate(root);

        var gridGo = new GameObject(GridName, typeof(Grid));
        gridGo.GetComponent<Grid>().cellSize = Vector3.one;
        var flip = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(-1f, 1f, 1f));
        int missing = 0;
        foreach (var l in d.layers)
        {
            var go = new GameObject(l.name, typeof(Tilemap), typeof(TilemapRenderer));
            go.transform.SetParent(gridGo.transform, false);
            var tm = go.GetComponent<Tilemap>();
            var tr = go.GetComponent<TilemapRenderer>();
            tr.sortingLayerName = l.sortingLayer;
            tr.sortingOrder = l.order;
            tr.mode = l.individual == 1 ? TilemapRenderer.Mode.Individual : TilemapRenderer.Mode.Chunk;
            tr.detectChunkCullingBounds = TilemapRenderer.DetectChunkCullingBounds.Auto;
            tr.enabled = l.renderer == 1;

            var pos = new List<Vector3Int>(); var tbs = new List<TileBase>();
            foreach (var c in l.cells)
            {
                if (!tiles.TryGetValue(c.t, out var tb)) { missing++; continue; }
                pos.Add(new Vector3Int(c.x, c.y, 0)); tbs.Add(tb);
            }
            tm.SetTiles(pos.ToArray(), tbs.ToArray());
            foreach (var c in l.cells) if (c.f == 1) tm.SetTransformMatrix(new Vector3Int(c.x, c.y, 0), flip);

            if (l.collider > 0)
            {
                var rb = go.AddComponent<Rigidbody2D>();
                rb.bodyType = RigidbodyType2D.Static;
                var tc = go.AddComponent<TilemapCollider2D>();
                if (l.collider == 1)
                {
                    var cc = go.AddComponent<CompositeCollider2D>();
                    cc.geometryType = CompositeCollider2D.GeometryType.Polygons;
#if UNITY_2023_1_OR_NEWER
                    tc.compositeOperation = Collider2D.CompositeOperation.Merge;
#else
                    tc.usedByComposite = true;
#endif
                }
                else tc.enabled = false;   // e.g. the chasm: switch on when the dash exists
            }
        }
        if (missing > 0) Debug.LogWarning("[L01] " + missing + " cells had no tile (run 'Build everything' first).");

        // markers (empty objects with a label icon, easy to find, rename or delete)
        var groups = new Dictionary<string, GameObject>();
        foreach (var m in d.markers)
        {
            if (!groups.TryGetValue(m.group, out var g)) { g = new GameObject(m.group); groups[m.group] = g; }
            var go = new GameObject(m.name);
            go.transform.SetParent(g.transform, false);
            go.transform.position = new Vector3(m.x, m.y, 0f);
            SetIcon(go, m.icon);
        }
        var start = new GameObject("L01_PlayerStart");
        start.transform.position = new Vector3(d.playerStart.x, d.playerStart.y, 0f);
        SetIcon(start, 3);
        var player = GameObject.Find("Player");
        if (player != null) player.transform.position = start.transform.position;
        var cam = Camera.main;
        if (cam != null) cam.transform.position = new Vector3(start.transform.position.x, start.transform.position.y, cam.transform.position.z);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Selection.activeGameObject = gridGo;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    static void SetIcon(GameObject go, int idx)
    {
        var tex = EditorGUIUtility.IconContent("sv_label_" + Mathf.Clamp(idx, 0, 7)).image as Texture2D;
        if (tex != null) EditorGUIUtility.SetIconForObject(go, tex);
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        var parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
