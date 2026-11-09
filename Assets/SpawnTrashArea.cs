using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SpawnTrashArea : MonoBehaviour
{
    [SerializeField] private BoxCollider spawnArea;
    [SerializeField] private GameObject trashPrefab;
    [SerializeField] private string trashTag = "Trash";
    [SerializeField, Min(1)] private int maxActiveTrash = 8;
    [SerializeField, Min(0.1f)] private float spawnInterval = 3f;
    [SerializeField, Min(0f)] private float edgePadding = 0.5f;
    [SerializeField, Min(0f)] private float surfaceOffset = 0.05f;
    [SerializeField] private GameObject fireEffectPrefab;
    [SerializeField] private GameObject smokeEffectPrefab;
    [SerializeField, Min(1)] private int fireThreshold = 5;
    [SerializeField, Min(0f)] private float fireHeightOffset = 0.1f;
    [SerializeField, Min(0f)] private float smokeHeightOffset = 0.25f;
    [SerializeField, Min(0.01f)] private float smokeScale = 0.55f;
    [SerializeField, Min(0.1f)] private float fireSpreadInterval = 1f;
    [SerializeField, Min(0f)] private float fireStartDelayPerStaff = 2.5f;
    [SerializeField, Min(0)] private int fireThresholdIncreasePerStaff = 1;
    [SerializeField, Min(1f)] private float burningTrashLifetime = 6f;

    private float nextSpawnTime;
    private Coroutine fireSpreadCoroutine;
    private readonly HashSet<GameObject> burningTrash = new HashSet<GameObject>();
    private bool hasWarnedMissingFirePrefab;
    private int hiredStaffCount;

    public BoxCollider SpawnAreaCollider => spawnArea;
    public int HiredStaffCount => hiredStaffCount;

    public void RegisterStaffHire()
    {
        hiredStaffCount++;
        if (fireSpreadCoroutine != null)
        {
            StopCoroutine(fireSpreadCoroutine);
            fireSpreadCoroutine = null;
        }
    }

    private void Awake()
    {
        if (spawnArea == null)
            spawnArea = GetComponent<BoxCollider>();

        if (spawnArea == null)
        {
            Debug.LogError("SpawnTrashArea needs a BoxCollider to define its spawn bounds.", this);
            enabled = false;
            return;
        }

        if (trashPrefab == null)
        {
            Debug.LogError("Assign the trash prefab to SpawnTrashArea.", this);
            enabled = false;
        }
    }

    private void Start()
    {
        SpawnIfNeeded();
        nextSpawnTime = Time.time + spawnInterval;
    }

    private void Update()
    {
        int garbageCount = CountTrashInArea();
        if (garbageCount < GetEffectiveFireThreshold())
        {
            if (fireSpreadCoroutine != null)
            {
                StopCoroutine(fireSpreadCoroutine);
                fireSpreadCoroutine = null;
            }
        }
        else if (fireSpreadCoroutine == null)
            fireSpreadCoroutine = StartCoroutine(SpreadFireWhileAreaIsFull());

        if (Time.time < nextSpawnTime)
            return;

        SpawnIfNeeded();
        nextSpawnTime = Time.time + spawnInterval;
    }

    private void SpawnIfNeeded()
    {
        // Keep the cap based on every uncollected trash item, even if physics
        // has moved one just outside the spawn box. Otherwise it is invisible
        // to the area count and the spawner keeps replacing it indefinitely.
        if (CountActiveTrash() >= maxActiveTrash)
            return;

        Vector3 halfSize = spawnArea.size * 0.5f;
        Vector3 scale = spawnArea.transform.lossyScale;
        float paddingX = Mathf.Min(edgePadding / Mathf.Max(Mathf.Abs(scale.x), 0.0001f), halfSize.x);
        float paddingZ = Mathf.Min(edgePadding / Mathf.Max(Mathf.Abs(scale.z), 0.0001f), halfSize.z);
        float verticalOffset = surfaceOffset / Mathf.Max(Mathf.Abs(scale.y), 0.0001f);

        Vector3 localPosition = spawnArea.center + new Vector3(
            Random.Range(-halfSize.x + paddingX, halfSize.x - paddingX),
            halfSize.y + verticalOffset,
            Random.Range(-halfSize.z + paddingZ, halfSize.z - paddingZ));
        Vector3 worldPosition = spawnArea.transform.TransformPoint(localPosition);

        Instantiate(trashPrefab, worldPosition, Quaternion.identity);
    }

    private int CountTrashInArea()
    {
        int count = 0;
        GameObject[] trashObjects = GameObject.FindGameObjectsWithTag(trashTag);
        foreach (GameObject trash in trashObjects)
        {
            TrashStacker stacker = trash.GetComponent<TrashStacker>();
            if (stacker != null && stacker.IsCollected)
                continue;

            if (IsInsideSpawnArea(trash.transform.position))
                count++;
        }

        return count;
    }

    private IEnumerator SpreadFireWhileAreaIsFull()
    {
        float ignitionDelay = hiredStaffCount * fireStartDelayPerStaff;
        float delayed = 0f;
        while (delayed < ignitionDelay && CountTrashInArea() >= GetEffectiveFireThreshold())
        {
            float step = Mathf.Min(0.2f, ignitionDelay - delayed);
            yield return new WaitForSeconds(step);
            delayed += step;
        }

        while (CountTrashInArea() >= GetEffectiveFireThreshold())
        {
            IgniteNextTrash();
            yield return new WaitForSeconds(fireSpreadInterval);
        }

        fireSpreadCoroutine = null;
    }

    private int GetEffectiveFireThreshold()
    {
        return fireThreshold + hiredStaffCount * fireThresholdIncreasePerStaff;
    }

    private void IgniteNextTrash()
    {
        if (fireEffectPrefab == null)
        {
            if (!hasWarnedMissingFirePrefab)
            {
                Debug.LogWarning("Assign a fire VFX prefab to SpawnTrashArea to ignite trash when the area reaches its threshold.", this);
                hasWarnedMissingFirePrefab = true;
            }
            return;
        }

        GameObject selectedTrash = null;
        float nearestDistance = float.PositiveInfinity;
        Vector3 areaCenter = spawnArea.transform.TransformPoint(spawnArea.center);
        GameObject[] trashObjects = GameObject.FindGameObjectsWithTag(trashTag);
        foreach (GameObject trash in trashObjects)
        {
            TrashStacker stacker = trash.GetComponent<TrashStacker>();
            if (burningTrash.Contains(trash) ||
                (stacker != null && stacker.IsCollected) ||
                !IsInsideSpawnArea(trash.transform.position))
                continue;

            float distance = (trash.transform.position - areaCenter).sqrMagnitude;
            if (distance < nearestDistance)
            {
                selectedTrash = trash;
                nearestDistance = distance;
            }
        }

        if (selectedTrash == null)
            return;

        Vector3 firePosition = selectedTrash.transform.position + Vector3.up * fireHeightOffset;
        GameObject fire = Instantiate(fireEffectPrefab, firePosition, Quaternion.identity, selectedTrash.transform);
        fire.name = "Trash Fire";
        GameObject smoke = null;
        if (smokeEffectPrefab != null)
        {
            // Use Unity's non-generic overload here. Some imported VFX prefab
            // references can fail the generic GameObject cast in Instantiate<T>.
            UnityEngine.Object smokeInstance = UnityEngine.Object.Instantiate(
                (UnityEngine.Object)smokeEffectPrefab, selectedTrash.transform, false);
            smoke = smokeInstance as GameObject;
            if (smoke == null && smokeInstance is Component smokeComponent)
                smoke = smokeComponent.gameObject;

            if (smoke != null)
            {
                smoke.name = "Trash Black Smoke";
                smoke.transform.localPosition = Vector3.up * smokeHeightOffset;
                smoke.transform.localRotation = Quaternion.identity;
                smoke.transform.localScale = Vector3.one * smokeScale;
            }
            else if (smokeInstance != null)
            {
                Destroy(smokeInstance);
                Debug.LogError("The Smoke Effect Prefab must be a GameObject or a component on a GameObject.", this);
            }
        }

        burningTrash.Add(selectedTrash);
        GameManager.Instance?.NotifyOpenBurningStarted(selectedTrash.transform);
        StartCoroutine(BurnTrashUntilCollectedOrExpired(selectedTrash, fire, smoke));
    }

    private IEnumerator BurnTrashUntilCollectedOrExpired(GameObject trash, GameObject fire, GameObject smoke)
    {
        float elapsed = 0f;
        while (elapsed < burningTrashLifetime && trash != null)
        {
            TrashStacker stacker = trash.GetComponent<TrashStacker>();
            if (stacker != null && stacker.IsCollected)
            {
                GameManager.Instance?.NotifyBurningTargetEnded(trash.transform);
                if (fire != null && fire.activeSelf)
                    fire.SetActive(false);

                StopSmokeAndLetItFade(smoke);
                burningTrash.Remove(trash);
                yield break;
            }

            float deltaTime = Time.deltaTime;
            elapsed += deltaTime;
            GameManager.Instance?.AddBurningTrash(deltaTime / burningTrashLifetime);
            yield return null;
        }

        if (trash != null)
        {
            GameManager.Instance?.NotifyBurningTargetEnded(trash.transform);
            StopSmokeAndLetItFade(smoke);
            Destroy(trash);
        }

        burningTrash.Remove(trash);
    }

    private void StopSmokeAndLetItFade(GameObject smoke)
    {
        if (smoke == null)
            return;

        smoke.transform.SetParent(null, true);
        ParticleSystem[] particleSystems = smoke.GetComponentsInChildren<ParticleSystem>(true);
        float cleanupDelay = 2f;
        foreach (ParticleSystem particleSystem in particleSystems)
        {
            ParticleSystem.MainModule main = particleSystem.main;
            cleanupDelay = Mathf.Max(cleanupDelay, main.startLifetime.constantMax + 0.5f);
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        Destroy(smoke, cleanupDelay);
    }

    private int CountActiveTrash()
    {
        int count = 0;
        TrashStacker[] trashItems = FindObjectsByType<TrashStacker>(FindObjectsSortMode.None);
        foreach (TrashStacker trash in trashItems)
        {
            if (trash != null && !trash.IsCollected)
                count++;
        }

        return count;
    }

    private bool IsInsideSpawnArea(Vector3 worldPosition)
    {
        Vector3 localPosition = spawnArea.transform.InverseTransformPoint(worldPosition) - spawnArea.center;
        Vector3 halfSize = spawnArea.size * 0.5f;
        Vector3 scale = spawnArea.transform.lossyScale;
        float localSurfaceOffset = surfaceOffset /
            Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        float localTolerance = 0.02f / Mathf.Max(Mathf.Abs(scale.y), 0.0001f);
        return Mathf.Abs(localPosition.x) <= halfSize.x &&
               Mathf.Abs(localPosition.y) <= halfSize.y + localSurfaceOffset + localTolerance &&
               Mathf.Abs(localPosition.z) <= halfSize.z;
    }
}
