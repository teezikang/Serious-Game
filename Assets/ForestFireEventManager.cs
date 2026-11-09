using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Runs the one-time forest fire and the lake-water firefighting loop.</summary>
public class ForestFireEventManager : MonoBehaviour
{
    private const int StartingFireCount = 3;
    private const int MaximumFireCount = 20;
    private const int WaterCapacity = 5;
    private const string FirePrefabResource = "ForestFire";
    private const string WaterPrefabResource = "WaterSphere";
    private const string BlackSmokePrefabResource = "VFX_BlackSmoke";
    private const string WarningSpriteResource = "WarningForestFire";

    [SerializeField, Min(0.1f)] private float spreadInterval = 15f;
    [SerializeField, Min(0.1f)] private float interactionRadius = 4f;
    [SerializeField, Min(0.1f)] private float lakeInteractionRadius = 2.5f;
    [SerializeField, Min(0.1f)] private float fireScale = 3.5f;
    [SerializeField, Min(0.1f)] private float fireParticleSizeMultiplier = 2.5f;
    [SerializeField, Min(0f)] private float aqiIncreasePerFireSpawn = 2f;
    [SerializeField, Min(0f)] private float aqiReductionPerExtinguishedFire = 1f;
    [SerializeField, Min(1)] private int moneyRewardPerExtinguishedFire = 1;
    [SerializeField, Min(0f)] private float fireSpawnHeightOffset = 2.28f;
    [SerializeField, Min(0.05f)] private float lakeRefillStepDelay = 0.3f;

    private static bool eventStarted;
    private static ForestFireEventManager activeInstance;
    private readonly List<GameObject> activeFires = new List<GameObject>();
    private readonly List<Transform> fireForests = new List<Transform>();
    private readonly List<Vector3> firePositions = new List<Vector3>();
    private readonly List<List<Vector3>> forestFireSites = new List<List<Vector3>>();
    private readonly List<GameObject> carriedWater = new List<GameObject>();
    private readonly HashSet<Transform> playersInLake = new HashSet<Transform>();
    private readonly HashSet<GameObject> extinguishingFires = new HashSet<GameObject>();

    private GameObject firePrefab;
    private GameObject waterPrefab;
    private GameObject blackSmokePrefab;
    private Transform player;
    private Transform waterStackRoot;
    private Coroutine lakeRefillRoutine;
    private Bounds lakeBounds;
    private TextMeshProUGUI eventText;
    private GameObject eventMessagePanel;
    private TextMeshProUGUI fireRewardNotificationText;
    private Image warningImage;
    private RectTransform warningImageRect;
    private Transform initialFireNotificationTarget;
    private bool initialFireSeen;
    private RectTransform eventCanvasRect;
    private RectTransform directionArrowRect;
    private TextMeshProUGUI directionArrowText;
    private float warningUntil;
    private float messageUntil;
    private float fireRewardNotificationUntil;
    private float nextBurningAlertRefresh;
    private int waterCount;
    private int pendingFireRewardAmount;
    private int spreadCursor;
    private bool eventComplete = true;
    private bool hasStarted;
    private bool warnedMissingSmokePrefab;

    public static bool IsFireEventActive => activeInstance != null && !activeInstance.eventComplete;

    /// <summary>Called the first time the player takes off in the helicopter.</summary>
    public static void BeginOnFirstHelicopterFlight()
    {
        if (eventStarted)
            return;

        ForestFireEventManager manager = FindFirstObjectByType<ForestFireEventManager>();
        if (manager == null)
        {
            Debug.LogError("Forest Fire Event Manager is missing from the GameScene hierarchy.");
            return;
        }

        manager.BeginEvent();
    }

    private void BeginEvent()
    {
        if (hasStarted || eventStarted)
            return;

        hasStarted = true;
        eventStarted = true;
        eventComplete = false;
        activeInstance = this;
        activeFires.Clear();
        fireForests.Clear();
        firePositions.Clear();
        forestFireSites.Clear();
        carriedWater.Clear();
        extinguishingFires.Clear();
        firePrefab = Resources.Load<GameObject>(FirePrefabResource);
        waterPrefab = Resources.Load<GameObject>(WaterPrefabResource);
        blackSmokePrefab = Resources.Load<GameObject>(BlackSmokePrefabResource);

        if (firePrefab == null || waterPrefab == null)
        {
            FailBeginEvent("Forest fire assets are missing from Assets/Resources. Check ForestFire and WaterSphere.");
            return;
        }

        FindForestsAndFireSites();
        if (firePositions.Count == 0)
        {
            FailBeginEvent("No renderable tree fire sites were found under the two Jungle forest groups.");
            return;
        }

        if (!FindExistingLake())
        {
            FailBeginEvent("Could not find the existing Lake object in PlayGround.");
            return;
        }

        CreateLakeRefillTrigger();
        player = FindPlayer();
        BuildEventUI();
        GameManager.Instance?.SetForestFireWaterStatus(waterCount, WaterCapacity);
        warningUntil = Time.unscaledTime + 5f;
        messageUntil = Time.unscaledTime + 14f;
        eventText.text = "FIRE ALERT   •   Fill with water at the lake   •   One sphere puts out one fire";

        for (int i = 0; i < StartingFireCount; i++)
            SpawnFire(ChooseFireSite(i % forestFireSites.Count));

        StartCoroutine(SpreadFires());
    }

    private void FailBeginEvent(string reason)
    {
        Debug.LogError(reason, this);
        if (activeInstance == this)
            activeInstance = null;

        eventStarted = false;
        hasStarted = false;
        eventComplete = true;
        activeFires.Clear();
        fireForests.Clear();
        firePositions.Clear();
        forestFireSites.Clear();
    }

    private void Update()
    {
        if (player == null)
            player = FindPlayer();

        if (fireRewardNotificationText != null && fireRewardNotificationText.gameObject.activeSelf &&
            Time.unscaledTime >= fireRewardNotificationUntil)
        {
            fireRewardNotificationText.gameObject.SetActive(false);
            pendingFireRewardAmount = 0;
        }

        if (playersInLake.Count > 0 && waterCount < WaterCapacity && lakeRefillRoutine == null)
            StartLakeRefill();

        if (player == null || eventComplete)
        {
            if (eventComplete)
            {
                if (directionArrowRect != null)
                    directionArrowRect.gameObject.SetActive(false);
                if (warningImage != null)
                    warningImage.gameObject.SetActive(false);
            }
            return;
        }

        RemoveDestroyedFires();
        UpdateEventPrompt();
        UpdateFireNotification();
        UpdateDirectionArrow();
    }

    private void FindForestsAndFireSites()
    {
        Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform candidateForest = allTransforms[i];
            if (candidateForest.name != "Jungle")
                continue;

            fireForests.Add(candidateForest);
            List<Vector3> forestSites = new List<Vector3>();
            Renderer[] renderers = candidateForest.GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer forestRenderer = renderers[r];
                if (forestRenderer == null || forestRenderer is ParticleSystemRenderer)
                    continue;

                Bounds bounds = forestRenderer.bounds;
                if (bounds.size.sqrMagnitude < 0.35f || bounds.size.sqrMagnitude > 600f)
                    continue;

                Vector3 point = new Vector3(bounds.center.x, 0f, bounds.center.z);
                bool duplicate = false;
                for (int p = 0; p < forestSites.Count; p++)
                {
                    if (HorizontalDistance(forestSites[p], point) < 2.2f)
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    forestSites.Add(point);
                    firePositions.Add(point);
                }
            }

            if (forestSites.Count > 0)
                forestFireSites.Add(forestSites);
        }

        if (fireForests.Count == 0)
            Debug.LogError("Could not find the two Jungle forest groups in the GameScene.", this);
    }

    private Vector3 ChooseFireSite(int forestIndex)
    {
        if (forestFireSites.Count == 0 || firePositions.Count == 0)
            return Vector3.zero;

        List<Vector3> sites = forestFireSites[forestIndex % forestFireSites.Count];
        Vector3 position = sites[Random.Range(0, sites.Count)];
        for (int attempt = 0; attempt < 24; attempt++)
        {
            Vector3 candidate = sites[Random.Range(0, sites.Count)];
            bool tooClose = false;
            for (int i = 0; i < activeFires.Count; i++)
            {
                if (activeFires[i] != null &&
                    HorizontalDistance(activeFires[i].transform.position, candidate) < 3f)
                {
                    tooClose = true;
                    break;
                }
            }

            if (!tooClose)
            {
                position = candidate;
                break;
            }
        }

        position += new Vector3(Random.Range(-0.8f, 0.8f), 0f, Random.Range(-0.8f, 0.8f));
        position.y = FindGroundHeight(position) + 0.05f;
        return position;
    }

    private bool FindExistingLake()
    {
        Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform lake = allTransforms[i];
            if (lake.name != "Lake")
                continue;

            Renderer[] renderers = lake.GetComponentsInChildren<Renderer>(true);
            Collider[] colliders = lake.GetComponentsInChildren<Collider>(true);
            bool foundBounds = false;
            Bounds bounds = new Bounds(lake.position, Vector3.one * 8f);

            for (int r = 0; r < renderers.Length; r++)
            {
                if (renderers[r] == null)
                    continue;

                if (!foundBounds)
                {
                    bounds = renderers[r].bounds;
                    foundBounds = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[r].bounds);
                }
            }

            for (int c = 0; c < colliders.Length; c++)
            {
                if (colliders[c] == null || !colliders[c].enabled)
                    continue;

                if (!foundBounds)
                {
                    bounds = colliders[c].bounds;
                    foundBounds = true;
                }
                else
                {
                    bounds.Encapsulate(colliders[c].bounds);
                }
            }

            lakeBounds = bounds;
            return true;
        }

        return false;
    }

    private float FindGroundHeight(Vector3 position)
    {
        RaycastHit[] hits = Physics.RaycastAll(position + Vector3.up * 100f, Vector3.down,
            200f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        if (hits.Length == 0)
            return 0f;

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

    private void SpawnFire(Vector3 position)
    {
        // Raise the VFX and its prefab trigger enough to overlap the player's
        // carried water spheres, while the enlarged box still reaches ground level.
        position += Vector3.up * fireSpawnHeightOffset;
        GameObject fire = Instantiate(firePrefab, position, Quaternion.identity, transform);
        fire.name = "Forest Fire";
        fire.transform.localScale *= fireScale;
        if (initialFireNotificationTarget == null)
            initialFireNotificationTarget = fire.transform;

        ParticleSystem[] particleSystems = fire.GetComponentsInChildren<ParticleSystem>(true);
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem.MainModule main = particleSystems[i].main;
            main.startSizeMultiplier *= fireParticleSizeMultiplier;
        }

        // The BoxCollider trigger is authored on the fire prefab so every
        // spawned fire instance is ready for water-sphere contact.
        Rigidbody fireBody = fire.AddComponent<Rigidbody>();
        fireBody.useGravity = false;
        fireBody.isKinematic = true;
        fireBody.detectCollisions = true;

        activeFires.Add(fire);
        GameManager.Instance?.IncreaseAQIFromForestFire(aqiIncreasePerFireSpawn);
    }

    private void CreateLakeRefillTrigger()
    {
        GameObject triggerObject = new GameObject("Lake Water Refill Trigger",
            typeof(BoxCollider), typeof(LakeWaterRefillTrigger));
        triggerObject.transform.SetParent(transform, true);
        float groundHeight = FindGroundHeight(lakeBounds.center);
        triggerObject.transform.position = new Vector3(lakeBounds.center.x,
            groundHeight + 0.95f, lakeBounds.center.z);

        BoxCollider trigger = triggerObject.GetComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(Mathf.Max(1f, lakeBounds.size.x), 2.2f,
            Mathf.Max(1f, lakeBounds.size.z));
    }

    private GameObject CreateWaterSphere(Vector3 position)
    {
        GameObject sphere = Instantiate(waterPrefab, position, Quaternion.identity);
        sphere.name = "Water Sphere";
        sphere.transform.localScale = Vector3.one * 1.5f;

        SphereCollider sphereCollider = sphere.GetComponent<SphereCollider>();
        if (sphereCollider == null)
            sphereCollider = sphere.AddComponent<SphereCollider>();
        sphereCollider.radius = 0.5f;
        sphereCollider.isTrigger = true;

        Rigidbody body = sphere.GetComponent<Rigidbody>();
        if (body == null)
            body = sphere.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.detectCollisions = true;

        if (sphere.GetComponent<WaterSpherePickup>() == null)
            sphere.AddComponent<WaterSpherePickup>();

        return sphere;
    }

    public static void PlayerEnteredLake(Transform enteringPlayer)
    {
        if (activeInstance != null)
            activeInstance.OnPlayerEnteredLake(enteringPlayer);
    }

    public static void PlayerExitedLake(Transform exitingPlayer)
    {
        if (activeInstance != null)
            activeInstance.OnPlayerExitedLake(exitingPlayer);
    }

    private void OnPlayerEnteredLake(Transform enteringPlayer)
    {
        if (enteringPlayer == null || eventComplete)
            return;

        player = enteringPlayer;
        playersInLake.Add(enteringPlayer);
        StartLakeRefill();
    }

    private void OnPlayerExitedLake(Transform exitingPlayer)
    {
        if (exitingPlayer == null)
            return;

        playersInLake.Remove(exitingPlayer);
        if (playersInLake.Count == 0 && lakeRefillRoutine != null)
        {
            StopCoroutine(lakeRefillRoutine);
            lakeRefillRoutine = null;
        }
    }

    private void StartLakeRefill()
    {
        if (!eventComplete && waterCount < WaterCapacity && lakeRefillRoutine == null)
            lakeRefillRoutine = StartCoroutine(RefillWaterStack());
    }

    private IEnumerator RefillWaterStack()
    {
        while (!eventComplete && waterCount < WaterCapacity && playersInLake.Count > 0)
        {
            Transform refillPlayer = player;
            if (refillPlayer == null || !playersInLake.Contains(refillPlayer))
                break;

            GameObject sphere = CreateWaterSphere(refillPlayer.position + Vector3.up * 0.55f);
            if (!CollectWaterSphere(sphere, refillPlayer))
            {
                Destroy(sphere);
                break;
            }

            yield return new WaitForSeconds(lakeRefillStepDelay);
        }

        lakeRefillRoutine = null;
    }

    private IEnumerator SpreadFires()
    {
        while (!eventComplete && activeFires.Count > extinguishingFires.Count &&
               activeFires.Count < MaximumFireCount)
        {
            yield return new WaitForSeconds(spreadInterval);
            if (eventComplete || activeFires.Count <= extinguishingFires.Count)
                yield break;

            int newFireCount = Mathf.Min(2, MaximumFireCount - activeFires.Count);
            for (int i = 0; i < newFireCount; i++)
                SpawnFire(ChooseFireSite(spreadCursor++));
        }
    }

    private void RemoveDestroyedFires()
    {
        for (int i = activeFires.Count - 1; i >= 0; i--)
        {
            if (activeFires[i] == null)
                activeFires.RemoveAt(i);
        }
    }

    private int FindNearestFireIndex()
    {
        int nearestIndex = -1;
        float nearestDistance = interactionRadius;
        for (int i = 0; i < activeFires.Count; i++)
        {
            if (activeFires[i] == null)
                continue;

            float distance = HorizontalDistance(player.position, activeFires[i].transform.position);
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    private int FindNearestFireIndexForDirection()
    {
        int nearestIndex = -1;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < activeFires.Count; i++)
        {
            if (activeFires[i] == null)
                continue;

            float distance = HorizontalDistance(player.position, activeFires[i].transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestIndex = i;
            }
        }

        return nearestIndex;
    }

    public static bool TryCollectWaterSphere(GameObject sphere, Transform collectingPlayer)
    {
        return activeInstance != null && activeInstance.CollectWaterSphere(sphere, collectingPlayer);
    }

    public static bool TryExtinguishFire(GameObject fire, GameObject waterSphere)
    {
        return activeInstance != null && activeInstance.ExtinguishFire(fire, waterSphere);
    }

    private bool CollectWaterSphere(GameObject sphere, Transform collectingPlayer)
    {
        if (sphere == null || collectingPlayer == null || waterCount >= WaterCapacity ||
            carriedWater.Contains(sphere))
            return false;

        player = collectingPlayer;
        if (waterStackRoot == null || waterStackRoot.parent != player)
        {
            Transform existingStack = player.Find("Collected Water Stack");
            if (existingStack == null)
            {
                GameObject stackObject = new GameObject("Collected Water Stack");
                existingStack = stackObject.transform;
                existingStack.SetParent(player, false);
            }

            waterStackRoot = existingStack;
        }

        WaterSpherePickup pickup = sphere.GetComponent<WaterSpherePickup>();
        if (pickup == null)
            pickup = sphere.AddComponent<WaterSpherePickup>();

        int stackIndex = carriedWater.Count;
        carriedWater.Add(sphere);
        waterCount = carriedWater.Count;
        GameManager.Instance?.SetForestFireWaterStatus(waterCount, WaterCapacity);
        pickup.BeginCarry(waterStackRoot, stackIndex);
        messageUntil = Time.unscaledTime + 2f;
        eventText.text = "Water collected. Carry it to a fire.";
        return true;
    }

    private bool ExtinguishFire(GameObject fire, GameObject waterSphere)
    {
        int fireIndex = activeFires.IndexOf(fire);
        int waterIndex = carriedWater.IndexOf(waterSphere);
        if (fireIndex < 0 || waterIndex < 0 || fire == null || waterSphere == null ||
            extinguishingFires.Contains(fire))
            return false;

        Vector3 firePosition = fire.transform.position;
        WaterSpherePickup dropPickup = waterSphere.GetComponent<WaterSpherePickup>();
        if (dropPickup != null)
            dropPickup.PrepareForFireDrop();
        else
            waterSphere.transform.SetParent(null, true);
        Vector3 dropStartPosition = waterSphere.transform.position;

        carriedWater.RemoveAt(waterIndex);
        waterCount = carriedWater.Count;
        GameManager.Instance?.SetForestFireWaterStatus(waterCount, WaterCapacity);
        extinguishingFires.Add(fire);

        for (int i = 0; i < carriedWater.Count; i++)
        {
            if (carriedWater[i] == null)
                continue;

            WaterSpherePickup pickup = carriedWater[i].GetComponent<WaterSpherePickup>();
            if (pickup != null && waterStackRoot != null)
                pickup.SnapToStackPosition(i);
        }

        StartCoroutine(DropWaterSphereAndExtinguish(fire, waterSphere,
            dropStartPosition, firePosition));
        return true;
    }

    private IEnumerator DropWaterSphereAndExtinguish(GameObject fire, GameObject waterSphere,
        Vector3 startPosition, Vector3 firePosition)
    {
        const float dropDuration = 0.35f;
        Vector3 impactPosition = firePosition + Vector3.up * 0.15f;
        float elapsed = 0f;

        while (elapsed < dropDuration && waterSphere != null)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dropDuration);
            waterSphere.transform.position = Vector3.Lerp(startPosition, impactPosition, t) +
                Vector3.down * (Mathf.Sin(t * Mathf.PI) * 0.2f);
            yield return null;
        }

        if (waterSphere != null)
        {
            waterSphere.transform.position = impactPosition;
            Destroy(waterSphere);
        }

        GameObject smoke = null;
        if (fire != null)
        {
            fire.SetActive(false);
            smoke = CreateExtinguishSmoke(firePosition);
            Destroy(fire);
            RewardPlayerForExtinguishingFire();
            GameManager.Instance?.DecreaseAQIFromExtinguishedForestFire(
                aqiReductionPerExtinguishedFire);
        }

        activeFires.Remove(fire);
        extinguishingFires.Remove(fire);

        if (activeFires.Count == 0)
        {
            eventComplete = true;
            if (lakeRefillRoutine != null)
                StopCoroutine(lakeRefillRoutine);
            lakeRefillRoutine = null;
            playersInLake.Clear();
            ClearCarriedWater();
            GameManager.Instance?.RestoreTrashStatus();
            if (eventMessagePanel != null)
                eventMessagePanel.SetActive(false);
            if (warningImage != null)
                warningImage.gameObject.SetActive(false);
            if (directionArrowRect != null)
                directionArrowRect.gameObject.SetActive(false);
            CloudSeedingEventManager.BeginAfterForestFireEvent();
        }
        else
        {
            messageUntil = Time.unscaledTime + 2f;
            if (eventText != null)
                eventText.text = $"Fire extinguished! {activeFires.Count} remain";
        }

        if (smoke != null)
            yield return StartCoroutine(FadeOutExtinguishSmoke(smoke));
    }

    private void RewardPlayerForExtinguishingFire()
    {
        int reward = Mathf.Max(1, moneyRewardPerExtinguishedFire);
        PlayerController rewardPlayer = player != null
            ? player.GetComponent<PlayerController>()
            : null;
        if (rewardPlayer == null)
            rewardPlayer = FindFirstObjectByType<PlayerController>();

        if (rewardPlayer != null)
            rewardPlayer.EarnMoney(reward);

        if (fireRewardNotificationText == null)
            return;

        if (Time.unscaledTime >= fireRewardNotificationUntil)
            pendingFireRewardAmount = 0;

        pendingFireRewardAmount += reward;
        fireRewardNotificationText.text = $"FIRE PUT OUT!  +${pendingFireRewardAmount}";
        fireRewardNotificationUntil = Time.unscaledTime + 2.5f;
        fireRewardNotificationText.gameObject.SetActive(true);
    }

    private void ClearCarriedWater()
    {
        for (int i = 0; i < carriedWater.Count; i++)
        {
            if (carriedWater[i] != null)
                Destroy(carriedWater[i]);
        }
        carriedWater.Clear();
        waterCount = 0;

        if (waterStackRoot != null)
            Destroy(waterStackRoot.gameObject);
        waterStackRoot = null;
        GameManager.Instance?.SetForestFireWaterStatus(0, WaterCapacity);
    }

    private GameObject CreateExtinguishSmoke(Vector3 firePosition)
    {
        if (blackSmokePrefab == null)
        {
            if (!warnedMissingSmokePrefab)
            {
                Debug.LogWarning("Black smoke VFX was not found in Resources/VFX_BlackSmoke.", this);
                warnedMissingSmokePrefab = true;
            }
            return null;
        }

        GameObject smoke = Instantiate(blackSmokePrefab,
            firePosition + Vector3.up * 0.25f, Quaternion.identity);
        smoke.name = "Forest Fire Black Smoke";
        smoke.transform.localScale = Vector3.one * 1.2f;
        return smoke;
    }

    private IEnumerator FadeOutExtinguishSmoke(GameObject smoke)
    {
        // Let the black plume remain visible briefly, then stop emission and
        // leave existing particles to fade naturally.
        yield return new WaitForSeconds(1.5f);
        if (smoke == null)
            yield break;

        ParticleSystem[] particleSystems = smoke.GetComponentsInChildren<ParticleSystem>(true);
        float cleanupDelay = 2f;
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem particleSystem = particleSystems[i];
            ParticleSystem.MainModule main = particleSystem.main;
            cleanupDelay = Mathf.Max(cleanupDelay, main.startLifetime.constantMax + 0.5f);
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        Destroy(smoke, cleanupDelay);
    }

    private bool IsNearLake(Vector3 position)
    {
        Vector3 closestPoint = lakeBounds.ClosestPoint(position);
        return HorizontalDistance(position, closestPoint) <= lakeInteractionRadius;
    }

    private void UpdateEventPrompt()
    {
        if (eventText == null)
            return;

        int nearbyFire = FindNearestFireIndex();
        bool nearLake = IsNearLake(player.position);
        if (nearbyFire >= 0)
            eventText.text = waterCount > 0
                ? "Carry water to put out the fire"
                : playersInLake.Contains(player)
                    ? "Collect water at the lake, then return to the fire"
                    : $"Enter the lake to collect water   •   FIRES {activeFires.Count}";
        else if (nearLake)
            eventText.text = waterCount < WaterCapacity
                ? "Collecting water at the lake"
                : $"Water ready   •   Carry a sphere into a fire   •   FIRES {activeFires.Count}";
        else if (Time.unscaledTime >= messageUntil)
            eventText.text = $"Forest fire spreading   •   FIRES {activeFires.Count}";
    }

    private void BuildEventUI()
    {
        GameObject canvasObject = new GameObject("Forest Fire Notification UI",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 160;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        eventCanvasRect = canvasObject.GetComponent<RectTransform>();

        warningImage = CreateImage(canvas.transform, "Forest Fire Warning", new Vector2(250f, 250f),
            new Vector2(0f, -140f));
        warningImageRect = warningImage.rectTransform;
        warningImage.sprite = Resources.Load<Sprite>(WarningSpriteResource);
        warningImage.preserveAspect = true;
        warningImage.raycastTarget = false;
        warningImage.gameObject.SetActive(true);
        if (warningImage.sprite == null)
            Debug.LogError("WarningForestFire.png is missing as a Sprite under Assets/Resources.", this);

        eventText = CreateText(canvas.transform, new Vector2(600f, 44f), new Vector2(0f, -400f));
        eventText.text = "Forest fire!";
        eventText.color = new Color(0.18f, 0.13f, 0.08f);
        eventText.fontSize = 18f;
        eventText.fontSizeMin = 14f;
        eventText.fontSizeMax = 18f;

        RectTransform textRect = eventText.rectTransform;
        GameObject panelObject = new GameObject("Forest Fire Message Panel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        eventMessagePanel = panelObject;
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(canvas.transform, false);
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.sizeDelta = new Vector2(640f, 52f);
        panelRect.anchoredPosition = new Vector2(0f, -68f);
        Image panel = panelObject.GetComponent<Image>();
        panel.color = new Color(0.98f, 0.9f, 0.68f, 0.96f);
        panel.raycastTarget = false;
        Outline outline = panelObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.28f, 0.19f, 0.1f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);
        textRect.SetParent(panelRect, false);
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(10f, 6f);
        textRect.offsetMax = new Vector2(-10f, -6f);

        fireRewardNotificationText = CreateText(canvas.transform,
            new Vector2(360f, 42f), new Vector2(0f, -128f));
        fireRewardNotificationText.name = "Fire Reward Notification";
        fireRewardNotificationText.fontSize = 24f;
        fireRewardNotificationText.fontSizeMin = 20f;
        fireRewardNotificationText.fontSizeMax = 24f;
        fireRewardNotificationText.color = new Color(0.16f, 0.43f, 0.18f, 1f);
        fireRewardNotificationText.gameObject.SetActive(false);

        directionArrowText = CreateText(canvas.transform, new Vector2(76f, 76f), Vector2.zero);
        directionArrowText.name = "Direction To Nearest Fire";
        directionArrowText.text = "▲";
        directionArrowText.fontSize = 60f;
        directionArrowText.fontSizeMin = 46f;
        directionArrowText.fontSizeMax = 60f;
        directionArrowText.color = new Color(0.82f, 0.12f, 0.06f, 1f);
        directionArrowRect = directionArrowText.rectTransform;
        directionArrowRect.anchorMin = new Vector2(0.5f, 0.5f);
        directionArrowRect.anchorMax = new Vector2(0.5f, 0.5f);
        directionArrowRect.pivot = new Vector2(0.5f, 0.5f);
        directionArrowRect.anchoredPosition = Vector2.zero;
        directionArrowRect.SetAsLastSibling();
        directionArrowRect.gameObject.SetActive(false);
    }

    private void UpdateDirectionArrow()
    {
        if (directionArrowRect == null)
            return;

        if (!initialFireSeen)
        {
            directionArrowRect.gameObject.SetActive(false);
            return;
        }

        int fireIndex = FindNearestFireIndexForDirection();
        if (fireIndex < 0 || fireIndex >= activeFires.Count || activeFires[fireIndex] == null)
        {
            directionArrowRect.gameObject.SetActive(false);
            return;
        }

        Camera camera = Camera.main;
        if (camera == null || eventCanvasRect == null)
        {
            directionArrowRect.gameObject.SetActive(false);
            return;
        }

        Vector3 targetScreen = camera.WorldToScreenPoint(activeFires[fireIndex].transform.position +
            Vector3.up * 1.2f);
        bool isOnScreen = targetScreen.z > camera.nearClipPlane &&
                          targetScreen.x >= 0f && targetScreen.x <= Screen.width &&
                          targetScreen.y >= 0f && targetScreen.y <= Screen.height;

        if (isOnScreen)
        {
            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(eventCanvasRect,
                new Vector2(targetScreen.x, targetScreen.y), null, out localPoint))
            {
                directionArrowRect.anchoredPosition = localPoint + Vector2.up * 42f;
                directionArrowRect.localRotation = Quaternion.identity;
                directionArrowText.text = "▼";
            }
        }
        else
        {
            Vector2 direction = new Vector2(targetScreen.x - Screen.width * 0.5f,
                targetScreen.y - Screen.height * 0.5f);
            if (targetScreen.z <= 0f)
                direction = -direction;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector2.up;
            direction.Normalize();

            float halfWidth = Mathf.Max(1f, eventCanvasRect.rect.width * 0.5f - 56f);
            float halfHeight = Mathf.Max(1f, eventCanvasRect.rect.height * 0.5f - 56f);
            float xScale = Mathf.Abs(direction.x) > 0.001f
                ? halfWidth / Mathf.Abs(direction.x)
                : float.PositiveInfinity;
            float yScale = Mathf.Abs(direction.y) > 0.001f
                ? halfHeight / Mathf.Abs(direction.y)
                : float.PositiveInfinity;
            directionArrowRect.anchoredPosition = direction * Mathf.Min(xScale, yScale);
            directionArrowRect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
            directionArrowText.text = "▲";
        }

        directionArrowRect.gameObject.SetActive(true);
    }

    private void UpdateFireNotification()
    {
        if (warningImage == null || warningImageRect == null)
            return;

        if (Time.unscaledTime < warningUntil)
        {
            warningImageRect.anchorMin = new Vector2(0.5f, 1f);
            warningImageRect.anchorMax = new Vector2(0.5f, 1f);
            warningImageRect.pivot = new Vector2(0.5f, 1f);
            warningImageRect.anchoredPosition = new Vector2(0f, -140f);
            warningImageRect.localRotation = Quaternion.identity;
            warningImage.gameObject.SetActive(true);
            return;
        }

        if (initialFireSeen)
        {
            warningImage.gameObject.SetActive(false);
            return;
        }

        if (initialFireNotificationTarget == null)
        {
            initialFireSeen = true;
            warningImage.gameObject.SetActive(false);
            return;
        }

        GameManager gameManager = GameManager.Instance;
        if (gameManager != null)
        {
            if (gameManager.IsBurningTargetVisibleToPlayer(initialFireNotificationTarget))
            {
                initialFireSeen = true;
                gameManager.NotifyBurningTargetEnded(initialFireNotificationTarget);
                warningImage.gameObject.SetActive(false);
                return;
            }

            if (Time.unscaledTime >= nextBurningAlertRefresh)
            {
                gameManager.NotifyOpenBurningStarted(initialFireNotificationTarget);
                nextBurningAlertRefresh = Time.unscaledTime + 5f;
            }

            warningImage.gameObject.SetActive(false);
            return;
        }

        Camera camera = Camera.main;
        if (camera == null || eventCanvasRect == null)
        {
            warningImage.gameObject.SetActive(true);
            return;
        }

        if (IsFireVisible(camera, initialFireNotificationTarget))
        {
            initialFireSeen = true;
            warningImage.gameObject.SetActive(false);
            return;
        }

        PositionWarningAtScreenEdge(camera, initialFireNotificationTarget);
        warningImage.gameObject.SetActive(true);
    }

    private bool IsFireVisible(Camera camera, Transform target)
    {
        Vector3 targetPoint = target.position + Vector3.up * 1.2f;
        Vector3 viewport = camera.WorldToViewportPoint(targetPoint);
        if (viewport.z <= camera.nearClipPlane || viewport.x < 0f || viewport.x > 1f ||
            viewport.y < 0f || viewport.y > 1f)
            return false;

        Vector3 toTarget = targetPoint - camera.transform.position;
        float distance = toTarget.magnitude;
        if (distance <= 0.01f)
            return true;

        RaycastHit[] hits = Physics.RaycastAll(camera.transform.position, toTarget / distance,
            distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        RaycastHit closestHit = default;
        bool foundBlockingHit = false;
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsPlayerTransform(hits[i].transform))
                continue;

            if (!foundBlockingHit || hits[i].distance < closestHit.distance)
            {
                closestHit = hits[i];
                foundBlockingHit = true;
            }
        }

        return !foundBlockingHit || closestHit.transform == target ||
               closestHit.transform.IsChildOf(target);
    }

    private void PositionWarningAtScreenEdge(Camera camera, Transform target)
    {
        Vector3 screen = camera.WorldToScreenPoint(target.position + Vector3.up * 1.2f);
        Vector2 direction = new Vector2(screen.x - Screen.width * 0.5f,
            screen.y - Screen.height * 0.5f);
        if (screen.z < 0f)
            direction = -direction;
        if (direction.sqrMagnitude < 0.001f)
            direction = Vector2.up;
        direction.Normalize();

        warningImageRect.anchorMin = new Vector2(0.5f, 0.5f);
        warningImageRect.anchorMax = new Vector2(0.5f, 0.5f);
        warningImageRect.pivot = new Vector2(0.5f, 0.5f);
        float halfWidth = Mathf.Max(1f, eventCanvasRect.rect.width * 0.5f - 140f);
        float halfHeight = Mathf.Max(1f, eventCanvasRect.rect.height * 0.5f - 140f);
        float xScale = Mathf.Abs(direction.x) > 0.001f
            ? halfWidth / Mathf.Abs(direction.x)
            : float.PositiveInfinity;
        float yScale = Mathf.Abs(direction.y) > 0.001f
            ? halfHeight / Mathf.Abs(direction.y)
            : float.PositiveInfinity;
        warningImageRect.anchoredPosition = direction * Mathf.Min(xScale, yScale);
        warningImageRect.localRotation = Quaternion.identity;
    }

    private static bool IsPlayerTransform(Transform candidate)
    {
        for (Transform current = candidate; current != null; current = current.parent)
        {
            if (current.CompareTag("Player"))
                return true;
        }

        return false;
    }

    private static Image CreateImage(Transform parent, string objectName, Vector2 size, Vector2 position)
    {
        GameObject imageObject = new GameObject(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(Image));
        RectTransform rect = imageObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return imageObject.GetComponent<Image>();
    }

    private static TextMeshProUGUI CreateText(Transform parent, Vector2 size, Vector2 position)
    {
        GameObject textObject = new GameObject("Forest Fire Event Text", typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform rect = textObject.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = new Vector2(0.5f, 1f);
        rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.sizeDelta = size;
        rect.anchoredPosition = position;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 25f;
        text.fontSizeMin = 18f;
        text.fontSizeMax = 25f;
        text.enableAutoSizing = true;
        text.alignment = TextAlignmentOptions.Center;
        text.fontStyle = FontStyles.Bold;
        text.raycastTarget = false;
        return text;
    }

    private static Transform FindPlayer()
    {
        GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
        return playerObject != null ? playerObject.transform : null;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }

    private void OnDestroy()
    {
        if (activeInstance == this)
        {
            GameManager.Instance?.RestoreTrashStatus();
            activeInstance = null;
            eventStarted = false;
        }
    }
}
