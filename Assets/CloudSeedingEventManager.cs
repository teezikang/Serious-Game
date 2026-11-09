using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Starts the cloud-seeding phase after the forest fire event is contained.</summary>
public class CloudSeedingEventManager : MonoBehaviour
{
    private const int MaximumAllowedClouds = 5;

    [Header("Laboratory")]
    [SerializeField] private Transform labBuilding;
    [SerializeField, Min(1f)] private float labApproachRadius = 10f;
    [SerializeField, Min(0.5f)] private float rooftopLandingRadius = 3.5f;
    [SerializeField, Min(0.5f)] private float rooftopClearance = 2f;
    [SerializeField, Min(0.1f)] private float rooftopArrivalTolerance = 1.25f;
    [SerializeField, Min(1)] private int seedPurchasePrice = 100;

    [Header("Seed Dose Visuals")]
    [SerializeField, Min(1)] private int seedDosesPerPurchase = 5;
    [SerializeField, Min(0.1f)] private float seedOrbOrbitRadius = 2.1f;
    [SerializeField, Min(0f)] private float seedOrbOrbitHeight = 1.3f;
    [SerializeField, Min(0.05f)] private float seedOrbSize = 0.34f;
    [SerializeField, Min(0f)] private float seedOrbOrbitSpeed = 95f;

    [Header("Lab Sign")]
    [SerializeField, Min(0.001f)] private float labSignScale = 0.011f;
    [SerializeField, Min(0f)] private float labSignHeightOffset = 1.2f;

    [Header("Clouds")]
    [SerializeField] private GameObject cloudPrefab;
    [SerializeField, Range(1, MaximumAllowedClouds)] private int maximumCloudCount = 5;
    [SerializeField, Min(0.1f)] private float cloudSpawnInterval = 1.5f;
    [SerializeField, Min(1f)] private float cloudHeightAboveGround = 7.5f;
    [SerializeField] private Vector3 cloudColliderSize = new Vector3(9f, 6f, 9f);
    [SerializeField, Min(0f)] private float cloudDriftSpeed = 1.2f;
    [SerializeField, Min(0f)] private float cloudVerticalBob = 0.25f;
    [SerializeField, Min(0f)] private float cloudBoundaryPadding = 2f;
    [SerializeField, Min(0.1f)] private float rainDuration = 4f;
    [SerializeField, Min(0f)] private float cloudDelayAfterRain = 5f;
    [SerializeField, Min(0f)] private float aqiReductionPerCloud = 10f;

    private static CloudSeedingEventManager activeInstance;
    private static bool eventStarted;

    private readonly List<CloudSeedingCloud> activeClouds = new List<CloudSeedingCloud>();
    private readonly List<Bounds> forestZones = new List<Bounds>();

    private Transform player;
    [SerializeField] private Transform playgroundRoot;
    private Bounds playgroundLocalBounds;
    private bool hasPlaygroundBounds;
    private PlayerController playerController;
    private HelicopterFlightController helicopter;
    private Transform labLandingTarget;
    private Vector3 labRoofPosition;
    private Bounds labBuildingBounds;
    private bool hasLabBuildingBounds;
    private GameObject labTopSign;
    private Canvas labSignCanvas;
    private TextMeshProUGUI labSignCostText;
    private TextMeshProUGUI labSignProgressText;
    private string lastLabSignText;
    private Transform seedOrbRoot;
    private Material seedOrbMaterial;
    private readonly List<Transform> seedDoseOrbs = new List<Transform>();
    private Coroutine cloudPopulationRoutine;
    private Material rainMaterial;
    private Material labPadMaterial;

    private Canvas notificationCanvas;
    private RectTransform notificationCanvasRect;
    private RectTransform directionArrowRect;
    private TextMeshProUGUI directionArrowText;
    private TextMeshProUGUI statusText;
    private GameObject purchasePanel;
    private TextMeshProUGUI purchasePriceText;
    private TextMeshProUGUI purchaseProgressText;
    private string persistentStatus;
    private float temporaryStatusUntil;

    private bool hasStarted;
    private bool labLiftStarted;
    private bool mustLeaveLabToRetry;
    private bool purchasePromptShown;
    private bool seedsPurchased;
    private bool returnedToFlightHeight;
    private bool helicopterInsideLabTrigger;
    private int remainingSeedDoses;
    private int moneyPaidTowardSeeds;

    public static bool SeedsHaveBeenPurchased => activeInstance != null && activeInstance.seedsPurchased;
    public static bool HasSeedDoses => activeInstance != null && activeInstance.remainingSeedDoses > 0;

    private void Start()
    {
        if (labBuilding == null)
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                string objectName = allTransforms[i].name.ToLowerInvariant();
                if (objectName == "lab" || objectName == "laboratory" ||
                    objectName.Contains("laboratory") || objectName.Contains("cloud seeding lab"))
                {
                    labBuilding = allTransforms[i];
                    break;
                }
            }
        }

        if (labBuilding != null && TryGetTopOfBuilding(labBuilding, out labRoofPosition))
            CreateLabTopSign();
    }

    public static void BeginAfterForestFireEvent()
    {
        if (eventStarted)
            return;

        CloudSeedingEventManager manager = FindFirstObjectByType<CloudSeedingEventManager>();
        if (manager == null)
        {
            Debug.LogError("Cloud Seeding Event Manager is missing from the GameScene hierarchy.");
            return;
        }

        manager.BeginEvent();
    }

    public static void OnHelicopterLoweredFromLab()
    {
        if (activeInstance != null)
            activeInstance.HandleHelicopterLoweredFromLab();
    }

    public static void OnHelicopterRaisedForSeeding()
    {
        if (activeInstance != null)
            activeInstance.HandleHelicopterRaisedForSeeding();
    }

    private void BeginEvent()
    {
        if (hasStarted || eventStarted)
            return;

        hasStarted = true;
        eventStarted = true;
        activeInstance = this;
        player = GameObject.FindGameObjectWithTag("Player")?.transform;
        playerController = player != null ? player.GetComponent<PlayerController>() : null;
        helicopter = FindFirstObjectByType<HelicopterFlightController>();

        CreateMaterials();
        FindForestZones();
        ResolvePlaygroundBounds();
        ResolveLabLandingTarget();
        BuildNotificationUI();
        SetPersistentStatus("Follow the arrow to the Cloud Seeding Lab");

        if (labLandingTarget == null)
            Debug.LogError("Could not choose a lab building. Assign Lab Building on the Cloud Seeding Event Manager.", this);
    }

    private void Update()
    {
        UpdateLabTopSignFacing();
        if (!hasStarted)
            return;

        UpdateMovingClouds();

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            player = playerObject != null ? playerObject.transform : null;
        }

        if (playerController == null && player != null)
            playerController = player.GetComponent<PlayerController>();
        if (helicopter == null)
            helicopter = FindFirstObjectByType<HelicopterFlightController>();
        UpdateSeedDoseOrbs();

        // Do not rely exclusively on OnTriggerExit here. The hidden player collider is
        // disabled during the rooftop lift, and the helicopter is a compound collider;
        // Unity can therefore miss the final exit callback. The approach trigger is a
        // square in world space, so poll its horizontal bounds as a reliable fallback.
        bool outsideLabApproach = helicopter != null &&
            IsOutsideLabApproach(helicopter.transform.position, 1.5f);
        if (outsideLabApproach)
            helicopterInsideLabTrigger = false;
        if (labLiftStarted && NeedsSeedPurchase && outsideLabApproach)
        {
            CancelLabApproach(helicopter);
        }

        if (temporaryStatusUntil > 0f && Time.unscaledTime >= temporaryStatusUntil)
        {
            temporaryStatusUntil = 0f;
            if (statusText != null)
                statusText.text = persistentStatus;
        }

        UpdatePurchasePanel();

        if (player == null || helicopter == null || labLandingTarget == null)
            return;

        if (!seedsPurchased || remainingSeedDoses <= 0)
        {
            if (mustLeaveLabToRetry && IsOutsideLabApproach(helicopter.transform.position, 1.5f))
            {
                mustLeaveLabToRetry = false;
                SetPersistentStatus("Return to the Cloud Seeding Lab to buy the seeds");
            }

            if (mustLeaveLabToRetry)
            {
                HideDirectionArrow();
                return;
            }
            UpdateDirectionArrow(labRoofPosition);
            return;
        }

        if (!returnedToFlightHeight)
        {
            HideDirectionArrow();
            return;
        }

        CloudSeedingCloud targetCloud = FindNearestUnseededCloud();
        if (targetCloud != null)
            UpdateDirectionArrow(targetCloud.transform.position);
        else
            HideDirectionArrow();
    }

    internal void OnLabOuterTrigger(HelicopterFlightController enteringHelicopter)
    {
        if (!hasStarted || enteringHelicopter == null)
            return;

        if (helicopter != null && enteringHelicopter != helicopter)
            return;

        helicopterInsideLabTrigger = true;
        if ((seedsPurchased && remainingSeedDoses > 0) || mustLeaveLabToRetry)
            return;

        helicopter = enteringHelicopter;
        CollectSeedPayment();
        if (!labLiftStarted || !enteringHelicopter.IsRaisedForCloudLab)
        {
            labLiftStarted = true;
            purchasePromptShown = false;
            helicopter.RaiseForCloudLab(labRoofPosition.y + rooftopClearance,
                labRoofPosition.y);
            SetPersistentStatus("Helicopter raised in place. Use arrow keys to fly into the center marker.");
            Debug.Log("Lab approach trigger entered. Helicopter is rising in place; fly to the center trigger to land.", this);
        }
    }

    internal void OnLabLandingTrigger(HelicopterFlightController enteringHelicopter)
    {
        if (!hasStarted || enteringHelicopter == null || enteringHelicopter != helicopter)
            return;

        helicopterInsideLabTrigger = true;
        TryLandAndPurchase(enteringHelicopter);
    }

    private void TryLandAndPurchase(HelicopterFlightController enteringHelicopter)
    {
        if ((seedsPurchased && remainingSeedDoses > 0) || mustLeaveLabToRetry || !labLiftStarted ||
            enteringHelicopter == null || enteringHelicopter != helicopter ||
            !enteringHelicopter.IsRaisedForCloudLab ||
            Mathf.Abs(enteringHelicopter.transform.position.y -
                      (labRoofPosition.y + rooftopClearance)) > rooftopArrivalTolerance)
            return;

        // Keep the hidden player body attached at altitude even if the player cannot
        // afford the seeds yet; pressing A will safely return it to the surface.
        playerController?.HoldAtHelicopterSeedingHeight();
        TryPurchaseSeeds();
    }

    private void CollectSeedPayment()
    {
        if (playerController == null || !NeedsSeedPurchase)
            return;

        int amountStillNeeded = Mathf.Max(0, seedPurchasePrice - moneyPaidTowardSeeds);
        int amountToPay = Mathf.Min(playerController.CheckMoney(), amountStillNeeded);
        if (amountToPay <= 0 || !playerController.TrySpendMoney(amountToPay))
            return;

        moneyPaidTowardSeeds += amountToPay;
        UpdateLabSignText();
        UpdatePurchasePanel();
    }

    internal void OnLabBuildingTriggerExit(HelicopterFlightController leavingHelicopter)
    {
        if (leavingHelicopter == null || leavingHelicopter != helicopter)
            return;

        // Some child colliders can report an early exit while another part of the
        // vehicle is still over the Lab. Confirm the helicopter actually left the
        // approach area before beginning the return.
        if (!IsOutsideLabApproach(leavingHelicopter.transform.position, 0.25f))
            return;

        helicopterInsideLabTrigger = false;
        if (NeedsSeedPurchase)
            CancelLabApproach(leavingHelicopter);
    }

    private bool IsOutsideLabApproach(Vector3 worldPosition, float margin)
    {
        float centerX = hasLabBuildingBounds ? labBuildingBounds.center.x : labRoofPosition.x;
        float centerZ = hasLabBuildingBounds ? labBuildingBounds.center.z : labRoofPosition.z;
        float halfWidth = hasLabBuildingBounds ? labBuildingBounds.extents.x : 0f;
        float halfDepth = hasLabBuildingBounds ? labBuildingBounds.extents.z : 0f;
        return Mathf.Abs(worldPosition.x - centerX) > halfWidth + labApproachRadius + margin ||
               Mathf.Abs(worldPosition.z - centerZ) > halfDepth + labApproachRadius + margin;
    }

    private void CancelLabApproach(HelicopterFlightController leavingHelicopter)
    {
        if (leavingHelicopter == null || leavingHelicopter != helicopter ||
            !labLiftStarted || !NeedsSeedPurchase)
            return;

        helicopterInsideLabTrigger = false;
        // Leaving the Lab before buying seeds cancels the rooftop approach and restores normal flight height.
        leavingHelicopter.ReturnFromCloudLab();
        labLiftStarted = false;
        purchasePromptShown = false;
        mustLeaveLabToRetry = false;
        SetPersistentStatus($"Enter the Lab to buy {seedDosesPerPurchase} seed doses for ${seedPurchasePrice}");
    }

    private bool NeedsSeedPurchase => !seedsPurchased || remainingSeedDoses <= 0;

    private void TryPurchaseSeeds()
    {
        if (playerController == null || !NeedsSeedPurchase)
            return;

        CollectSeedPayment();
        if (moneyPaidTowardSeeds >= seedPurchasePrice)
        {
            seedsPurchased = true;
            remainingSeedDoses = Mathf.Max(1, seedDosesPerPurchase);
            moneyPaidTowardSeeds = 0;
            returnedToFlightHeight = true;
            playerController.HoldAtHelicopterSeedingHeight();
            RefreshSeedDoseOrbs();
            SetPersistentStatus("Seeds purchased. Press A to return to the surface");
            if (cloudPopulationRoutine == null)
                cloudPopulationRoutine = StartCoroutine(MaintainCloudPopulation());
            return;
        }

        if (!purchasePromptShown)
        {
            int shortBy = Mathf.Max(0, seedPurchasePrice - moneyPaidTowardSeeds);
            SetPersistentStatus($"{seedDosesPerPurchase} seed doses cost ${seedPurchasePrice}. Need ${shortBy} more");
            purchasePromptShown = true;
        }
    }

    private void HandleHelicopterLoweredFromLab()
    {
        if (!seedsPurchased)
        {
            labLiftStarted = false;
            purchasePromptShown = false;
            mustLeaveLabToRetry = true;
            SetPersistentStatus($"Seed doses cost ${seedPurchasePrice}. Leave the Lab, collect money, and return");
            return;
        }

        if (remainingSeedDoses <= 0)
        {
            labLiftStarted = false;
            purchasePromptShown = false;
            mustLeaveLabToRetry = true;
            SetPersistentStatus("At the surface. Fly out of the Lab marker, then return to refill seed doses");
            return;
        }

        returnedToFlightHeight = false;
        HideDirectionArrow();
        SetPersistentStatus("At the surface. Press A to fly back to cloud-seeding height");
    }

    private void HandleHelicopterRaisedForSeeding()
    {
        if (!seedsPurchased)
            return;

        returnedToFlightHeight = true;
        SetPersistentStatus(remainingSeedDoses > 0
            ? "Fly through a cloud to seed it and lower the AQI"
            : $"No seed doses left. Return to the Lab for {seedDosesPerPurchase} more for ${seedPurchasePrice}");
    }

    private IEnumerator MaintainCloudPopulation()
    {
        while (seedsPurchased)
        {
            activeClouds.RemoveAll(cloud => cloud == null);
            int cloudLimit = Mathf.Clamp(maximumCloudCount, 1, MaximumAllowedClouds);
            if (activeClouds.Count < cloudLimit)
                SpawnCloud();

            yield return new WaitForSecondsRealtime(cloudSpawnInterval);
        }

        cloudPopulationRoutine = null;
    }

    private void SpawnCloud()
    {
        if (cloudPrefab == null)
        {
            Debug.LogError("Cloud prefab is not assigned on the Cloud Seeding Event Manager.", this);
            return;
        }

        Vector3 position = ChooseCloudPosition();
        GameObject cloudObject = new GameObject($"Cloud Seeding Cloud {activeClouds.Count + 1}");
        cloudObject.transform.SetParent(transform, true);
        cloudObject.transform.position = position;

        // Keep the trigger root at unit scale so its rain particles use world-sized settings.
        // The custom-shader prefab is a visual child sized to the same gameplay volume.
        GameObject cloudVisual = Instantiate(cloudPrefab, cloudObject.transform);
        cloudVisual.name = "Cloud Visual";
        cloudVisual.transform.localPosition = Vector3.zero;
        cloudVisual.transform.localScale = cloudColliderSize;
        Collider[] visualColliders = cloudVisual.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < visualColliders.Length; i++)
        {
            if (visualColliders[i] != null)
                visualColliders[i].enabled = false;
        }

        BoxCollider trigger = cloudObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = Vector3.zero;
        trigger.size = cloudColliderSize;

        Rigidbody body = cloudObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.detectCollisions = true;

        CloudSeedingCloud cloud = cloudObject.GetComponent<CloudSeedingCloud>();
        if (cloud == null)
            cloud = cloudObject.AddComponent<CloudSeedingCloud>();
        Vector2 wind = Random.insideUnitCircle;
        if (wind.sqrMagnitude < 0.001f)
            wind = Vector2.right;
        wind.Normalize();
        Vector3 windDirection = new Vector3(wind.x, 0f, wind.y);
        cloud.Initialize(this, rainMaterial, cloudColliderSize, rainDuration, cloudDelayAfterRain,
            windDirection, cloudDriftSpeed, cloudVerticalBob);
        activeClouds.Add(cloud);
    }

    internal void OnHelicopterContactWithCloud(CloudSeedingCloud cloud,
        HelicopterFlightController contactingHelicopter)
    {
        if (!seedsPurchased || cloud == null || contactingHelicopter == null ||
            !contactingHelicopter.IsFlying ||
            (playerController != null && playerController.IsMovingForHelicopterFlight))
            return;

        if (remainingSeedDoses <= 0)
        {
            SetPersistentStatus($"No seed doses left. Return to the Lab for {seedDosesPerPurchase} more for ${seedPurchasePrice}");
            return;
        }

        if (!cloud.BeginRain())
            return;

        remainingSeedDoses--;
        RefreshSeedDoseOrbs();
        GameManager.Instance?.DecreaseAQIFromCloudSeeding(aqiReductionPerCloud);
        if (remainingSeedDoses > 0)
            ShowTemporaryStatus($"Cloud seeded! AQI -{Mathf.RoundToInt(aqiReductionPerCloud)}", 3f);
        else
            SetPersistentStatus($"Cloud seeded! AQI -{Mathf.RoundToInt(aqiReductionPerCloud)}. Return to the Lab for a refill");
    }

    internal void OnCloudFinishedRaining(CloudSeedingCloud cloud)
    {
        activeClouds.Remove(cloud);
    }

    private CloudSeedingCloud FindNearestUnseededCloud()
    {
        if (helicopter == null)
            return null;

        CloudSeedingCloud nearest = null;
        float nearestDistance = float.PositiveInfinity;
        for (int i = 0; i < activeClouds.Count; i++)
        {
            CloudSeedingCloud cloud = activeClouds[i];
            if (cloud == null || cloud.IsSeeded)
                continue;

            float distance = HorizontalDistance(helicopter.transform.position, cloud.transform.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = cloud;
            }
        }

        return nearest;
    }

    private Vector3 ChooseCloudPosition()
    {
        Vector3 position;
        if (forestZones.Count == 0)
        {
            position = labRoofPosition + new Vector3(Random.Range(-25f, 25f), 0f,
                Random.Range(-25f, 25f));
        }
        else
        {
            Bounds zone = forestZones[Random.Range(0, forestZones.Count)];
            float marginX = Mathf.Min(2f, zone.size.x * 0.1f);
            float marginZ = Mathf.Min(2f, zone.size.z * 0.1f);
            position = new Vector3(
                Random.Range(zone.min.x + marginX, zone.max.x - marginX),
                0f,
                Random.Range(zone.min.z + marginZ, zone.max.z - marginZ));
        }

        float groundHeight = FindGroundHeight(position);
        float cloudLayerHeight = Mathf.Max(groundHeight + cloudHeightAboveGround,
            labRoofPosition.y + rooftopClearance);
        position.y = cloudLayerHeight + Random.Range(0f, 1.5f);
        return ClampCloudPositionToPlayground(position);
    }

    private void ResolvePlaygroundBounds()
    {
        hasPlaygroundBounds = false;
        if (playgroundRoot == null)
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                if (string.Equals(allTransforms[i].name, "PlayGround", System.StringComparison.OrdinalIgnoreCase))
                {
                    playgroundRoot = allTransforms[i];
                    break;
                }
            }
        }

        if (playgroundRoot == null)
        {
            Debug.LogWarning("Could not find the PlayGround object. Moving clouds will not despawn at its boundary.", this);
            return;
        }

        MeshFilter meshFilter = playgroundRoot.GetComponent<MeshFilter>();
        if (meshFilter != null && meshFilter.sharedMesh != null)
        {
            playgroundLocalBounds = meshFilter.sharedMesh.bounds;
            hasPlaygroundBounds = true;
            return;
        }

        BoxCollider box = playgroundRoot.GetComponent<BoxCollider>();
        if (box != null)
        {
            playgroundLocalBounds = new Bounds(box.center, box.size);
            hasPlaygroundBounds = true;
        }
    }

    private Vector3 ClampCloudPositionToPlayground(Vector3 worldPosition)
    {
        if (!hasPlaygroundBounds || playgroundRoot == null)
            return worldPosition;

        GetPlaygroundMargins(out float marginX, out float marginZ);
        Vector3 localPosition = playgroundRoot.InverseTransformPoint(worldPosition);
        float minX = playgroundLocalBounds.min.x + marginX;
        float maxX = playgroundLocalBounds.max.x - marginX;
        float minZ = playgroundLocalBounds.min.z + marginZ;
        float maxZ = playgroundLocalBounds.max.z - marginZ;
        if (minX > maxX)
            minX = maxX = playgroundLocalBounds.center.x;
        if (minZ > maxZ)
            minZ = maxZ = playgroundLocalBounds.center.z;

        localPosition.x = Mathf.Clamp(localPosition.x, minX, maxX);
        localPosition.z = Mathf.Clamp(localPosition.z, minZ, maxZ);
        float originalWorldY = worldPosition.y;
        worldPosition = playgroundRoot.TransformPoint(localPosition);
        worldPosition.y = originalWorldY;
        return worldPosition;
    }

    private bool IsCloudInsidePlayground(Vector3 worldPosition)
    {
        if (!hasPlaygroundBounds || playgroundRoot == null)
            return true;

        GetPlaygroundMargins(out float marginX, out float marginZ);
        Vector3 localPosition = playgroundRoot.InverseTransformPoint(worldPosition);
        return localPosition.x >= playgroundLocalBounds.min.x + marginX &&
               localPosition.x <= playgroundLocalBounds.max.x - marginX &&
               localPosition.z >= playgroundLocalBounds.min.z + marginZ &&
               localPosition.z <= playgroundLocalBounds.max.z - marginZ;
    }

    private void GetPlaygroundMargins(out float marginX, out float marginZ)
    {
        Vector3 localHalfWidth = playgroundRoot.InverseTransformVector(
            Vector3.right * (Mathf.Abs(cloudColliderSize.x) * 0.5f));
        Vector3 localHalfDepth = playgroundRoot.InverseTransformVector(
            Vector3.forward * (Mathf.Abs(cloudColliderSize.z) * 0.5f));
        Vector3 scale = playgroundRoot.lossyScale;
        float localPaddingX = cloudBoundaryPadding / Mathf.Max(0.001f, Mathf.Abs(scale.x));
        float localPaddingZ = cloudBoundaryPadding / Mathf.Max(0.001f, Mathf.Abs(scale.z));
        marginX = Mathf.Abs(localHalfWidth.x) + Mathf.Abs(localHalfDepth.x) + localPaddingX;
        marginZ = Mathf.Abs(localHalfWidth.z) + Mathf.Abs(localHalfDepth.z) + localPaddingZ;
    }

    private void UpdateMovingClouds()
    {
        if (!seedsPurchased || !hasPlaygroundBounds)
            return;

        for (int i = activeClouds.Count - 1; i >= 0; i--)
        {
            CloudSeedingCloud cloud = activeClouds[i];
            if (cloud == null)
            {
                activeClouds.RemoveAt(i);
                continue;
            }

            if (cloud.IsSeeded || IsCloudInsidePlayground(cloud.transform.position))
                continue;

            activeClouds.RemoveAt(i);
            Destroy(cloud.gameObject);
        }
    }

    private void FindForestZones()
    {
        forestZones.Clear();
        Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform forest = allTransforms[i];
            if (forest.name != "Jungle")
                continue;

            Renderer[] renderers = forest.GetComponentsInChildren<Renderer>(true);
            bool foundBounds = false;
            Bounds bounds = new Bounds(forest.position, Vector3.one);
            for (int r = 0; r < renderers.Length; r++)
            {
                Renderer renderer = renderers[r];
                if (renderer == null || renderer is ParticleSystemRenderer)
                    continue;

                if (!foundBounds)
                {
                    bounds = renderer.bounds;
                    foundBounds = true;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }

            if (foundBounds && bounds.size.x > 2f && bounds.size.z > 2f)
                forestZones.Add(bounds);
        }
    }

    private void ResolveLabLandingTarget()
    {
        if (labBuilding == null)
        {
            Transform[] allTransforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            for (int i = 0; i < allTransforms.Length; i++)
            {
                string objectName = allTransforms[i].name.ToLowerInvariant();
                if (objectName == "lab" || objectName == "laboratory" ||
                    objectName.Contains("laboratory") || objectName.Contains("cloud seeding lab"))
                {
                    labBuilding = allTransforms[i];
                    break;
                }
            }
        }

        if (labBuilding != null && TryGetTopOfBuilding(labBuilding, out labRoofPosition))
        {
            CreateLabLandingMarker();
            CreateLabTopSign();
            return;
        }

        Transform buildingGroup = null;
        Transform[] transforms = FindObjectsByType<Transform>(FindObjectsInactive.Exclude,
            FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i].name == "Building Group")
            {
                buildingGroup = transforms[i];
                break;
            }
        }

        if (buildingGroup == null)
            return;

        Renderer[] candidates = buildingGroup.GetComponentsInChildren<Renderer>(true);
        Renderer tallestBuildingPart = null;
        float tallestHeight = 6f;
        for (int i = 0; i < candidates.Length; i++)
        {
            Renderer candidate = candidates[i];
            if (candidate == null || candidate is ParticleSystemRenderer ||
                candidate.bounds.size.y <= tallestHeight || candidate.bounds.size.x > 80f ||
                candidate.bounds.size.z > 80f)
                continue;

            string candidateName = candidate.name.ToLowerInvariant();
            if (candidateName.Contains("tree") || candidateName.Contains("route") ||
                candidateName.Contains("road") || candidateName.Contains("floor"))
                continue;

            tallestHeight = candidate.bounds.size.y;
            tallestBuildingPart = candidate;
        }

        if (tallestBuildingPart == null)
            return;

        labBuilding = tallestBuildingPart.transform;
        Bounds roofBounds = tallestBuildingPart.bounds;
        labBuildingBounds = roofBounds;
        hasLabBuildingBounds = true;
        labRoofPosition = new Vector3(roofBounds.center.x, roofBounds.max.y, roofBounds.center.z);
        CreateLabLandingMarker();
        CreateLabTopSign();
    }

    private bool TryGetTopOfBuilding(Transform building, out Vector3 roofPosition)
    {
        Renderer[] renderers = building.GetComponentsInChildren<Renderer>(true);
        bool found = false;
        Bounds bounds = new Bounds(building.position, Vector3.one);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || renderer is ParticleSystemRenderer)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
                bounds.Encapsulate(renderer.bounds);
        }

        roofPosition = found
            ? new Vector3(bounds.center.x, bounds.max.y, bounds.center.z)
            : Vector3.zero;
        if (found)
        {
            labBuildingBounds = bounds;
            hasLabBuildingBounds = true;
        }
        // Ignore broad scene containers so they cannot become accidental landing surfaces.
        return found && bounds.size.x <= 80f && bounds.size.z <= 80f;
    }

    private void CreateLabLandingMarker()
    {
        GameObject targetObject = new GameObject("Cloud Seeding Lab Landing Target");
        targetObject.transform.SetParent(transform, true);
        targetObject.transform.position = labRoofPosition;
        labLandingTarget = targetObject.transform;

        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "Cloud Seeding Lab Roof Pad";
        pad.transform.SetParent(labLandingTarget, false);
        pad.transform.localPosition = Vector3.up * 0.08f;
        pad.transform.localScale = new Vector3(6f, 0.08f, 6f);
        Collider padCollider = pad.GetComponent<Collider>();
        if (padCollider != null)
        {
            padCollider.enabled = true;
            padCollider.isTrigger = false;
        }
        Renderer padRenderer = pad.GetComponent<Renderer>();
        if (padRenderer != null && labPadMaterial != null)
            padRenderer.sharedMaterial = labPadMaterial;

        GameObject labelObject = new GameObject("Lab Landing Label");
        labelObject.transform.SetParent(labLandingTarget, false);
        labelObject.transform.localPosition = Vector3.up * 0.35f;
        labelObject.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
        TextMesh label = labelObject.AddComponent<TextMesh>();
        label.text = "CLOUD LAB";
        label.anchor = TextAnchor.MiddleCenter;
        label.alignment = TextAlignment.Center;
        label.fontSize = 48;
        label.characterSize = 0.12f;
        label.color = new Color(0.1f, 0.28f, 0.16f, 1f);

        AttachLabBoxTrigger();
    }

    private void CreateLabTopSign()
    {
        if (labTopSign != null)
            return;

        labTopSign = new GameObject("Cloud Seeding Cost Sign", typeof(RectTransform), typeof(Canvas));
        labTopSign.transform.SetParent(transform, true);
        labTopSign.transform.position = labRoofPosition + Vector3.up * labSignHeightOffset;

        labSignCanvas = labTopSign.GetComponent<Canvas>();
        labSignCanvas.renderMode = RenderMode.WorldSpace;
        labSignCanvas.sortingOrder = 20;

        RectTransform canvasRect = labTopSign.GetComponent<RectTransform>();
        canvasRect.sizeDelta = new Vector2(420f, 116f);
        canvasRect.localScale = Vector3.one * labSignScale;

        GameObject panelObject = new GameObject("Toon Cost Sign", typeof(RectTransform),
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

        TextMeshProUGUI moneySymbol = CreateLabSignText(moneyBadgeRect, "Dollar Symbol", 56f,
            new Vector2(0f, -15f), new Vector2(84f, 72f));
        moneySymbol.text = "$";
        moneySymbol.color = Color.white;
        moneySymbol.fontStyle = FontStyles.Bold;

        labSignCostText = CreateLabSignText(panelRect, "Cloud Seeding Cost", 44f,
            new Vector2(58f, -9f), new Vector2(338f, 60f));
        labSignCostText.color = new Color(0.13f, 0.17f, 0.19f, 1f);
        labSignCostText.fontStyle = FontStyles.Bold;

        labSignProgressText = CreateLabSignText(panelRect, "Cloud Seeding Progress", 20f,
            new Vector2(58f, -70f), new Vector2(338f, 34f));
        labSignProgressText.color = new Color(0.29f, 0.24f, 0.14f, 1f);
        labSignProgressText.fontStyle = FontStyles.Bold;

        UpdateLabSignText();
    }

    private TextMeshProUGUI CreateLabSignText(Transform parent, string objectName, float fontSize,
        Vector2 topOffset, Vector2 size)
    {
        GameObject textObject = new GameObject(objectName, typeof(RectTransform),
            typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform textRect = textObject.GetComponent<RectTransform>();
        textRect.SetParent(parent, false);
        textRect.anchorMin = new Vector2(0.5f, 1f);
        textRect.anchorMax = new Vector2(0.5f, 1f);
        textRect.pivot = new Vector2(0.5f, 1f);
        textRect.anchoredPosition = topOffset;
        textRect.sizeDelta = size;

        TextMeshProUGUI text = textObject.GetComponent<TextMeshProUGUI>();
        text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = fontSize;
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMin = fontSize * 0.75f;
        text.fontSizeMax = fontSize;
        text.raycastTarget = false;
        return text;
    }

    private void UpdateLabTopSignFacing()
    {
        if (labTopSign == null)
            return;
        Camera camera = Camera.main;
        if (camera == null)
            return;

        Vector3 awayFromCamera = labTopSign.transform.position - camera.transform.position;
        if (awayFromCamera.sqrMagnitude > 0.001f)
            labTopSign.transform.rotation = Quaternion.LookRotation(awayFromCamera, Vector3.up);
        labSignCanvas.worldCamera = camera;
        labSignCanvas.transform.localScale = Vector3.one * labSignScale;
        UpdateLabSignText();
    }

    private void UpdateLabSignText()
    {
        if (labSignCostText == null || labSignProgressText == null)
            return;

        if (playerController == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                playerController = playerObject.GetComponent<PlayerController>();
        }

        int amountLeft = Mathf.Max(0, seedPurchasePrice - moneyPaidTowardSeeds);
        string progressText = seedsPurchased && remainingSeedDoses > 0
            ? $"{remainingSeedDoses} DOSES LEFT"
            : amountLeft == 0
                ? "READY TO LAND AND BUY"
                : $"LEFT ${amountLeft}  •  PAID ${moneyPaidTowardSeeds} / ${seedPurchasePrice}";
        string displayText = $"CLOUD SEEDING ${seedPurchasePrice}|{progressText}";
        if (displayText == lastLabSignText)
            return;
        lastLabSignText = displayText;
        string[] signLines = displayText.Split('|');
        labSignCostText.text = signLines[0];
        labSignProgressText.text = signLines[1];
    }

    private void AttachLabBoxTrigger()
    {
        float groundY = FindGroundHeight(labRoofPosition);
        float topY = Mathf.Max(labRoofPosition.y + rooftopClearance + 2f, groundY + 6f);
        float approachHeight = Mathf.Max(6f, topY - groundY);
        Vector3 approachCenter = new Vector3(labRoofPosition.x,
            groundY + approachHeight * 0.5f, labRoofPosition.z);
        float buildingWidth = hasLabBuildingBounds ? labBuildingBounds.size.x : 0f;
        float buildingDepth = hasLabBuildingBounds ? labBuildingBounds.size.z : 0f;
        Vector3 approachSize = new Vector3(buildingWidth + labApproachRadius * 2f,
            approachHeight, buildingDepth + labApproachRadius * 2f);
        Vector3 landingCenter = labRoofPosition + Vector3.up * rooftopClearance;
        Vector3 landingSize = new Vector3(rooftopLandingRadius * 2f,
            Mathf.Max(3f, rooftopClearance * 2f), rooftopLandingRadius * 2f);

        if (labBuilding != null)
        {
            CloudSeedingLabTrigger[] oldRelays = labBuilding.GetComponentsInChildren<CloudSeedingLabTrigger>(true);
            for (int i = 0; i < oldRelays.Length; i++)
                oldRelays[i].enabled = false;

            BoxCollider[] colliders = labBuilding.GetComponentsInChildren<BoxCollider>(true);
            BoxCollider largest = null;
            BoxCollider smallest = null;
            float largestFootprint = float.NegativeInfinity;
            float smallestFootprint = float.PositiveInfinity;
            for (int i = 0; i < colliders.Length; i++)
            {
                BoxCollider candidate = colliders[i];
                if (candidate == null || !candidate.isTrigger)
                    continue;

                Vector3 scale = candidate.transform.lossyScale;
                Vector3 size = Vector3.Scale(candidate.size,
                    new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z)));
                float footprint = Mathf.Abs(size.x * size.z);
                if (footprint > largestFootprint)
                {
                    largestFootprint = footprint;
                    largest = candidate;
                }
                if (footprint < smallestFootprint)
                {
                    smallestFootprint = footprint;
                    smallest = candidate;
                }
            }

            if (largest != null && smallest != null && largest != smallest)
            {
                // Reuse the user's two Lab trigger colliders: the larger footprint
                // starts the lift outside the building; the smaller one lands/buys.
                SetLabColliderWorldBox(largest, approachCenter, approachSize);
                ConfigureLabTrigger(largest, false);
                SetLabColliderWorldBox(smallest, landingCenter, landingSize);
                ConfigureLabTrigger(smallest, true);
                return;
            }
        }

        CreateLabTriggerProxy("Cloud Seeding Lab Approach Trigger", approachCenter,
            approachSize, false);
        CreateLabTriggerProxy("Cloud Seeding Lab Center Landing Trigger", landingCenter,
            landingSize, true);
    }

    private static void SetLabColliderWorldBox(BoxCollider collider, Vector3 worldCenter,
        Vector3 worldSize)
    {
        Vector3 scale = collider.transform.lossyScale;
        collider.center = collider.transform.InverseTransformPoint(worldCenter);
        collider.size = new Vector3(
            worldSize.x / Mathf.Max(0.001f, Mathf.Abs(scale.x)),
            worldSize.y / Mathf.Max(0.001f, Mathf.Abs(scale.y)),
            worldSize.z / Mathf.Max(0.001f, Mathf.Abs(scale.z)));
        collider.isTrigger = true;
        collider.enabled = true;
    }

    private void CreateLabTriggerProxy(string objectName, Vector3 worldCenter, Vector3 worldSize,
        bool landingCollider)
    {
        GameObject triggerObject = new GameObject(objectName);
        triggerObject.transform.SetParent(transform, true);
        triggerObject.transform.position = worldCenter;
        triggerObject.transform.rotation = Quaternion.identity;

        BoxCollider trigger = triggerObject.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = worldSize;
        ConfigureLabTrigger(trigger, landingCollider);
    }

    private void ConfigureLabTrigger(Collider collider, bool landingCollider)
    {
        CloudSeedingLabTrigger[] existing = collider.GetComponents<CloudSeedingLabTrigger>();
        CloudSeedingLabTrigger relay = null;
        for (int i = 0; i < existing.Length; i++)
        {
            if (existing[i].IsConfiguredFor(collider, landingCollider))
            {
                relay = existing[i];
                break;
            }

            if (relay == null && existing[i].IsUnconfigured)
                relay = existing[i];
        }

        if (relay == null)
            relay = collider.gameObject.AddComponent<CloudSeedingLabTrigger>();
        relay.Initialize(this, collider, landingCollider);
        relay.enabled = true;
    }

    private void CreateMaterials()
    {
        Shader toonShader = Shader.Find("ArcadeRecycler/Toon");
        if (toonShader == null)
            toonShader = Shader.Find("Universal Render Pipeline/Lit");
        if (toonShader == null)
            toonShader = Shader.Find("Standard");

        if (toonShader != null)
        {
            labPadMaterial = CreateColorMaterial(toonShader, new Color(0.36f, 0.77f, 0.45f, 1f));
        }

        Shader particlesShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (particlesShader == null)
            particlesShader = Shader.Find("Particles/Standard Unlit");
        if (particlesShader == null)
            particlesShader = toonShader;
        if (particlesShader != null)
            rainMaterial = CreateColorMaterial(particlesShader, new Color(0.38f, 0.72f, 1f, 0.88f));
    }

    private static Material CreateColorMaterial(Shader shader, Color color)
    {
        Material material = new Material(shader);
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        return material;
    }

    private void BuildNotificationUI()
    {
        GameObject canvasObject = new GameObject("Cloud Seeding Notification UI",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        notificationCanvas = canvasObject.GetComponent<Canvas>();
        notificationCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        notificationCanvas.sortingOrder = 165;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        notificationCanvasRect = canvasObject.GetComponent<RectTransform>();

        GameObject panelObject = new GameObject("Cloud Seeding Message Panel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(notificationCanvasRect, false);
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.sizeDelta = new Vector2(650f, 56f);
        panelRect.anchoredPosition = new Vector2(0f, -68f);
        Image panel = panelObject.GetComponent<Image>();
        panel.color = new Color(0.95f, 0.96f, 0.84f, 0.97f);
        panel.raycastTarget = false;
        Outline outline = panelObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.18f, 0.34f, 0.2f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        GameObject statusObject = new GameObject("Cloud Seeding Status",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        RectTransform statusRect = statusObject.GetComponent<RectTransform>();
        statusRect.SetParent(panelRect, false);
        statusRect.anchorMin = Vector2.zero;
        statusRect.anchorMax = Vector2.one;
        statusRect.offsetMin = new Vector2(10f, 5f);
        statusRect.offsetMax = new Vector2(-10f, -5f);
        statusText = statusObject.GetComponent<TextMeshProUGUI>();
        statusText.font = TMP_Settings.defaultFontAsset;
        statusText.fontSize = 21f;
        statusText.fontSizeMin = 16f;
        statusText.fontSizeMax = 21f;
        statusText.enableAutoSizing = true;
        statusText.alignment = TextAlignmentOptions.Center;
        statusText.fontStyle = FontStyles.Bold;
        statusText.color = new Color(0.16f, 0.25f, 0.15f, 1f);
        statusText.raycastTarget = false;

        purchasePanel = CreatePurchasePanel(notificationCanvasRect);

        GameObject arrowObject = new GameObject("Direction To Cloud Seeding Target",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
        directionArrowRect = arrowObject.GetComponent<RectTransform>();
        directionArrowRect.SetParent(notificationCanvasRect, false);
        directionArrowRect.anchorMin = new Vector2(0.5f, 0.5f);
        directionArrowRect.anchorMax = new Vector2(0.5f, 0.5f);
        directionArrowRect.pivot = new Vector2(0.5f, 0.5f);
        directionArrowRect.sizeDelta = new Vector2(86f, 86f);
        directionArrowText = arrowObject.GetComponent<TextMeshProUGUI>();
        directionArrowText.font = TMP_Settings.defaultFontAsset;
        directionArrowText.text = "▲";
        directionArrowText.fontSize = 64f;
        directionArrowText.fontSizeMin = 48f;
        directionArrowText.fontSizeMax = 64f;
        directionArrowText.alignment = TextAlignmentOptions.Center;
        directionArrowText.fontStyle = FontStyles.Bold;
        directionArrowText.color = new Color(0.13f, 0.42f, 0.2f, 1f);
        directionArrowText.raycastTarget = false;
        directionArrowRect.SetAsLastSibling();
        directionArrowRect.gameObject.SetActive(false);
    }

    private GameObject CreatePurchasePanel(Transform parent)
    {
        GameObject panelObject = new GameObject("Cloud Seed Price Panel",
            typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Outline));
        RectTransform panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.SetParent(parent, false);
        panelRect.anchorMin = new Vector2(0.5f, 1f);
        panelRect.anchorMax = new Vector2(0.5f, 1f);
        panelRect.pivot = new Vector2(0.5f, 1f);
        panelRect.sizeDelta = new Vector2(500f, 72f);
        panelRect.anchoredPosition = new Vector2(0f, -138f);

        Image panel = panelObject.GetComponent<Image>();
        panel.color = new Color(0.95f, 0.96f, 0.84f, 0.97f);
        panel.raycastTarget = false;
        Outline outline = panelObject.GetComponent<Outline>();
        outline.effectColor = new Color(0.18f, 0.34f, 0.2f, 1f);
        outline.effectDistance = new Vector2(2f, -2f);

        purchasePriceText = CreatePurchasePanelText(panelRect, "Cloud Seed Price", 21f,
            new Vector2(0f, -5f), new Vector2(480f, 31f));
        purchasePriceText.fontStyle = FontStyles.Bold;
        purchaseProgressText = CreatePurchasePanelText(panelRect, "Cloud Seed Money", 16f,
            new Vector2(0f, -39f), new Vector2(480f, 25f));
        panelObject.SetActive(false);
        return panelObject;
    }

    private TextMeshProUGUI CreatePurchasePanelText(Transform parent, string objectName,
        float fontSize, Vector2 anchoredPosition, Vector2 size)
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
        text.fontSizeMin = fontSize * 0.8f;
        text.fontSizeMax = fontSize;
        text.enableAutoSizing = true;
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.16f, 0.25f, 0.15f, 1f);
        text.raycastTarget = false;
        return text;
    }

    private void UpdatePurchasePanel()
    {
        if (purchasePanel == null)
            return;

        bool showPanel = hasStarted && helicopterInsideLabTrigger && NeedsSeedPurchase;
        if (purchasePanel.activeSelf != showPanel)
            purchasePanel.SetActive(showPanel);
        if (!showPanel)
            return;

        int money = playerController != null ? playerController.CheckMoney() : 0;
        int amountLeft = Mathf.Max(0, seedPurchasePrice - moneyPaidTowardSeeds);
        purchasePriceText.text = $"CLOUD SEED DOSES  •  {seedDosesPerPurchase} FOR ${seedPurchasePrice}";
        if (amountLeft > 0)
            purchaseProgressText.text = $"PAID: ${moneyPaidTowardSeeds}  •  LEFT: ${amountLeft}  •  CASH: ${money}";
        else if (labLiftStarted && helicopter != null && helicopter.IsRaisedForCloudLab)
            purchaseProgressText.text = "FULLY FUNDED  •  FLY INTO CENTER TO LAND AND BUY";
        else
            purchaseProgressText.text = "FULLY FUNDED  •  STAY IN THE LAB TO LAND AND BUY";
    }

    private void SetPersistentStatus(string message)
    {
        persistentStatus = message;
        temporaryStatusUntil = 0f;
        if (statusText != null)
            statusText.text = message;
    }

    private void ShowTemporaryStatus(string message, float seconds)
    {
        if (statusText == null)
            return;

        statusText.text = message;
        temporaryStatusUntil = Time.unscaledTime + seconds;
    }

    private void UpdateDirectionArrow(Vector3 targetPosition)
    {
        if (directionArrowRect == null || notificationCanvasRect == null)
            return;

        Camera camera = Camera.main;
        if (camera == null)
        {
            HideDirectionArrow();
            return;
        }

        Vector3 screen = camera.WorldToScreenPoint(targetPosition + Vector3.up * 0.8f);
        bool onScreen = screen.z > camera.nearClipPlane && screen.x >= 0f &&
                        screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;
        if (onScreen)
        {
            Vector2 localPoint;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(notificationCanvasRect,
                new Vector2(screen.x, screen.y), null, out localPoint))
            {
                directionArrowRect.anchoredPosition = localPoint + Vector2.up * 44f;
                directionArrowRect.localRotation = Quaternion.identity;
                directionArrowText.text = "▼";
            }
        }
        else
        {
            Vector2 direction = new Vector2(screen.x - Screen.width * 0.5f,
                screen.y - Screen.height * 0.5f);
            if (screen.z <= 0f)
                direction = -direction;
            if (direction.sqrMagnitude < 0.001f)
                direction = Vector2.up;
            direction.Normalize();

            float halfWidth = Mathf.Max(1f, notificationCanvasRect.rect.width * 0.5f - 60f);
            float halfHeight = Mathf.Max(1f, notificationCanvasRect.rect.height * 0.5f - 60f);
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

    private void HideDirectionArrow()
    {
        if (directionArrowRect != null)
            directionArrowRect.gameObject.SetActive(false);
    }

    private void UpdateSeedDoseOrbs()
    {
        if (helicopter == null)
            return;

        RefreshSeedDoseOrbs();
        if (seedDoseOrbs.Count == 0)
            return;

        float orbitTime = Time.time * seedOrbOrbitSpeed * Mathf.Deg2Rad;
        for (int i = 0; i < seedDoseOrbs.Count; i++)
        {
            Transform orb = seedDoseOrbs[i];
            if (orb == null)
                continue;

            float angle = orbitTime + Mathf.PI * 2f * i / seedDoseOrbs.Count;
            orb.localPosition = new Vector3(Mathf.Cos(angle) * seedOrbOrbitRadius,
                seedOrbOrbitHeight + Mathf.Sin(angle * 2f) * 0.16f,
                Mathf.Sin(angle) * seedOrbOrbitRadius);
            orb.Rotate(Vector3.up, 140f * Time.deltaTime, Space.Self);
        }
    }

    private void RefreshSeedDoseOrbs()
    {
        if (helicopter == null)
            return;

        if (seedOrbRoot == null)
        {
            GameObject rootObject = new GameObject("Cloud Seed Dose Orbs");
            seedOrbRoot = rootObject.transform;
        }
        if (seedOrbRoot.parent != helicopter.transform)
            seedOrbRoot.SetParent(helicopter.transform, false);
        seedOrbRoot.localPosition = Vector3.zero;

        int wantedCount = seedsPurchased ? Mathf.Max(0, remainingSeedDoses) : 0;
        while (seedDoseOrbs.Count > wantedCount)
        {
            Transform orb = seedDoseOrbs[seedDoseOrbs.Count - 1];
            seedDoseOrbs.RemoveAt(seedDoseOrbs.Count - 1);
            if (orb != null)
                Destroy(orb.gameObject);
        }

        while (seedDoseOrbs.Count < wantedCount)
        {
            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Cloud Seed Dose Orb";
            orb.transform.SetParent(seedOrbRoot, false);
            orb.transform.localScale = Vector3.one * seedOrbSize;

            Collider collider = orb.GetComponent<Collider>();
            if (collider != null)
                collider.enabled = false;

            Renderer renderer = orb.GetComponent<Renderer>();
            if (renderer != null)
            {
                if (seedOrbMaterial == null)
                {
                    Shader toon = Shader.Find("ArcadeRecycler/Toon");
                    if (toon == null)
                        toon = Shader.Find("Universal Render Pipeline/Lit");
                    if (toon == null)
                        toon = Shader.Find("Standard");
                    if (toon != null)
                    {
                        seedOrbMaterial = CreateColorMaterial(toon,
                            new Color(0.32f, 0.95f, 0.49f, 1f));
                        if (seedOrbMaterial.HasProperty("_EmissionColor"))
                        {
                            seedOrbMaterial.EnableKeyword("_EMISSION");
                            seedOrbMaterial.SetColor("_EmissionColor", new Color(0.08f, 0.28f, 0.1f));
                        }
                    }
                }
                if (seedOrbMaterial != null)
                    renderer.sharedMaterial = seedOrbMaterial;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            seedDoseOrbs.Add(orb.transform);
        }
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
            activeInstance = null;
            eventStarted = false;
        }

        if (rainMaterial != null)
            Destroy(rainMaterial);
        if (labPadMaterial != null)
            Destroy(labPadMaterial);
        if (labTopSign != null)
            Destroy(labTopSign);
        if (seedOrbMaterial != null)
            Destroy(seedOrbMaterial);
    }
}
