using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>World-space hire pad that adds capacity and sends staff to recycle trash.</summary>
public class StaffHireArea : MonoBehaviour
{
    [Header("Hiring and Benefits")]
    [SerializeField, Min(1)] private int hireCost = 25;
    [SerializeField, Min(1)] private int capacityPerStaff = 5;
    [SerializeField, Min(1)] private int moneyPerTrash = 1;
    [SerializeField, Min(0.1f)] private float workerCycleDelay = 1.5f;
    [SerializeField, Min(0.1f)] private float workerMoveSpeed = 3.5f;
    [SerializeField, Min(1)] private int workerPickupLimit = 5;
    [SerializeField, Min(0f)] private float workerCarryStartHeight = 1.5f;
    [SerializeField, Min(0f)] private float workerCarryHeadOffset = 0.02f;
    [SerializeField, Min(0.05f)] private float workerCarryItemSpacing = 0.8f;
    [SerializeField] private Color workerTint = new Color(0.25f, 0.72f, 0.88f, 1f);
    [SerializeField] private GameObject workerPrefab;
    [SerializeField] private bool lockedUntilTruckPurchased = true;
    private SpawnTrashArea trashArea;
    private TextMeshPro label;
    private GameObject workerStation;
    private Canvas hireCostCanvas;
    private TextMeshProUGUI hireCostText;
    private TextMeshProUGUI hireMoneyText;
    private PlayerController trackedPlayer;
    private Material padMaterial;
    private float nextHireTime;
    private int staffCount;
    private int moneyPaidTowardNextHire;
    private int lastDisplayedHireCost = int.MinValue;
    private int lastDisplayedHireProgress = int.MinValue;
    private int lastDisplayedMoney = int.MinValue;
    private int lastDisplayedStaffCount = int.MinValue;
    private RuntimeAnimatorController workerAnimatorController;
    private Avatar workerAvatar;
    private readonly HashSet<TrashStacker> assignedTrash = new HashSet<TrashStacker>();
    private readonly HashSet<Collider> playerCollidersInside = new HashSet<Collider>();
    private bool completedHireDuringCurrentVisit;

    public int StaffCount => staffCount;
    public bool HasHiredStaff => staffCount > 0;

    private void Start()
    {
        trashArea = FindFirstObjectByType<SpawnTrashArea>();
        trackedPlayer = FindFirstObjectByType<PlayerController>();
        ResolveWorkerStation();

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.useGravity = false;
            body.isKinematic = true;
        }

        BoxCollider trigger = GetComponent<BoxCollider>();
        if (trigger != null)
        {
            trigger.isTrigger = true;
            trigger.enabled = !lockedUntilTruckPurchased;
        }

        BuildPadVisuals();
        BuildHireCostPanel();
        SetUnlockedVisuals(!lockedUntilTruckPurchased);
        RefreshHireCostPanel();
        RefreshLabel();
    }

    public void Unlock()
    {
        lockedUntilTruckPurchased = false;
        if (workerStation == null)
            ResolveWorkerStation();

        BoxCollider trigger = GetComponent<BoxCollider>();
        if (trigger != null)
            trigger.enabled = true;
        SetUnlockedVisuals(true);
    }

    private void ResolveWorkerStation()
    {
        Transform garbageArea = transform.parent;
        if (garbageArea == null || garbageArea.name != "Garbage Area")
        {
            GameObject garbageAreaObject = GameObject.Find("Garbage Area");
            garbageArea = garbageAreaObject != null ? garbageAreaObject.transform : null;
        }

        Transform canvas = garbageArea != null ? garbageArea.Find("Canvas") : null;
        Transform station = canvas != null ? canvas.Find("WorkerStation") : null;
        workerStation = station != null ? station.gameObject : null;
    }

    private void SetUnlockedVisuals(bool visible)
    {
        SetPadVisible(visible);
        if (workerStation != null)
            workerStation.SetActive(visible);
        if (hireCostCanvas != null)
            hireCostCanvas.gameObject.SetActive(visible);
    }

    private void SetPadVisible(bool visible)
    {
        foreach (Renderer padRenderer in GetComponentsInChildren<Renderer>(true))
            padRenderer.enabled = visible;
        foreach (TextMeshPro padText in GetComponentsInChildren<TextMeshPro>(true))
            padText.enabled = visible;
    }

    private void BuildPadVisuals()
    {
        GameObject tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tile.name = "Blue Staff Hire Tile";
        tile.transform.SetParent(transform, false);
        tile.transform.localPosition = new Vector3(0f, 0.06f, 0f);
        tile.transform.localScale = new Vector3(1.5f, 0.12f, 1.5f);
        Collider tileCollider = tile.GetComponent<Collider>();
        if (tileCollider != null)
            Destroy(tileCollider);

        Shader toonShader = Shader.Find("ArcadeRecycler/Toon");
        if (toonShader == null)
            toonShader = Shader.Find("Universal Render Pipeline/Lit");
        if (toonShader != null)
        {
            padMaterial = new Material(toonShader);
            Color tileColor = new Color(0.05f, 0.34f, 0.48f, 1f);
            if (padMaterial.HasProperty("_BaseColor"))
                padMaterial.SetColor("_BaseColor", tileColor);
            else if (padMaterial.HasProperty("_Color"))
                padMaterial.SetColor("_Color", tileColor);
            if (padMaterial.HasProperty("_OutlineColor"))
                padMaterial.SetColor("_OutlineColor", Color.black);
            if (padMaterial.HasProperty("_OutlineWidth"))
                padMaterial.SetFloat("_OutlineWidth", 1.35f);
            tile.GetComponent<Renderer>().sharedMaterial = padMaterial;
        }

        GameObject labelObject = new GameObject("Staff Hire Label");
        labelObject.transform.SetParent(transform, false);
        labelObject.transform.localPosition = new Vector3(0f, 1.35f, 0f);
        label = labelObject.AddComponent<TextMeshPro>();
        label.text = "HIRE STAFF\n$25\nSTAFF: 0";
        label.fontSize = 3.4f;
        label.alignment = TextAlignmentOptions.Center;
        label.color = new Color(0.08f, 0.2f, 0.16f, 1f);
        label.outlineWidth = 0.12f;
        label.outlineColor = new Color(1f, 0.95f, 0.8f, 1f);
    }

    private void LateUpdate()
    {
        Camera camera = Camera.main;
        if (label != null && camera != null)
        {
            Vector3 toCamera = camera.transform.position - label.transform.position;
            toCamera.y = 0f;
            if (toCamera.sqrMagnitude > 0.001f)
                label.transform.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }

        UpdateHireCostPanelTransform(camera);
        RefreshHireCostPanel();
    }

    private void BuildHireCostPanel()
    {
        GameObject signObject = new GameObject("Staff Hire Cost Sign", typeof(RectTransform), typeof(Canvas));
        hireCostCanvas = signObject.GetComponent<Canvas>();
        hireCostCanvas.renderMode = RenderMode.WorldSpace;
        hireCostCanvas.sortingOrder = 20;

        RectTransform canvasRect = signObject.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(420f, 116f);
        canvasRect.localScale = Vector3.one * 0.011f;

        GameObject panelObject = new GameObject("Toon Staff Cost Sign", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
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

        TextMeshProUGUI moneySymbol = CreateHireSignText(moneyBadgeRect, "Dollar Symbol", 56f,
            new Vector2(0f, -15f), new Vector2(84f, 72f));
        moneySymbol.text = "$";
        moneySymbol.color = Color.white;
        moneySymbol.fontStyle = FontStyles.Bold;

        hireCostText = CreateHireSignText(panelRect, "Hire Cost", 56f,
            new Vector2(58f, -9f), new Vector2(338f, 60f));
        hireCostText.color = new Color(0.13f, 0.17f, 0.19f, 1f);
        hireCostText.fontStyle = FontStyles.Bold;

        hireMoneyText = CreateHireSignText(panelRect, "Hire Money Progress", 20f,
            new Vector2(58f, -70f), new Vector2(338f, 34f));
        hireMoneyText.color = new Color(0.29f, 0.24f, 0.14f, 1f);
        hireMoneyText.fontStyle = FontStyles.Bold;
    }

    private TextMeshProUGUI CreateHireSignText(Transform parent, string objectName, float size,
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

    private void UpdateHireCostPanelTransform(Camera camera)
    {
        if (hireCostCanvas == null)
            return;

        Transform signTransform = hireCostCanvas.transform;
        Vector3 signAnchor = workerStation != null ? workerStation.transform.position : transform.position;
        signTransform.position = signAnchor + Vector3.up * 0.9f;

        if (camera != null)
        {
            Vector3 awayFromCamera = signTransform.position - camera.transform.position;
            if (awayFromCamera.sqrMagnitude > 0.001f)
                signTransform.rotation = Quaternion.LookRotation(awayFromCamera, Vector3.up);
            hireCostCanvas.worldCamera = camera;
        }

        signTransform.localScale = Vector3.one * 0.011f;
    }

    private void RefreshHireCostPanel()
    {
        if (hireCostText == null || hireMoneyText == null)
            return;

        if (trackedPlayer == null)
            trackedPlayer = FindFirstObjectByType<PlayerController>();

        int money = trackedPlayer != null ? trackedPlayer.CheckMoney() : 0;
        if (hireCost == lastDisplayedHireCost && moneyPaidTowardNextHire == lastDisplayedHireProgress &&
            money == lastDisplayedMoney && staffCount == lastDisplayedStaffCount)
            return;

        lastDisplayedHireCost = hireCost;
        lastDisplayedHireProgress = moneyPaidTowardNextHire;
        lastDisplayedMoney = money;
        lastDisplayedStaffCount = staffCount;

        int moneyLeft = Mathf.Max(0, hireCost - moneyPaidTowardNextHire);
        hireCostText.text = moneyLeft == 0 ? "READY!" : $"${moneyLeft} LEFT";
        hireMoneyText.text = $"PAID ${moneyPaidTowardNextHire} / ${hireCost}   CASH ${money}   STAFF {staffCount}";
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerController player = FindEligiblePlayer(other);
        if (player == null)
            return;

        playerCollidersInside.Add(other);
        TryCollectHirePayment(player);
    }

    private void OnTriggerStay(Collider other)
    {
        PlayerController player = FindEligiblePlayer(other);
        if (player == null)
            return;

        // Stay also handles the pad being unlocked while the player is already
        // standing on it, and takes later-earned cash toward a partially paid hire.
        playerCollidersInside.Add(other);
        TryCollectHirePayment(player);
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerController player = FindEligiblePlayer(other);
        if (player == null)
            return;

        playerCollidersInside.Remove(other);
        if (playerCollidersInside.Count == 0)
            completedHireDuringCurrentVisit = false;
    }

    private PlayerController FindEligiblePlayer(Collider other)
    {
        if (other == null)
            return null;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        if (player == null || other.GetComponentInParent<MoneyStacker>() != null)
            return null;

        // The Amount on a carried payout stack is not the player's body. Some
        // older pickups attached Amount to their parent, so only reject a stack
        // collider when that Amount is separate from the player root.
        Amount amount = other.GetComponentInParent<Amount>();
        if (amount != null && amount.gameObject != player.gameObject)
            return null;

        return player;
    }

    private void TryCollectHirePayment(PlayerController player)
    {
        if (Time.time < nextHireTime || completedHireDuringCurrentVisit)
            return;

        trackedPlayer = player;
        int amountStillNeeded = Mathf.Max(0, hireCost - moneyPaidTowardNextHire);
        int amountToPay = Mathf.Min(player.CheckMoney(), amountStillNeeded);
        if (amountToPay <= 0 || !player.TrySpendMoney(amountToPay))
        {
            RefreshHireCostPanel();
            return;
        }

        nextHireTime = Time.time + 0.75f;
        moneyPaidTowardNextHire += amountToPay;
        RefreshHireCostPanel();

        if (moneyPaidTowardNextHire < hireCost)
            return;

        moneyPaidTowardNextHire = 0;
        completedHireDuringCurrentVisit = true;
        staffCount++;
        player.AddCapacity(capacityPerStaff);
        if (trashArea != null)
            trashArea.RegisterStaffHire();

        SpawnWorker(player, staffCount - 1);
        RefreshLabel();
        RefreshHireCostPanel();
    }

    private void SpawnWorker(PlayerController player, int workerIndex)
    {
        if (workerPrefab == null)
        {
            Debug.LogWarning("No staff visual prefab is assigned; staff will still work automatically.", this);
            StartCoroutine(WorkerRoutine(player, null, workerIndex));
            return;
        }

        Vector3 idleOffset = new Vector3((workerIndex % 3 - 1) * 0.35f, 0f,
            0.45f + (workerIndex / 3) * 0.35f);
        GameObject worker = Instantiate(workerPrefab, transform.position + idleOffset, transform.rotation);
        worker.name = $"Recycling Staff {workerIndex + 1}";

        Animator playerAnimator = player.GetComponentInChildren<Animator>();
        Animator workerAnimator = worker.GetComponentInChildren<Animator>();
        if (playerAnimator != null && workerAnimator != null)
        {
            workerAnimator.runtimeAnimatorController = playerAnimator.runtimeAnimatorController;
            workerAnimator.avatar = playerAnimator.avatar;
            workerAnimator.applyRootMotion = false;
            workerAnimator.Rebind();
            workerAnimator.Update(0f);
            workerAnimatorController = playerAnimator.runtimeAnimatorController;
            workerAvatar = playerAnimator.avatar;
            PlayWorkerAnimation(workerAnimator, "Idle");
        }
        else
            Debug.LogWarning("The player and staff prefab both need an Animator for staff animations.", this);

        ApplyWorkerTint(worker);

        foreach (Collider workerCollider in worker.GetComponentsInChildren<Collider>(true))
            workerCollider.enabled = false;
        foreach (Rigidbody workerBody in worker.GetComponentsInChildren<Rigidbody>(true))
        {
            workerBody.useGravity = false;
            workerBody.isKinematic = true;
        }

        StartCoroutine(WorkerRoutine(player, worker.transform, workerIndex));
    }

    private IEnumerator WorkerRoutine(PlayerController player, Transform worker, int workerIndex)
    {
        RecycleCenter recycleCenter = FindFirstObjectByType<RecycleCenter>();
        Animator animator = worker != null ? worker.GetComponentInChildren<Animator>() : null;
        Transform carryAnchor = null;
        if (animator != null && animator.isHuman)
        {
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
            carryAnchor = head != null ? (head.Find("HeadTop_End") ?? head) : null;
        }
        while (player != null)
        {
            PlayWorkerAnimation(animator, "Idle");
            yield return new WaitForSeconds(workerCycleDelay);
            List<TrashStacker> carriedTrash = new List<TrashStacker>();
            for (int i = 0; i < workerPickupLimit; i++)
            {
                TrashStacker trash = FindNearestAvailableTrash(worker != null ? worker.position : transform.position);
                if (trash == null)
                    break;

                if (worker != null)
                    yield return MoveWorker(worker, trash.transform.position);
                if (trash == null || !trash.CollectByWorker())
                {
                    if (trash != null)
                        assignedTrash.Remove(trash);
                    continue;
                }

                if (worker != null)
                {
                    Transform carryParent = carryAnchor != null ? carryAnchor : worker;
                    trash.transform.SetParent(carryParent, true);
                    if (carryAnchor != null)
                    {
                        float stackOffset = workerCarryHeadOffset +
                            carriedTrash.Count * workerCarryItemSpacing;
                        trash.transform.position = carryAnchor.position + Vector3.up * stackOffset;
                    }
                    else
                    {
                        trash.transform.localPosition = Vector3.up * (workerCarryStartHeight +
                            carriedTrash.Count * workerCarryItemSpacing);
                    }
                    trash.transform.rotation = Quaternion.identity;
                }
                carriedTrash.Add(trash);
            }

            if (carriedTrash.Count == 0)
                continue;

            Vector3 destination = recycleCenter != null
                ? recycleCenter.transform.position
                : transform.position;
            if (worker != null)
                yield return MoveWorker(worker, destination);

            bool deliveryHandled = recycleCenter != null &&
                recycleCenter.ReceiveStaffDelivery(carriedTrash, moneyPerTrash);
            if (!deliveryHandled)
            {
                foreach (TrashStacker trash in carriedTrash)
                {
                    if (trash != null)
                    {
                        GameManager.Instance?.RecycleTrash(1);
                        player.EarnMoney(moneyPerTrash);
                        Destroy(trash.gameObject);
                    }
                }
            }

            foreach (TrashStacker trash in carriedTrash)
                assignedTrash.Remove(trash);

            if (worker != null)
                yield return MoveWorker(worker, transform.position);
        }

        if (worker != null)
            Destroy(worker.gameObject);
    }

    private TrashStacker FindNearestAvailableTrash(Vector3 fromPosition)
    {
        TrashStacker nearest = null;
        float nearestDistance = float.PositiveInfinity;
        foreach (TrashStacker candidate in FindObjectsByType<TrashStacker>(FindObjectsSortMode.None))
        {
            if (candidate == null || candidate.IsCollected || assignedTrash.Contains(candidate))
                continue;

            float distance = (candidate.transform.position - fromPosition).sqrMagnitude;
            if (distance < nearestDistance)
            {
                nearest = candidate;
                nearestDistance = distance;
            }
        }

        if (nearest != null)
            assignedTrash.Add(nearest);
        return nearest;
    }

    private IEnumerator MoveWorker(Transform worker, Vector3 destination)
    {
        Animator animator = worker.GetComponentInChildren<Animator>();
        PlayWorkerAnimation(animator, "Run");
        destination.y = worker.position.y;
        while (worker != null && (worker.position - destination).sqrMagnitude > 0.04f)
        {
            worker.position = Vector3.MoveTowards(worker.position, destination,
                workerMoveSpeed * Time.deltaTime);
            Vector3 direction = destination - worker.position;
            if (direction.sqrMagnitude > 0.001f)
                worker.rotation = Quaternion.LookRotation(direction, Vector3.up);
            yield return null;
        }
        PlayWorkerAnimation(animator, "Idle");
    }

    private void PlayWorkerAnimation(Animator animator, string stateName)
    {
        if (animator == null)
            return;

        if (animator.runtimeAnimatorController == null && workerAnimatorController != null)
        {
            animator.runtimeAnimatorController = workerAnimatorController;
            animator.avatar = workerAvatar;
            animator.applyRootMotion = false;
        }

        int stateHash = Animator.StringToHash($"Base Layer.{stateName}");
        if (animator.HasState(0, stateHash))
            animator.CrossFadeInFixedTime(stateHash, 0.12f);
    }

    private void ApplyWorkerTint(GameObject worker)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        foreach (Renderer renderer in worker.GetComponentsInChildren<Renderer>(true))
        {
            renderer.GetPropertyBlock(block);
            block.SetColor("_Color", workerTint);
            block.SetColor("_BaseColor", workerTint);
            renderer.SetPropertyBlock(block);
        }
    }

    private void RefreshLabel()
    {
        if (label != null)
            label.text = $"HIRE STAFF\n${hireCost}\nSTAFF: {staffCount}";
    }

    private void OnDestroy()
    {
        if (padMaterial != null)
            Destroy(padMaterial);
        if (hireCostCanvas != null)
            Destroy(hireCostCanvas.gameObject);
    }
}
