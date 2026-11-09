using System.Collections;
using UnityEngine;

/// <summary>Visual cloud and helicopter trigger for the cloud-seeding event.</summary>
public class CloudSeedingCloud : MonoBehaviour
{
    private CloudSeedingEventManager owner;
    private Material rainMaterial;
    private Vector3 cloudSize;
    private float rainDuration;
    private float delayAfterRain;
    private Vector3 spawnPosition;
    private Vector3 driftDirection;
    private float driftSpeed;
    private float bobAmount;
    private float bobPhase;
    private float movementTime;
    private bool isSeeded;
    private ParticleSystem rainParticles;

    public bool IsSeeded => isSeeded;

    public void Initialize(CloudSeedingEventManager eventManager, Material particleMaterial,
        Vector3 triggerSize, float rainSeconds, float cleanupDelay, Vector3 windDirection,
        float windSpeed, float verticalBobAmount)
    {
        owner = eventManager;
        rainMaterial = particleMaterial;
        cloudSize = triggerSize;
        rainDuration = Mathf.Max(0.1f, rainSeconds);
        delayAfterRain = Mathf.Max(0f, cleanupDelay);
        driftDirection = Vector3.ProjectOnPlane(windDirection, Vector3.up).normalized;
        driftSpeed = Mathf.Max(0f, windSpeed);
        bobAmount = Mathf.Max(0f, verticalBobAmount);
        bobPhase = Random.Range(0f, Mathf.PI * 2f);
        spawnPosition = transform.position;

        BuildRainParticles();
    }

    private void Update()
    {
        if (isSeeded || (driftSpeed <= 0f && bobAmount <= 0f))
            return;

        movementTime += Time.deltaTime;
        transform.position = spawnPosition + driftDirection * (driftSpeed * movementTime) +
                             Vector3.up * (Mathf.Sin(movementTime * 0.65f + bobPhase) * bobAmount);
    }

    private void BuildRainParticles()
    {
        GameObject rainObject = new GameObject("Cloud Rain");
        rainObject.transform.SetParent(transform, false);
        rainObject.transform.localPosition = new Vector3(0f, -cloudSize.y * 0.42f, 0f);
        rainParticles = rainObject.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = rainParticles.main;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = 1.15f;
        main.startSpeed = 9f;
        main.startSize = 0.075f;
        main.startColor = new Color(0.58f, 0.82f, 1f, 0.9f);
        main.gravityModifier = 0.15f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        ParticleSystem.EmissionModule emission = rainParticles.emission;
        emission.rateOverTime = 160f;

        ParticleSystem.ShapeModule shape = rainParticles.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(Mathf.Max(1f, cloudSize.x * 0.88f), 0.1f,
            Mathf.Max(1f, cloudSize.z * 0.88f));

        ParticleSystemRenderer particleRenderer = rainParticles.GetComponent<ParticleSystemRenderer>();
        if (particleRenderer != null && rainMaterial != null)
            particleRenderer.sharedMaterial = rainMaterial;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (isSeeded || owner == null || other == null)
            return;

        HelicopterFlightController contactingHelicopter =
            other.GetComponentInParent<HelicopterFlightController>();
        if (contactingHelicopter != null)
            owner.OnHelicopterContactWithCloud(this, contactingHelicopter);
    }

    public bool BeginRain()
    {
        if (isSeeded)
            return false;

        isSeeded = true;
        Renderer[] cloudRenderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < cloudRenderers.Length; i++)
        {
            Renderer cloudRenderer = cloudRenderers[i];
            if (cloudRenderer == null || cloudRenderer is ParticleSystemRenderer)
                continue;

            Material[] materials = cloudRenderer.sharedMaterials;
            bool supportsWeatherState = false;
            for (int materialIndex = 0; materialIndex < materials.Length; materialIndex++)
            {
                if (materials[materialIndex] != null && materials[materialIndex].HasProperty("_Weather"))
                {
                    supportsWeatherState = true;
                    break;
                }
            }

            if (supportsWeatherState)
            {
                MaterialPropertyBlock properties = new MaterialPropertyBlock();
                cloudRenderer.GetPropertyBlock(properties);
                properties.SetFloat("_Weather", 2f);
                cloudRenderer.SetPropertyBlock(properties);
            }
            else
            {
                Debug.LogWarning($"Cloud renderer '{cloudRenderer.name}' has no _Weather shader property; " +
                                 "it cannot switch to the rainy cloud appearance.", cloudRenderer);
            }
        }

        if (rainParticles != null)
            rainParticles.Play();
        StartCoroutine(RainAndCleanup());
        return true;
    }

    private IEnumerator RainAndCleanup()
    {
        yield return new WaitForSecondsRealtime(rainDuration);
        if (rainParticles != null)
            rainParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        yield return new WaitForSecondsRealtime(delayAfterRain);
        if (owner != null)
            owner.OnCloudFinishedRaining(this);
        Destroy(gameObject);
    }
}
