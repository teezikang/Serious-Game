using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class GameManager : MonoBehaviour
{
    private const float MaximumAQI = 500f;

    [Header("Air Quality")]
    [SerializeField, Range(0f, 500f)] private float startingAQI = 250f;
    [SerializeField, Min(0f)] private float aqiPerBurningTrash = 0.5f;
    [SerializeField, Min(0f)] private float aqiReductionPerRecycledTrash = 0.1f;
    [SerializeField, Range(0f, 500f)] private float winAQI = 50f;
    [SerializeField, Range(0f, 500f)] private float loseAQI = 301f;

    [Header("Opening Sequence")]
    [SerializeField, Min(0.1f)] private float openingZoomDuration = 3f;
    [SerializeField, Min(1f)] private float openingStartProjectionSize = 70f;
    [SerializeField, Min(0f)] private float openingOverlayFadeDuration = 0.4f;

    [Header("Session Recording")]
    [SerializeField, Min(1)] private int participantCount = 1;
    [SerializeField] private string sessionLogFolderName = "GameSessionLogs";

    [Header("Victory Sequence")]
    [SerializeField, Min(0f)] private float victoryDelayBeforeZoom = 2f;
    [SerializeField, Min(0.1f)] private float victoryOverviewDuration = 3f;
    [SerializeField, Min(1f)] private float victoryOverviewPadding = 1.12f;

    [Header("Haze Screen Filter")]
    [SerializeField] private Image hazeFilter;
    [SerializeField, Range(0f, 0.7f)] private float maximumHazeOpacity = 0.38f;
    [SerializeField] private Color hazeTint = new Color(0.58f, 0.61f, 0.60f, 1f);
    [SerializeField] private Color clearAirTint = new Color(0.58f, 0.78f, 0.52f, 1f);
    [SerializeField, Range(0f, 0.2f)] private float clearAirTintOpacity = 0.08f;

    public static GameManager Instance { get; private set; }
    public static bool GameplayStarted { get; private set; } = true;
    public float CurrentAQI => currentAQI;
    public event Action GameWon;
    public event Action GameLost;

    private float currentAQI;
    private bool gameEnded;
    private bool winSequenceStarted;
    private Coroutine winSequenceRoutine;
    private CanvasGroup openingOverlayGroup;
    private RawImage openingTitleImage;
    private Text openingFallbackTitle;
    private Text restartButtonText;
    private Text sessionRecordText;
    private DateTime sessionStartedAtUtc;
    private double sessionStartedAtRealtime = -1d;
    private string activeSessionId;
    private bool sessionResultRecorded;
    private Button restartButton;
    [SerializeField] private Image aqiFill;
    private Image clearAirTintFilter;
    [SerializeField] private Canvas hudCanvas;
    [SerializeField] private Text aqiValueText;
    [SerializeField] private Text aqiCategoryText;
    [SerializeField] private Text moneyStatusText;
    [SerializeField] private Text trashCapacityStatusText;
    [SerializeField] private Text carryStatusLabelText;
    private PlayerController statusPlayer;
    private int lastDisplayedMoney = int.MinValue;
    private int lastDisplayedTrashCount = int.MinValue;
    private int lastDisplayedTrashCapacity = int.MinValue;
    private int fireWaterCount;
    private int fireWaterCapacity;
    private bool showingFireWaterStatus;
    private bool lastDisplayedWaterMode;
    [SerializeField] private GameObject endOverlay;
    [SerializeField] private Text endTitleText;
    [SerializeField] private Text endMessageText;
    [Header("Game End Result Card")]
    [SerializeField] private Image resultCardImage;
    [SerializeField] private Sprite failedConditionSprite;
    [SerializeField] private Sprite winConditionSprite;

    [Header("Open Burning Indicator")]
    [SerializeField, Min(0.1f)] private float burningAlertDuration = 6f;
    [SerializeField, Min(0.2f)] private float burningAlertPulsePeriod = 1.1f;
    [SerializeField, Range(0f, 0.3f)] private float burningAlertPulseAmount = 0.08f;
    [SerializeField, Min(0f)] private float burningAlertScreenPadding = 22f;
    [SerializeField] private Vector2 burningAlertSize = new Vector2(300f, 164f);
    private RectTransform burningAlertRoot;
    private float burningAlertUntil;
    private float burningAlertShownAt;
    private readonly List<Transform> burningAlertTargets = new List<Transform>();
    private Camera burningAlertCamera;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        GameplayStarted = false;
        Time.timeScale = 0f;
        currentAQI = Mathf.Clamp(startingAQI, 0f, MaximumAQI);
        if (aqiFill == null || aqiValueText == null || aqiCategoryText == null ||
            moneyStatusText == null || trashCapacityStatusText == null ||
            endOverlay == null || endTitleText == null || endMessageText == null)
        {
            BuildToonHud();
        }

        ResolveHazeFilter();
        ResolveResultCardImage();
        EnsureBurningAlert();
        EnsureRestartButton();
        BuildOpeningOverlay();
        RefreshAQIDisplay();
    }

    private void Start()
    {
        StartCoroutine(PlayOpeningSequence());
    }

    private IEnumerator PlayOpeningSequence()
    {
        Camera mainCamera = Camera.main;
        CameraFollowPlayer cameraFollow = mainCamera != null
            ? mainCamera.GetComponent<CameraFollowPlayer>()
            : null;
        float duration = Mathf.Max(0.1f, openingZoomDuration);
        Bounds mapBounds = FindPlaygroundBounds();

        if (openingOverlayGroup != null)
        {
            openingOverlayGroup.gameObject.SetActive(true);
            openingOverlayGroup.alpha = 1f;
        }

        bool introStarted = cameraFollow != null && cameraFollow.BeginIntroZoom(mapBounds, duration,
            victoryOverviewPadding, openingStartProjectionSize);
        if (introStarted || openingOverlayGroup != null)
            yield return new WaitForSecondsRealtime(duration);

        if (openingOverlayGroup != null)
            yield return FadeOutOpeningOverlay(openingOverlayFadeDuration);

        if (Instance != this)
            yield break;

        GameplayStarted = true;
        Time.timeScale = 1f;
        BeginSessionRecording();
    }

    private void Update()
    {
        RefreshPlayerStatusDisplay();
        AnimateBurningAlert();
    }

    public void SetForestFireWaterStatus(int waterCount, int waterCapacity)
    {
        showingFireWaterStatus = true;
        fireWaterCapacity = Mathf.Max(1, waterCapacity);
        fireWaterCount = Mathf.Clamp(waterCount, 0, fireWaterCapacity);
        RefreshPlayerStatusDisplay();
    }

    public void RestoreTrashStatus()
    {
        showingFireWaterStatus = false;
        lastDisplayedTrashCount = int.MinValue;
        lastDisplayedTrashCapacity = int.MinValue;
        RefreshPlayerStatusDisplay();
    }

    /// <summary>Tracks a burning item so the HUD can point toward it until it is seen.</summary>
    public void NotifyOpenBurningStarted(Transform burningTarget)
    {
        if (burningTarget == null || hudCanvas == null)
            return;

        EnsureBurningAlert();
        if (burningAlertRoot == null)
            return;

        if (!burningAlertTargets.Contains(burningTarget))
            burningAlertTargets.Add(burningTarget);

        // This is an adjustable maximum lifetime; seeing the fire hides the
        // indicator earlier. New ignitions refresh the timer while fire spreads.
        burningAlertUntil = Time.unscaledTime + burningAlertDuration;
        if (!burningAlertRoot.gameObject.activeSelf)
        {
            burningAlertShownAt = Time.unscaledTime;
            burningAlertRoot.localScale = Vector3.one * 0.72f;
            burningAlertRoot.gameObject.SetActive(true);
        }
    }

    public void NotifyBurningTargetEnded(Transform burningTarget)
    {
        if (burningTarget != null)
            burningAlertTargets.Remove(burningTarget);

        if (burningAlertTargets.Count == 0 && burningAlertRoot != null)
            burningAlertRoot.gameObject.SetActive(false);
    }

    /// <summary>Reports whether a tracked fire is on screen and has a clear view.</summary>
    public bool IsBurningTargetVisibleToPlayer(Transform burningTarget)
    {
        Camera camera = Camera.main;
        return burningTarget != null && camera != null &&
               IsBurningTargetVisible(camera, burningTarget);
    }

    private void AnimateBurningAlert()
    {
        if (burningAlertRoot == null || !burningAlertRoot.gameObject.activeSelf)
            return;

        if (Time.unscaledTime >= burningAlertUntil || burningAlertCamera == null)
        {
            if (Time.unscaledTime >= burningAlertUntil)
                burningAlertTargets.Clear();
            else
                burningAlertCamera = Camera.main;

            if (burningAlertCamera == null)
            {
                burningAlertRoot.gameObject.SetActive(false);
                return;
            }
        }

        for (int i = burningAlertTargets.Count - 1; i >= 0; i--)
        {
            Transform target = burningAlertTargets[i];
            if (target == null || IsBurningTargetVisible(burningAlertCamera, target))
                burningAlertTargets.RemoveAt(i);
        }

        if (burningAlertTargets.Count == 0)
        {
            burningAlertRoot.gameObject.SetActive(false);
            return;
        }

        PositionBurningAlertAtScreenEdge(burningAlertCamera, burningAlertTargets[0]);

        float pop = Mathf.Clamp01((Time.unscaledTime - burningAlertShownAt) / 0.22f);
        float eased = 1f - Mathf.Pow(1f - pop, 3f);
        float phase = Mathf.Repeat((Time.unscaledTime - burningAlertShownAt) /
            Mathf.Max(0.2f, burningAlertPulsePeriod), 1f);
        float firstBeat = phase < 0.18f
            ? Mathf.Sin(phase / 0.18f * Mathf.PI)
            : 0f;
        float secondBeat = phase >= 0.27f && phase < 0.43f
            ? 0.72f * Mathf.Sin((phase - 0.27f) / 0.16f * Mathf.PI)
            : 0f;
        float pulseScale = 1f + Mathf.Max(firstBeat, secondBeat) * burningAlertPulseAmount;
        burningAlertRoot.localScale = Vector3.one * Mathf.Lerp(0.72f, pulseScale, eased);
    }

    private bool IsBurningTargetVisible(Camera camera, Transform target)
    {
        Vector3 targetPoint = GetBurningTargetPoint(target);
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
        foreach (RaycastHit hit in hits)
        {
            if (IsPlayerTransform(hit.transform))
                continue;
            if (!foundBlockingHit || hit.distance < closestHit.distance)
            {
                closestHit = hit;
                foundBlockingHit = true;
            }
        }

        // No collider blocks the sight line, or the first hit is the target.
        return !foundBlockingHit || closestHit.transform == target || closestHit.transform.IsChildOf(target);
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

    private static Vector3 GetBurningTargetPoint(Transform target)
    {
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>();
        foreach (Renderer targetRenderer in renderers)
        {
            if (!(targetRenderer is ParticleSystemRenderer))
                return targetRenderer.bounds.center;
        }

        return target.position + Vector3.up * 0.5f;
    }

    private void PositionBurningAlertAtScreenEdge(Camera camera, Transform target)
    {
        Vector3 screen = camera.WorldToScreenPoint(GetBurningTargetPoint(target));
        Vector2 direction = new Vector2(screen.x - Screen.width * 0.5f,
            screen.y - Screen.height * 0.5f);
        if (screen.z < 0f)
            direction = -direction;
        if (direction.sqrMagnitude < 0.001f)
            direction = Vector2.up;
        direction.Normalize();

        RectTransform canvasRect = hudCanvas.transform as RectTransform;
        if (canvasRect == null)
            return;

        float halfWidth = Mathf.Max(1f, canvasRect.rect.width * 0.5f -
            burningAlertSize.x * 0.5f - burningAlertScreenPadding);
        float halfHeight = Mathf.Max(1f, canvasRect.rect.height * 0.5f -
            burningAlertSize.y * 0.5f - burningAlertScreenPadding);
        float xScale = Mathf.Abs(direction.x) > 0.001f ? halfWidth / Mathf.Abs(direction.x) : float.PositiveInfinity;
        float yScale = Mathf.Abs(direction.y) > 0.001f ? halfHeight / Mathf.Abs(direction.y) : float.PositiveInfinity;
        float edgeDistance = Mathf.Min(xScale, yScale);
        burningAlertRoot.anchoredPosition = direction * edgeDistance;
    }

    private void EnsureBurningAlert()
    {
        if (burningAlertRoot != null || hudCanvas == null)
            return;

        Sprite indicatorSprite = Resources.Load<Sprite>("OpenBurningIndicator");
        if (indicatorSprite == null)
        {
            Debug.LogError("OpenBurningIndicator.png was not found as a Sprite under Assets/Resources.", this);
            return;
        }

        burningAlertRoot = CreateRect("Open Burning Alert", hudCanvas.transform,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 0.5f),
            burningAlertSize, Vector2.zero);
        burningAlertRoot.anchorMin = new Vector2(0.5f, 0.5f);
        burningAlertRoot.anchorMax = new Vector2(0.5f, 0.5f);
        burningAlertRoot.pivot = new Vector2(0.5f, 0.5f);
        burningAlertRoot.SetAsLastSibling();
        Image alertImage = AddImage(burningAlertRoot.gameObject, Color.white);
        alertImage.sprite = indicatorSprite;
        alertImage.preserveAspect = true;
        burningAlertRoot.gameObject.SetActive(false);
    }

    public void AddBurningTrash(float burnFraction)
    {
        ChangeAQI(aqiPerBurningTrash * Mathf.Max(0f, burnFraction));
    }

    public void RecycleTrash(int trashCount)
    {
        if (trashCount > 0)
            ChangeAQI(-aqiReductionPerRecycledTrash * trashCount);
    }

    public void IncreaseAQIFromForestFire(float amount)
    {
        ChangeAQI(Mathf.Max(0f, amount));
    }

    public void DecreaseAQIFromExtinguishedForestFire(float amount)
    {
        ChangeAQI(-Mathf.Max(0f, amount));
    }

    public void DecreaseAQIFromCloudSeeding(float amount)
    {
        ChangeAQI(-Mathf.Max(0f, amount));
    }

    private void ChangeAQI(float amount)
    {
        if (gameEnded)
            return;

        currentAQI = Mathf.Clamp(currentAQI + amount, 0f, MaximumAQI);
        RefreshAQIDisplay();

        if (currentAQI >= loseAQI)
            FinishGame(false);
        else if (currentAQI <= winAQI && !winSequenceStarted)
        {
            winSequenceStarted = true;
            winSequenceRoutine = StartCoroutine(PlayVictorySequence());
        }
    }

    private IEnumerator PlayVictorySequence()
    {
        yield return new WaitForSecondsRealtime(victoryDelayBeforeZoom);
        if (gameEnded)
            yield break;

        Camera mainCamera = Camera.main;
        CameraFollowPlayer cameraFollow = mainCamera != null
            ? mainCamera.GetComponent<CameraFollowPlayer>()
            : null;

        if (cameraFollow != null)
        {
            cameraFollow.BeginOverview(FindPlaygroundBounds(), victoryOverviewDuration,
                victoryOverviewPadding);
            yield return new WaitForSecondsRealtime(victoryOverviewDuration);
        }

        if (!gameEnded)
            FinishGame(true);
    }

    private Bounds FindPlaygroundBounds()
    {
        GameObject playground = GameObject.Find("PlayGround");
        if (playground == null)
            playground = GameObject.Find("Playground");

        if (playground != null)
        {
            Renderer[] renderers = playground.GetComponentsInChildren<Renderer>(true);
            bool foundRenderer = false;
            Bounds rendererBounds = new Bounds(playground.transform.position, Vector3.one);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                    continue;

                if (!foundRenderer)
                {
                    rendererBounds = renderer.bounds;
                    foundRenderer = true;
                }
                else
                    rendererBounds.Encapsulate(renderer.bounds);
            }

            if (foundRenderer)
                return rendererBounds;

            Collider[] colliders = playground.GetComponentsInChildren<Collider>(true);
            bool foundCollider = false;
            Bounds colliderBounds = new Bounds(playground.transform.position, Vector3.one);
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || !collider.gameObject.activeInHierarchy)
                    continue;

                if (!foundCollider)
                {
                    colliderBounds = collider.bounds;
                    foundCollider = true;
                }
                else
                    colliderBounds.Encapsulate(collider.bounds);
            }

            if (foundCollider)
                return colliderBounds;

            return new Bounds(playground.transform.position, new Vector3(100f, 20f, 100f));
        }

        Debug.LogWarning("PlayGround was not found; using a broad camera overview for the victory sequence.", this);
        return new Bounds(Vector3.zero, new Vector3(100f, 20f, 100f));
    }

    private void FinishGame(bool won)
    {
        if (gameEnded)
            return;

        gameEnded = true;
        GameplayStarted = false;
        if (!won && winSequenceRoutine != null)
        {
            StopCoroutine(winSequenceRoutine);
            winSequenceRoutine = null;
        }

        endTitleText.text = won ? " " : " ";
        if (resultCardImage != null)
        {
            Sprite resultSprite = won ? winConditionSprite : failedConditionSprite;
            if (resultSprite != null)
            {
                resultCardImage.sprite = resultSprite;
                resultCardImage.color = Color.white;
                resultCardImage.preserveAspect = true;
            }
        }
        endTitleText.color = won
            ? new Color(0.10f, 0.43f, 0.22f, 1f)
            : new Color(0.68f, 0.15f, 0.12f, 1f);
        endTitleText.gameObject.SetActive(true);
        endMessageText.text = won
            ? " "
            : " ";
        endMessageText.gameObject.SetActive(true);
        if (restartButtonText != null)
            restartButtonText.text = won ? "PLAY AGAIN" : "TRY AGAIN";

        RecordSessionResult(won);
        endOverlay.SetActive(true);
        Time.timeScale = 0f;

        if (won)
            GameWon?.Invoke();
        else
            GameLost?.Invoke();
    }

    private void BeginSessionRecording()
    {
        sessionStartedAtUtc = DateTime.UtcNow;
        sessionStartedAtRealtime = Time.realtimeSinceStartupAsDouble;
        activeSessionId = Guid.NewGuid().ToString("N");
        sessionResultRecorded = false;
        if (sessionRecordText != null)
            sessionRecordText.text = string.Empty;
    }

    private void RecordSessionResult(bool won)
    {
        if (sessionResultRecorded)
            return;

        sessionResultRecorded = true;
        DateTime completedAtUtc = DateTime.UtcNow;
        double durationSeconds = sessionStartedAtRealtime >= 0d
            ? Math.Max(0d, Time.realtimeSinceStartupAsDouble - sessionStartedAtRealtime)
            : 0d;
        string sessionId = string.IsNullOrEmpty(activeSessionId)
            ? Guid.NewGuid().ToString("N")
            : activeSessionId;
        string result = won ? "Win" : "Lose";

        string outputDirectory = Application.isEditor
            ? Path.GetFullPath(Path.Combine(Application.dataPath, "..", sessionLogFolderName))
            : Path.Combine(Application.persistentDataPath, sessionLogFolderName);
        string fileName = $"session_{completedAtUtc.ToString("yyyyMMdd_HHmmss_fff", CultureInfo.InvariantCulture)}_{sessionId.Substring(0, 8)}.csv";
        string filePath = Path.Combine(outputDirectory, fileName);

        string header = "SessionId,StartedAtUtc,CompletedAtUtc,PlayingDurationSeconds,Result,ParticipantCount,FinalAQI";
        string row = string.Join(",", new[]
        {
            EscapeCsv(sessionId),
            EscapeCsv(sessionStartedAtUtc == default
                ? completedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)
                : sessionStartedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)),
            EscapeCsv(completedAtUtc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture)),
            durationSeconds.ToString("F2", CultureInfo.InvariantCulture),
            EscapeCsv(result),
            Mathf.Max(1, participantCount).ToString(CultureInfo.InvariantCulture),
            currentAQI.ToString("F0", CultureInfo.InvariantCulture)
        });

        try
        {
            Directory.CreateDirectory(outputDirectory);
            File.WriteAllText(filePath, header + Environment.NewLine + row + Environment.NewLine,
                new UTF8Encoding(true));
            if (sessionRecordText != null)
                sessionRecordText.text = $"Session report saved: {fileName}";
            Debug.Log($"Session report saved to: {filePath}", this);
        }
        catch (Exception exception)
        {
            if (sessionRecordText != null)
                sessionRecordText.text = "Session report could not be saved.";
            Debug.LogError($"Could not write the game session report: {exception.Message}", this);
        }
    }

    private static string EscapeCsv(string value)
    {
        return "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
    }

    private void RestartGame()
    {
        Time.timeScale = 1f;
        int activeBuildIndex = SceneManager.GetActiveScene().buildIndex;
        if (activeBuildIndex >= 0)
        {
            SceneManager.LoadScene(activeBuildIndex);
            return;
        }

        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void RefreshAQIDisplay()
    {
        if (aqiFill != null)
        {
            aqiFill.fillAmount = currentAQI / MaximumAQI;
            aqiValueText.text = Mathf.RoundToInt(currentAQI).ToString();

            if (currentAQI <= 50f)
            {
                aqiCategoryText.text = "GOOD";
                aqiFill.color = new Color(0.38f, 0.84f, 0.19f);
            }
            else if (currentAQI <= 100f)
            {
                aqiCategoryText.text = "MODERATE";
                aqiFill.color = new Color(0.96f, 0.94f, 0.27f);
            }
            else if (currentAQI <= 150f)
            {
                aqiCategoryText.text = "SENSITIVE GROUPS";
                aqiFill.color = new Color(0.91f, 0.31f, 0.17f);
            }
            else if (currentAQI <= 200f)
            {
                aqiCategoryText.text = "UNHEALTHY";
                aqiFill.color = new Color(0.91f, 0.16f, 0.19f);
            }
            else if (currentAQI <= 300f)
            {
                aqiCategoryText.text = "VERY UNHEALTHY";
                aqiFill.color = new Color(0.53f, 0.10f, 0.32f);
            }
            else
            {
                aqiCategoryText.text = "HAZARDOUS";
                aqiFill.color = new Color(0.47f, 0.06f, 0.15f);
            }
        }

        RefreshHazeFilter();
    }

    private void ResolveHazeFilter()
    {
        if (hazeFilter == null)
        {
            Image[] images = FindObjectsOfType<Image>(true);

            // Prefer an explicitly named filter; otherwise use a full-screen Image
            // that is already in the scene and is not part of the generated HUD.
            foreach (Image image in images)
            {
                if (image == null || image.canvas == hudCanvas)
                    continue;

                string lowerName = image.name.ToLowerInvariant();
                if (lowerName.Contains("haze") || lowerName.Contains("filter"))
                {
                    hazeFilter = image;
                    break;
                }
            }

            if (hazeFilter == null)
            {
                foreach (Image image in images)
                {
                    if (image == null || image.canvas == hudCanvas)
                        continue;

                    RectTransform rect = image.rectTransform;
                    if (rect.anchorMin == Vector2.zero && rect.anchorMax == Vector2.one)
                    {
                        hazeFilter = image;
                        break;
                    }
                }
            }
        }

        if (hazeFilter == null)
            hazeFilter = CreateHazeFilter();

        if (hazeFilter == null)
            return;

        hazeFilter.raycastTarget = false;
        hazeFilter.transform.SetAsFirstSibling();
        Canvas filterCanvas = hazeFilter.canvas;
        if (filterCanvas != null && filterCanvas != hudCanvas && filterCanvas.sortingOrder >= hudCanvas.sortingOrder)
        {
            filterCanvas.overrideSorting = true;
            filterCanvas.sortingOrder = hudCanvas.sortingOrder - 1;
        }

        ResolveClearAirTintFilter();
    }

    private void ResolveClearAirTintFilter()
    {
        if (hazeFilter == null || hazeFilter.transform.parent == null)
            return;

        Transform filterParent = hazeFilter.transform.parent;
        foreach (Image image in filterParent.GetComponentsInChildren<Image>(true))
        {
            if (image != null && image.name == "AQI Clear Air Tint")
            {
                clearAirTintFilter = image;
                break;
            }
        }

        if (clearAirTintFilter == null)
        {
            GameObject tintObject = new GameObject("AQI Clear Air Tint",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform tintRect = tintObject.GetComponent<RectTransform>();
            tintRect.SetParent(filterParent, false);
            tintRect.anchorMin = Vector2.zero;
            tintRect.anchorMax = Vector2.one;
            tintRect.pivot = new Vector2(0.5f, 0.5f);
            tintRect.offsetMin = Vector2.zero;
            tintRect.offsetMax = Vector2.zero;
            clearAirTintFilter = tintObject.GetComponent<Image>();
        }

        clearAirTintFilter.raycastTarget = false;
        clearAirTintFilter.transform.SetAsFirstSibling();
    }

    private Image CreateHazeFilter()
    {
        GameObject canvasObject = new GameObject(
            "AQI Haze Filter Canvas",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = hudCanvas != null ? hudCanvas.sortingOrder - 1 : 99;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform rect = CreateRect("AQI Haze Filter", canvas.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        return AddImage(rect.gameObject, hazeTint);
    }

    private void RefreshHazeFilter()
    {
        if (hazeFilter == null)
            return;

        float hazeProgress = Mathf.InverseLerp(winAQI, loseAQI, currentAQI);
        hazeFilter.color = new Color(hazeTint.r, hazeTint.g, hazeTint.b,
            hazeProgress * maximumHazeOpacity);

        if (clearAirTintFilter != null)
        {
            float clearAirProgress = 1f - hazeProgress;
            clearAirTintFilter.color = new Color(clearAirTint.r, clearAirTint.g,
                clearAirTint.b, clearAirProgress * clearAirTintOpacity);
        }
    }

    private void BuildToonHud()
    {
        GameObject canvasObject = new GameObject(
            "AQI Toon HUD",
            typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = canvasObject.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 100;
        hudCanvas = canvas;

        CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        RectTransform hazeRect = CreateRect("AQI Haze Filter", canvas.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        hazeRect.offsetMin = Vector2.zero;
        hazeRect.offsetMax = Vector2.zero;
        hazeFilter = AddImage(hazeRect.gameObject, hazeTint);

        RectTransform frame = CreateRect("AQI Card Outline", canvas.transform,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(382f, 148f), new Vector2(28f, -28f));
        AddImage(frame.gameObject, new Color(0.09f, 0.13f, 0.18f));

        RectTransform card = CreateRect("AQI Card", frame,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        card.offsetMin = new Vector2(6f, 6f);
        card.offsetMax = new Vector2(-6f, -6f);
        AddImage(card.gameObject, new Color(0.98f, 0.90f, 0.68f));

        CreateText("AQI Heading", card, "AIR QUALITY INDEX", 21, FontStyle.Bold,
            new Color(0.12f, 0.16f, 0.20f), TextAnchor.MiddleLeft,
            new Vector2(18f, -12f), new Vector2(260f, 34f), new Vector2(0f, 1f));
        aqiValueText = CreateText("AQI Value", card, "250", 30, FontStyle.Bold,
            new Color(0.12f, 0.16f, 0.20f), TextAnchor.MiddleRight,
            new Vector2(-18f, -10f), new Vector2(100f, 40f), new Vector2(1f, 1f));

        RectTransform barFrame = CreateRect("AQI Bar Outline", card,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(346f, 24f), new Vector2(18f, -60f));
        AddImage(barFrame.gameObject, new Color(0.10f, 0.14f, 0.18f));

        RectTransform barBackground = CreateRect("AQI Bar Background", barFrame,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        barBackground.offsetMin = new Vector2(4f, 4f);
        barBackground.offsetMax = new Vector2(-4f, -4f);
        AddImage(barBackground.gameObject, new Color(0.77f, 0.77f, 0.68f));

        RectTransform fillRect = CreateRect("AQI Bar Fill", barBackground,
            Vector2.zero, Vector2.one, new Vector2(0f, 0.5f), Vector2.zero, Vector2.zero);
        aqiFill = AddImage(fillRect.gameObject, new Color(0.95f, 0.72f, 0.20f));
        aqiFill.type = Image.Type.Filled;
        aqiFill.fillMethod = Image.FillMethod.Horizontal;
        aqiFill.fillOrigin = (int)Image.OriginHorizontal.Left;

        aqiCategoryText = CreateText("AQI Category", card, "VERY UNHEALTHY", 18, FontStyle.Bold,
            new Color(0.12f, 0.16f, 0.20f), TextAnchor.MiddleLeft,
            new Vector2(18f, -93f), new Vector2(300f, 30f), new Vector2(0f, 1f));

        BuildPlayerStatusCard(canvas.transform);
        BuildEndOverlay(canvas.transform);
    }

#if UNITY_EDITOR
    public bool BuildEditableHudInEditor()
    {
        if (hudCanvas != null)
        {
            return false;
        }

        currentAQI = Mathf.Clamp(startingAQI, 0f, MaximumAQI);
        BuildToonHud();
        RefreshAQIDisplay();
        return true;
    }
#endif

    private void BuildPlayerStatusCard(Transform canvasTransform)
    {
        RectTransform frame = CreateRect("Player Status Outline", canvasTransform,
            new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
            new Vector2(310f, 148f), new Vector2(430f, -28f));
        AddImage(frame.gameObject, new Color(0.09f, 0.13f, 0.18f));

        RectTransform card = CreateRect("Player Status Card", frame,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
            Vector2.zero, Vector2.zero);
        card.offsetMin = new Vector2(6f, 6f);
        card.offsetMax = new Vector2(-6f, -6f);
        AddImage(card.gameObject, new Color(0.98f, 0.90f, 0.68f));

        CreateText("Player Status Heading", card, "PLAYER STATUS", 21, FontStyle.Bold,
            new Color(0.12f, 0.16f, 0.20f), TextAnchor.MiddleLeft,
            new Vector2(18f, -12f), new Vector2(260f, 32f), new Vector2(0f, 1f));
        CreateText("Money Label", card, "MONEY", 18, FontStyle.Bold,
            new Color(0.12f, 0.16f, 0.20f), TextAnchor.MiddleLeft,
            new Vector2(18f, -53f), new Vector2(130f, 30f), new Vector2(0f, 1f));
        moneyStatusText = CreateText("Money Value", card, "$0", 23, FontStyle.Bold,
            new Color(0.12f, 0.16f, 0.20f), TextAnchor.MiddleRight,
            new Vector2(-18f, -51f), new Vector2(135f, 32f), new Vector2(1f, 1f));
        carryStatusLabelText = CreateText("Trash Capacity Label", card, "TRASH HELD", 18, FontStyle.Bold,
            new Color(0.12f, 0.16f, 0.20f), TextAnchor.MiddleLeft,
            new Vector2(18f, -96f), new Vector2(140f, 30f), new Vector2(0f, 1f));
        trashCapacityStatusText = CreateText("Trash Capacity Value", card, "0 / 5", 21, FontStyle.Bold,
            new Color(0.12f, 0.16f, 0.20f), TextAnchor.MiddleRight,
            new Vector2(-18f, -94f), new Vector2(135f, 32f), new Vector2(1f, 1f));
    }

    private void RefreshPlayerStatusDisplay()
    {
        if (moneyStatusText == null || trashCapacityStatusText == null)
            return;

        if (carryStatusLabelText == null)
        {
            Transform statusCard = trashCapacityStatusText.transform.parent;
            if (statusCard != null)
            {
                foreach (Text candidate in statusCard.GetComponentsInChildren<Text>(true))
                {
                    if (candidate != null && candidate.name == "Trash Capacity Label")
                    {
                        carryStatusLabelText = candidate;
                        break;
                    }
                }
            }
        }

        if (statusPlayer == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
                statusPlayer = playerObject.GetComponent<PlayerController>();
        }

        if (statusPlayer != null)
        {
            int money = statusPlayer.CheckMoney();
            if (money != lastDisplayedMoney)
            {
                moneyStatusText.text = $"${money}";
                lastDisplayedMoney = money;
            }
        }

        if (carryStatusLabelText != null)
            carryStatusLabelText.text = showingFireWaterStatus ? "WATER" : "TRASH HELD";

        int carriedCount = showingFireWaterStatus
            ? fireWaterCount
            : statusPlayer != null ? statusPlayer.CurrentTrashCount : -1;
        int capacity = showingFireWaterStatus
            ? fireWaterCapacity
            : statusPlayer != null ? statusPlayer.MaxTrashCapacity : -1;
        if (carriedCount >= 0 &&
            (showingFireWaterStatus != lastDisplayedWaterMode ||
             carriedCount != lastDisplayedTrashCount || capacity != lastDisplayedTrashCapacity))
        {
            trashCapacityStatusText.text = $"{carriedCount} / {capacity}";
            lastDisplayedTrashCount = carriedCount;
            lastDisplayedTrashCapacity = capacity;
            lastDisplayedWaterMode = showingFireWaterStatus;
        }
    }

    private void BuildEndOverlay(Transform parent)
    {
        RectTransform overlayRect = CreateRect("Game End Overlay", parent,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        Image overlayImage = AddImage(overlayRect.gameObject, new Color(0.03f, 0.06f, 0.09f, 0.78f));
        overlayImage.raycastTarget = true;
        endOverlay = overlayRect.gameObject;

        RectTransform frame = CreateRect("Result Card Outline", overlayRect,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(540f, 270f), Vector2.zero);
        AddImage(frame.gameObject, new Color(0.08f, 0.12f, 0.17f));

        RectTransform card = CreateRect("Result Card", frame,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        card.offsetMin = new Vector2(7f, 7f);
        card.offsetMax = new Vector2(-7f, -7f);
        resultCardImage = AddImage(card.gameObject, new Color(0.98f, 0.90f, 0.68f));

        endTitleText = CreateText("Result Title", card, "CITY CLEAN!", 42, FontStyle.Bold,
            new Color(0.12f, 0.16f, 0.20f), TextAnchor.MiddleCenter,
            new Vector2(0f, -54f), new Vector2(500f, 78f), new Vector2(0.5f, 1f));
        endMessageText = CreateText("Result Message", card, "Great work! Air quality is safe.", 23, FontStyle.Bold,
            new Color(0.12f, 0.16f, 0.20f), TextAnchor.MiddleCenter,
            new Vector2(0f, 42f), new Vector2(480f, 55f), new Vector2(0.5f, 0.5f));
        endOverlay.SetActive(false);
    }

    private void ResolveResultCardImage()
    {
        if (resultCardImage != null || endTitleText == null)
            return;

        Transform card = endTitleText.transform.parent;
        if (card == null)
            return;

        Transform imageTransform = card.Find("Image");
        resultCardImage = imageTransform != null
            ? imageTransform.GetComponent<Image>()
            : card.GetComponent<Image>();
        if (resultCardImage == null)
            resultCardImage = card.GetComponentInChildren<Image>(true);
    }

    private void EnsureRestartButton()
    {
        if (endTitleText == null || endMessageText == null)
            return;

        endTitleText.gameObject.SetActive(true);
        endMessageText.gameObject.SetActive(true);
        Transform card = endTitleText.transform.parent;
        if (card == null)
            return;

        Transform buttonTransform = card.Find("Restart Button");
        if (buttonTransform == null)
        {
            RectTransform buttonRect = CreateRect("Restart Button", card,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(240f, 56f), new Vector2(0f, -82f));
            Image buttonImage = AddImage(buttonRect.gameObject,
                new Color(0.30f, 0.23f, 0.19f, 1f));
            buttonImage.raycastTarget = true;
            restartButton = buttonRect.gameObject.AddComponent<Button>();
            restartButton.targetGraphic = buttonImage;

            ColorBlock buttonColors = restartButton.colors;
            buttonColors.normalColor = new Color(0.30f, 0.23f, 0.19f, 1f);
            buttonColors.highlightedColor = new Color(0.40f, 0.31f, 0.25f, 1f);
            buttonColors.pressedColor = new Color(0.20f, 0.15f, 0.13f, 1f);
            buttonColors.selectedColor = buttonColors.highlightedColor;
            restartButton.colors = buttonColors;
            buttonTransform = buttonRect;
        }
        else
        {
            restartButton = buttonTransform.GetComponent<Button>();
        }

        if (restartButton == null)
            restartButton = buttonTransform.gameObject.AddComponent<Button>();
        restartButton.onClick.RemoveListener(RestartGame);
        restartButton.onClick.AddListener(RestartGame);
        restartButton.interactable = true;

        restartButtonText = buttonTransform.Find("Restart Button Label")?.GetComponent<Text>();
        if (restartButtonText == null)
        {
            restartButtonText = CreateText("Restart Button Label", buttonTransform,
                "TRY AGAIN", 22, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.zero);
            RectTransform labelRect = restartButtonText.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = new Vector2(8f, 4f);
            labelRect.offsetMax = new Vector2(-8f, -4f);
        }

        sessionRecordText = card.Find("Session Record")?.GetComponent<Text>();
        if (sessionRecordText == null)
        {
            sessionRecordText = CreateText("Session Record", card, string.Empty, 12,
                FontStyle.Normal, new Color(0.24f, 0.26f, 0.24f, 1f), TextAnchor.MiddleCenter,
                new Vector2(0f, -117f), new Vector2(500f, 20f), new Vector2(0.5f, 0.5f));
        }
    }

    private void BuildOpeningOverlay()
    {
        if (hudCanvas == null)
            return;

        RectTransform root = CreateRect("Opening Overlay", hudCanvas.transform,
            Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        root.offsetMin = Vector2.zero;
        root.offsetMax = Vector2.zero;
        openingOverlayGroup = root.gameObject.AddComponent<CanvasGroup>();
        openingOverlayGroup.alpha = 0f;
        openingOverlayGroup.interactable = true;
        openingOverlayGroup.blocksRaycasts = true;

        Image backdrop = AddImage(root.gameObject, new Color(0.06f, 0.08f, 0.09f, 0.46f));
        backdrop.raycastTarget = true;

        RectTransform artRect = CreateRect("Opening Word Art", root,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
            new Vector2(1660f, 170f), new Vector2(0f, 44f));
        openingTitleImage = artRect.gameObject.AddComponent<RawImage>();
        openingTitleImage.texture = Resources.Load<Texture2D>("WordArtStrip");
        openingTitleImage.uvRect = new Rect(0f, 0.40f, 1f, 0.20f);
        openingTitleImage.color = new Color(0.73f, 0.79f, 0.81f, 1f);
        openingTitleImage.raycastTarget = false;
        openingTitleImage.enabled = openingTitleImage.texture != null;

        openingFallbackTitle = CreateText("Opening Title Fallback", root,
            "WELCOME TO\nKBANDAR", 66, FontStyle.Bold, new Color(0.73f, 0.79f, 0.81f, 1f),
            TextAnchor.MiddleCenter, new Vector2(0f, 42f), new Vector2(1000f, 190f),
            new Vector2(0.5f, 0.5f));
        openingFallbackTitle.gameObject.SetActive(openingTitleImage.texture == null);

        CreateText("Opening Subtitle", root, "A CLEANER CITY STARTS WITH YOU", 24,
            FontStyle.Bold, new Color(0.97f, 0.91f, 0.78f, 1f), TextAnchor.MiddleCenter,
            new Vector2(0f, -90f), new Vector2(900f, 46f), new Vector2(0.5f, 0.5f));

        root.gameObject.SetActive(false);
    }

    private IEnumerator FadeOutOpeningOverlay(float duration)
    {
        if (openingOverlayGroup == null)
            yield break;

        float fadeDuration = Mathf.Max(0f, duration);
        if (fadeDuration <= 0f)
        {
            openingOverlayGroup.alpha = 0f;
            openingOverlayGroup.gameObject.SetActive(false);
            yield break;
        }

        float startAlpha = openingOverlayGroup.alpha;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / fadeDuration);
            openingOverlayGroup.alpha = Mathf.Lerp(startAlpha, 0f, progress);
            yield return null;
        }

        openingOverlayGroup.alpha = 0f;
        openingOverlayGroup.gameObject.SetActive(false);
    }

    private static RectTransform CreateRect(string objectName, Transform parent,
        Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size, Vector2 position)
    {
        GameObject child = new GameObject(objectName, typeof(RectTransform));
        RectTransform rect = child.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = pivot;
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        return rect;
    }

    private static Image AddImage(GameObject target, Color color)
    {
        Image image = target.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static Text CreateText(string objectName, Transform parent, string value, int size,
        FontStyle style, Color color, TextAnchor alignment, Vector2 position, Vector2 dimensions,
        Vector2 anchor = default)
    {
        RectTransform rect = CreateRect(objectName, parent, anchor, anchor,
            new Vector2(anchor.x, anchor.y), dimensions, position);
        Text text = rect.gameObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.text = value;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;
        return text;
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
            GameplayStarted = true;
            Time.timeScale = 1f;
        }
    }
}
