using System.Collections;
using UnityEngine;

/// <summary>Animates carried water onto the player stack and consumes it on fire contact.</summary>
public class WaterSpherePickup : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float pickupAnimationDuration = 0.28f;
    [SerializeField, Min(0f)] private float pickupArcHeight = 0.45f;

    private bool isCarried;
    private bool isCollecting;

    public void BeginCarry(Transform stack, int stackIndex)
    {
        if (isCarried || isCollecting || stack == null)
            return;

        isCollecting = true;
        Rigidbody body = GetComponent<Rigidbody>();
        if (body == null)
            body = gameObject.AddComponent<Rigidbody>();
        body.useGravity = false;
        body.isKinematic = true;
        body.linearVelocity = Vector3.zero;
        body.angularVelocity = Vector3.zero;

        transform.SetParent(stack, true);
        Vector3 startPosition = transform.localPosition;
        Quaternion startRotation = transform.localRotation;
        Vector3 startScale = transform.localScale;
        Vector3 targetPosition = Vector3.up * (1.5f + stackIndex * 0.8f);
        StartCoroutine(AnimateIntoStack(startPosition, startRotation, startScale, targetPosition));
    }

    public void SnapToStackPosition(int stackIndex)
    {
        StopAllCoroutines();
        isCollecting = false;
        isCarried = true;
        transform.localPosition = Vector3.up * (1.5f + stackIndex * 0.8f);
        transform.localRotation = Quaternion.identity;
    }

    public void PrepareForFireDrop()
    {
        StopAllCoroutines();
        isCarried = false;
        isCollecting = true;
        transform.SetParent(null, true);

        Collider[] colliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
            colliders[i].enabled = false;

        Rigidbody body = GetComponent<Rigidbody>();
        if (body != null)
        {
            body.useGravity = false;
            body.isKinematic = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    private IEnumerator AnimateIntoStack(Vector3 startPosition, Quaternion startRotation,
        Vector3 startScale, Vector3 targetPosition)
    {
        float elapsed = 0f;
        while (elapsed < pickupAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / pickupAnimationDuration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            float arc = Mathf.Sin(t * Mathf.PI) * pickupArcHeight;
            transform.localPosition = Vector3.Lerp(startPosition, targetPosition, eased) +
                                      Vector3.up * arc;
            transform.localRotation = Quaternion.Slerp(startRotation, Quaternion.identity, eased) *
                                      Quaternion.Euler(0f, 180f * Mathf.Sin(t * Mathf.PI), 0f);
            transform.localScale = startScale;
            yield return null;
        }

        transform.localPosition = targetPosition;
        transform.localRotation = Quaternion.identity;
        transform.localScale = startScale;
        isCollecting = false;
        isCarried = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == null)
            return;

        if (isCarried)
        {
            GameObject fire = FindForestFire(other.transform);
            if (fire != null)
                ForestFireEventManager.TryExtinguishFire(fire, gameObject);
            return;
        }

        if (isCollecting)
            return;

        Transform player = FindTaggedParent(other.transform, "Player");
        if (player != null)
            ForestFireEventManager.TryCollectWaterSphere(gameObject, player);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision != null)
            OnTriggerEnter(collision.collider);
    }

    private void OnTriggerStay(Collider other)
    {
        if (!isCarried || other == null)
            return;

        GameObject fire = FindForestFire(other.transform);
        if (fire != null)
            ForestFireEventManager.TryExtinguishFire(fire, gameObject);
    }

    private static GameObject FindForestFire(Transform candidate)
    {
        for (Transform current = candidate; current != null; current = current.parent)
        {
            if (current.name == "Forest Fire")
                return current.gameObject;
        }

        return null;
    }

    private static Transform FindTaggedParent(Transform candidate, string tag)
    {
        for (Transform current = candidate; current != null; current = current.parent)
        {
            if (current.CompareTag(tag))
                return current;
        }

        return null;
    }
}
