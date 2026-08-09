using UnityEngine;
using UnityEngine.AI;
using System.Collections;
using CrazyMinnow.SALSA;

public class MovieActor : MonoBehaviour
{
    [Header("--- ESTADO ---")]
    public bool isControlled = false;

    [Header("--- ORIENTACIÓN (FRENTE) ---")]
    [Tooltip("Arrastra aquí el objeto que este Actor debe mirar al llegar a su destino.")]
    public Transform frontPoint;

    [Header("--- CONFIG ANIMATOR ---")]
    public string locomotionState = "Free Locomotion";
    public float rotationSpeed = 500f;

    private float targetVertical;
    private float targetHorizontal;
    private bool useAnimMove;
    private Coroutine moveRoutine;

    [Header("--- COMPONENTES ---")]
    public Animator anim;
    public NavMeshAgent navAgent;
    public Salsa salsa;
    public Emoter emoter;
    public AudioSource audioSource;

    void Awake()
    {
        anim = GetComponent<Animator>();
        navAgent = GetComponent<NavMeshAgent>();
        salsa = GetComponent<Salsa>();
        emoter = GetComponent<Emoter>();
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;

        if (navAgent)
        {
            navAgent.updatePosition = false;
            navAgent.updateRotation = true;
        }
    }

    public void SetDirectorControl(bool active)
    {
        isControlled = active;
        if (navAgent)
        {
            navAgent.enabled = active;
            if (active)
            {
                if (moveRoutine != null) StopCoroutine(moveRoutine);
                navAgent.ResetPath();
                navAgent.isStopped = false;
                navAgent.Warp(transform.position);
                navAgent.updatePosition = true;
                navAgent.updateRotation = true;
            }
            else
            {
                navAgent.isStopped = true;
                navAgent.updatePosition = false;
            }
        }
    }

    void Update()
    {
        if (!isControlled || navAgent == null || !navAgent.enabled) return;

        if (anim != null)
        {
            if (useAnimMove && !navAgent.isStopped && navAgent.hasPath && navAgent.remainingDistance > 0.1f)
            {
                // Calculamos magnitud real basada en los valores procesados en Cmd_Move
                float magnitude = Mathf.Abs(targetVertical) + Mathf.Abs(targetHorizontal);

                // Invector usa InputMagnitude para mezclar estados
                anim.SetFloat("InputMagnitude", magnitude);
                anim.SetFloat("Vertical", targetVertical);
                anim.SetFloat("Horizontal", targetHorizontal);
            }
            else
            {
                anim.SetFloat("InputMagnitude", 0f);
                anim.SetFloat("Vertical", 0f);
                anim.SetFloat("Horizontal", 0f);
            }
        }
    }

    // --- COMANDO MOVIMIENTO ---
    public void Cmd_Move(Vector3 pos, Vector3 rotGhost, Transform lookObj, MovementSpeed speedType, float vertical, float horizontal, bool animate, bool lockRotation)
    {
        if (moveRoutine != null) StopCoroutine(moveRoutine);

        // 1. CORRECCIÓN MATEMÁTICA PARA INVECTOR
        // Normalizamos la dirección para no perder el rumbo, pero ajustamos la intensidad
        Vector2 direction = new Vector2(horizontal, vertical).normalized;
        if (direction == Vector2.zero) direction = new Vector2(0, 1); // Default hacia adelante

        float animIntensity = 0.5f; // Walk por defecto (0.5 en BlendTree)

        switch (speedType)
        {
            case MovementSpeed.Walk:
                animIntensity = 0.5f; // Valor exacto para CAMINAR
                break;
            case MovementSpeed.Run:
                animIntensity = 1.0f; // Valor exacto para CORRER
                break;
            case MovementSpeed.Sprint:
                animIntensity = 1.5f; // Valor exacto para SPRINT
                break;
        }

        // Aplicamos la intensidad a las variables globales que lee el Update
        this.targetVertical = direction.y * animIntensity;
        this.targetHorizontal = direction.x * animIntensity;

        this.useAnimMove = animate;

        moveRoutine = StartCoroutine(MoveSequence(pos, rotGhost, lookObj, speedType, lockRotation));
    }

    private IEnumerator MoveSequence(Vector3 destination, Vector3 rotGhost, Transform lookTarget, MovementSpeed speedType, bool lockRotation)
    {
        if (!navAgent || !navAgent.enabled) yield break;

        // 2. VELOCIDAD FÍSICA DEL NAVMESH
        float finalSpeed = 1.5f;
        switch (speedType)
        {
            case MovementSpeed.Walk: finalSpeed = 1.5f; break;
            case MovementSpeed.Run: finalSpeed = 3.5f; break;
            case MovementSpeed.Sprint: finalSpeed = 6.0f; break;
        }

        navAgent.isStopped = false;
        navAgent.updateRotation = !lockRotation;
        navAgent.speed = finalSpeed;
        navAgent.SetDestination(destination);

        yield return null;
        while (navAgent.pathPending) yield return null;

        float dist = navAgent.remainingDistance;
        while (dist > navAgent.stoppingDistance && dist != float.PositiveInfinity)
        {
            dist = navAgent.remainingDistance;
            yield return null;
        }

        // 3. FRENADO
        navAgent.isStopped = true;
        navAgent.velocity = Vector3.zero;
        navAgent.ResetPath();
        navAgent.updateRotation = false;

        // 4. ROTACIÓN FINAL (Objeto > Fantasma)
        Quaternion finalRotation = transform.rotation;

        if (lookTarget != null)
        {
            Vector3 direction = lookTarget.position - transform.position;
            direction.y = 0;
            if (direction != Vector3.zero) finalRotation = Quaternion.LookRotation(direction);
        }
        else if (rotGhost != Vector3.zero)
        {
            finalRotation = Quaternion.Euler(0, rotGhost.y, 0);
        }

        if (Quaternion.Angle(transform.rotation, finalRotation) > 1.0f)
        {
            while (Quaternion.Angle(transform.rotation, finalRotation) > 0.5f)
            {
                float step = rotationSpeed * Time.deltaTime;
                transform.rotation = Quaternion.RotateTowards(transform.rotation, finalRotation, step);
                yield return null;
            }
            transform.rotation = finalRotation;
        }

        moveRoutine = null;
    }

    // --- OTROS COMANDOS ---
    public float Cmd_Speak(AudioClip clip)
    {
        if (clip == null) return 0f;
        if (audioSource != null) { audioSource.clip = clip; if (salsa != null) salsa.audioSrc = audioSource; audioSource.Play(); return clip.length; }
        return 0f;
    }
    public void Cmd_Anim(AnimationClip clip, string stateName, int layer) { if (anim) { string finalName = (clip != null) ? clip.name : stateName; anim.CrossFadeInFixedTime(finalName, 0.2f, layer); } }
    public void Cmd_Emote(string emoteName, float duration) { if (emoter != null && !string.IsNullOrEmpty(emoteName)) { emoter.ManualEmote(emoteName, ExpressionComponent.ExpressionHandler.OneWay); if (duration > 0) StartCoroutine(TurnOffEmoteRoutine(emoteName, duration)); } }
    private IEnumerator TurnOffEmoteRoutine(string emoteName, float delay) { yield return new WaitForSeconds(delay); }
    public void Cmd_Interact(UnityEngine.Object triggerObj) { if (triggerObj) ((Component)triggerObj).SendMessage("OnPressActionInput", SendMessageOptions.DontRequireReceiver); }
    public void Cmd_VFX(GameObject prefab, HumanBodyBones bone, Transform customPoint, float lifeTime)
    {
        Vector3 pos = transform.position; Quaternion rot = transform.rotation;
        if (customPoint != null) { pos = customPoint.position; rot = customPoint.rotation; }
        else if (anim != null) { Transform t = anim.GetBoneTransform(bone); if (t != null) { pos = t.position; rot = t.rotation; } }
        if (prefab) { GameObject vfx = Instantiate(prefab, pos, rot); if (lifeTime > 0) Destroy(vfx, lifeTime); }
    }
    public bool CheckArrived() { return moveRoutine == null; }
}