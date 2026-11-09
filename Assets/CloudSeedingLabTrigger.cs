using UnityEngine;

/// <summary>Relays contacts with the Lab's box trigger to the cloud-seeding manager.</summary>
public class CloudSeedingLabTrigger : MonoBehaviour
{
    private CloudSeedingEventManager manager;
    private Collider targetCollider;
    private bool isLandingCollider;

    public bool IsConfiguredFor(Collider collider, bool landingCollider)
    {
        return targetCollider == collider && isLandingCollider == landingCollider;
    }

    public bool IsUnconfigured => targetCollider == null;

    public void Initialize(CloudSeedingEventManager eventManager, Collider collider,
        bool landingCollider)
    {
        manager = eventManager;
        targetCollider = collider;
        isLandingCollider = landingCollider;
    }

    private void OnTriggerEnter(Collider other)
    {
        NotifyManager(other);
    }

    private void OnTriggerStay(Collider other)
    {
        NotifyManager(other);
    }

    private void NotifyManager(Collider other)
    {
        if (manager == null || other == null || targetCollider == null ||
            !IsOverlappingTarget(other))
            return;

        HelicopterFlightController helicopter =
            other.GetComponentInParent<HelicopterFlightController>();
        if (helicopter == null)
            return;

        if (isLandingCollider)
            manager.OnLabLandingTrigger(helicopter);
        else
            manager.OnLabOuterTrigger(helicopter);
    }

    private void OnTriggerExit(Collider other)
    {
        if (manager == null || other == null || targetCollider == null || isLandingCollider)
            return;

        HelicopterFlightController helicopter =
            other.GetComponentInParent<HelicopterFlightController>();
        if (helicopter == null || AnyHelicopterColliderStillOverlaps(helicopter))
            return;

        manager.OnLabBuildingTriggerExit(helicopter);
    }

    private bool AnyHelicopterColliderStillOverlaps(HelicopterFlightController helicopter)
    {
        PlayerController player = helicopter.GetComponentInParent<PlayerController>();
        Collider[] colliders = player != null
            ? player.GetComponentsInChildren<Collider>(true)
            : helicopter.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < colliders.Length; i++)
        {
            Collider candidate = colliders[i];
            if (candidate != null && candidate.enabled && IsOverlappingTarget(candidate))
                return true;
        }

        return false;
    }

    private bool IsOverlappingTarget(Collider other)
    {
        if (targetCollider == null || other == null || !targetCollider.enabled || !other.enabled)
            return false;

        Vector3 direction;
        float distance;
        return Physics.ComputePenetration(targetCollider, targetCollider.transform.position,
            targetCollider.transform.rotation, other, other.transform.position,
            other.transform.rotation, out direction, out distance);
    }
}
