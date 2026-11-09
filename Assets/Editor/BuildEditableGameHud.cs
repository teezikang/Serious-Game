using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[InitializeOnLoad]
public static class BuildEditableGameHud
{
    private const string GameScenePath = "Assets/Scenes/GameScene.unity";

    static BuildEditableGameHud()
    {
        EditorApplication.delayCall += BuildWhenGameSceneIsOpen;
    }

    private static void BuildWhenGameSceneIsOpen()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            SceneManager.GetActiveScene().path != GameScenePath)
            return;

        GameManager gameManager = Object.FindFirstObjectByType<GameManager>();
        if (gameManager == null)
            return;

        CreateInScene(gameManager, SceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Serious Game/Create Editable HUD in GameScene")]
    private static void CreateFromMenu()
    {
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        GameManager gameManager = Object.FindFirstObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("Could not find GameManager in " + GameScenePath);
            return;
        }

        CreateInScene(gameManager, scene);
    }

    [MenuItem("Tools/Serious Game/Apply Air Quality Bar Artwork")]
    private static void ApplyArtworkFromMenu()
    {
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        if (ApplyAQIArtwork())
        {
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
        }
    }

    public static void CreateAndSave()
    {
        Scene scene = EditorSceneManager.OpenScene(GameScenePath, OpenSceneMode.Single);
        GameManager gameManager = Object.FindFirstObjectByType<GameManager>();
        if (gameManager == null)
        {
            Debug.LogError("Could not find GameManager in " + GameScenePath);
            EditorApplication.Exit(1);
            return;
        }

        CreateInScene(gameManager, scene);
        EditorApplication.Exit(0);
    }

    private static void CreateInScene(GameManager gameManager, Scene scene)
    {
        Undo.RegisterFullObjectHierarchyUndo(gameManager.gameObject, "Create Editable Game HUD");
        bool changed = gameManager.BuildEditableHudInEditor();
        changed |= ApplyAQIArtwork();
        if (!changed)
            return;

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Created and saved the editable AQI Toon HUD in GameScene.");
    }

    private static bool ApplyAQIArtwork()
    {
        const string spritePath = "Assets/Art/AIR QUALITY BAR.png";
        Sprite artwork = AssetDatabase.LoadAllAssetsAtPath(spritePath)
            .OfType<Sprite>()
            .FirstOrDefault(sprite => sprite.name == "AIR QUALITY BAR_0");
        if (artwork == null)
        {
            Debug.LogError("Could not find the AIR QUALITY BAR sprite at " + spritePath);
            return false;
        }

        Transform hud = GameObject.Find("AQI Toon HUD")?.transform;
        if (hud == null)
        {
            Debug.LogError("Could not find AQI Toon HUD in GameScene.");
            return false;
        }

        Transform frame = hud.Find("AQI Card Outline");
        Transform card = frame != null ? frame.Find("AQI Card") : null;
        if (frame == null || card == null)
        {
            Debug.LogError("Could not find the AQI Card hierarchy.");
            return false;
        }

        Image cardImage = card.GetComponent<Image>();
        if (cardImage != null && cardImage.sprite == artwork)
            return false;

        RectTransform frameRect = frame.GetComponent<RectTransform>();
        frameRect.sizeDelta = new Vector2(430f, 175f);
        frame.GetComponent<Image>().enabled = false;

        RectTransform cardRect = card.GetComponent<RectTransform>();
        cardRect.offsetMin = Vector2.zero;
        cardRect.offsetMax = Vector2.zero;
        cardImage.sprite = artwork;
        cardImage.color = Color.white;
        cardImage.type = Image.Type.Simple;
        cardImage.preserveAspect = false;

        Transform heading = card.Find("AQI Heading");
        if (heading != null)
            heading.gameObject.SetActive(false);

        Transform value = card.Find("AQI Value");
        if (value != null)
        {
            RectTransform rect = value.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(-38f, -82f);
            rect.sizeDelta = new Vector2(112f, 38f);
            Text text = value.GetComponent<Text>();
            text.fontSize = 29;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.08f, 0.29f, 0.19f);
        }

        Transform barOutline = card.Find("AQI Bar Outline");
        if (barOutline != null)
        {
            RectTransform rect = barOutline.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(174f, -119f);
            rect.sizeDelta = new Vector2(210f, 18f);
        }

        Transform category = card.Find("AQI Category");
        if (category != null)
        {
            RectTransform rect = category.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(69f, -143f);
            rect.sizeDelta = new Vector2(220f, 24f);
            Text text = category.GetComponent<Text>();
            text.fontSize = 16;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = new Color(0.08f, 0.29f, 0.19f);
        }

        Transform statusFrame = hud.Find("Player Status Outline");
        if (statusFrame != null)
        {
            RectTransform rect = statusFrame.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(480f, -28f);
        }

        EditorUtility.SetDirty(frame.gameObject);
        EditorUtility.SetDirty(card.gameObject);
        EditorUtility.SetDirty(hud.gameObject);
        return true;
    }
}
