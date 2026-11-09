using System.Collections;
using UnityEngine;

/// <summary>Attaches this trash item to the player and places it on the carried stack.</summary>
public class TrashStacker : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string stackObjectName = "Collected Trash Stack";
    [SerializeField] private float firstItemHeight = 1.5f;
    [SerializeField] private float itemSpacing = 0.8f;
    [SerializeField, Min(0.01f)] private float pickupAnimationDuration = 0.3f;
    [SerializeField, Min(0f)] private float pickupArcHeight = 0.55f;
    [SerializeField, Min(0f)] private float pickupSpinDegrees = 180f;

    private bool collected;
    public bool IsCollected => collected;

    public void StopPickupAnimation()
    {
        StopAllCoroutines();
    }

    public bool CollectByWorker()
    {
        if (collected)
            return false;

        collected = true;
        StopAllCoroutines();

        foreach (Collider itemCollider in GetComponentsInChildren<Collider>(true))
            itemCollider.enabled = false;

        Rigidbody itemBody = GetComponent<Rigidbody>();
        if (itemBody != null)
        {
            itemBody.linearVelocity = Vector3.zero;
            itemBody.angularVelocity = Vector3.zero;
            itemBody.isKinematic = true;
        }

        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        TryAttachToPlayer(other != null ? other.transform : null);
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryAttachToPlayer(collision != null ? collision.transform : null);
    }

    private void TryAttachToPlayer(Transform candidate)
    {
        if (collected || candidate == null || ForestFireEventManager.IsFireEventActive)
            return;

        Transform player = FindTaggedParent(candidate);
        if (player == null)
            return;

        Transform stack = player.Find(stackObjectName);
        if (stack == null)
        {
            GameObject stackObject = new GameObject(stackObjectName);
            stack = stackObject.transform;
            stack.SetParent(player, false);
        }

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController != null && !playerController.CanCarryTrash(stack.childCount))
            return;

        int stackIndex = stack.childCount;
        collected = true;
        transform.SetParent(stack, true);
        Vector3 targetLocalPosition = Vector3.up * (firstItemHeight + stackIndex * itemSpacing);

        foreach (Collider itemCollider in GetComponentsInChildren<Collider>())
            itemCollider.enabled = false;

        Rigidbody itemBody = GetComponent<Rigidbody>();
        if (itemBody != null)
        {
            itemBody.linearVelocity = Vector3.zero;
            itemBody.angularVelocity = Vector3.zero;
            itemBody.isKinematic = true;
        }

        StartCoroutine(AnimatePickupToStack(targetLocalPosition));
    }

    private IEnumerator AnimatePickupToStack(Vector3 targetLocalPosition)
    {
        Vector3 startLocalPosition = transform.localPosition;
        Quaternion startLocalRotation = transform.localRotation;
        Vector3 startLocalScale = transform.localScale;
        float elapsed = 0f;

        while (elapsed < pickupAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / pickupAnimationDuration);
            float easedT = 1f - Mathf.Pow(1f - t, 3f);
            float arc = Mathf.Sin(t * Mathf.PI) * pickupArcHeight;

            transform.localPosition = Vector3.Lerp(startLocalPosition, targetLocalPosition, easedT) +
                                      Vector3.up * arc;
            transform.localRotation = Quaternion.Slerp(startLocalRotation, Quaternion.identity, easedT) *
                                      Quaternion.Euler(0f, pickupSpinDegrees * Mathf.Sin(t * Mathf.PI), 0f);
            transform.localScale = startLocalScale * (1f + 0.12f * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }

        transform.localPosition = targetLocalPosition;
        transform.localRotation = Quaternion.identity;
        transform.localScale = startLocalScale;
    }

    private Transform FindTaggedParent(Transform candidate)
    {
        Transform current = candidate;
        while (current != null)
        {
            if (current.CompareTag(playerTag))
                return current;

            current = current.parent;
        }

        return null;
    }

}
