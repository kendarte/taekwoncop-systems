using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Invector;
using Invector.vCharacterController;

[RequireComponent(typeof(vSnapToTarget))]
public class vSnapAttackHook : MonoBehaviour
{
    public enum ActiveSpace { None, Defender, ClashSkill, PowerClash, Additional }

    [System.Serializable]
    public class SnapAuraFX
    {
        [Tooltip("Objeto del aura que existe en el modelo (apagado por defecto).")]
        public GameObject auraObject;
        [Tooltip("Tiempo visible del aura en segundos.")]
        public float duration = 2f;
    }

    [System.Serializable]
    public class ManagerInputSlot
    {
        public bool enabled = true;
        public GenericInput input = new GenericInput("Fire2", "Y", "Y");
        public PlayerClashManager manager;
        public SnapAuraFX auraFX;
    }

    [Header("═══ Input Nativo Invector ═══")]
    public GenericInput attackInput = new GenericInput("Fire1", "X", "X");
    public SnapAuraFX normalAttackAura;
    public bool listenInput = true;

    [Header("═══ Snap Settings ═══")]
    public float attackStopDistance = 1.0f;
    public float snapCooldown = 0.3f;

    [Header("═══ Lock temporal del vSnapAttackHook ═══")]
    public bool lockHookByDuration = false;
    [Min(0.01f)] public float hookLockDuration = 1.0f;

    [SerializeField] private bool isHookLocked = false;
    [SerializeField] private bool externalLockActive = false;

    [Header("═══ Player Clash Defender ═══")]
    public bool usePlayerClashDefender = true;
    public PlayerClashDefender playerClashDefender;
    public GenericInput defenseInput = new GenericInput("Fire3", "LB", "LB");

    public enum DefensePositionMode { LockInPlace, SnapToTarget }
    public DefensePositionMode defensePositionMode = DefensePositionMode.LockInPlace;
    public SnapAuraFX defenseAura;

    [Header("═══ Cerebro Avanzado (Player ClashSkill Manager) ═══")]
    public bool usePlayerClashSkillManager = false;
    public PlayerClashManager playerClashManager;
    public SnapAuraFX clashSkillAura;

    [Header("═══ Player Power Clash Manager ═══")]
    public bool usePlayerPowerClashManger = false;
    public GenericInput powerClashInput = new GenericInput("Fire2", "Y", "Y");
    public PlayerPowerClashManger playerPowerClashManger;
    public SnapAuraFX powerClashAura;

    [Header("═══ Managers adicionales por Input ═══")]
    public List<ManagerInputSlot> additionalManagerInputs = new List<ManagerInputSlot>();

    [Header("═══ Animator ═══")]
    public Animator animator;
    public string attackTriggerName = "WeakAttack";
    public string[] additionalTriggerNames = new string[0];
    public bool triggerAfterSnap = false;

    [Header("═══ Eventos ═══")]
    public UnityEngine.Events.UnityEvent OnSnapStarted;
    public UnityEngine.Events.UnityEvent OnSnapCompleted;

    [Header("═══ Estado de Espacios (Solo Lectura) ═══")]
    [SerializeField] private ActiveSpace currentSpace = ActiveSpace.None;
    private PlayerClashManager currentAdditionalManager = null;

    private vSnapToTarget snap;
    private float lastSnapTime;
    private Coroutine hookLockRoutine;
    private Dictionary<GameObject, Coroutine> activeAuraRoutines = new Dictionary<GameObject, Coroutine>();

    void Awake()
    {
        snap = GetComponent<vSnapToTarget>();

        if (animator == null) animator = GetComponent<Animator>();

        if (playerClashDefender == null)
        {
            playerClashDefender = GetComponent<PlayerClashDefender>();
            if (playerClashDefender == null) playerClashDefender = GetComponentInParent<PlayerClashDefender>();
            if (playerClashDefender == null) playerClashDefender = GetComponentInChildren<PlayerClashDefender>(true);
        }

        if (playerClashDefender != null)
        {
            usePlayerClashDefender = true;
        }

        if (usePlayerClashSkillManager && playerClashManager == null)
        {
            playerClashManager = FindNormalPlayerClashManager();
        }

        if (usePlayerPowerClashManger && playerPowerClashManger == null)
        {
            playerPowerClashManger = GetComponent<PlayerPowerClashManger>();
            if (playerPowerClashManger == null) playerPowerClashManger = GetComponentInParent<PlayerPowerClashManger>();
            if (playerPowerClashManger == null) playerPowerClashManger = GetComponentInChildren<PlayerPowerClashManger>(true);
        }
    }

    private PlayerClashManager FindNormalPlayerClashManager()
    {
        PlayerClashManager[] localManagers = GetComponents<PlayerClashManager>();
        for (int i = 0; i < localManagers.Length; i++)
        {
            if (localManagers[i] != null && localManagers[i].GetType() == typeof(PlayerClashManager))
                return localManagers[i];
        }

        PlayerClashManager[] parentManagers = GetComponentsInParent<PlayerClashManager>(true);
        for (int i = 0; i < parentManagers.Length; i++)
        {
            if (parentManagers[i] != null && parentManagers[i].GetType() == typeof(PlayerClashManager))
                return parentManagers[i];
        }

        PlayerClashManager[] childManagers = GetComponentsInChildren<PlayerClashManager>(true);
        for (int i = 0; i < childManagers.Length; i++)
        {
            if (childManagers[i] != null && childManagers[i].GetType() == typeof(PlayerClashManager))
                return childManagers[i];
        }

        return null;
    }

    void OnDisable()
    {
        UnlockHook();
        externalLockActive = false;
        currentSpace = ActiveSpace.None;

        foreach (var kvp in activeAuraRoutines)
        {
            if (kvp.Key != null) kvp.Key.SetActive(false);
        }
        activeAuraRoutines.Clear();
    }

    void Update()
    {
        if (!listenInput || IsHookLocked()) return;

        bool isDefending = usePlayerClashDefender && playerClashDefender != null && playerClashDefender.IsDefending;
        bool isAttacking = animator != null && animator.GetBool("IsAttacking");

        // Liberar espacio activo si terminaron las animaciones y pasó el cooldown
        if (!isDefending && !isAttacking && (Time.time - lastSnapTime > snapCooldown))
        {
            currentSpace = ActiveSpace.None;
            currentAdditionalManager = null;
        }

        // 1. INPUT DE DEFENSA (Clash Defender)
        if (usePlayerClashDefender && playerClashDefender != null && defenseInput != null && defenseInput.GetButtonDown())
        {
            if (!isDefending && !isAttacking) currentSpace = ActiveSpace.None;

            if (currentSpace == ActiveSpace.None || currentSpace == ActiveSpace.Defender)
            {
                if (playerClashDefender.CanBeginDefense())
                {
                    currentSpace = ActiveSpace.Defender;
                    lastSnapTime = Time.time;

                    if (defensePositionMode == DefensePositionMode.LockInPlace)
                    {
                        if (playerClashDefender.TryBeginDefense())
                        {
                            PlayAuraFX(defenseAura);
                            if (lockHookByDuration) LockForDuration(hookLockDuration);
                        }
                        else
                        {
                            currentSpace = ActiveSpace.None;
                        }
                    }
                    else
                    {
                        BeginSnapForDefense(defenseAura);
                    }
                    return;
                }
            }
        }

        if (isDefending || currentSpace == ActiveSpace.Defender) return;

        if (Time.time - lastSnapTime < snapCooldown) return;

        // 2. INPUT BREAKER (Power Clash)
        if (usePlayerPowerClashManger && powerClashInput != null && powerClashInput.GetButtonDown() && playerPowerClashManger != null)
        {
            if (currentSpace != ActiveSpace.None && currentSpace != ActiveSpace.PowerClash) return;

            currentSpace = ActiveSpace.PowerClash;
            DoSnapWithPowerClashManger();
            return;
        }

        // 3. INPUT STRIKER (Ataque Normal / Clash Skill)
        if (attackInput != null && attackInput.GetButtonDown())
        {
            if (currentSpace != ActiveSpace.None && currentSpace != ActiveSpace.ClashSkill) return;

            currentSpace = ActiveSpace.ClashSkill;
            DoSnap();
            return;
        }

        // 4. ESPACIOS ADICIONALES
        for (int i = 0; i < additionalManagerInputs.Count; i++)
        {
            ManagerInputSlot slot = additionalManagerInputs[i];
            if (slot == null || !slot.enabled || slot.input == null || slot.manager == null) continue;

            if (slot.input.GetButtonDown())
            {
                if (currentSpace != ActiveSpace.None && currentSpace != ActiveSpace.Additional) return;
                if (currentSpace == ActiveSpace.Additional && currentAdditionalManager != slot.manager) return;

                currentSpace = ActiveSpace.Additional;
                currentAdditionalManager = slot.manager;

                DoSnapWithManager(slot.manager, slot.auraFX);
                return;
            }
        }
    }

    public void DoSnap()
    {
        SnapAuraFX auraToUse = usePlayerClashSkillManager ? clashSkillAura : normalAttackAura;
        BeginSnap(null, false, auraToUse);
    }

    public void DoSnapWithPowerClashManger()
    {
        if (playerPowerClashManger == null) return;
        BeginSnap(playerPowerClashManger, true, powerClashAura);
    }

    public void DoSnapWithManager(PlayerClashManager manager, SnapAuraFX auraFX)
    {
        if (manager == null) return;
        BeginSnap(manager, true, auraFX);
    }

    private void PlayAuraFX(SnapAuraFX fxSettings)
    {
        if (fxSettings == null || fxSettings.auraObject == null) return;

        foreach (var kvp in activeAuraRoutines)
        {
            if (kvp.Value != null) StopCoroutine(kvp.Value);
            if (kvp.Key != null) kvp.Key.SetActive(false);
        }
        activeAuraRoutines.Clear();

        Coroutine auraRoutine = StartCoroutine(AuraRoutine(fxSettings.auraObject, fxSettings.duration));
        activeAuraRoutines.Add(fxSettings.auraObject, auraRoutine);
    }

    private IEnumerator AuraRoutine(GameObject auraObject, float duration)
    {
        if (auraObject == null) yield break;
        auraObject.SetActive(true);

        if (duration > 0f) yield return new WaitForSeconds(duration);
        else yield return null;

        if (auraObject != null)
        {
            auraObject.SetActive(false);
            if (activeAuraRoutines.ContainsKey(auraObject)) activeAuraRoutines.Remove(auraObject);
        }
    }

    private void BeginSnap(PlayerClashManager explicitManager, bool useExplicitManager, SnapAuraFX fxToPlay)
    {
        if (IsHookLocked()) return;

        lastSnapTime = Time.time;
        if (lockHookByDuration) LockForDuration(hookLockDuration);

        PlayAuraFX(fxToPlay);
        OnSnapStarted?.Invoke();

        if (!triggerAfterSnap) ExecuteAttackLogic(explicitManager, useExplicitManager);

        snap.TrySnapAndAttack(
            attackStopDistance,
            () => OnSnapDone(explicitManager, useExplicitManager));
    }

    private void BeginSnapForDefense(SnapAuraFX fxToPlay)
    {
        if (IsHookLocked()) return;

        lastSnapTime = Time.time;
        if (lockHookByDuration) LockForDuration(hookLockDuration);

        PlayAuraFX(fxToPlay);
        OnSnapStarted?.Invoke();

        snap.TrySnapAndAttack(
            attackStopDistance,
            () =>
            {
                if (playerClashDefender != null)
                {
                    playerClashDefender.TryBeginDefense();
                }
                OnSnapCompleted?.Invoke();
            });
    }

    void OnSnapDone(PlayerClashManager explicitManager, bool useExplicitManager)
    {
        if (triggerAfterSnap) ExecuteAttackLogic(explicitManager, useExplicitManager);
        OnSnapCompleted?.Invoke();
    }

    public void LockForDuration(float duration)
    {
        if (hookLockRoutine != null)
        {
            StopCoroutine(hookLockRoutine);
            hookLockRoutine = null;
        }

        isHookLocked = true;
        hookLockRoutine = StartCoroutine(HookLockRoutine(Mathf.Max(0.01f, duration)));
    }

    private IEnumerator HookLockRoutine(float duration)
    {
        yield return new WaitForSeconds(duration);
        hookLockRoutine = null;
        isHookLocked = false;
    }

    public void UnlockHook()
    {
        if (hookLockRoutine != null)
        {
            StopCoroutine(hookLockRoutine);
            hookLockRoutine = null;
        }
        isHookLocked = false;
    }

    public void SetExternalLock(bool locked)
    {
        externalLockActive = locked;
    }

    public bool IsHookLocked()
    {
        return isHookLocked || externalLockActive;
    }

    private void ExecuteAttackLogic(PlayerClashManager explicitManager, bool useExplicitManager)
    {
        if (useExplicitManager)
        {
            if (explicitManager != null) explicitManager.ExecuteClash(snap.GetCurrentTarget());
            return;
        }

        if (usePlayerClashSkillManager && playerClashManager != null)
        {
            playerClashManager.ExecuteClash(snap.GetCurrentTarget());
        }
        else if (animator != null)
        {
            FireAllTriggers();
        }
    }

    void FireAllTriggers()
    {
        if (!string.IsNullOrEmpty(attackTriggerName)) animator.SetTrigger(attackTriggerName);

        if (additionalTriggerNames != null)
        {
            for (int i = 0; i < additionalTriggerNames.Length; i++)
            {
                if (!string.IsNullOrEmpty(additionalTriggerNames[i]))
                    animator.SetTrigger(additionalTriggerNames[i]);
            }
        }
    }
}