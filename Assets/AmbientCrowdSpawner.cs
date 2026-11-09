using UnityEngine;

public class AmbientCrowdSpawner : MonoBehaviour
{
    [SerializeField] private GameObject characterPrefab;
    [SerializeField, Min(1)] private int characterCount = 4;
    [SerializeField, Min(0.1f)] private float walkSpeed = 1.6f;
    [SerializeField, Min(1f)] private float wanderRadius = 14f;
    [SerializeField, Min(1f)] private float minSpawnDistanceFromPlayer = 16f;
    [SerializeField, Min(1f)] private float maxSpawnDistanceFromPlayer = 38f;
    [SerializeField, Min(0.5f)] private float minSpacingBetweenCharacters = 7f;
    [SerializeField, Min(1)] private int spawnAttemptsPerCharacter = 40;

    private static readonly Color[] ToonPalette =
    {
        new Color(0.35f, 0.72f, 0.93f),
        new Color(0.94f, 0.43f, 0.34f),
        new Color(0.42f, 0.78f, 0.48f),
        new Color(0.70f, 0.51f, 0.87f),
        new Color(0.95f, 0.69f, 0.25f)
    };

    private void Start()
    {
        if (characterPrefab == null)
        {
            Debug.LogWarning("Assign the Resident prefab to AmbientCrowdSpawner.", this);
            return;
        }

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("Ambient characters could not find the Player tag.", this);
            return;
        }

        Animator playerAnimator = player.GetComponentInChildren<Animator>();
        RuntimeAnimatorController playerController =
            playerAnimator != null ? playerAnimator.runtimeAnimatorController : null;

        Vector3 playerPosition = player.transform.position;
        Vector3 spawnCenter = playerPosition;
        float minDistance = Mathf.Min(minSpawnDistanceFromPlayer, maxSpawnDistanceFromPlayer);
        float maxDistance = Mathf.Max(minSpawnDistanceFromPlayer, maxSpawnDistanceFromPlayer);
        int spawned = 0;
        Vector3[] chosenPositions = new Vector3[characterCount];

        for (int i = 0; i < characterCount; i++)
        {
            if (!TryFindSpawnPoint(spawnCenter, minDistance, maxDistance,
                    chosenPositions, spawned, out Vector3 position))
            {
                Debug.LogWarning($"Could only find {spawned} clear Resident spawn points.", this);
                break;
            }

            GameObject character = Instantiate(characterPrefab, position,
                Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            character.name = $"Town Resident {i + 1}";
            character.tag = "Untagged";

            Animator animator = character.GetComponentInChildren<Animator>();
            AmbientCharacterWalker walker = character.AddComponent<AmbientCharacterWalker>();
            walker.Initialize(animator, playerController, player.transform, walkSpeed, wanderRadius,
                ToonPalette[i % ToonPalette.Length]);
            chosenPositions[spawned] = position;
            spawned++;
        }
    }

    private bool TryFindSpawnPoint(Vector3 center, float minDistance, float maxDistance,
        Vector3[] chosenPositions, int chosenCount, out Vector3 position)
    {
        for (int attempt = 0; attempt < spawnAttemptsPerCharacter; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle.normalized * Random.Range(minDistance, maxDistance);
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
            candidate.y = center.y;

            if (Physics.CheckSphere(candidate + Vector3.up * 0.55f, 0.45f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                continue;

            bool tooClose = false;
            for (int i = 0; i < chosenCount; i++)
            {
                Vector3 existing = chosenPositions[i] - candidate;
                existing.y = 0f;
                if (existing.sqrMagnitude < minSpacingBetweenCharacters * minSpacingBetweenCharacters)
                {
                    tooClose = true;
                    break;
                }
            }

            if (tooClose)
                continue;

            position = candidate;
            return true;
        }

        position = default;
        return false;
    }
}
