using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class RecycleCenter : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string trashTag = "Trash";
    [SerializeField] private string stackObjectName = "Collected Trash Stack";
    [SerializeField] private GameObject moneyPrefab;
    [SerializeField, Min(1)] private int moneyPerTrash = 2;
    [SerializeField] private float moneySpawnHeight = 0.6f;
    [SerializeField] private float moneyStackSpacing = 0.25f;
    [SerializeField, Min(0.01f)] private float trashDropAnimationDuration = 0.4f;
    [SerializeField, Min(0f)] private float trashDropStaggerDelay = 0.045f;
    [SerializeField, Min(0f)] private float trashDropArcHeight = 0.45f;
    [SerializeField, Min(0f)] private float trashDropSpinDegrees = 300f;
    [SerializeField, Min(0f)] private float trashDropScatterRadius = 0.25f;
    [SerializeField] private float trashDropLandingHeight = 0.2f;

    private Transform spawnedMoneyStack;

    private void OnTriggerEnter(Collider other)
    {
        if (other == null || ForestFireEventManager.IsFireEventActive)
            return;

        if (other.CompareTag(trashTag))
        {
            DestroyTrash(other.gameObject);
            return;
        }

        Transform player = FindTaggedParent(other.transform, playerTag);
        if (player == null)
            return;

        Transform stack = player.Find(stackObjectName);
        if (stack == null)
            return;

        int trashCount = stack.childCount;
        AnimateTrashDelivery(stack);
        Destroy(stack.gameObject);
        GameManager.Instance?.RecycleTrash(trashCount);
        SpawnMoney(trashCount * Mathf.Max(1, moneyPerTrash));
    }

    private void AnimateTrashDelivery(Transform stack)
    {
        Vector3 landingCenter = transform.position + Vector3.up * trashDropLandingHeight;
        for (int i = stack.childCount - 1; i >= 0; i--)
        {
            Transform trash = stack.GetChild(i);
            trash.SetParent(null, true);

            foreach (Collider itemCollider in trash.GetComponentsInChildren<Collider>(true))
                itemCollider.enabled = false;

            Rigidbody body = trash.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            TrashStacker stacker = trash.GetComponent<TrashStacker>();
            if (stacker != null)
            {
                stacker.StopPickupAnimation();
                stacker.enabled = false;
            }

            Vector2 scatter = Random.insideUnitCircle * trashDropScatterRadius;
            Vector3 landingPosition = landingCenter + new Vector3(scatter.x, 0f, scatter.y);
            StartCoroutine(AnimateTrashDrop(trash, landingPosition, i * trashDropStaggerDelay));
        }
    }

    /// <summary>Receives a worker's load, tosses the trash into the center, then spawns its collectible payout.</summary>
    public bool ReceiveStaffDelivery(IList<TrashStacker> deliveredTrash, int rewardPerTrash)
    {
        if (deliveredTrash == null || deliveredTrash.Count == 0)
            return false;

        int deliveredCount = 0;
        Vector3 landingCenter = transform.position + Vector3.up * trashDropLandingHeight;
        for (int i = 0; i < deliveredTrash.Count; i++)
        {
            TrashStacker stacker = deliveredTrash[i];
            if (stacker == null)
                continue;

            Transform trash = stacker.transform;
            trash.SetParent(null, true);
            foreach (Collider itemCollider in trash.GetComponentsInChildren<Collider>(true))
                itemCollider.enabled = false;

            Rigidbody body = trash.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            stacker.StopPickupAnimation();
            stacker.enabled = false;

            Vector2 scatter = Random.insideUnitCircle * trashDropScatterRadius;
            Vector3 landingPosition = landingCenter + new Vector3(scatter.x, 0f, scatter.y);
            StartCoroutine(AnimateTrashDrop(trash, landingPosition, i * trashDropStaggerDelay));
            deliveredCount++;
        }

        if (deliveredCount == 0)
            return false;

        float deliveryAnimationTime = trashDropAnimationDuration +
            Mathf.Max(0, deliveredCount - 1) * trashDropStaggerDelay;
        StartCoroutine(FinalizeStaffDelivery(deliveredCount, rewardPerTrash, deliveryAnimationTime));
        return true;
    }

    private IEnumerator FinalizeStaffDelivery(int trashCount, int rewardPerTrash, float delay)
    {
        yield return new WaitForSecondsRealtime(delay);
        GameManager.Instance?.RecycleTrash(trashCount);
        SpawnMoney(trashCount * Mathf.Max(0, rewardPerTrash));
    }

    private IEnumerator AnimateTrashDrop(Transform trash, Vector3 landingPosition, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSecondsRealtime(delay);

        if (trash == null)
            yield break;

        Vector3 startPosition = trash.position;
        Vector3 startScale = trash.localScale;
        Quaternion startRotation = trash.rotation;
        float elapsed = 0f;

        while (elapsed < trashDropAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / trashDropAnimationDuration);
            float easedT = 1f - Mathf.Pow(1f - t, 3f);
            float arc = Mathf.Sin(t * Mathf.PI) * trashDropArcHeight;

            trash.position = Vector3.Lerp(startPosition, landingPosition, easedT) + Vector3.up * arc;
            trash.rotation = Quaternion.Euler(0f, trashDropSpinDegrees * t, 0f) * startRotation;
            trash.localScale = Vector3.Lerp(startScale, Vector3.zero, easedT);
            yield return null;
        }

        if (trash != null)
            Destroy(trash.gameObject);
    }

    private void DestroyTrash(GameObject trash)
    {
        if (trash != null)
        {
            GameManager.Instance?.RecycleTrash(1);
            Destroy(trash);
        }

    }

    private void SpawnMoney(int count)
    {
        if (count <= 0)
            return;

        if (moneyPrefab == null)
        {
            Debug.LogWarning("Assign the Money prefab to RecycleCenter to spawn delivery rewards.", this);
            return;
        }

        EnsureMoneyStack();
        int existingMoneyCount = spawnedMoneyStack.childCount;

        for (int i = 0; i < count; i++)
        {
            Vector3 spawnPosition = spawnedMoneyStack.position +
                Vector3.up * ((existingMoneyCount + i) * moneyStackSpacing);
            GameObject money = Instantiate(moneyPrefab, spawnPosition, Quaternion.identity);
            money.transform.SetParent(spawnedMoneyStack, true);
        }

        if(spawnedMoneyStack.GetComponent<Amount>()!=null)
        {
            spawnedMoneyStack.GetComponent<Amount>().amount = spawnedMoneyStack.childCount;
        }
    }

    private void EnsureMoneyStack()
    {
        // MoneyStacker moves this payout stack under the player when it is
        // collected. Do not reuse that carried object for the next payout.
        if (spawnedMoneyStack != null &&
            FindTaggedParent(spawnedMoneyStack.parent, playerTag) != null)
        {
            spawnedMoneyStack = null;
        }

        if (spawnedMoneyStack != null)
            return;

        GameObject stackObject = new GameObject("Recycle Center Money Stack");
        stackObject.transform.position = transform.position + Vector3.up * moneySpawnHeight;
        spawnedMoneyStack = stackObject.transform;
        spawnedMoneyStack.gameObject.AddComponent<Amount>();
        spawnedMoneyStack.AddComponent<BoxCollider>().isTrigger = true;


    }

    private Transform FindTaggedParent(Transform candidate, string tagToFind)
    {
        Transform current = candidate;
        while (current != null)
        {
            if (current.CompareTag(tagToFind))
                return current;

            current = current.parent;
        }

        return null;
    }
}


