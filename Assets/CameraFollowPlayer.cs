using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Camera))]
public class CameraFollowPlayer : MonoBehaviour
{
    [SerializeField] private string targetTag = "Player";
    [SerializeField, Min(0f)] private float smoothTime = 0.15f;
    [SerializeField, Min(0f)] private float focusHeight = 0.8f;
    [SerializeField, Range(0.01f, 0.4f)] private float occlusionRevealRadius = 0.17f;
    [SerializeField, Range(0.001f, 0.1f)] private float occlusionEdgeSoftness = 0.025f;

    private static readonly int ApplyPlayerMaskId = Shader.PropertyToID("_ApplyPlayerMask");
    private static readonly int OcclusionCircleId = Shader.PropertyToID("_PlayerOcclusionCircle");
    private static readonly int OcclusionDepthId = Shader.PropertyToID("_PlayerOcclusionDepth");

    private Transform target;
    private Vector3 targetOffset;
    private Vector3 followVelocity;
    private bool hasTargetOffset;
    private bool overviewActive;
    private Vector3 overviewStartPosition;
    private Vector3 overviewTargetPosition;
    private float overviewStartSize;
    private float overviewTargetSize;
    private float overviewDuration;
    private float overviewElapsed;
    private float defaultOrthographicSize;
    private float introOriginalFarClipPlane;
    private bool overviewReturnsToFollow;
    private bool occlusionRenderersInitialized;
    private readonly HashSet<Renderer> maskedRenderers = new HashSet<Renderer>();
    private MaterialPropertyBlock propertyBlock;

    private void Awake()
    {
        propertyBlock = new MaterialPropertyBlock();
        Camera camera = GetComponent<Camera>();
        if (camera != null)
            defaultOrthographicSize = camera.orthographicSize;
    }

    private void LateUpdate()
    {
        if (overviewActive)
        {
            UpdateOverview();
            return;
        }

        if (target == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(targetTag);
            if (player == null)
                return;

            target = player.transform;
        }

        if (!hasTargetOffset)
        {
            targetOffset = transform.position - target.position;
            hasTargetOffset = true;
        }

        Vector3 desiredPosition = target.position + targetOffset;
        Vector3 focusPoint = target.position + Vector3.up * focusHeight;
        if (smoothTime <= 0f)
        {
            transform.position = desiredPosition;
        }
        else
        {
            transform.position = Vector3.SmoothDamp(
                transform.position,
                desiredPosition,
                ref followVelocity,
                smoothTime);
        }

        if (!occlusionRenderersInitialized)
            InitializeOcclusionRenderers();

        UpdateOcclusionMask(focusPoint);
    }

    /// <summary>Pulls the orthographic camera back and centers it on the whole playground.</summary>
    public void BeginOverview(Bounds mapBounds, float duration, float padding)
    {
        Camera camera = GetComponent<Camera>();
        if (camera == null || !camera.orthographic)
            return;

        ResolveTarget();
        CalculateOverviewPose(mapBounds, padding, out Vector3 overviewPosition,
            out float overviewSize);
        overviewStartPosition = transform.position;
        overviewTargetPosition = overviewPosition;
        overviewStartSize = camera.orthographicSize;
        overviewTargetSize = overviewSize;

        ExpandFarClipForOverview(camera, mapBounds, overviewTargetPosition);

        ClearPlayerOcclusionMask();
        overviewDuration = Mathf.Max(0.1f, duration);
        overviewElapsed = 0f;
        overviewActive = true;
        overviewReturnsToFollow = false;
        followVelocity = Vector3.zero;
    }

    /// <summary>Starts at a configured overview size, then returns to the normal player view.</summary>
    public bool BeginIntroZoom(Bounds mapBounds, float duration, float padding,
        float startingProjectionSize)
    {
        Camera camera = GetComponent<Camera>();
        if (camera == null || !camera.orthographic)
            return false;

        ResolveTarget();
        if (target == null)
            return false;

        if (!hasTargetOffset)
        {
            targetOffset = transform.position - target.position;
            hasTargetOffset = true;
        }

        Vector3 playerViewPosition = target.position + targetOffset;
        CalculateOverviewPose(mapBounds, padding, out Vector3 overviewPosition,
            out float overviewSize);

        introOriginalFarClipPlane = camera.farClipPlane;
        ExpandFarClipForOverview(camera, mapBounds, overviewPosition);

        transform.position = overviewPosition;
        float introStartSize = Mathf.Max(1f, startingProjectionSize);
        camera.orthographicSize = introStartSize;
        overviewStartPosition = overviewPosition;
        overviewTargetPosition = playerViewPosition;
        overviewStartSize = introStartSize;
        overviewTargetSize = defaultOrthographicSize > 0f
            ? defaultOrthographicSize
            : camera.orthographicSize;
        overviewDuration = Mathf.Max(0.1f, duration);
        overviewElapsed = 0f;
        overviewActive = true;
        overviewReturnsToFollow = true;
        followVelocity = Vector3.zero;
        ClearPlayerOcclusionMask();
        return true;
    }

    private void ResolveTarget()
    {
        if (target != null)
            return;

        GameObject player = GameObject.FindGameObjectWithTag(targetTag);
        if (player != null)
            target = player.transform;
    }

    private void CalculateOverviewPose(Bounds mapBounds, float padding,
        out Vector3 overviewPosition, out float overviewSize)
    {
        Vector3 currentFocus = target != null
            ? target.position + Vector3.up * focusHeight
            : transform.position + transform.forward * 10f;
        overviewPosition = transform.position + (mapBounds.center - currentFocus);

        Vector3 halfExtents = mapBounds.extents;
        float projectedHalfWidth =
            Mathf.Abs(transform.right.x) * halfExtents.x +
            Mathf.Abs(transform.right.y) * halfExtents.y +
            Mathf.Abs(transform.right.z) * halfExtents.z;
        float projectedHalfHeight =
            Mathf.Abs(transform.up.x) * halfExtents.x +
            Mathf.Abs(transform.up.y) * halfExtents.y +
            Mathf.Abs(transform.up.z) * halfExtents.z;
        float aspect = Mathf.Max(0.1f, GetComponent<Camera>().aspect);
        overviewSize = Mathf.Max(projectedHalfHeight, projectedHalfWidth / aspect) *
                       Mathf.Max(1f, padding);
    }

    private void ExpandFarClipForOverview(Camera camera, Bounds mapBounds, Vector3 overviewPosition)
    {
        Vector3 extents = mapBounds.extents;
        float farthestDepth = 0f;
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
        {
            Vector3 corner = mapBounds.center + Vector3.Scale(extents,
                new Vector3(x, y, z));
            farthestDepth = Mathf.Max(farthestDepth,
                Vector3.Dot(corner - overviewPosition, transform.forward));
        }

        camera.farClipPlane = Mathf.Max(camera.farClipPlane, farthestDepth + 10f);
    }

    private void UpdateOverview()
    {
        Camera camera = GetComponent<Camera>();
        if (camera == null)
            return;

        overviewElapsed += Time.unscaledDeltaTime;
        float progress = Mathf.Clamp01(overviewElapsed / overviewDuration);
        float easedProgress = progress * progress * (3f - 2f * progress);
        transform.position = Vector3.Lerp(overviewStartPosition, overviewTargetPosition,
            easedProgress);
        camera.orthographicSize = Mathf.Lerp(overviewStartSize, overviewTargetSize,
            easedProgress);

        if (progress >= 1f && overviewReturnsToFollow)
        {
            overviewActive = false;
            overviewReturnsToFollow = false;
            transform.position = overviewTargetPosition;
            camera.orthographicSize = overviewTargetSize;
            if (introOriginalFarClipPlane > 0f)
                camera.farClipPlane = introOriginalFarClipPlane;
            followVelocity = Vector3.zero;
        }
    }

    private void ClearPlayerOcclusionMask()
    {
        foreach (Renderer renderer in maskedRenderers)
        {
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(ApplyPlayerMaskId, 0f);
            renderer.SetPropertyBlock(propertyBlock);
        }

        maskedRenderers.Clear();
        occlusionRenderersInitialized = false;
    }

    private void InitializeOcclusionRenderers()
    {
        maskedRenderers.Clear();
        Scene scene = SceneManager.GetActiveScene();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || renderer.transform == target || renderer.transform.IsChildOf(target))
                    continue;

                if (!HasPlayerMaskShader(renderer))
                    continue;

                renderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetFloat(ApplyPlayerMaskId, 1f);
                renderer.SetPropertyBlock(propertyBlock);
                maskedRenderers.Add(renderer);
            }
        }

        occlusionRenderersInitialized = true;
    }

    private void UpdateOcclusionMask(Vector3 focusPoint)
    {
        Camera camera = GetComponent<Camera>();
        if (camera == null)
            return;

        Vector3 viewportPoint = camera.WorldToViewportPoint(focusPoint);
        if (viewportPoint.z <= 0f)
            return;

        Vector3 playerInCameraSpace = transform.InverseTransformPoint(focusPoint);
        Vector4 circle = new Vector4(
            viewportPoint.x,
            viewportPoint.y,
            occlusionRevealRadius,
            occlusionEdgeSoftness);
        // Transform.InverseTransformPoint uses the camera transform's +Z
        // forward axis, while the shader converts view-space -Z to positive
        // depth. Keep both values positive so occluders in front compare correctly.
        float playerDepth = playerInCameraSpace.z;

        foreach (Renderer renderer in maskedRenderers)
        {
            if (renderer == null)
                continue;

            renderer.GetPropertyBlock(propertyBlock);
            propertyBlock.SetFloat(ApplyPlayerMaskId, 1f);
            propertyBlock.SetVector(OcclusionCircleId, circle);
            propertyBlock.SetFloat(OcclusionDepthId, playerDepth);
            renderer.SetPropertyBlock(propertyBlock);
        }
    }

    private static bool HasPlayerMaskShader(Renderer renderer)
    {
        foreach (Material material in renderer.sharedMaterials)
        {
            if (material != null && material.shader != null && material.shader.name == "ArcadeRecycler/Toon")
                return true;
        }

        return false;
    }

    private void OnDisable()
    {
        ClearPlayerOcclusionMask();
    }
}
