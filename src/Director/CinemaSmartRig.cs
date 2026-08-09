using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using AC;
using UnityEngine.Rendering.PostProcessing;

[RequireComponent(typeof(_Camera))]
[RequireComponent(typeof(AudioSource))] // NUEVO: Necesitamos emitir sonido
public class CinemaSmartRig : MonoBehaviour
{
    public static CinemaSmartRig Instance;

    [Header("CONEXIÓN POST-PROCESSING (V2)")]
    public PostProcessVolume sceneVolume;

    private _Camera acGameCamera;
    private AudioSource rigAudio; // Referencia al audio
    private CameraClip currentClip;
    private bool isRunning = false;
    private float timer = 0f;

    // Variables FX
    private float currentTrauma = 0f;
    private float seedX = 0f, seedY = 0f;

    // Física
    private Vector3 recoilPos = Vector3.zero;
    private Vector3 recoilVelocity = Vector3.zero;
    private float punchFovVal = 0f;
    private float punchFovVel = 0f;

    private Coroutine renderRoutine;

    // Cache de Efectos
    private DepthOfField dofComponent;
    private ChromaticAberration chromaComponent;

    void Awake()
    {
        if (Instance == null) Instance = this;
        acGameCamera = GetComponent<_Camera>();
        rigAudio = GetComponent<AudioSource>(); // Obtener AudioSource

        seedX = Random.Range(0f, 1000f);
        seedY = Random.Range(0f, 1000f);

        if (sceneVolume != null && sceneVolume.profile != null)
        {
            sceneVolume.profile.TryGetSettings(out dofComponent);
            sceneVolume.profile.TryGetSettings(out chromaComponent);
        }
    }

    void OnDisable()
    {
        StopShot();
    }

    public void ExecuteShot(CameraClip clip)
    {
        if (renderRoutine != null) StopCoroutine(renderRoutine);

        currentClip = clip;
        isRunning = true;
        timer = 0f;
        currentTrauma = Mathf.Clamp01(clip.traumaImpact);

        // Reset Física
        recoilPos = Vector3.zero;
        recoilVelocity = Vector3.zero;
        punchFovVal = 0f;
        punchFovVel = 0f;

        // Impulso Físico
        if (clip.useImpulse)
        {
            recoilVelocity = clip.impulseDirection.normalized * clip.impulseForce;
            if (clip.punchZoomAmount != 0) punchFovVel = clip.punchZoomAmount * 5f;
        }

        // --- MÓDULO AUDIO: REPRODUCIR SFX ---
        if (clip.shotSFX != null && rigAudio != null)
        {
            rigAudio.PlayOneShot(clip.shotSFX, clip.sfxVolume);
        }

        renderRoutine = StartCoroutine(RenderLoop());
    }

    private IEnumerator RenderLoop()
    {
        while (isRunning && currentClip != null)
        {
            // --- 1. CHRONOS ---
            timer += Time.unscaledDeltaTime;
            float percentage = 0f;
            if (currentClip.shotDuration > 0) percentage = Mathf.Clamp01(timer / currentClip.shotDuration);
            else percentage = 1f;

            if (currentClip.enableChronos)
            {
                float targetTimeScale = currentClip.timeCurve.Evaluate(percentage);
                targetTimeScale = Mathf.Max(0.0f, targetTimeScale);

                if (KickStarter.playerInput) KickStarter.playerInput.SetTimeScale(targetTimeScale);
                else Time.timeScale = targetTimeScale;

                if (currentClip.syncPhysics) Time.fixedDeltaTime = 0.02f * Time.timeScale;
            }
            else
            {
                if (Time.timeScale != 1.0f && KickStarter.playerInput) KickStarter.playerInput.SetTimeScale(1.0f);
            }

            // --- 2. CÁLCULOS FÍSICOS ---
            Vector3 targetPos = GetPointAt(percentage);
            Quaternion targetRot = GetRotationAt(targetPos);

            // Trauma
            currentTrauma = Mathf.Clamp01(currentTrauma - (Time.unscaledDeltaTime * currentClip.traumaDecay));
            float shake = currentTrauma * currentTrauma * currentTrauma;

            // Ruido & Sway
            float noiseX = (Mathf.PerlinNoise(seedX, Time.unscaledTime * 20f) - 0.5f) * 2f * shake;
            float noiseY = (Mathf.PerlinNoise(seedY, Time.unscaledTime * 20f) - 0.5f) * 2f * shake;
            float noiseZ = (Mathf.PerlinNoise(seedX + seedY, Time.unscaledTime * 20f) - 0.5f) * 2f * shake;

            Vector3 shakePos = new Vector3(noiseX, noiseY, noiseZ) * 0.5f;
            Vector3 shakeRot = new Vector3(noiseY, noiseX, noiseZ) * 5.0f;

            if (currentClip.idleSwayAmount > 0)
            {
                float tSway = Time.unscaledTime;
                float swayX = Mathf.Sin(tSway * currentClip.idleSwaySpeed) * 0.1f * currentClip.idleSwayAmount;
                float swayY = Mathf.Cos(tSway * currentClip.idleSwaySpeed * 0.5f) * 0.1f * currentClip.idleSwayAmount;
                targetPos += (targetRot * Vector3.right) * swayX;
                targetPos += (targetRot * Vector3.up) * swayY;
            }

            // Física Resorte
            float dt = Time.unscaledDeltaTime;
            Vector3 springForce = -currentClip.impulseStiffness * recoilPos - currentClip.impulseDamping * recoilVelocity;
            recoilVelocity += springForce * dt;
            recoilPos += recoilVelocity * dt;

            float fovForce = -currentClip.impulseStiffness * punchFovVal - currentClip.impulseDamping * punchFovVel;
            punchFovVel += fovForce * dt;
            punchFovVal += punchFovVel * dt;


            // --- 3. APLICACIÓN VISUAL (POST-RENDER) ---
            yield return new WaitForEndOfFrame();

            // Transform
            Vector3 finalRecoil = targetRot * recoilPos;
            transform.position = targetPos + shakePos + finalRecoil;
            transform.rotation = targetRot * Quaternion.Euler(shakeRot);

            // FOV
            Camera cam = GetComponent<Camera>();
            if (cam != null)
            {
                float baseFOV = Mathf.Lerp(currentClip.startFOV, currentClip.endFOV, percentage);
                cam.fieldOfView = baseFOV + punchFovVal;
            }

            // --- 4. MANIPULACIÓN DEL VOLUMEN (V2) ---
            if (sceneVolume != null)
            {
                if (dofComponent != null)
                {
                    float finalFocusDist = currentClip.focusDistance;
                    if (currentClip.focusMode == FocusMode.AutoTarget && currentClip.target != null)
                        finalFocusDist = Vector3.Distance(transform.position, currentClip.target.position);
                    else if (currentClip.focusMode == FocusMode.ManualCurve)
                        finalFocusDist = currentClip.focusCurve.Evaluate(percentage);

                    dofComponent.focusDistance.value = finalFocusDist;
                }

                if (chromaComponent != null && currentClip.useOpticalStress)
                {
                    chromaComponent.intensity.value = currentTrauma * 1.5f;
                }
            }

            if (timer >= currentClip.shotDuration) StopShot();
        }
    }

    public void StopShot()
    {
        isRunning = false;
        if (renderRoutine != null) StopCoroutine(renderRoutine);

        // Restaurar tiempo
        if (KickStarter.playerInput) KickStarter.playerInput.SetTimeScale(1.0f);
        else Time.timeScale = 1.0f;
        Time.fixedDeltaTime = 0.02f;

        // Restaurar FX
        if (chromaComponent != null) chromaComponent.intensity.value = 0f;
    }

    // --- HELPERS ---
    private Vector3 GetPointAt(float t)
    {
        CameraClip clip = currentClip;
        if (clip == null || clip.waypoints == null || clip.waypoints.Count == 0) return transform.position;
        if (clip.waypoints.Count == 1) return clip.waypoints[0].position;

        t = Mathf.Clamp01(t);
        int count = clip.waypoints.Count;
        if (t >= 0.999f) return clip.waypoints[count - 1].position;

        float segmentLength = 1f / (count - 1);
        int currentIndex = Mathf.FloorToInt(t / segmentLength);
        if (currentIndex >= count - 1) currentIndex = count - 2;

        float segmentT = (t - (currentIndex * segmentLength)) / segmentLength;
        Transform p1 = clip.waypoints[currentIndex];
        Transform p2 = clip.waypoints[currentIndex + 1];

        if (p1 == null || p2 == null) return transform.position;
        return Vector3.Lerp(p1.position, p2.position, segmentT);
    }

    private Quaternion GetRotationAt(Vector3 pos)
    {
        if (currentClip != null && currentClip.target != null)
        {
            Vector3 dir = currentClip.target.position - pos;
            dir.y += 1.4f;
            if (dir != Vector3.zero)
                return Quaternion.LookRotation(dir) * Quaternion.Euler(0, 0, currentClip.dutchTilt);
        }
        return transform.rotation;
    }

    public void PreviewShotAtTime(CameraClip clip, float timePercent)
    {
        currentClip = clip;
        Vector3 pos = GetPointAt(timePercent);
        Quaternion rot = GetRotationAt(pos);
        transform.position = pos;
        transform.rotation = rot;
        Camera cam = GetComponent<Camera>();
        if (cam != null) cam.fieldOfView = Mathf.Lerp(clip.startFOV, clip.endFOV, timePercent);
    }
}