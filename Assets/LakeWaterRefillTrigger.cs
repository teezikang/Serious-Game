using System.Collections.Generic;
using UnityEngine;

/// <summary>Refills the player's carried water when they enter the lake volume.</summary>
public class LakeWaterRefillTrigger : MonoBehaviour
{
    private readonly Dictionary<Transform, HashSet<Collider>> playerColliders =
        new Dictionary<Transform, HashSet<Collider>>();

    private void OnTriggerEnter(Collider other)
    {
        Transform player = FindTaggedParent(other != null ? other.transform : null);
        if (player == null || other == null)
            return;

        HashSet<Collider> colliders;
        if (!playerColliders.TryGetValue(player, out colliders))
        {
            colliders = new HashSet<Collider>();
            playerColliders.Add(player, colliders);
        }

        bool wasOutside = colliders.Count == 0;
        colliders.Add(other);
        if (wasOutside)
            ForestFireEventManager.PlayerEnteredLake(player);
    }

    private void OnTriggerExit(Collider other)
    {
        Transform player = FindTaggedParent(other != null ? other.transform : null);
        if (player == null || other == null)
            return;

        HashSet<Collider> colliders;
        if (playerColliders.TryGetValue(player, out colliders))
        {
            colliders.Remove(other);
            if (colliders.Count == 0)
            {
                playerColliders.Remove(player);
                ForestFireEventManager.PlayerExitedLake(player);
            }
        }
    }

    private void OnDisable()
    {
        foreach (Transform player in playerColliders.Keys)
            ForestFireEventManager.PlayerExitedLake(player);
        playerColliders.Clear();
    }

    private static Transform FindTaggedParent(Transform candidate)
    {
        for (Transform current = candidate; current != null; current = current.parent)
        {
            if (current.CompareTag("Player"))
                return current;
        }

        return null;
    }
}
