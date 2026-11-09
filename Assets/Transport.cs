using UnityEngine;

public class Transport : MonoBehaviour
{
    [Header("Transport Attribute")]
    [SerializeField] private int maxCapacity = 10;
    [SerializeField] private float moveSpeed = 5;
    [SerializeField] private int purchaseCost = 15;
    [SerializeField] private bool isAvailable = false;
    [SerializeField] private bool unlocksStaffHiring;
    [SerializeField] private bool requiresHiredStaff;

    [Header("Player Attachment")]
    [Tooltip("Local position of this transport relative to the player after purchase.")]
    [SerializeField] private Vector3 attachedLocalPosition = Vector3.zero;
    [Tooltip("Local rotation correction for this vehicle model after purchase.")]
    [SerializeField] private Vector3 attachedLocalEulerAngles = Vector3.zero;
    [Tooltip("Scale multiplier applied while attached to the player.")]
    [SerializeField, Min(0.01f)] private Vector3 attachedScaleMultiplier = Vector3.one;

    [Header("Preview Display")]
    [SerializeField, Min(0.01f)] private Vector3 previewScaleMultiplier = Vector3.one;
    [Tooltip("Per-vehicle local-space position offset from the Transport Center preview anchor.")]
    [SerializeField] private Vector3 previewPositionOffset = Vector3.zero;
    [Tooltip("Per-vehicle local-space adjustment to the preview's rotation center.")]
    [SerializeField] private Vector3 previewPivotOffset = Vector3.zero;
    private bool attachedToPlayer;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    private void OnTriggerEnter(Collider other)
    {
        if(other.gameObject == null)
        {  return; }

        if (other.gameObject.CompareTag("Player"))
            AttachToPlayer(other.transform);
    }

    /// <summary>Mounts a purchased transport even when it spawns inside the player trigger.</summary>
    public void AttachToPlayer(Transform player)
    {
        if (attachedToPlayer || player == null)
            return;

        PlayerController playerController = player.GetComponent<PlayerController>();
        if (playerController == null)
            return;

        attachedToPlayer = true;
        playerController.SetCharacterMeshVisible(false);
        playerController.AddCapacity(maxCapacity);
        playerController.AddSpeed(moveSpeed);

        transform.SetParent(player, false);
        transform.localPosition = attachedLocalPosition;
        transform.localRotation = Quaternion.Euler(attachedLocalEulerAngles);
        transform.localScale = Vector3.Scale(transform.localScale, attachedScaleMultiplier);

        // A purchased transport follows the player as a visual upgrade. Keep its
        // physics from pulling it away or tipping it over while it is parented.
        foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>(true))
        {
            body.useGravity = false;
            body.isKinematic = true;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }

        // The attached vehicle is a visual upgrade, so its colliders do not need
        // to participate in physics. Disabling them also supports concave mesh
        // colliders, which Unity cannot convert into triggers.
        foreach (Collider vehicleCollider in GetComponentsInChildren<Collider>(true))
            vehicleCollider.enabled = false;
    }

    public int ReturnCost()
    {
        return purchaseCost;
    }
    public void SetAvailability(bool isAvailable)
    {
        this.isAvailable = isAvailable;
    }
    public bool IsAvailable
    {
        get { return this.isAvailable; }
    }

    public int MaxCapacity => maxCapacity;
    public float MoveSpeed => moveSpeed;
    public int PurchaseCost => purchaseCost;
    public bool UnlocksStaffHiring => unlocksStaffHiring;
    public bool RequiresHiredStaff => requiresHiredStaff;
    public Vector3 PreviewScaleMultiplier => previewScaleMultiplier;
    public Vector3 PreviewPositionOffset => previewPositionOffset;
    public Vector3 PreviewPivotOffset => previewPivotOffset;

    public void PaidCost(int i)
    {
        if (purchaseCost > 0)
        {
            purchaseCost -= i;
        }
        if (purchaseCost <= 0)
        {
            return;
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
