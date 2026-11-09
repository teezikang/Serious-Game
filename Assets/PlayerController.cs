using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Player Stats")]
    [SerializeField] private int money = 0;
    [SerializeField] private int life = 5;
    [SerializeField, Min(1)] private int maxTrashCapacity = 5;
    [SerializeField] private float moveSpeed = 6f;
    [SerializeField] private float turnSpeed = 720f;

    [Header("Character Animation")]
    [SerializeField] private Animator characterAnimator;
    [SerializeField] private AnimationClip idleAnimationClip;
    [SerializeField] private AnimationClip walkAnimationClip;
    [SerializeField] private string idleStateName = "Idle";
    [SerializeField] private string walkStateName = "Run";
    [SerializeField, Min(0.05f)] private float helicopterRootLiftSmoothTime = 2.2f;

    private const string AnimatorLayerName = "Base Layer";

    [Header("Other Settings")]
    private Rigidbody body;
    private Vector3 movementInput;
    private bool wasWalking;
    private bool animationClipsApplied;
    private bool helicopterFlightControlEnabled;
    private Renderer[] characterRenderers;
    private Collider rootCollider;
    private bool helicopterRootMoveActive;
    private bool helicopterRootPositionCaptured;
    private bool returningFromHelicopterLab;
    private bool originalUseGravity;
    private bool originalColliderEnabled;
    private float originalHelicopterRootY;
    private float helicopterRootColliderBottomOffset;
    private float helicopterRootTargetY;
    private float helicopterRootVerticalVelocity;

    public bool IsMovingForHelicopterFlight => helicopterRootMoveActive;

    public void SetHelicopterFlightControlEnabled(bool enabled)
    {
        helicopterFlightControlEnabled = enabled;
    }

    public void SetCharacterMeshVisible(bool visible)
    {
        if (characterRenderers == null || characterRenderers.Length == 0)
            CacheCharacterRenderers();

        for (int i = 0; i < characterRenderers.Length; i++)
        {
            if (characterRenderers[i] != null)
                characterRenderers[i].enabled = visible;
        }
    }

    /// <summary>Raises the hidden player body so its collider can reach the helicopter landing roof.</summary>
    public float BeginHelicopterLabLift(float platformTopWorldY)
    {
        CaptureHelicopterRootPosition();
        return BeginHelicopterLabLiftToWorldY(
            platformTopWorldY - helicopterRootColliderBottomOffset + 0.02f);
    }

    /// <summary>Moves the hidden player body to a previously saved helicopter altitude.</summary>
    public float BeginHelicopterLabLiftToWorldY(float worldY)
    {
        CaptureHelicopterRootPosition();
        helicopterRootTargetY = worldY;
        returningFromHelicopterLab = false;
        StartHelicopterRootHeightMove();
        return helicopterRootTargetY;
    }

    private void CaptureHelicopterRootPosition()
    {
        if (body == null)
            body = GetComponent<Rigidbody>();
        if (rootCollider == null)
            rootCollider = GetComponent<Collider>();

        if (helicopterRootPositionCaptured)
            return;

        originalHelicopterRootY = body != null ? body.position.y : transform.position.y;
        originalUseGravity = body != null && body.useGravity;
        originalColliderEnabled = rootCollider != null && rootCollider.enabled;
        helicopterRootColliderBottomOffset = rootCollider != null && rootCollider.enabled
            ? rootCollider.bounds.min.y - transform.position.y
            : 0f;
        helicopterRootPositionCaptured = true;
    }

    /// <summary>Returns the player body to its original ground height when leaving the lab roof.</summary>
    public void ReturnFromHelicopterLab()
    {
        if (!helicopterRootPositionCaptured)
            return;

        helicopterRootTargetY = originalHelicopterRootY;
        returningFromHelicopterLab = true;
        StartHelicopterRootHeightMove();
    }

    /// <summary>Enables the player collider once the helicopter and player are over the roof.</summary>
    public void CompleteHelicopterLabLanding()
    {
        if (!helicopterRootPositionCaptured || helicopterRootMoveActive)
            return;

        RestoreHelicopterRootPhysics();
    }

    /// <summary>Keeps the hidden player body at seeding altitude after seed purchase.</summary>
    public void HoldAtHelicopterSeedingHeight()
    {
        if (!helicopterRootPositionCaptured || helicopterRootMoveActive)
            return;

        if (body != null)
        {
            body.useGravity = false;
            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
        }

        if (rootCollider != null)
            rootCollider.enabled = false;
    }

    private void StartHelicopterRootHeightMove()
    {
        helicopterRootMoveActive = true;
        helicopterRootVerticalVelocity = 0f;
        if (body != null)
        {
            body.useGravity = false;
            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
        }

        if (rootCollider != null)
            rootCollider.enabled = false;
    }

    private void FinishHelicopterRootHeightMove()
    {
        helicopterRootMoveActive = false;
        if (body != null)
        {
            Vector3 position = body.position;
            position.y = helicopterRootTargetY;
            body.position = position;
            Vector3 velocity = body.linearVelocity;
            velocity.y = 0f;
            body.linearVelocity = velocity;
            body.angularVelocity = Vector3.zero;
        }
        else
        {
            Vector3 position = transform.position;
            position.y = helicopterRootTargetY;
            transform.position = position;
        }

        if (returningFromHelicopterLab)
        {
            RestoreHelicopterRootPhysics();
            helicopterRootPositionCaptured = false;
            returningFromHelicopterLab = false;
        }
    }

    private void RestoreHelicopterRootPhysics()
    {
        if (body != null)
            body.useGravity = originalUseGravity;
        if (rootCollider != null)
            rootCollider.enabled = originalColliderEnabled;
    }

    private void CacheCharacterRenderers()
    {
        if (characterAnimator == null)
            characterAnimator = GetComponentInChildren<Animator>();

        Transform characterRoot = characterAnimator != null ? characterAnimator.transform : transform;
        Renderer[] candidates = characterRoot.GetComponentsInChildren<Renderer>(true);
        var visibleCharacterRenderers = new List<Renderer>(candidates.Length);

        for (int i = 0; i < candidates.Length; i++)
        {
            Transform current = candidates[i].transform;
            bool belongsToNonCharacterObject = false;
            while (current != null && current != characterRoot)
            {
                if (current.name == "Collected Trash Stack" ||
                    current.name == "Collected Water Stack" ||
                    current.name == "Collected Money Stack" ||
                    current.name == "Recycle Center Money Stack" ||
                    current.GetComponent<Transport>() != null)
                {
                    belongsToNonCharacterObject = true;
                    break;
                }

                current = current.parent;
            }

            if (!belongsToNonCharacterObject)
                visibleCharacterRenderers.Add(candidates[i]);
        }

        characterRenderers = visibleCharacterRenderers.ToArray();
    }

    public void EarnMoney(int i)
    { 
        money += i;
    }
    public int CheckMoney()
    {
        return money;
    }

    public int MaxTrashCapacity => maxTrashCapacity;

    public int CurrentTrashCount
    {
        get
        {
            Transform trashStack = transform.Find("Collected Trash Stack");
            return trashStack != null ? trashStack.childCount : 0;
        }
    }

    public void AddCapacity(int i)
    {
        maxTrashCapacity += i;
    }

    public void AddSpeed(float i)
    {
        moveSpeed += i;
    }

    public void Paid(int i)
    {
        money -= i;
        Debug.Log("Money Left: " + money);
    }

    public bool TrySpendMoney(int amount)
    {
        if (amount <= 0 || money < amount)
            return false;

        Paid(amount);
        int remainingVisualMoney = amount;
        RemoveSpentMoneyVisuals(transform, ref remainingVisualMoney);

        return true;
    }

    private void RemoveSpentMoneyVisuals(Transform parent, ref int amountToRemove)
    {
        if (parent == null || amountToRemove <= 0)
            return;

        for (int i = 0; i < parent.childCount && amountToRemove > 0; i++)
        {
            Transform child = parent.GetChild(i);
            if (child == null)
                continue;

            if (child.CompareTag("Money"))
            {
                foreach (Collider moneyCollider in child.GetComponentsInChildren<Collider>(true))
                    moneyCollider.enabled = false;
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
                amountToRemove--;
            }
            else if (child != transform && child.GetComponent<Amount>() != null)
            {
                List<Transform> cashItems = new List<Transform>();
                for (int itemIndex = 0; itemIndex < child.childCount; itemIndex++)
                {
                    Transform item = child.GetChild(itemIndex);
                    if (item.CompareTag("Money") || item.GetComponent<MoneyStacker>() != null)
                        cashItems.Add(item);
                }

                if (cashItems.Count == 0)
                {
                    RemoveSpentMoneyVisuals(child, ref amountToRemove);
                    continue;
                }

                int removedCount = Mathf.Min(amountToRemove, cashItems.Count);
                for (int itemIndex = 0; itemIndex < removedCount; itemIndex++)
                {
                    cashItems[itemIndex].gameObject.SetActive(false);
                    Destroy(cashItems[itemIndex].gameObject);
                }

                amountToRemove -= removedCount;
                int remainingItems = cashItems.Count - removedCount;
                Amount amountComponent = child.GetComponent<Amount>();
                if (amountComponent != null)
                    amountComponent.amount = remainingItems;

                foreach (Collider stackCollider in child.GetComponentsInChildren<Collider>(true))
                    stackCollider.enabled = false;

                if (remainingItems <= 0)
                {
                    child.gameObject.SetActive(false);
                    Destroy(child.gameObject);
                }
            }
            else
            {
                RemoveSpentMoneyVisuals(child, ref amountToRemove);
            }
        }
    }

    public bool CanCarryTrash(int currentTrashCount)
    {
        return currentTrashCount < maxTrashCapacity;
    }

    private void Awake()
    {
        body = GetComponent<Rigidbody>();
        rootCollider = GetComponent<Collider>();
        body.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        body.interpolation = RigidbodyInterpolation.Interpolate;

        if (characterAnimator == null)
            characterAnimator = GetComponentInChildren<Animator>();

        ApplyAnimationClips();

        if (characterAnimator != null)
        {
            int idleHash = GetAnimationStateHash(idleStateName);
            if (idleHash != 0)
                characterAnimator.Play(idleHash, 0, 0f);
        }
    }

    private void Update()
    {
        if (GameManager.Instance != null && !GameManager.GameplayStarted)
        {
            movementInput = Vector3.zero;
            UpdateMovementAnimation();
            return;
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
        {
            movementInput = Vector3.zero;
            UpdateMovementAnimation();
            return;
        }

        float horizontal = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                         - (keyboard.leftArrowKey.isPressed ||
                            (!helicopterFlightControlEnabled && keyboard.aKey.isPressed) ? 1f : 0f);
        float vertical = (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed ? 1f : 0f)
                       - (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed ? 1f : 0f);

        // Normalize so diagonal movement is not faster than moving along one axis.
        movementInput = new Vector3(horizontal, 0f, vertical).normalized;
        UpdateMovementAnimation();
    }

    private void FixedUpdate()
    {
        if (movementInput.sqrMagnitude > 0.001f)
        {
            Quaternion targetRotation = Quaternion.LookRotation(movementInput, Vector3.up);
            body.MoveRotation(Quaternion.RotateTowards(
                body.rotation,
                targetRotation,
                turnSpeed * Time.fixedDeltaTime));
        }
        else if (body != null)
        {
            // MoveRotation controls the player's facing directly. Clear leftover
            // physics yaw once input stops so the attached helicopter cannot keep spinning.
            Vector3 angularVelocity = body.angularVelocity;
            angularVelocity.y = 0f;
            body.angularVelocity = angularVelocity;
        }

        Vector3 velocity = movementInput * moveSpeed;
        if (helicopterRootMoveActive)
        {
            // The helicopter can climb or descend while the player steers freely in X/Z.
            float currentY = body.position.y;
            float nextY = Mathf.SmoothDamp(currentY, helicopterRootTargetY,
                ref helicopterRootVerticalVelocity, helicopterRootLiftSmoothTime,
                Mathf.Infinity, Time.fixedDeltaTime);
            bool reachedTarget = Mathf.Abs(nextY - helicopterRootTargetY) <= 0.02f &&
                                 Mathf.Abs(helicopterRootVerticalVelocity) <= 0.05f;
            if (reachedTarget)
            {
                nextY = helicopterRootTargetY;
                FinishHelicopterRootHeightMove();
            }
            else
                velocity.y = (nextY - currentY) / Time.fixedDeltaTime;

            if (reachedTarget)
                velocity.y = 0f;
        }
        else
            velocity.y = body.linearVelocity.y;

        body.linearVelocity = velocity;
    }

    private void UpdateMovementAnimation()
    {
        if (characterAnimator == null)
        {
            characterAnimator = GetComponentInChildren<Animator>();
            if (characterAnimator != null)
                ApplyAnimationClips();
        }

        if (characterAnimator == null)
            return;

        bool isWalking = movementInput.sqrMagnitude > 0.001f;
        string stateName = isWalking ? walkStateName : idleStateName;
        int stateHash = GetAnimationStateHash(stateName);

        if (stateHash == 0)
        {
            if (!animationClipsApplied)
                Debug.LogWarning($"Animator on {characterAnimator.name} has no '{stateName}' state.", characterAnimator);
            return;
        }

        AnimatorStateInfo currentState = characterAnimator.GetCurrentAnimatorStateInfo(0);
        AnimatorStateInfo nextState = characterAnimator.GetNextAnimatorStateInfo(0);
        bool alreadyInDesiredState = currentState.fullPathHash == stateHash || nextState.fullPathHash == stateHash;
        if (isWalking == wasWalking && alreadyInDesiredState)
            return;

        wasWalking = isWalking;
        characterAnimator.CrossFadeInFixedTime(stateHash, 0.12f);
    }

    private int GetAnimationStateHash(string stateName)
    {
        int fullPathHash = Animator.StringToHash($"{AnimatorLayerName}.{stateName}");
        if (characterAnimator.HasState(0, fullPathHash))
            return fullPathHash;

        int shortNameHash = Animator.StringToHash(stateName);
        return characterAnimator.HasState(0, shortNameHash) ? shortNameHash : 0;
    }

    private void ApplyAnimationClips()
    {
        if (characterAnimator == null || animationClipsApplied)
            return;

        animationClipsApplied = true;
        if ((idleAnimationClip == null && walkAnimationClip == null) ||
            characterAnimator.runtimeAnimatorController == null)
            return;

        AnimatorOverrideController overrideController =
            new AnimatorOverrideController(characterAnimator.runtimeAnimatorController);
        var overrides = new List<KeyValuePair<AnimationClip, AnimationClip>>();
        overrideController.GetOverrides(overrides);

        for (int i = 0; i < overrides.Count; i++)
        {
            AnimationClip originalClip = overrides[i].Key;
            AnimationClip replacementClip = overrides[i].Value;

            if (originalClip == null)
                continue;

            if (originalClip.name == idleStateName && idleAnimationClip != null)
                replacementClip = idleAnimationClip;
            else if ((originalClip.name == walkStateName || originalClip.name == "Walk" || originalClip.name == "Run") && walkAnimationClip != null)
                replacementClip = walkAnimationClip;

            overrides[i] = new KeyValuePair<AnimationClip, AnimationClip>(originalClip, replacementClip);
        }

        overrideController.ApplyOverrides(overrides);
        characterAnimator.runtimeAnimatorController = overrideController;
    }
}
