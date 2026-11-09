using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;

/// <summary>
/// Plays the KBandar intro title: letters rise in, hold, then fade out.
/// When it finishes, the renderer is turned off, so it costs nothing during gameplay.
/// Put this on the cube that uses the Custom/CubeInteriorWordArt material.
/// </summary>
[RequireComponent(typeof(Renderer))]
public class IntroTitle : MonoBehaviour
{
    [Header("Timing (seconds)")]
    public float startDelay = 0.4f;
    public float revealTime = 1.8f;
    public float holdTime   = 3.0f;
    public float fadeTime   = 1.2f;

    [Header("Behaviour")]
    public bool playOnStart = true;
    [Tooltip("Use unscaled time so it also plays if the game starts paused (Time.timeScale = 0).")]
    public bool useUnscaledTime = true;

    [Header("Events")]
    public UnityEvent onFinished;      // e.g. enable player input / start the game timer

    static readonly int RevealId = Shader.PropertyToID("_Reveal");
    static readonly int FadeId   = Shader.PropertyToID("_Fade");
    static readonly int FrontColorId = Shader.PropertyToID("_FrontColor");
    static readonly int FrontColor2Id = Shader.PropertyToID("_FrontColor2");
    static readonly int SideColorId = Shader.PropertyToID("_SideColor");
    static readonly int ShineColorId = Shader.PropertyToID("_ShineColor");

    [Header("Opening and victory colors")]
    [SerializeField] private Color introFrontColor = new Color(0.72f, 0.78f, 0.80f, 1f);
    [SerializeField] private Color introFrontColor2 = new Color(0.43f, 0.51f, 0.54f, 1f);
    [SerializeField] private Color introSideColor = new Color(0.12f, 0.20f, 0.22f, 1f);
    [SerializeField] private Color victoryFrontColor = new Color(0.88f, 1f, 0.68f, 1f);
    [SerializeField] private Color victoryFrontColor2 = new Color(0.42f, 0.82f, 0.32f, 1f);
    [SerializeField] private Color victorySideColor = new Color(0.06f, 0.34f, 0.15f, 1f);

    Renderer rend;
    Material mat;
    Coroutine routine;

    void Awake()
    {
        rend = GetComponent<Renderer>();
        mat  = rend.material;          // per-object instance, the shared asset is not modified
        rend.shadowCastingMode = ShadowCastingMode.Off;
        rend.receiveShadows = false;
        SetPalette(false);
        SetState(0f, 1f);
    }

    void Start()
    {
        if (playOnStart) Play();
    }

    /// <summary>Start (or restart) the intro.</summary>
    public void Play()
    {
        if (routine != null) StopCoroutine(routine);
        rend.enabled = true;
        routine = StartCoroutine(Run());
    }

    /// <summary>Sizes the title cube around the playable map so the camera starts inside it.</summary>
    public void FitAround(Bounds mapBounds)
    {
        float width = Mathf.Max(120f, mapBounds.size.x + 24f);
        float depth = Mathf.Max(120f, mapBounds.size.z + 24f);
        float height = Mathf.Max(120f, mapBounds.size.y + 90f);
        transform.position = mapBounds.center;
        transform.rotation = Quaternion.identity;
        transform.localScale = new Vector3(width, height, depth);
    }

    /// <summary>Fits the title reveal and fade to the camera's opening zoom duration.</summary>
    public void SetOpeningDuration(float duration)
    {
        float safeDuration = Mathf.Max(0.1f, duration);
        startDelay = 0f;
        revealTime = safeDuration * 0.45f;
        holdTime = safeDuration * 0.12f;
        fadeTime = safeDuration * 0.43f;
    }

    /// <summary>Shows the title again in clean-air green during the final map overview.</summary>
    public void SetVictoryLook()
    {
        if (rend == null || mat == null)
            return;

        SetPalette(true);
        rend.enabled = true;
        SetState(1f, 1f);
    }

    /// <summary>Jump straight to the fade-out (call this when the player taps "Start", for example).</summary>
    public void Skip()
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(FadeOnly(mat.GetFloat(FadeId)));
    }

    IEnumerator Run()
    {
        SetState(0f, 1f);
        yield return Wait(startDelay);

        // 1) letters rise and fade in
        for (float t = 0f; t < revealTime; t += Dt())
        {
            SetState(Mathf.SmoothStep(0f, 1f, t / revealTime), 1f);
            yield return null;
        }
        SetState(1f, 1f);

        // 2) hold
        yield return Wait(holdTime);

        // 3) fade out, then turn the renderer off
        yield return FadeOnly(1f);
    }

    IEnumerator FadeOnly(float from)
    {
        for (float t = 0f; t < fadeTime; t += Dt())
        {
            SetState(1f, Mathf.Lerp(from, 0f, Mathf.SmoothStep(0f, 1f, t / fadeTime)));
            yield return null;
        }
        SetState(1f, 0f);
        rend.enabled = false;          // zero cost from here on
        routine = null;
        onFinished?.Invoke();
    }

    IEnumerator Wait(float seconds)
    {
        for (float t = 0f; t < seconds; t += Dt()) yield return null;
    }

    float Dt() => useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

    void SetState(float reveal, float fade)
    {
        mat.SetFloat(RevealId, reveal);
        mat.SetFloat(FadeId, fade);
    }

    void SetPalette(bool victory)
    {
        if (mat == null)
            return;

        mat.SetColor(FrontColorId, victory ? victoryFrontColor : introFrontColor);
        mat.SetColor(FrontColor2Id, victory ? victoryFrontColor2 : introFrontColor2);
        mat.SetColor(SideColorId, victory ? victorySideColor : introSideColor);
        mat.SetColor(ShineColorId, victory
            ? new Color(0.88f, 1f, 0.70f, 1f)
            : new Color(0.88f, 0.94f, 0.96f, 1f));
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }
}
