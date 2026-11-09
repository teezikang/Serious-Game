using UnityEngine;

public class AmbientCharacterWalker : MonoBehaviour
{
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int LegacyColorId = Shader.PropertyToID("_Color");

    [SerializeField, Min(0.1f)] private float walkSpeed = 1.6f;
    [SerializeField, Min(1f)] private float wanderRadius = 14f;
    [SerializeField, Min(0.1f)] private float minPauseDuration = 0.5f;
    [SerializeField, Min(0.1f)] private float maxPauseDuration = 2.5f;
    [SerializeField, Min(1f)] private float minSocialVisitInterval = 18f;
    [SerializeField, Min(1f)] private float maxSocialVisitInterval = 35f;

    private Animator characterAnimator;
    private Transform player;
    private Vector3 homePosition;
    private Vector3 destination;
    private float idleTimeRemaining;
    private float socialVisitTimer;
    private bool isWalking;
    private bool visitingPlayer;
    private int idleStateHash;
    private int walkStateHash;

    public void Initialize(Animator animator, RuntimeAnimatorController controller, Transform playerTransform,
        float speed, float radius, Color tint)
    {
        characterAnimator = animator;
        player = playerTransform;
        walkSpeed = speed;
        wanderRadius = radius;
        homePosition = transform.position;
        idleTimeRemaining = Random.Range(minPauseDuration, maxPauseDuration);
        socialVisitTimer = Random.Range(minSocialVisitInterval, maxSocialVisitInterval);

        if (characterAnimator != null && controller != null)
        {
            characterAnimator.runtimeAnimatorController = controller;
            characterAnimator.Rebind();
            idleStateHash = Animator.StringToHash("Base Layer.Idle");
            walkStateHash = Animator.StringToHash("Base Layer.Run");
            characterAnimator.Play(idleStateHash, 0, Random.value);
        }

        ApplyTint(tint);
    }

    private void Update()
    {
        if (characterAnimator == null)
            return;

        if (isWalking)
        {
            Vector3 offset = destination - transform.position;
            offset.y = 0f;

            if (offset.sqrMagnitude <= 0.08f)
            {
                isWalking = false;
                idleTimeRemaining = Random.Range(minPauseDuration, maxPauseDuration);
                if (visitingPlayer && player != null)
                    FacePlayer();
                visitingPlayer = false;
                PlayState(idleStateHash);
                return;
            }

            transform.position = Vector3.MoveTowards(
                transform.position, destination, walkSpeed * Time.deltaTime);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                Quaternion.LookRotation(offset.normalized, Vector3.up),
                360f * Time.deltaTime);
            return;
        }

        idleTimeRemaining -= Time.deltaTime;
        socialVisitTimer -= Time.deltaTime;
        if (idleTimeRemaining > 0f)
            return;

        // Occasionally stroll near the player, pause, and face them before resuming normal wandering.
        if (player != null && socialVisitTimer <= 0f && Random.value < 0.55f && TryChoosePlayerVisit())
        {
            socialVisitTimer = Random.Range(minSocialVisitInterval, maxSocialVisitInterval);
            return;
        }

        if (socialVisitTimer <= 0f)
            socialVisitTimer = Random.Range(minSocialVisitInterval, maxSocialVisitInterval);
        ChooseWanderDestination();
    }

    private bool TryChoosePlayerVisit()
    {
        Vector2 towardPlayer = new Vector2(player.position.x - transform.position.x,
            player.position.z - transform.position.z);
        if (towardPlayer.sqrMagnitude < 0.01f)
            return false;

        Vector2 side = new Vector2(-towardPlayer.y, towardPlayer.x).normalized;
        Vector2 targetOffset = -towardPlayer.normalized * Random.Range(2.5f, 4f)
            + side * Random.Range(-2f, 2f);
        Vector3 candidate = new Vector3(player.position.x + targetOffset.x,
            homePosition.y, player.position.z + targetOffset.y);

        if (Vector3.Distance(transform.position, candidate) > wanderRadius * 1.5f
            || !HasClearPath(candidate))
            return false;

        destination = candidate;
        visitingPlayer = true;
        isWalking = true;
        PlayState(walkStateHash);
        return true;
    }

    private void ChooseWanderDestination()
    {
        for (int attempt = 0; attempt < 12; attempt++)
        {
            Vector2 randomOffset = Random.insideUnitCircle * wanderRadius;
            Vector3 candidate = homePosition + new Vector3(randomOffset.x, 0f, randomOffset.y);
            Vector3 direction = candidate - transform.position;
            direction.y = 0f;

            if (direction.sqrMagnitude < 1.5f || !HasClearPath(candidate))
                continue;

            destination = candidate;
            visitingPlayer = false;
            isWalking = true;
            PlayState(walkStateHash);
            return;
        }

        idleTimeRemaining = Random.Range(minPauseDuration, maxPauseDuration);
        PlayState(idleStateHash);
    }

    private bool HasClearPath(Vector3 candidate)
    {
        Vector3 direction = candidate - transform.position;
        direction.y = 0f;
        float distance = direction.magnitude;
        if (distance <= 0.1f)
            return false;

        Vector3 rayOrigin = transform.position + Vector3.up * 0.55f;
        return !Physics.SphereCast(rayOrigin, 0.25f, direction.normalized, out _, distance,
            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
    }

    private void FacePlayer()
    {
        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude > 0.01f)
            transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
    }

    private void PlayState(int stateHash)
    {
        if (characterAnimator != null && stateHash != 0 && characterAnimator.HasState(0, stateHash))
            characterAnimator.CrossFadeInFixedTime(stateHash, 0.15f);
    }

    private void ApplyTint(Color tint)
    {
        MaterialPropertyBlock block = new MaterialPropertyBlock();
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>(true))
        {
            renderer.GetPropertyBlock(block);
            block.SetColor(BaseColorId, tint);
            block.SetColor(LegacyColorId, tint);
            renderer.SetPropertyBlock(block);
        }
    }
}
