#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class ArcadeToonSceneTools
{
    private const string ToonShaderName = "ArcadeRecycler/Toon";
    private const string OutputFolder = "Assets/Art/ToonMaterials";
    private const string DressingRootName = "Toon Street Dressing";
    private const float OutlineWidthPixels = 1.35f;

    [MenuItem("Tools/Art Style/Apply Black-Line Toon to Selected Object")]
    private static void ApplyBlackLineToSelectedObject()
    {
        GameObject[] selectedObjects = Selection.gameObjects;
        Shader toonShader = Shader.Find(ToonShaderName);
        if (selectedObjects == null || selectedObjects.Length == 0 || toonShader == null)
        {
            Debug.LogWarning("Select one or more route objects in the Hierarchy before applying the toon style.");
            return;
        }

        EnsureOutputFolder();
        var materialCache = new Dictionary<Material, Material>();
        int styledMaterialCount = 0;
        int styledRendererCount = 0;
        foreach (GameObject selected in selectedObjects)
        {
            foreach (Renderer renderer in selected.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                    continue;
                if (!ShouldStyleRenderer(renderer))
                    continue;

                if (ApplyToonToRenderer(renderer, toonShader, materialCache, true, ref styledMaterialCount))
                    styledRendererCount++;
            }
        }

        if (styledRendererCount == 0)
        {
            Debug.LogWarning("No selected opaque mesh renderers needed the toon style.");
            return;
        }

        AssetDatabase.SaveAssets();
        Scene scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        if (!string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Debug.Log($"Applied black-line toon materials to {styledRendererCount} renderer(s) across {selectedObjects.Length} selected object(s).");
    }

    [MenuItem("Tools/Art Style/Apply Black-Line Toon + Street Dressing")]
    private static void ApplyBlackLineToonAndDressing()
    {
        Scene scene = SceneManager.GetActiveScene();
        Shader toonShader = Shader.Find(ToonShaderName);

        if (!scene.IsValid() || toonShader == null)
        {
            Debug.LogError("Could not find the active scene or ArcadeRecycler/Toon shader.");
            return;
        }

        EnsureOutputFolder();
        var materialCache = new Dictionary<Material, Material>();
        var renderers = GetSceneRenderers(scene).ToArray();
        int styledRendererCount = 0;
        int styledMaterialCount = 0;

        foreach (Renderer renderer in renderers)
        {
            if (!ShouldStyleRenderer(renderer))
                continue;

            if (ApplyToonToRenderer(renderer, toonShader, materialCache, true, ref styledMaterialCount))
            {
                styledRendererCount++;
            }
        }

        StyleReferencedPrefabs(scene, toonShader, materialCache, ref styledRendererCount, ref styledMaterialCount);
        AddStreetDressing(scene, toonShader, renderers);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();

        if (!string.IsNullOrEmpty(scene.path))
            EditorSceneManager.SaveScene(scene);

        Debug.Log($"Applied black-line toon materials to {styledRendererCount} renderers ({styledMaterialCount} materials) in '{scene.name}'. Added perimeter trees and recycling bins if they were not already present.");
    }

    [MenuItem("Tools/Art Style/Arrange Play Areas for Clear Routes")]
    private static void ArrangePlayAreasForClearRoutes()
    {
        const string playgroundPath = "Assets/Models/PlayGround.prefab";
        GameObject root = PrefabUtility.LoadPrefabContents(playgroundPath);
        try
        {
            SetZonePosition(root, "Transport Center", new Vector3(-0.24f, 1.08f, 0.02f));
            SetZonePosition(root, "Garbage Area", new Vector3(0f, 0.45f, 0.17f));
            SetZonePosition(root, "Recycle Center", new Vector3(0.24f, 1.08f, -0.02f));
            PrefabUtility.SaveAsPrefabAsset(root, playgroundPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        Scene activeScene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(activeScene);
        EditorSceneManager.SaveScene(activeScene);
        Debug.Log("Spread the transport, garbage, and recycling areas into a clear three-stop route while preserving the central play lane.");
    }

    [MenuItem("Tools/Art Style/Replace Street Trees With Random Prefabs")]
    private static void ReplaceStreetTreesWithRandomPrefabs()
    {
        const string playgroundPath = "Assets/Models/PlayGround.prefab";
        string[] treePaths =
        {
            "Assets/Loading Games/Toon City Pack/Prefabs/Vegetation/tree-type-A.prefab",
            "Assets/Loading Games/Toon City Pack/Prefabs/Vegetation/tree-type-B.prefab",
            "Assets/Loading Games/Toon City Pack/Prefabs/Vegetation/tree-type-C.prefab",
            "Assets/Loading Games/Toon City Pack/Prefabs/Vegetation/tree-type-D.prefab"
        };

        GameObject[] treePrefabs = treePaths
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(prefab => prefab != null)
            .ToArray();
        if (treePrefabs.Length == 0)
        {
            Debug.LogError("Could not find any tree prefabs in Toon City Pack/Prefabs/Vegetation.");
            return;
        }

        GameObject root = PrefabUtility.LoadPrefabContents(playgroundPath);
        try
        {
            Transform[] streetTrees = root.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.name.StartsWith("Street Tree", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (streetTrees.Length == 0)
            {
                Debug.LogWarning("No Street Tree objects were found in the PlayGround prefab.");
                return;
            }

            var random = new System.Random(20261022);
            int previousTreeIndex = -1;
            int replacedCount = 0;
            for (int i = 0; i < streetTrees.Length; i++)
            {
                Transform oldTree = streetTrees[i];
                if (oldTree == null || !TryGetRendererBounds(oldTree.gameObject, out Bounds oldBounds))
                {
                    Debug.LogWarning($"Skipped '{oldTree?.name}' because its render bounds could not be measured.");
                    continue;
                }

                int treeIndex = random.Next(treePrefabs.Length);
                if (treePrefabs.Length > 1 && treeIndex == previousTreeIndex)
                    treeIndex = (treeIndex + 1 + random.Next(treePrefabs.Length - 1)) % treePrefabs.Length;
                previousTreeIndex = treeIndex;

                GameObject replacement = PrefabUtility.InstantiatePrefab(treePrefabs[treeIndex], root.scene) as GameObject;
                if (replacement == null)
                    continue;

                Transform replacementTransform = replacement.transform;
                replacementTransform.SetParent(oldTree.parent, false);
                replacementTransform.localPosition = oldTree.localPosition;
                replacementTransform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                replacementTransform.localScale = Vector3.one;

                if (!TryGetRendererBounds(replacement, out Bounds prefabBounds) || prefabBounds.size.y <= 0.001f)
                {
                    UnityEngine.Object.DestroyImmediate(replacement);
                    Debug.LogWarning($"Skipped '{oldTree.name}' because '{treePrefabs[treeIndex].name}' has no measurable render bounds.");
                    continue;
                }

                float sizeVariation = Mathf.Lerp(0.9f, 1.1f, (float)random.NextDouble());
                float uniformScale = oldBounds.size.y / prefabBounds.size.y * sizeVariation;
                replacementTransform.localScale = Vector3.one * uniformScale;
                TryGetRendererBounds(replacement, out Bounds replacementBounds);
                replacementTransform.position += new Vector3(
                    oldBounds.center.x - replacementBounds.center.x,
                    oldBounds.min.y - replacementBounds.min.y,
                    oldBounds.center.z - replacementBounds.center.z);

                replacement.name = $"Street Tree {treePrefabs[treeIndex].name} {i + 1:00}";
                replacement.SetActive(oldTree.gameObject.activeSelf);
                UnityEngine.Object.DestroyImmediate(oldTree.gameObject);
                replacedCount++;
            }

            PrefabUtility.SaveAsPrefabAsset(root, playgroundPath);
            Debug.Log($"Replaced {replacedCount} Street Tree object(s) with varied Toon City Pack tree prefabs in '{playgroundPath}'.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool TryGetRendererBounds(GameObject root, out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds && bounds.size.sqrMagnitude > 0.000001f;
    }

    private static void SetZonePosition(GameObject root, string objectName, Vector3 position)
    {
        Transform zone = root.GetComponentsInChildren<Transform>(true)
            .FirstOrDefault(candidate => candidate.name.Equals(objectName, StringComparison.Ordinal));
        if (zone == null)
        {
            Debug.LogWarning($"Could not find '{objectName}' in the PlayGround prefab.");
            return;
        }

        zone.localPosition = position;
    }

    private static IEnumerable<Renderer> GetSceneRenderers(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer is MeshRenderer || renderer is SkinnedMeshRenderer)
                    yield return renderer;
            }
        }
    }

    private static bool ShouldStyleRenderer(Renderer renderer)
    {
        if (renderer == null || renderer.GetComponentInParent<Canvas>() != null)
            return false;

        if (renderer.gameObject.name.Equals("Ground", StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (Material material in renderer.sharedMaterials)
        {
            if (material == null || material.shader == null)
                continue;

            if (material.shader.name.IndexOf("Isometric Ground Grid", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;
        }

        return true;
    }

    private static bool ApplyToonToRenderer(Renderer renderer, Shader toonShader,
        Dictionary<Material, Material> materialCache, bool recordSceneUndo, ref int styledMaterialCount)
    {
        Material[] sourceMaterials = renderer.sharedMaterials;
        Material[] styledMaterials = new Material[sourceMaterials.Length];
        bool changed = false;

        for (int i = 0; i < sourceMaterials.Length; i++)
        {
            Material source = sourceMaterials[i];
            if (source == null || IsTransparent(source))
            {
                styledMaterials[i] = source;
                continue;
            }

            if (!materialCache.TryGetValue(source, out Material styled))
            {
                styled = CreateOrUpdateToonMaterial(source, toonShader);
                materialCache[source] = styled;
                styledMaterialCount++;
            }

            styledMaterials[i] = styled;
            changed |= styled != source;
        }

        if (!changed)
            return false;

        if (recordSceneUndo)
            Undo.RecordObject(renderer, "Apply Black-Line Toon Style");

        renderer.sharedMaterials = styledMaterials;
        if (recordSceneUndo)
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);

        EditorUtility.SetDirty(renderer);
        return true;
    }

    private static void StyleReferencedPrefabs(Scene scene, Shader toonShader,
        Dictionary<Material, Material> materialCache, ref int styledRendererCount, ref int styledMaterialCount)
    {
        var prefabPaths = new HashSet<string>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null)
                    continue;

                var serialized = new SerializedObject(behaviour);
                SerializedProperty property = serialized.GetIterator();
                while (property.Next(true))
                {
                    if (property.propertyType != SerializedPropertyType.ObjectReference)
                        continue;

                    UnityEngine.Object reference = property.objectReferenceValue;
                    if (!(reference is GameObject) && !(reference is Component))
                        continue;

                    string path = AssetDatabase.GetAssetPath(reference);
                    if (path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        prefabPaths.Add(path);
                }
            }
        }

        foreach (string path in prefabPaths)
        {
            GameObject prefabContents = null;
            try
            {
                prefabContents = PrefabUtility.LoadPrefabContents(path);
                if (prefabContents == null)
                    continue;

                bool prefabChanged = false;
                foreach (Renderer renderer in prefabContents.GetComponentsInChildren<Renderer>(true))
                {
                    if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
                        continue;
                    if (!ShouldStyleRenderer(renderer))
                        continue;

                    if (ApplyToonToRenderer(renderer, toonShader, materialCache, false, ref styledMaterialCount))
                    {
                        prefabChanged = true;
                        styledRendererCount++;
                    }
                }

                if (prefabChanged)
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, path);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"Could not apply toon materials to prefab '{path}': {exception.Message}");
            }
            finally
            {
                if (prefabContents != null)
                    PrefabUtility.UnloadPrefabContents(prefabContents);
            }
        }
    }

    private static bool IsTransparent(Material material)
    {
        string renderType = material.GetTag("RenderType", false, string.Empty);
        return renderType.Equals("Transparent", StringComparison.OrdinalIgnoreCase)
            || material.renderQueue >= (int)UnityEngine.Rendering.RenderQueue.Transparent;
    }

    private static Material CreateOrUpdateToonMaterial(Material source, Shader toonShader)
    {
        string stableId = "scene";
        if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
            stableId = $"{guid.Substring(0, Math.Min(8, guid.Length))}_{localId}";
        else
            stableId = $"instance_{source.GetInstanceID()}";

        string safeName = Regex.Replace(source.name, "[^A-Za-z0-9_-]+", "_");
        string path = $"{OutputFolder}/{safeName}_{stableId}_Toon.mat";
        Material toonMaterial = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (toonMaterial == null)
        {
            toonMaterial = new Material(toonShader);
            toonMaterial.name = $"{safeName}_{stableId}_Toon";
            AssetDatabase.CreateAsset(toonMaterial, path);
        }
        else
        {
            toonMaterial.shader = toonShader;
        }

        Texture baseMap = null;
        Vector2 textureScale = Vector2.one;
        Vector2 textureOffset = Vector2.zero;

        if (source.HasProperty("_BaseMap"))
        {
            baseMap = source.GetTexture("_BaseMap");
            textureScale = source.GetTextureScale("_BaseMap");
            textureOffset = source.GetTextureOffset("_BaseMap");
        }

        if (baseMap == null && source.HasProperty("_MainTex"))
        {
            baseMap = source.GetTexture("_MainTex");
            textureScale = source.GetTextureScale("_MainTex");
            textureOffset = source.GetTextureOffset("_MainTex");
        }

        Color baseColor = Color.white;
        if (source.HasProperty("_BaseColor"))
            baseColor = source.GetColor("_BaseColor");
        else if (source.HasProperty("_Color"))
            baseColor = source.GetColor("_Color");

        toonMaterial.SetTexture("_BaseMap", baseMap != null ? baseMap : Texture2D.whiteTexture);
        toonMaterial.SetTextureScale("_BaseMap", textureScale);
        toonMaterial.SetTextureOffset("_BaseMap", textureOffset);
        toonMaterial.SetColor("_BaseColor", baseColor);
        toonMaterial.SetColor("_OutlineColor", Color.black);
        float outlineWidth = source.shader == toonShader && source.HasProperty("_OutlineWidth")
            ? source.GetFloat("_OutlineWidth")
            : OutlineWidthPixels;
        toonMaterial.SetFloat("_OutlineWidth", outlineWidth);
        EditorUtility.SetDirty(toonMaterial);
        return toonMaterial;
    }

    private static void AddStreetDressing(Scene scene, Shader toonShader, IEnumerable<Renderer> sceneRenderers)
    {
        if (GameObject.Find(DressingRootName) != null)
            return;

        Bounds cityBounds = default;
        bool hasBounds = false;
        float groundY = 0f;

        foreach (Renderer renderer in sceneRenderers)
        {
            if (renderer == null || !ShouldUseForCityBounds(renderer))
                continue;

            if (!hasBounds)
            {
                cityBounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                cityBounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds || cityBounds.size.x < 3f || cityBounds.size.z < 3f)
        {
            Debug.LogWarning("Street dressing was skipped because the scene's city bounds could not be determined.");
            return;
        }

        GameObject dressing = new GameObject(DressingRootName);
        Undo.RegisterCreatedObjectUndo(dressing, "Add Toon Street Dressing");
        SceneManager.MoveGameObjectToScene(dressing, scene);

        Material planterMaterial = CreateOrUpdateColorMaterial("Scene_Planter_Toon", ParseColor("C2B89E"), toonShader);
        Material barkMaterial = CreateOrUpdateColorMaterial("Scene_TreeBark_Toon", ParseColor("7A3B30"), toonShader);
        Material leafDarkMaterial = CreateOrUpdateColorMaterial("Scene_LeafDark_Toon", ParseColor("387342"), toonShader);
        Material leafLightMaterial = CreateOrUpdateColorMaterial("Scene_LeafLight_Toon", ParseColor("579C52"), toonShader);
        Material tealBinMaterial = CreateOrUpdateColorMaterial("Scene_RecycleTeal_Toon", ParseColor("19A891"), toonShader);
        Material yellowBinMaterial = CreateOrUpdateColorMaterial("Scene_TruckYellow_Toon", ParseColor("F7B029"), toonShader);
        Material binInkMaterial = CreateOrUpdateColorMaterial("Scene_BinInk_Toon", ParseColor("192629"), toonShader);

        float padding = Mathf.Max(1.6f, Mathf.Min(cityBounds.size.x, cityBounds.size.z) * 0.08f);
        Vector3[] treePositions =
        {
            new Vector3(cityBounds.min.x - padding, groundY, cityBounds.min.z - padding),
            new Vector3(cityBounds.max.x + padding, groundY, cityBounds.min.z - padding),
            new Vector3(cityBounds.min.x - padding, groundY, cityBounds.max.z + padding),
            new Vector3(cityBounds.max.x + padding, groundY, cityBounds.max.z + padding)
        };

        for (int i = 0; i < treePositions.Length; i++)
            CreateStreetTree(dressing.transform, i, treePositions[i], planterMaterial, barkMaterial, leafDarkMaterial, leafLightMaterial);

        CreateRecyclingBin(dressing.transform, "Teal Recycling Bin", tealBinMaterial, binInkMaterial,
            new Vector3(cityBounds.min.x - padding, groundY, cityBounds.center.z));
        CreateRecyclingBin(dressing.transform, "Yellow Recycling Bin", yellowBinMaterial, binInkMaterial,
            new Vector3(cityBounds.max.x + padding, groundY, cityBounds.center.z));
    }

    private static bool ShouldUseForCityBounds(Renderer renderer)
    {
        if (!ShouldStyleRenderer(renderer))
            return false;

        string name = renderer.gameObject.name;
        if (name.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("money", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;

        return renderer.bounds.size.x < 40f && renderer.bounds.size.z < 40f;
    }

    private static void CreateStreetTree(Transform parent, int index, Vector3 position, Material planterMaterial,
        Material barkMaterial, Material leafDarkMaterial, Material leafLightMaterial)
    {
        GameObject tree = new GameObject($"Street Tree {index + 1:00}");
        Undo.RegisterCreatedObjectUndo(tree, "Add Toon Street Tree");
        tree.transform.SetParent(parent, false);
        tree.transform.position = position;

        CreatePrimitive(tree.transform, PrimitiveType.Cube, "Cream Planter", new Vector3(0f, 0.18f, 0f),
            new Vector3(1.15f, 0.36f, 1.15f), planterMaterial);
        CreatePrimitive(tree.transform, PrimitiveType.Cylinder, "Warm Bark Trunk", new Vector3(0f, 1.2f, 0f),
            new Vector3(0.18f, 0.9f, 0.18f), barkMaterial);
        CreatePrimitive(tree.transform, PrimitiveType.Sphere, "Leaf Crown", new Vector3(0f, 2.15f, 0f),
            new Vector3(1.05f, 1.1f, 1.05f), index % 2 == 0 ? leafDarkMaterial : leafLightMaterial);
        CreatePrimitive(tree.transform, PrimitiveType.Sphere, "Leaf Cluster Left", new Vector3(-0.36f, 2.12f, 0.16f),
            new Vector3(0.72f, 0.78f, 0.72f), leafLightMaterial);
        CreatePrimitive(tree.transform, PrimitiveType.Sphere, "Leaf Cluster Right", new Vector3(0.38f, 2.28f, -0.12f),
            new Vector3(0.76f, 0.8f, 0.76f), leafDarkMaterial);
    }

    private static void CreateRecyclingBin(Transform parent, string name, Material bodyMaterial, Material inkMaterial, Vector3 position)
    {
        GameObject bin = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(bin, "Add Toon Recycling Bin");
        bin.transform.SetParent(parent, false);
        bin.transform.position = position;

        CreatePrimitive(bin.transform, PrimitiveType.Cube, "Bin Body", new Vector3(0f, 0.48f, 0f),
            new Vector3(0.72f, 0.82f, 0.62f), bodyMaterial);
        CreatePrimitive(bin.transform, PrimitiveType.Cube, "Bin Lid", new Vector3(0f, 0.94f, 0f),
            new Vector3(0.8f, 0.14f, 0.68f), bodyMaterial);
        CreatePrimitive(bin.transform, PrimitiveType.Cube, "Front Slot", new Vector3(0f, 0.68f, -0.314f),
            new Vector3(0.34f, 0.08f, 0.025f), inkMaterial);
    }

    private static GameObject CreatePrimitive(Transform parent, PrimitiveType type, string name, Vector3 localPosition,
        Vector3 localScale, Material material)
    {
        GameObject primitive = GameObject.CreatePrimitive(type);
        primitive.name = name;
        Undo.RegisterCreatedObjectUndo(primitive, "Add Toon Street Detail");
        primitive.transform.SetParent(parent, false);
        primitive.transform.localPosition = localPosition;
        primitive.transform.localScale = localScale;
        primitive.GetComponent<Renderer>().sharedMaterial = material;

        Collider collider = primitive.GetComponent<Collider>();
        if (collider != null)
            UnityEngine.Object.DestroyImmediate(collider);

        return primitive;
    }

    private static Material CreateOrUpdateColorMaterial(string name, Color color, Shader toonShader)
    {
        string path = $"{OutputFolder}/{name}.mat";
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(toonShader);
            material.name = name;
            AssetDatabase.CreateAsset(material, path);
        }
        else
        {
            material.shader = toonShader;
        }

        material.SetTexture("_BaseMap", null);
        material.SetColor("_BaseColor", color);
        material.SetColor("_OutlineColor", Color.black);
        material.SetFloat("_OutlineWidth", OutlineWidthPixels);
        EditorUtility.SetDirty(material);
        return material;
    }

    private static Color ParseColor(string html)
    {
        return ColorUtility.TryParseHtmlString($"#{html}", out Color color) ? color : Color.white;
    }

    private static void EnsureOutputFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Art"))
            AssetDatabase.CreateFolder("Assets", "Art");

        if (!AssetDatabase.IsValidFolder(OutputFolder))
            AssetDatabase.CreateFolder("Assets/Art", "ToonMaterials");
    }
}
#endif
