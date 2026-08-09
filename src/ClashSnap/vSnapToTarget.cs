using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Invector.vCharacterController;

/// <summary>
/// Sistema de "Snap to Target" estilo Batman Arkham / Sifu / Yakuza.
/// 
/// Cuando el player ataca, este sistema:
///   1. Busca el target (LockOn primero, sino el enemigo más cercano en rango/cono, priorizando Input Direccional)
///   2. Reproduce una animación de transición y un VFX (opcional)
///   3. Hace un Lerp rápido (0.1-0.2s) hacia una posición a "stopDistance" del enemigo
///   4. Activa automáticamente el LockOn en el enemigo detectado para que Invector gestione la rotación
///   5. Llama al callback para que ejecutes el ataque cuando el snap termine
/// </summary>
[DisallowMultipleComponent]
public class vSnapToTarget : MonoBehaviour
{
    [Header("═══ Directional Targeting (Arkham Style) ═══")]
    [Tooltip("Eje horizontal para seleccionar objetivo (Teclado o Mando, ej: 'Horizontal')")]
    public string horizontalAxis = "Horizontal";

    [Tooltip("Eje vertical para seleccionar objetivo (Teclado o Mando, ej: 'Vertical')")]
    public string verticalAxis = "Vertical";

    [Tooltip("Zona muerta para considerar que el jugador está apuntando a una dirección")]
    public float inputDeadzone = 0.2f;

    [Tooltip("Cámara principal para calcular la dirección relativa (si es null usará Camera.main automáticamente)")]
    public Camera referenceCamera;

    [Tooltip("Si el jugador presiona una dirección, ignora el LockOn y busca al enemigo en esa dirección")]
    public bool breakLockOnWithDirection = true;

    [Header("═══ Detección de Target ═══")]
    [Tooltip("Distancia máxima a la que el snap puede alcanzar un enemigo")]
    public float maxSnapRange = 5f;

    [Tooltip("Ángulo del cono frontal para buscar enemigos (180 = todo alrededor, 90 = solo enfrente). Funciona cuando NO hay input direccional.")]
    [Range(0f, 360f)]
    public float searchConeAngle = 180f;

    [Tooltip("Tags que se consideran enemigos válidos")]
    public string[] enemyTags = new string[] { "Enemy" };

    [Tooltip("LayerMask para filtrar la búsqueda (None = todas las layers)")]
    public LayerMask searchLayers = ~0;

    [Header("═══ Movimiento del Snap ═══")]
    [Tooltip("Duración del lerp en segundos (0.1-0.2 = Batman Arkham, 0.05 = casi instantáneo)")]
    [Range(0.01f, 0.5f)]
    public float snapDuration = 0.15f;

    [Tooltip("Curva de velocidad del snap. Default = ease-out (rápido al inicio, suave al llegar)")]
    public AnimationCurve snapCurve = new AnimationCurve(
        new Keyframe(0f, 0f, 2f, 2f),
        new Keyframe(1f, 1f, 0f, 0f)
    );

    [Tooltip("Si está ON, el player rota para mirar al enemigo durante el snap (complementario al Auto Lock-On)")]
    public bool rotateToTarget = true;

    [Header("═══ Distancia al Target ═══")]
    [Tooltip("Distancia POR DEFECTO a la que se posiciona del enemigo. Cada ataque puede sobrescribirla.")]
    public float defaultStopDistance = 1.0f;

    [Header("═══ Integración con LockOn ═══")]
    [Tooltip("Si está ON, prioriza el target del vLockOn sobre la búsqueda por proximidad")]
    public bool useLockOnTargetFirst = true;

    [Tooltip("Si está ON, cuando el snap detecta a un enemigo, activa automáticamente el LockOn en él.")]
    public bool autoLockOnSnappedTarget = true;

    [Header("═══ Transition Visuals (Referencia Dash Ultimate) ═══")]
    [Tooltip("Nombre del estado de animación en el Animator para reproducir durante el trayecto (ej: 'DashForward'). Dejar vacío para ignorar.")]
    public string transitionAnimState = "";

    [Tooltip("Tiempo de transición suave para entrar en la animación de trayecto")]
    public float transitionCrossfade = 0.1f;

    [Tooltip("Prefab del VFX a instanciar al iniciar el Snap (estela, blur, etc.)")]
    public GameObject snapVfxPrefab;

    [Tooltip("Socket de donde sale el VFX. Si es null, sale del centro del Player.")]
    public Transform vfxSocket;

    [Header("═══ Seguridad ═══")]
    [Tooltip("LayerMask de obstáculos. Si hay una pared entre el player y el enemigo, no hace snap.")]
    public LayerMask obstacleLayers = 0;

    [Tooltip("Si está ON, ignora el snap si el enemigo ya está dentro del stopDistance (ya estás pegado)")]
    public bool skipIfAlreadyClose = true;

    [Header("═══ Debug ═══")]
    [Tooltip("Dibuja gizmos del rango y target detectado")]
    public bool debugDraw = true;
    [SerializeField] private Transform _currentTarget;

    // ── Cache ──
    private vLockOn lockOnComponent;
    private vThirdPersonController controller;
    private CharacterController charController;
    private Animator animator;
    private Coroutine activeSnap;

    void Awake()
    {
        lockOnComponent = GetComponent<vLockOn>();
        controller = GetComponent<vThirdPersonController>();
        charController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
    }

    // ════════════════════════════════════════════════════════
    //  API PÚBLICA
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Intenta hacer snap al mejor target disponible y luego ejecuta el callback.
    /// Si no encuentra target, ejecuta el callback inmediatamente.
    /// </summary>
    public bool TrySnapAndAttack(float stopDistance = -1f, System.Action onSnapComplete = null)
    {
        if (stopDistance < 0f) stopDistance = defaultStopDistance;

        Transform target = FindBestTarget();
        _currentTarget = target;

        if (target == null)
        {
            // Sin target, ejecutar callback de inmediato
            onSnapComplete?.Invoke();
            return false;
        }

        if (autoLockOnSnappedTarget)
        {
            ForceLockOn(target);
        }

        // Si ya está cerca y skipIfAlreadyClose está activo, no hacer snap
        if (skipIfAlreadyClose)
        {
            float currentDist = Vector3.Distance(transform.position, target.position);
            if (currentDist <= stopDistance + 0.2f)
            {
                if (rotateToTarget) FaceTarget(target);
                onSnapComplete?.Invoke();
                return true;
            }
        }

        // Cancelar snap anterior si existe
        if (activeSnap != null) StopCoroutine(activeSnap);
        activeSnap = StartCoroutine(SnapRoutine(target, stopDistance, onSnapComplete));
        return true;
    }

    /// <summary>
    /// Versión que solo hace el snap sin callback. Útil para llamar desde Animation Events.
    /// </summary>
    public void Snap()
    {
        TrySnapAndAttack(-1f, null);
    }

    /// <summary>
    /// Hace snap a un target ESPECÍFICO sin pasar por la lógica de búsqueda.
    /// Útil cuando otro sistema (ej: vSnapBeatBridge) ya sabe a qué enemigo apuntar.
    /// </summary>
    public bool SnapToSpecificTarget(Transform target, float stopDistance = -1f, System.Action onSnapComplete = null)
    {
        if (target == null)
        {
            onSnapComplete?.Invoke();
            return false;
        }

        if (stopDistance < 0f) stopDistance = defaultStopDistance;

        _currentTarget = target;

        if (autoLockOnSnappedTarget)
        {
            ForceLockOn(target);
        }

        if (skipIfAlreadyClose)
        {
            float currentDist = Vector3.Distance(transform.position, target.position);
            if (currentDist <= stopDistance + 0.2f)
            {
                if (rotateToTarget) FaceTarget(target);
                onSnapComplete?.Invoke();
                return true;
            }
        }

        if (activeSnap != null) StopCoroutine(activeSnap);
        activeSnap = StartCoroutine(SnapRoutine(target, stopDistance, onSnapComplete));
        return true;
    }

    /// <summary>
    /// Cancela cualquier snap en progreso.
    /// </summary>
    public void CancelSnap()
    {
        if (activeSnap != null)
        {
            StopCoroutine(activeSnap);
            activeSnap = null;
        }
    }

    /// <summary>
    /// Retorna el target actual (útil para que otros sistemas sepan a quién va el snap).
    /// </summary>
    public Transform GetCurrentTarget() => _currentTarget;

    // ════════════════════════════════════════════════════════
    //  BÚSQUEDA DE TARGET Y LOCK-ON
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Fuerza el sistema nativo de Lock-On de Invector a fijar un objetivo específico.
    /// </summary>
    private void ForceLockOn(Transform target)
    {
        var camera = Invector.vCamera.vThirdPersonCamera.instance;
        if (camera != null)
        {
            // Asignar el objetivo directamente a la cámara de Invector
            float heightOffset = lockOnComponent != null ? lockOnComponent.cameraHeightOffset : 0f;
            camera.SetLockTarget(target, heightOffset);
        }

        if (lockOnComponent != null)
        {
            lockOnComponent.isLockingOn = true;

            // Usamos Reflection para inyectar el target en las variables protegidas de vLockOn y vLockOnBehaviour
            // Esto asegura que la UI y la lógica interna de Invector reconozcan el Lock-On forzado
            System.Type type = lockOnComponent.GetType();
            System.Type baseType = type.BaseType;

            var inTargetField = type.GetField("inTarget", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (inTargetField != null)
            {
                inTargetField.SetValue(lockOnComponent, true);
            }

            var currentTargetField = baseType.GetField("currentTarget", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            if (currentTargetField != null)
            {
                currentTargetField.SetValue(lockOnComponent, target);
            }
        }
    }

    /// <summary>
    /// Encuentra el mejor target evaluando la dirección de input, LockOn y cono.
    /// </summary>
    Transform FindBestTarget()
    {
        Vector3 inputDir = GetInputDirection();
        bool hasDirectionalInput = inputDir.sqrMagnitude > 0f;

        // Prioridad 1: LockOn target
        if (useLockOnTargetFirst && lockOnComponent != null)
        {
            // Ignorar LockOn si el jugador presiona una dirección agresivamente para cambiar de target
            if (!hasDirectionalInput || !breakLockOnWithDirection)
            {
                Transform lockTarget = GetLockOnTarget();
                if (lockTarget != null && IsValidTarget(lockTarget))
                {
                    return lockTarget;
                }
            }
        }

        // Prioridad 2: enemigo más cercano en la dirección del input o en el cono frontal
        return FindNearestEnemyInCone();
    }

    /// <summary>
    /// Obtiene la dirección del input del jugador basada en la rotación de la cámara.
    /// </summary>
    private Vector3 GetInputDirection()
    {
        float h = Input.GetAxisRaw(horizontalAxis);
        float v = Input.GetAxisRaw(verticalAxis);
        Vector2 input = new Vector2(h, v);

        if (input.magnitude < inputDeadzone) return Vector3.zero;

        Camera cam = referenceCamera != null ? referenceCamera : Camera.main;
        if (cam == null) return transform.forward;

        Vector3 camForward = cam.transform.forward;
        Vector3 camRight = cam.transform.right;

        camForward.y = 0f;
        camRight.y = 0f;
        camForward.Normalize();
        camRight.Normalize();

        Vector3 finalDir = (camRight * input.x + camForward * input.y).normalized;
        return finalDir;
    }

    /// <summary>
    /// Obtiene el target del LockOn de forma segura (sin acoplamiento directo).
    /// </summary>
    Transform GetLockOnTarget()
    {
        if (lockOnComponent == null) return null;

        // Usar reflexión para evitar dependencia dura con la API interna de Invector
        var camera = Invector.vCamera.vThirdPersonCamera.instance;
        if (camera == null) return null;

        return camera.lockTarget;
    }

    /// <summary>
    /// Busca el enemigo más cercano dentro del rango, evaluando el cono o la dirección del input.
    /// </summary>
    Transform FindNearestEnemyInCone()
    {
        Collider[] hits = Physics.OverlapSphere(transform.position, maxSnapRange, searchLayers);

        Transform best = null;
        float bestScore = float.MaxValue;

        HashSet<Transform> processedRoots = new HashSet<Transform>();

        Vector3 inputDir = GetInputDirection();
        bool hasDirectionalInput = inputDir.sqrMagnitude > 0f;

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i];

            // Encontrar el "root" del enemigo (su StatusC, ya que el collider puede ser un hueso)
            Transform enemyRoot = GetEnemyRoot(col);
            if (enemyRoot == null) continue;

            // No procesar el mismo enemigo dos veces (ragdoll tiene múltiples colliders)
            if (processedRoots.Contains(enemyRoot)) continue;
            processedRoots.Add(enemyRoot);

            // No targetearse a sí mismo
            if (enemyRoot == transform) continue;

            // Verificar tag
            if (!IsEnemyTag(enemyRoot.tag)) continue;

            // Verificar que no esté detrás de una pared
            if (!HasLineOfSight(enemyRoot)) continue;

            Vector3 toEnemy = enemyRoot.position - transform.position;
            toEnemy.y = 0f;

            float distance = toEnemy.magnitude;

            // VALIDACIÓN ESTRICTA DE DISTANCIA: 
            // Evita que un collider grande (esfera de visión, aggro) registre al enemigo si la raíz real está más lejos que el maxSnapRange.
            if (distance > maxSnapRange) continue;

            float score = 0f;

            if (hasDirectionalInput)
            {
                // Sistema Arkham: el jugador dicta la dirección hacia la que quiere atacar
                float angleToInput = Vector3.Angle(inputDir, toEnemy);

                // VALIDACIÓN ESTRICTA DE ÁNGULO PARA INPUT:
                // Aplica el límite del cono también cuando se usa el stick direccional.
                if (angleToInput > searchConeAngle * 0.5f) continue;

                // Penalizar fuertemente los grados de desviación para priorizar la dirección del stick sobre la distancia
                float anglePenalty = (angleToInput / 180f) * 10f;
                score = distance + anglePenalty;
            }
            else
            {
                // Sistema estándar: el jugador ataca hacia donde mira el personaje dentro de un cono
                float angleToForward = Vector3.Angle(transform.forward, toEnemy);
                if (angleToForward > searchConeAngle * 0.5f) continue;

                float anglePenalty = (angleToForward / 180f) * 2f;
                score = distance + anglePenalty;
            }

            if (score < bestScore)
            {
                bestScore = score;
                best = enemyRoot;
            }
        }

        return best;
    }

    /// <summary>
    /// Obtiene el root del enemigo: busca StatusC en el collider o sus padres.
    /// </summary>
    Transform GetEnemyRoot(Collider col)
    {
        // Intentar StatusC primero (más confiable)
        StatusC stat = col.GetComponentInParent<StatusC>();
        if (stat != null) return stat.transform;

        // Si no tiene StatusC, usar el root del collider si tiene tag enemigo
        if (IsEnemyTag(col.tag)) return col.transform;

        return null;
    }

    bool IsValidTarget(Transform t)
    {
        if (t == null) return false;

        // Verificar distancia
        float dist = Vector3.Distance(transform.position, t.position);
        if (dist > maxSnapRange) return false;

        // Verificar que esté vivo (si tiene StatusC)
        StatusC stat = t.GetComponent<StatusC>();
        if (stat == null) stat = t.GetComponentInChildren<StatusC>();
        if (stat != null && stat.health <= 0) return false;

        return true;
    }

    bool IsEnemyTag(string tag)
    {
        for (int i = 0; i < enemyTags.Length; i++)
        {
            if (tag == enemyTags[i]) return true;
        }
        return false;
    }

    bool HasLineOfSight(Transform target)
    {
        if (obstacleLayers == 0) return true; // sin obstáculos definidos, siempre permite

        Vector3 origin = transform.position + Vector3.up * 1f;
        Vector3 dir = (target.position + Vector3.up * 1f) - origin;
        float dist = dir.magnitude;

        return !Physics.Raycast(origin, dir.normalized, dist, obstacleLayers);
    }

    // ════════════════════════════════════════════════════════
    //  EJECUCIÓN DEL SNAP
    // ════════════════════════════════════════════════════════

    IEnumerator SnapRoutine(Transform target, float stopDistance, System.Action onComplete)
    {
        Vector3 startPos = transform.position;
        Quaternion startRot = transform.rotation;

        // Calcular posición final: a "stopDistance" del enemigo, en la dirección desde el enemigo hacia el player
        Vector3 enemyPos = target.position;
        Vector3 dirFromEnemy = (transform.position - enemyPos);
        dirFromEnemy.y = 0f;
        if (dirFromEnemy.sqrMagnitude < 0.01f)
            dirFromEnemy = -target.forward;
        dirFromEnemy.Normalize();

        Vector3 endPos = enemyPos + dirFromEnemy * stopDistance;
        endPos.y = startPos.y; // mantener la altura del player

        // Desactivar el CharacterController temporalmente para no chocar con cosas durante el snap
        bool ccWasEnabled = false;
        if (charController != null)
        {
            ccWasEnabled = charController.enabled;
            charController.enabled = false;
        }

        // BLOQUEAR ROTACIÓN Y MOVIMIENTO DE INVECTOR PARA EVITAR CONFLICTOS CON EL SNAP
        bool wasLockRotation = false;
        bool wasLockMovement = false;
        if (controller != null)
        {
            wasLockRotation = controller.lockRotation;
            wasLockMovement = controller.lockMovement;
            controller.lockRotation = true;
            controller.lockMovement = true;
        }

        // --- LÓGICA DE VFX Y ANIMACIÓN DE TRANSICIÓN (Extraída de vDashInput_Ultimate) ---
        if (animator != null && !string.IsNullOrEmpty(transitionAnimState))
        {
            animator.CrossFadeInFixedTime(transitionAnimState, transitionCrossfade);
        }

        if (snapVfxPrefab != null)
        {
            // Determinar posición exacta del spawn
            Vector3 spawnPos = vfxSocket != null ? vfxSocket.position : transform.position + Vector3.up;

            // Determinar Rotación (mirando hacia el objetivo del snap)
            Vector3 fireDirection = (enemyPos - startPos).normalized;
            if (fireDirection == Vector3.zero) fireDirection = transform.forward;
            Quaternion spawnRot = Quaternion.LookRotation(fireDirection);

            // Instanciar FORZANDO posición y rotación
            GameObject vfx = Instantiate(snapVfxPrefab, spawnPos, spawnRot);
            vfx.transform.position = spawnPos;
            vfx.transform.rotation = spawnRot;
            Destroy(vfx, 2.0f);
        }
        // --------------------------------------------------------------------------------

        // Lerp
        float elapsed = 0f;
        while (elapsed < snapDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / snapDuration);
            float curve = snapCurve.Evaluate(t);

            transform.position = Vector3.Lerp(startPos, endPos, curve);

            if (rotateToTarget)
            {
                // Calcular dirección dinámica hacia el objetivo por si se mueve durante el frame
                Vector3 currentLookDir = (target.position - transform.position);
                currentLookDir.y = 0f;
                if (currentLookDir.sqrMagnitude > 0.01f)
                {
                    Quaternion currentTargetRot = Quaternion.LookRotation(currentLookDir);
                    transform.rotation = Quaternion.Slerp(startRot, currentTargetRot, curve);
                }
            }

            yield return null;
        }

        // Asegurar posición y rotación final exacta
        transform.position = endPos;
        if (rotateToTarget)
        {
            Vector3 finalLookDir = (target.position - transform.position);
            finalLookDir.y = 0f;
            if (finalLookDir.sqrMagnitude > 0.01f)
                transform.rotation = Quaternion.LookRotation(finalLookDir);
        }

        // Reactivar CharacterController
        if (charController != null && ccWasEnabled)
        {
            charController.enabled = true;
        }

        // DEVOLVER EL CONTROL A INVECTOR
        if (controller != null)
        {
            controller.lockRotation = wasLockRotation;
            controller.lockMovement = wasLockMovement;
        }

        activeSnap = null;
        onComplete?.Invoke();
    }

    /// <summary>
    /// Rota instantáneamente al player para mirar al target (sin moverse).
    /// </summary>
    void FaceTarget(Transform target)
    {
        Vector3 dir = target.position - transform.position;
        dir.y = 0f;
        if (dir.sqrMagnitude > 0.01f)
        {
            transform.rotation = Quaternion.LookRotation(dir);
        }
    }

    // ════════════════════════════════════════════════════════
    //  GIZMOS
    // ════════════════════════════════════════════════════════

    void OnDrawGizmosSelected()
    {
        if (!debugDraw) return;

        // Rango de snap
        Gizmos.color = new Color(0f, 1f, 1f, 0.15f);
        Gizmos.DrawSphere(transform.position, maxSnapRange);

        // Debug de dirección de input
        if (Application.isPlaying)
        {
            Vector3 inputDir = GetInputDirection();
            if (inputDir.sqrMagnitude > 0f)
            {
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position, transform.position + inputDir * maxSnapRange);
            }
        }

        // Cono de búsqueda
        Gizmos.color = Color.cyan;
        Vector3 fwd = transform.forward * maxSnapRange;
        Quaternion leftRot = Quaternion.AngleAxis(-searchConeAngle * 0.5f, Vector3.up);
        Quaternion rightRot = Quaternion.AngleAxis(searchConeAngle * 0.5f, Vector3.up);
        Gizmos.DrawLine(transform.position, transform.position + leftRot * fwd);
        Gizmos.DrawLine(transform.position, transform.position + rightRot * fwd);

        // Target actual
        if (_currentTarget != null)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position, _currentTarget.position);
            Gizmos.DrawWireSphere(_currentTarget.position, 0.4f);
        }
    }
}