using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using TMPro;

public class TransportCenter : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string moneyTag = "Money";
    [SerializeField] private GameObject[] transportPrefab;
    private int currentTransport = 0;

    [Header("Staff Hiring Unlock")]
    [SerializeField] private StaffHireArea staffHireArea;

    [Header("Next Vehicle Preview")]
    [SerializeField] private Transform previewAnchor;
    [SerializeField] private Vector3 previewWorldOffset = new Vector3(0f, 1.75f, 0f);
    [SerializeField, Min(0f)] private float previewFloatHeight = 0.2f;
    [SerializeField, Min(0f)] private float previewFloatSpeed = 1.5f;
    [SerializeField] private float previewRotationSpeed = 35f;
    [SerializeField, Range(0.05f, 1f)] private float previewOpacity = 0.5f;

    [Header("Unlock Cost Sign")]
    [SerializeField] private Vector3 costSignOffset = new Vector3(0f, 0.9f, 0f);
    [SerializeField, Min(0.001f)] private float costSignScale = 0.011f;

    private GameObject currentPreview;
    private Transform currentPreviewPivot;
    private GameObject currentOwnedTransport;
    private Vector3 previewBaseWorldPosition;
    private Vector3 previewPrefabBaseScale;
    private Vector3 currentPreviewScaleMultiplier = Vector3.one;
    private Vector3 currentPreviewPositionOffsetWorld;
    private Canvas costSignCanvas;
    private TextMeshProUGUI costText;
    private TextMeshProUGUI progressText;
    private int lastSignCost = int.MinValue;
    private int lastSignPaid = int.MinValue;
    private int lastSignStaffGate = -1;
    private int moneyPaidTowardCurrentVehicle;
    private readonly System.Collections.Generic.List<Material> previewMaterials =
        new System.Collections.Generic.List<Material>();

    private void Start()
    {
        ResolveStaffHireArea();

        CreateCostSign();
        RefreshNextVehiclePreview();
        RefreshCostSign();
    }

    private void ResolveStaffHireArea()
    {
        // The playable pad is inside Garbage Area. The serialized reference in
        // the level can point at a second, hidden pad under Vegetation instead.
        GameObject garbageArea = GameObject.Find("Garbage Area");
        if (garbageArea != null)
        {
            StaffHireArea visibleHireArea = garbageArea.GetComponentInChildren<StaffHireArea>(true);
            if (visibleHireArea != null)
            {
                staffHireArea = visibleHireArea;
                return;
            }
        }

        if (staffHireArea == null)
            staffHireArea = FindFirstObjectByType<StaffHireArea>(FindObjectsInactive.Include);
    }

    private void LateUpdate()
    {
        UpdateCostSignTransform();
        RefreshCostSign();

        if (currentPreview == null || currentPreviewPivot == null)
            return;

        Transform anchor = previewAnchor != null ? previewAnchor : transform;
        float bob = previewFloatHeight > 0f
            ? Mathf.Sin(Time.time * previewFloatSpeed) * previewFloatHeight
            : 0f;
        currentPreviewPivot.position = anchor.position + previewWorldOffset +
            currentPreviewPositionOffsetWorld + Vector3.up * bob;
        currentPreviewPivot.Rotate(Vector3.up, previewRotationSpeed * Time.deltaTime, Space.World);
        currentPreviewPivot.localScale = currentPreviewScaleMultiplier;
    }

    private void OnDestroy()
    {
        DestroyPreviewMaterials();
        if (costSignCanvas != null)
            Destroy(costSignCanvas.gameObject);
    }

    //public void DisplayTransport()
    //{
    //    if (transportPrefab == null)
    //    {
    //        Debug.Log("There is no transport available");
    //    }
    //    ;
    //    if (transportPrefab.Length > 0)
    //    {
    //        for (int i = 0; i < transportPrefab.Length; i++)
    //        {
    //            if (transportPrefab[i] != null && transportPrefab[i].ReturnCost() != 0)
    //            {
    //                transportPrefab[i].SetAvailability(true);
    //            }
    //            return;
    //        }
    //    }
    //}

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
            return;

        if (!other.CompareTag(playerTag))
            return;

        PlayerController playerController = other.GetComponent<PlayerController>();
        if (playerController != null)
            CheckCurrentTransport(playerController.transform, playerController.transform.position);
    }

    public void CheckCurrentTransport(Transform player_, Vector3 spawnPosition)
    {
        if (player_ == null)
            return;

        if (transportPrefab == null || currentTransport >= transportPrefab.Length)
        {
            Debug.Log("There are no more vehicles available to purchase.", this);
            return;
        }

        GameObject vehiclePrefab = transportPrefab[currentTransport];
        if (vehiclePrefab == null)
        {
            Debug.LogWarning($"Vehicle prefab at index {currentTransport} is not assigned.", this);
            return;
        }

        Transport transport = vehiclePrefab.GetComponent<Transport>();
        PlayerController playerController = player_.GetComponent<PlayerController>();
        if (transport == null || playerController == null)
        {
            Debug.LogWarning("The vehicle needs a Transport component and the player needs a PlayerController component.", this);
            return;
        }

        if (transport.RequiresHiredStaff && !HasHiredStaff())
        {
            Debug.Log("Hire at least one worker before purchasing this vehicle.", this);
            RefreshCostSign();
            return;
        }

        int cost = transport.ReturnCost();
        int amountStillNeeded = Mathf.Max(0, cost - moneyPaidTowardCurrentVehicle);
        int playerMoney = playerController.CheckMoney();
        int amountToDeposit = Mathf.Min(playerMoney, amountStillNeeded);

        if (amountToDeposit > 0)
        {
            if (!playerController.TrySpendMoney(amountToDeposit))
                return;

            moneyPaidTowardCurrentVehicle += amountToDeposit;
            RefreshCostSign();
        }

        if (moneyPaidTowardCurrentVehicle < cost)
        {
            Debug.Log($"${moneyPaidTowardCurrentVehicle} paid toward {vehiclePrefab.name}; ${cost - moneyPaidTowardCurrentVehicle} still needed.", this);
            return;
        }

        GameObject purchasedTransport = Instantiate(vehiclePrefab, spawnPosition, Quaternion.identity);
        Transport purchasedTransportComponent = purchasedTransport.GetComponent<Transport>();
        if (purchasedTransportComponent != null)
            purchasedTransportComponent.AttachToPlayer(player_);

        if (currentOwnedTransport != null)
            Destroy(currentOwnedTransport);

        currentOwnedTransport = purchasedTransport;
        moneyPaidTowardCurrentVehicle = 0;
        currentTransport++;
        if (purchasedTransportComponent != null && purchasedTransportComponent.UnlocksStaffHiring)
        {
            ResolveStaffHireArea();
            if (staffHireArea != null)
                staffHireArea.Unlock();
            else
                Debug.LogWarning("Assign the pre-placed Staff Hiring Area in the Transport Center Inspector.", this);
        }
        RefreshNextVehiclePreview();
    }

    private void RefreshNextVehiclePreview()
    {
        if (currentPreview != null)
            Destroy(currentPreview);
        currentPreview = null;
        if (currentPreviewPivot != null)
            Destroy(currentPreviewPivot.gameObject);
        currentPreviewPivot = null;
        DestroyPreviewMaterials();

        if (transportPrefab == null || currentTransport >= transportPrefab.Length ||
            transportPrefab[currentTransport] == null)
        {
            RefreshCostSign();
            return;
        }

        Transform anchor = previewAnchor != null ? previewAnchor : transform;
        previewBaseWorldPosition = anchor.position + previewWorldOffset;
        currentPreview = Instantiate(transportPrefab[currentTransport], previewBaseWorldPosition, Quaternion.identity);
        currentPreview.name = $"Next Vehicle Preview - {transportPrefab[currentTransport].name}";
        currentPreview.transform.position = previewBaseWorldPosition;
        previewPrefabBaseScale = currentPreview.transform.localScale;
        Transport previewTransport = currentPreview.GetComponent<Transport>();
        currentPreviewScaleMultiplier = previewTransport != null
            ? previewTransport.PreviewScaleMultiplier
            : Vector3.one;
        currentPreviewPositionOffsetWorld = previewTransport != null
            ? currentPreview.transform.TransformVector(previewTransport.PreviewPositionOffset)
            : Vector3.zero;
        Vector3 pivotOffset = previewTransport != null
            ? currentPreview.transform.TransformVector(previewTransport.PreviewPivotOffset)
            : Vector3.zero;

        // Imported vehicle roots can have an off-center pivot. Rotate a wrapper
        // placed at the combined renderer bounds center so the preview spins in
        // place rather than orbiting around the FBX root.
        Renderer[] previewRenderers = currentPreview.GetComponentsInChildren<Renderer>(true);
        if (previewRenderers.Length > 0)
        {
            Bounds previewBounds = previewRenderers[0].bounds;
            for (int i = 1; i < previewRenderers.Length; i++)
                previewBounds.Encapsulate(previewRenderers[i].bounds);

            GameObject pivot = new GameObject($"{currentPreview.name} Pivot");
            currentPreviewPivot = pivot.transform;
            currentPreviewPivot.position = previewBounds.center + pivotOffset;
            currentPreview.transform.SetParent(currentPreviewPivot, true);
            currentPreviewPivot.localScale = currentPreviewScaleMultiplier;
        }
        else
        {
            currentPreviewPivot = currentPreview.transform;
            currentPreviewPivot.localScale = Vector3.Scale(previewPrefabBaseScale, currentPreviewScaleMultiplier);
        }

        foreach (Collider previewCollider in currentPreview.GetComponentsInChildren<Collider>(true))
            previewCollider.enabled = false;

        foreach (Rigidbody previewBody in currentPreview.GetComponentsInChildren<Rigidbody>(true))
        {
            previewBody.useGravity = false;
            previewBody.isKinematic = true;
        }

        foreach (MonoBehaviour previewBehaviour in currentPreview.GetComponentsInChildren<MonoBehaviour>(true))
            previewBehaviour.enabled = false;

        Shader ghostShader = Shader.Find("Universal Render Pipeline/Unlit");
        if (ghostShader == null)
        {
            Debug.LogWarning("Could not find the URP Unlit shader; the next vehicle preview will be opaque.", this);
            return;
        }

        foreach (Renderer previewRenderer in previewRenderers)
        {
            previewRenderer.shadowCastingMode = ShadowCastingMode.Off;
            previewRenderer.receiveShadows = false;

            Material[] sourceMaterials = previewRenderer.sharedMaterials;
            Material[] ghostMaterials = new Material[sourceMaterials.Length];
            for (int i = 0; i < sourceMaterials.Length; i++)
            {
                ghostMaterials[i] = CreateGhostMaterial(sourceMaterials[i], ghostShader);
                previewMaterials.Add(ghostMaterials[i]);
            }

            previewRenderer.sharedMaterials = ghostMaterials;
        }

        RefreshCostSign();
    }

    private void CreateCostSign()
    {
        GameObject signObject = new GameObject("Transport Unlock Cost Sign", typeof(RectTransform), typeof(Canvas));
        costSignCanvas = signObject.GetComponent<Canvas>();
        costSignCanvas.renderMode = RenderMode.WorldSpace;
        costSignCanvas.sortingOrder = 20;

        RectTransform canvasRect = signObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(420f, 116f);
        canvasRect.localScale = Vector3.one * costSignScale;

        GameObject panelObject = new GameObject("Toon Cost Sign", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(canvasRect, false);
        panelRect.anchorMin = Vector2.zero;
        panelRect.anchorMax = Vector2.one;
        panelRect.offsetMin = Vector2.zero;
        panelRect.offsetMax = Vector2.zero;

        Image panel = panelObject.GetComponent<Image>();
        panel.color = new Color(0.97f, 0.95f, 0.88f, 1f);
        panel.raycastTarget = false;
        Outline outline = panelObject.AddComponent<Outline>();
        outline.effectColor = new Color(0.04f, 0.06f, 0.07f, 1f);
        outline.effectDistance = new Vector2(4f, -4f);
        outline.useGraphicAlpha = true;

        GameObject moneyBadgeObject = new GameObject("Money Symbol Tile", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        RectTransform moneyBadgeRect = moneyBadgeObject.GetComponent<RectTransform>();
        moneyBadgeRect.SetParent(panelRect, false);
        moneyBadgeRect.anchorMin = new Vector2(0f, 1f);
        moneyBadgeRect.anchorMax = new Vector2(0f, 1f);
        moneyBadgeRect.pivot = new Vector2(0f, 1f);
        moneyBadgeRect.anchoredPosition = new Vector2(9f, -9f);
        moneyBadgeRect.sizeDelta = new Vector2(92f, 98f);
        Image moneyBadge = moneyBadgeObject.GetComponent<Image>();
        moneyBadge.color = new Color(0.1f, 0.43f, 0.17f, 1f);
        moneyBadge.raycastTarget = false;

        TextMeshProUGUI moneySymbol = CreateSignText(moneyBadgeRect, "Dollar Symbol", 56f,
            new Vector2(0f, -15f), new Vector2(84f, 72f));
        moneySymbol.text = "$";
        moneySymbol.color = Color.white;
        moneySymbol.fontStyle = FontStyles.Bold;

        costText = CreateSignText(panelRect, "Unlock Cost", 56f,
            new Vector2(58f, -9f), new Vector2(338f, 60f));
        costText.color = new Color(0.13f, 0.17f, 0.19f, 1f);
        costText.fontStyle = FontStyles.Bold;

        progressText = CreateSignText(panelRect, "Money Progress", 20f,
            new Vector2(58f, -70f), new Vector2(338f, 34f));
        progressText.color = new Color(0.29f, 0.24f, 0.14f, 1f);
        progressText.fontStyle = FontStyles.Bold;
    }

    private TextMeshProUGUI CreateSignText(Transform parent, string objectName, float size,
        Vector2 topOffset, Vector2 rectSize)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(parent, false);
        textRect.anchorMin = new Vector2(0.5f, 1f);
        textRect.anchorMax = new Vector2(0.5f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = topOffset;
        textRect.sizeDelta = rectSize;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = size;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = size * 0.75f;
        text.fontSizeMax = size;
        text.raycastTarget = false;
        return text;
    }

    private void UpdateCostSignTransform()
    {
        if (costSignCanvas == null)
            return;

        Transform signTransform = costSignCanvas.transform;
        signTransform.position = transform.position + costSignOffset;

        Camera activeCamera = Camera.main;
        if (activeCamera != null)
        {
            // A world-space Unity UI canvas presents its readable face along local -Z.
            // Point that face toward the camera to avoid mirrored/upside-down text.
            Vector3 awayFromCamera = signTransform.position - activeCamera.transform.position;
            if (awayFromCamera.sqrMagnitude > 0.001f)
                signTransform.rotation = Quaternion.LookRotation(awayFromCamera, Vector3.up);
            costSignCanvas.worldCamera = activeCamera;
        }

        signTransform.localScale = Vector3.one * costSignScale;
    }

    private void RefreshCostSign()
    {
        if (costText == null || progressText == null)
            return;

        int cost = GetCurrentTransportCost();
        bool staffGateActive = IsWaitingForStaff();
        int staffGateState = staffGateActive ? 1 : 0;
        if (cost == lastSignCost && moneyPaidTowardCurrentVehicle == lastSignPaid &&
            staffGateState == lastSignStaffGate)
            return;

        lastSignCost = cost;
        lastSignPaid = moneyPaidTowardCurrentVehicle;
        lastSignStaffGate = staffGateState;
        if (cost < 0)
        {
            costText.text = "ALL UNLOCKED!";
            progressText.text = "EVERY VEHICLE OWNED";
            return;
        }

        if (staffGateActive)
        {
            costText.text = "HIRE STAFF FIRST";
            progressText.text = "HIRE 1 WORKER TO UNLOCK";
            return;
        }

        int moneyRemaining = Mathf.Max(0, cost - moneyPaidTowardCurrentVehicle);
        costText.text = moneyRemaining == 0 ? "READY!" : $"${moneyRemaining} LEFT";
        progressText.text = $"PAID ${moneyPaidTowardCurrentVehicle} / ${cost}";
    }

    private bool HasHiredStaff()
    {
        return staffHireArea != null && staffHireArea.HasHiredStaff;
    }

    private bool IsWaitingForStaff()
    {
        if (transportPrefab == null || currentTransport >= transportPrefab.Length ||
            transportPrefab[currentTransport] == null)
            return false;

        Transport nextTransport = transportPrefab[currentTransport].GetComponent<Transport>();
        return nextTransport != null && nextTransport.RequiresHiredStaff && !HasHiredStaff();
    }

    private int GetCurrentTransportCost()
    {
        if (transportPrefab == null || currentTransport >= transportPrefab.Length ||
            transportPrefab[currentTransport] == null)
            return -1;

        Transport nextTransport = transportPrefab[currentTransport].GetComponent<Transport>();
        return nextTransport != null ? nextTransport.ReturnCost() : -1;
    }

    private Material CreateGhostMaterial(Material source, Shader ghostShader)
    {
        Material ghost = new Material(ghostShader)
        {
            name = source != null ? $"{source.name} (Vehicle Preview)" : "Vehicle Preview Material",
            renderQueue = (int)RenderQueue.Transparent
        };

        Texture sourceTexture = null;
        string textureProperty = null;
        if (source != null)
        {
            string[] textureProperties = { "_BaseMap", "_MainTex", "_TextureSample" };
            foreach (string property in textureProperties)
            {
                if (source.HasProperty(property) && source.GetTexture(property) != null)
                {
                    textureProperty = property;
                    sourceTexture = source.GetTexture(property);
                    break;
                }
            }
        }

        if (sourceTexture != null)
        {
            ghost.SetTexture("_BaseMap", sourceTexture);
            ghost.SetTextureScale("_BaseMap", source.GetTextureScale(textureProperty));
            ghost.SetTextureOffset("_BaseMap", source.GetTextureOffset(textureProperty));
        }

        Color tint = Color.white;
        if (source != null)
        {
            if (source.HasProperty("_BaseColor"))
                tint = source.GetColor("_BaseColor");
            else if (source.HasProperty("_Color"))
                tint = source.GetColor("_Color");
        }
        tint.a *= previewOpacity;
        ghost.SetColor("_BaseColor", tint);

        // URP Unlit uses these values for alpha blending on a transparent surface.
        ghost.SetFloat("_Surface", 1f);
        ghost.SetFloat("_Blend", 0f);
        ghost.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        ghost.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        ghost.SetInt("_ZWrite", 0);
        ghost.SetOverrideTag("RenderType", "Transparent");
        ghost.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        ghost.DisableKeyword("_ALPHATEST_ON");

        return ghost;
    }

    private void DestroyPreviewMaterials()
    {
        foreach (Material previewMaterial in previewMaterials)
        {
            if (previewMaterial != null)
                Destroy(previewMaterial);
        }

        previewMaterials.Clear();
    }


}
