using System.Collections;
using UnityEngine;

/// <summary>Lets the player collect a money pickup and carry it in a stack.</summary>
public class MoneyStacker : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string stackObjectName = "Collected Money Stack";
    [SerializeField] private Vector3 stackOffset = new Vector3(0f, 0.24f, 0.7f);
    [SerializeField] private float itemSpacing = 0.3f;
    [SerializeField, Min(0.01f)] private float pickupAnimationDuration = 0.35f;
    [SerializeField, Min(0f)] private float pickupArcHeight = 0.4f;
    [SerializeField, Min(0f)] private float pickupSpinDegrees = 160f;

    private bool collected;

    private void OnTriggerEnter(Collider other)
    {
        TryAttachToPlayer(other != null ? other.transform : null);
    }

    private void OnTriggerStay(Collider other)
    {
        TryAttachToPlayer(other != null ? other.transform : null);
    }

    private void TryAttachToPlayer(Transform candidate)
    {
        if (collected || candidate == null || ForestFireEventManager.IsFireEventActive)
            return;

        Transform player = FindTaggedParent(candidate);
        if (player == null)
            return;

        Transform moneyStack = transform.parent != null ? transform.parent : transform;
        PlayerController playerController = player.GetComponent<PlayerController>();
        Amount payout = moneyStack != player ? moneyStack.GetComponent<Amount>() : GetComponent<Amount>();
        if (payout != null)
            payout.Collect(playerController);

        // All coins in the recycle-center pile share this parent. Reparent the
        // pile once, then place it at a fixed point in the player's local space.
        if (moneyStack == player)
        {
            foreach (Collider carriedMoneyCollider in GetComponents<Collider>())
                carriedMoneyCollider.enabled = false;
        }
        else
        {
            bool needsAttach = moneyStack.parent != player;
            if (needsAttach)
            {
                Transform carriedStack = player.Find("Recycle Center Money Stack") ??
                                         player.Find(stackObjectName);
                if (carriedStack != null && carriedStack != moneyStack)
                {
                    int itemIndex = carriedStack.childCount;
                    while (moneyStack.childCount > 0)
                    {
                        Transform item = moneyStack.GetChild(0);
                        item.SetParent(carriedStack, true);
                        item.localPosition = Vector3.up * (itemIndex++ * itemSpacing);
                        item.localRotation = Quaternion.identity;
                    }

                    Amount carriedAmount = carriedStack.GetComponent<Amount>();
                    if (carriedAmount != null)
                        carriedAmount.amount = carriedStack.childCount;
                    Destroy(moneyStack.gameObject);
                    moneyStack = carriedStack;
                    needsAttach = false;
                }
                else
                {
                    moneyStack.SetParent(player, true);
                }
            }

            // Do this even when the stack was already parented to the player.
            // Otherwise its trigger remains active and keeps colliding while carried.
            foreach (Collider carriedMoneyCollider in moneyStack.GetComponentsInChildren<Collider>(true))
                carriedMoneyCollider.enabled = false;

            if (needsAttach)
                StartCoroutine(AnimateMoneyStackToPlayer(moneyStack));
        }

        collected = true;
    }

    private IEnumerator AnimateMoneyStackToPlayer(Transform moneyStack)
    {
        Vector3 startLocalPosition = moneyStack.localPosition;
        Quaternion startLocalRotation = moneyStack.localRotation;
        Vector3 startLocalScale = moneyStack.localScale;
        float elapsed = 0f;

        while (elapsed < pickupAnimationDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / pickupAnimationDuration);
            float easedT = 1f - Mathf.Pow(1f - t, 3f);
            float arc = Mathf.Sin(t * Mathf.PI) * pickupArcHeight;

            moneyStack.localPosition = Vector3.Lerp(startLocalPosition, stackOffset, easedT) +
                                       Vector3.up * arc;
            moneyStack.localRotation = Quaternion.Slerp(startLocalRotation, Quaternion.identity, easedT) *
                                       Quaternion.Euler(0f, pickupSpinDegrees * Mathf.Sin(t * Mathf.PI), 0f);
            moneyStack.localScale = startLocalScale * (1f + 0.12f * Mathf.Sin(t * Mathf.PI));
            yield return null;
        }

        if (moneyStack != null)
        {
            moneyStack.localPosition = stackOffset;
            moneyStack.localRotation = Quaternion.identity;
            moneyStack.localScale = startLocalScale;
        }
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

public class Amount: MonoBehaviour
{
    public int amount = 0;
    private bool collected;
    private void Start()
    {
        amount = this.transform.childCount;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (collected || other == null || ForestFireEventManager.IsFireEventActive)
            return;

        PlayerController player = other.GetComponentInParent<PlayerController>();
        Collect(player);
    }

    public void Collect(PlayerController player)
    {
        if (collected || player == null || ForestFireEventManager.IsFireEventActive)
            return;

        if (amount <= 0)
            amount = transform.childCount;

        collected = true;
        player.EarnMoney(amount);

        Collider stackCollider = GetComponent<Collider>();
        if (stackCollider != null)
            stackCollider.enabled = false;
    }
}
