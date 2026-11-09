using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>Provides a simple take-off/landing control and flight animation for the purchased helicopter.</summary>
public class HelicopterFlightController : MonoBehaviour
{
    [Header("Flight")]
    [SerializeField, Min(0f)] private float flightHeight = 2f;
    [SerializeField, Min(0.05f)] private float liftSmoothTime = 1.1f;
    [SerializeField, Min(0f)] private float hoverAmplitude = 0.08f;
    [SerializeField, Min(0.1f)] private float hoverFrequency = 1.6f;
    [SerializeField, Min(0f)] private float flightTiltAngle = 2.5f;

    [Header("Rotor Animation")]
    [SerializeField, Min(0f)] private float mainRotorSpeed = 900f;
    [SerializeField, Min(0f)] private float tailRotorSpeed = 1100f;

    [Header("Ground Position Marker")]
    [SerializeField, Min(0.5f)] private float groundMarkerDiameter = 5f;
    [SerializeField, Min(0f)] private float groundMarkerHeightOffset = 0.04f;

    private Transform mainRotor;
    private Transform tailRotor;
    private PlayerController playerController;
    private Canvas promptCanvas;
    private TextMeshProUGUI promptText;
    private TextMeshProUGUI controlsText;
    private Vector3 baseLocalPosition;
    private Quaternion baseLocalRotation;
    private GameObject groundMarker;
    private Material generatedGroundMarkerMaterial;
    private float verticalVelocity;
    private bool isFlying;
    private bool flightPromptDismissed;
    private bool hasObservedGameplayStart;
    private bool cloudLabAltitudeOverride;
    private bool hasSavedCloudLabAltitude;
    private bool seedingReturnAtSurface;
    private float cloudLabTargetLocalY;
    private float cloudLabPlayerRootTargetY;

    public bool IsFlying => isFlying;
    public bool IsRaisedForCloudLab => cloudLabAltitudeOverride;

    private void Start()
    {
        baseLocalPosition = transform.localPosition;
        baseLocalRotation = transform.localRotation;
        FindRotors();

        playerController = GetComponentInParent<PlayerController>();
        if (playerController != null)
            playerController.SetHelicopterFlightControlEnabled(true);

        CreateGroundMarker();
        CreateCloudContactSensor();
        CreateFlightPrompt();
        RefreshFlightPrompt();
    }

    private void OnDestroy()
    {
        if (playerController != null)
            playerController.SetHelicopterFlightControlEnabled(false);

        if (promptCanvas != null)
            Destroy(promptCanvas.gameObject);

        if (groundMarker != null)
            Destroy(groundMarker);

        if (generatedGroundMarkerMaterial != null)
            Destroy(generatedGroundMarkerMaterial);
    }

    private void Update()
    {
        if (GameManager.Instance != null && !GameManager.GameplayStarted)
        {
            if (promptCanvas != null && promptCanvas.gameObject.activeSelf)
                promptCanvas.gameObject.SetActive(false);
            return;
        }

        if (!hasObservedGameplayStart)
        {
            hasObservedGameplayStart = true;
            RefreshFlightPrompt();
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.aKey.wasPressedThisFrame)
        {
            if (cloudLabAltitudeOverride)
            {
                LowerFromCloudLab();
                CloudSeedingEventManager.OnHelicopterLoweredFromLab();
                RefreshFlightPrompt();
                AnimateFlight();
                AnimateRotors();
                UpdateGroundMarker();
                return;
            }

            if (seedingReturnAtSurface && CloudSeedingEventManager.SeedsHaveBeenPurchased)
            {
                RaiseForCloudSeeding();
                CloudSeedingEventManager.OnHelicopterRaisedForSeeding();
                RefreshFlightPrompt();
                AnimateFlight();
                AnimateRotors();
                UpdateGroundMarker();
                return;
            }

            bool wasFlying = isFlying;
            isFlying = !isFlying;
            if (!wasFlying && isFlying)
            {
                ForestFireEventManager.BeginOnFirstHelicopterFlight();
                flightPromptDismissed = true;
                if (promptCanvas != null)
                    promptCanvas.gameObject.SetActive(false);
            }

            else
                RefreshFlightPrompt();
        }

        AnimateFlight();
        AnimateRotors();
        UpdateGroundMarker();
    }

    public void RaiseForCloudLab(float worldHeight)
    {
        RaiseForCloudLab(worldHeight, worldHeight);
    }

    public void RaiseForCloudLab(float helicopterWorldHeight, float platformTopWorldY)
    {
        if (playerController == null)
            playerController = GetComponentInParent<PlayerController>();

        float playerRootTargetY = transform.parent != null ? transform.parent.position.y : 0f;
        if (playerController != null)
            playerRootTargetY = playerController.BeginHelicopterLabLift(platformTopWorldY);

        cloudLabPlayerRootTargetY = playerRootTargetY;

        if (transform.parent != null)
        {
            Transform parent = transform.parent;
            Vector3 desiredWorldPosition = new Vector3(transform.position.x,
                helicopterWorldHeight, transform.position.z);
            Vector3 localTarget = parent.InverseTransformPoint(desiredWorldPosition);
            float parentWorldDeltaY = playerRootTargetY - parent.position.y;
            Vector3 parentLocalDelta = parent.InverseTransformVector(Vector3.up * parentWorldDeltaY);
            cloudLabTargetLocalY = localTarget.y - parentLocalDelta.y;
        }
        else
            cloudLabTargetLocalY = helicopterWorldHeight;

        cloudLabAltitudeOverride = true;
        hasSavedCloudLabAltitude = true;
        seedingReturnAtSurface = false;
        isFlying = true;
    }

    private void RaiseForCloudSeeding()
    {
        if (!hasSavedCloudLabAltitude)
            return;

        if (playerController == null)
            playerController = GetComponentInParent<PlayerController>();
        if (playerController != null)
            playerController.BeginHelicopterLabLiftToWorldY(cloudLabPlayerRootTargetY);

        cloudLabAltitudeOverride = true;
        seedingReturnAtSurface = false;
        verticalVelocity = 0f;
        isFlying = true;
    }

    /// <summary>Starts a return to normal flight height when the helicopter leaves the Lab unpurchased.</summary>
    public void ReturnFromCloudLab()
    {
        if (!cloudLabAltitudeOverride)
            return;

        LowerFromCloudLab();
    }

    private void LowerFromCloudLab()
    {
        cloudLabAltitudeOverride = false;
        isFlying = true;
        verticalVelocity = 0f;
        seedingReturnAtSurface = CloudSeedingEventManager.SeedsHaveBeenPurchased;
        if (playerController == null)
            playerController = GetComponentInParent<PlayerController>();
        playerController?.ReturnFromHelicopterLab();
    }

    private void CreateCloudContactSensor()
    {
        Transform existing = transform.Find("Cloud Seeding Contact Sensor");
        if (existing != null)
            return;

        GameObject sensor = new GameObject("Cloud Seeding Contact Sensor");
        sensor.transform.SetParent(transform, false);
        sensor.transform.localPosition = new Vector3(0f, 0.2f, 0f);
        SphereCollider collider = sensor.AddComponent<SphereCollider>();
        collider.radius = 1.5f;
        collider.isTrigger = true;
    }

    private void CreateGroundMarker()
    {
        groundMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        groundMarker.name = "Helicopter Ground Position Marker";
        groundMarker.transform.localScale = new Vector3(groundMarkerDiameter, 0.012f,
            groundMarkerDiameter);

        Collider markerCollider = groundMarker.GetComponent<Collider>();
        if (markerCollider != null)
            markerCollider.enabled = false;

        Renderer markerRenderer = groundMarker.GetComponent<Renderer>();
        if (markerRenderer != null)
        {
            Material markerMaterial = Resources.Load<Material>("Materials/Ink_192629FF");
            if (markerMaterial == null)
            {
                Shader toonShader = Shader.Find("ArcadeRecycler/Toon");
                if (toonShader != null)
                {
                    generatedGroundMarkerMaterial = new Material(toonShader);
                    if (generatedGroundMarkerMaterial.HasProperty("_BaseColor"))
                        generatedGroundMarkerMaterial.SetColor("_BaseColor", new Color(0.08f, 0.1f, 0.1f));
                    markerMaterial = generatedGroundMarkerMaterial;
                }
            }

            if (markerMaterial != null)
                markerRenderer.sharedMaterial = markerMaterial;
            markerRenderer.shadowCastingMode = ShadowCastingMode.Off;
            markerRenderer.receiveShadows = false;
        }

        groundMarker.SetActive(false);
    }

    private void UpdateGroundMarker()
    {
        if (groundMarker == null)
            return;

        if (!isFlying)
        {
            groundMarker.SetActive(false);
            return;
        }

        Vector3 helicopterPosition = transform.position;
        float groundY = FindGroundHeight(helicopterPosition);
        groundMarker.transform.position = new Vector3(helicopterPosition.x,
            groundY + groundMarkerHeightOffset, helicopterPosition.z);
        groundMarker.SetActive(true);
    }

    private static float FindGroundHeight(Vector3 position)
    {
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 100f, Vector3.down,
            200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        float lowestHit = float.PositiveInfinity;
        for (int i = 0; i < hits.Length; i++)
        {
            if (hits[i].collider != null && hits[i].collider.CompareTag("Floor"))
                return hits[i].point.y;

            if (hits[i].point.y < lowestHit)
                lowestHit = hits[i].point.y;
        }

        return float.IsInfinity(lowestHit) ? 0f : lowestHit;
    }

    private void FindRotors()
    {
        Transform[] allTransforms = GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            string childName = allTransforms[i].name.ToLowerInvariant();
            if (!childName.Contains("rotor"))
                continue;

            if (childName.Contains("tail"))
                tailRotor = allTransforms[i];
            else if (mainRotor == null)
                mainRotor = allTransforms[i];
        }
    }

    private void AnimateFlight()
    {
        float hover = isFlying
            ? Mathf.Sin(Time.time * hoverFrequency * Mathf.PI * 2f) * hoverAmplitude
            : 0f;
        float targetY = (cloudLabAltitudeOverride
            ? cloudLabTargetLocalY
            : baseLocalPosition.y + (isFlying ? flightHeight : 0f)) + hover;

        Vector3 localPosition = transform.localPosition;
        localPosition.y = Mathf.SmoothDamp(
            localPosition.y,
            targetY,
            ref verticalVelocity,
            liftSmoothTime);
        transform.localPosition = localPosition;

        float tilt = isFlying
            ? Mathf.Sin(Time.time * hoverFrequency * Mathf.PI * 2f) * flightTiltAngle
            : 0f;
        transform.localRotation = baseLocalRotation * Quaternion.Euler(tilt, 0f, tilt * 0.5f);
    }

    private void AnimateRotors()
    {
        if (mainRotor != null)
            mainRotor.Rotate(Vector3.forward, mainRotorSpeed * Time.deltaTime, Space.Self);

        if (tailRotor != null)
            tailRotor.Rotate(Vector3.right, tailRotorSpeed * Time.deltaTime, Space.Self);
    }

    private void CreateFlightPrompt()
    {
        GameObject canvasObject = new GameObject(
            "Helicopter Flight Prompt",
            typeof(RectTransform),
            typeof(Canvas),
            typeof(CanvasScaler));
        promptCanvas = canvasObject.GetComponent<Canvas>();
        promptCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        promptCanvas.sortingOrder = 120;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
        GameObject panelObject = new GameObject("Toon Flight Panel", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(canvasRect, false);
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.anchoredPosition = new Vector2(0f, -68f);
        panelRect.sizeDelta = new Vector2(650f, 56f);

        Image panel = panelObject.GetComponent<Image>();
        panel.color = new Color(0.97f, 0.95f, 0.87f, 0.97f);
        panel.raycastTarget = false;
        Outline outline = panelObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.15f, 0.35f, 0.19f, 1f);
        outline.effectDistance = new Vector2(3f, -3f);
        outline.useGraphicAlpha = true;

        promptText = CreatePromptText(panelRect, "Flight Action", 23f,
            new Vector2(0f, -4f), new Vector2(620f, 28f));
        promptText.color = new Color(0.12f, 0.28f, 0.17f, 1f);
        promptText.fontStyle = FontStyles.Bold;

        controlsText = CreatePromptText(panelRect, "Flight Controls", 14f,
            new Vector2(0f, -31f), new Vector2(620f, 20f));
        controlsText.color = new Color(0.32f, 0.29f, 0.22f, 1f);
    }

    private TextMeshProUGUI CreatePromptText(Transform parent, string objectName, float fontSize,
        Vector2 anchoredPosition, Vector2 size)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(parent, false);
        textRect.anchorMin = new Vector2(0.5f, 1f);
        textRect.anchorMax = new Vector2(0.5f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = anchoredPosition;
        textRect.sizeDelta = size;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.fontSizeMin = fontSize * 0.75f;
        text.fontSizeMax = fontSize;
        text.enableAutoSizing = true;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    private void RefreshFlightPrompt()
    {
        if (flightPromptDismissed || promptText == null || controlsText == null)
            return;

        promptText.text = isFlying ? "[A]  LAND HELICOPTER" : "[A]  FLY HELICOPTER";
        controlsText.text = "Use Arrow Keys to steer; A toggles flight";
    }
}
