using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using AC;

public class CameraSequencer : MonoBehaviour
{
    public bool playOnStart = false;

    [Header("VISUALIZACIÓN EN ESCENA")]
    public bool mostrarGizmos = true;
    public Color colorRuta = Color.cyan;
    public Color colorCamara = new Color(1f, 1f, 1f, 0.5f); // Blanco semi-transparente

    public List<CameraClip> shotList = new List<CameraClip>();

    private _Camera previousCamera;
    private _Camera rigCamera;

    // SISTEMA DE OVERLAY (Fundidos)
    private Texture2D overlayTexture;
    private Color overlayColor = Color.clear;
    private float overlayAlpha = 0f;

    void Start()
    {
        overlayTexture = new Texture2D(1, 1);

        if (CinemaSmartRig.Instance != null)
            rigCamera = CinemaSmartRig.Instance.GetComponent<_Camera>();
        else
        {
            CinemaSmartRig foundRig = FindObjectOfType<CinemaSmartRig>();
            if (foundRig) rigCamera = foundRig.GetComponent<_Camera>();
        }

        if (playOnStart) PlaySequence();
    }

    [ContextMenu("REPRODUCIR")]
    public void PlaySequence()
    {
        StartCoroutine(SequenceRoutine());
    }

    private void OnGUI()
    {
        if (overlayAlpha > 0f)
        {
            GUI.color = new Color(overlayColor.r, overlayColor.g, overlayColor.b, overlayAlpha);
            GUI.depth = -9999;
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), overlayTexture);
        }
    }

    private IEnumerator SequenceRoutine()
    {
        if (KickStarter.mainCamera != null) previousCamera = KickStarter.mainCamera.attachedCamera;

        for (int i = 0; i < shotList.Count; i++)
        {
            CameraClip clip = shotList[i];

            // 1. TRANSICIÓN
            if (clip.transition != CineTransitionType.Cut)
            {
                overlayColor = clip.effectColor;
                overlayTexture.SetPixel(0, 0, Color.white);
                overlayTexture.Apply();
                yield return StartCoroutine(FadeRoutine(0f, 1f, clip.transitionDuration * 0.2f));
            }

            // 2. CORTE
            if (i == 0 && rigCamera != null && KickStarter.mainCamera != null)
                KickStarter.mainCamera.SetGameCamera(rigCamera, 0f, MoveMethod.Linear);

            if (CinemaSmartRig.Instance != null)
                CinemaSmartRig.Instance.ExecuteShot(clip);

            // 3. RECUPERAR VISIÓN
            if (clip.transition != CineTransitionType.Cut)
            {
                StartCoroutine(FadeRoutine(1f, 0f, clip.transitionDuration));
            }

            // 4. DURACIÓN
            float duration = clip.shotDuration > 0 ? clip.shotDuration : 0.1f;
            yield return new WaitForSecondsRealtime(duration);
        }

        // 5. RESTAURAR
        overlayAlpha = 0f;
        if (previousCamera != null && KickStarter.mainCamera != null)
        {
            KickStarter.mainCamera.SetGameCamera(previousCamera, 0.5f, MoveMethod.Smooth);
        }
    }

    private IEnumerator FadeRoutine(float start, float end, float time)
    {
        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / time;
            overlayAlpha = Mathf.Lerp(start, end, t);
            yield return null;
        }
        overlayAlpha = end;
    }

    // =========================================================
    // 👁️ GIZMOS DE CÁMARA REALES (CORREGIDO)
    // =========================================================
    private void OnDrawGizmos()
    {
        if (!mostrarGizmos || shotList == null) return;

        foreach (var clip in shotList)
        {
            if (clip.waypoints == null || clip.waypoints.Count == 0) continue;

            for (int i = 0; i < clip.waypoints.Count; i++)
            {
                Transform wp = clip.waypoints[i];
                if (wp == null) continue;

                // 1. DIBUJAR RUTA (LÍNEA CYAN)
                if (i < clip.waypoints.Count - 1 && clip.waypoints[i + 1] != null)
                {
                    Gizmos.color = colorRuta;
                    Gizmos.DrawLine(wp.position, clip.waypoints[i + 1].position);
                }

                // 2. DIBUJAR DIRECCIÓN (FLECHA ROJA)
                Gizmos.color = Color.red;
                Gizmos.DrawRay(wp.position, wp.forward * 2.0f);

                // 3. DIBUJAR LA CÁMARA (PIRÁMIDE / FRUSTUM)
                Gizmos.matrix = Matrix4x4.TRS(wp.position, wp.rotation, Vector3.one);

                // Usamos startFOV ya que lensFOV no existe en la definición actual de CameraClip
                float fovPreview = clip.startFOV;

                // Marco sólido suave
                Gizmos.color = colorCamara;
                Gizmos.DrawFrustum(Vector3.zero, fovPreview, 1.5f, 0.1f, 1.0f);

                // Marco de alambre para definición
                Gizmos.color = Color.white;
                Gizmos.DrawFrustum(Vector3.zero, fovPreview, 1.5f, 0.1f, 1.0f);

                Gizmos.matrix = Matrix4x4.identity;
            }
        }
    }
}