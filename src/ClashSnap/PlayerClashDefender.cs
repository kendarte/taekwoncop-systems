using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using MalbersAnimations.Cards;
using Invector.vCharacterController;

[DisallowMultipleComponent]
[RequireComponent(typeof(vThirdPersonController))]
[RequireComponent(typeof(vThirdPersonInput))]
public class PlayerClashDefender : MonoBehaviour
{
    [Header("═══ Defensa integrada en PlayerClashDefender ═══")]
    public float cooldown = 5f;
    public float maxHoldingTime = 3f;

    [Header("═══ Control físico durante la defensa ═══")]
    public bool freezePlayerPositionDuringDefense = true;
    public bool negateDamageAndDestroyProjectile = true;

    [Header("═══ Animación de defensa ═══")]
    public string holdingPoseAnim = "ChargeAttk";
    public float crossfade = 0.15f;
    public bool syncHoldingAnimationToCD = true;
    public int defenseAnimationLayer = 0;
    public string defenseExitTrigger = "ResetState";
    public string defenseExitStateName = "";
    public float defenseExitCrossfade = 0.1f;

    [Header("═══ Defender vence a Striker ═══")]
    public ClashCategory incomingCategoryToDefend = ClashCategory.Strike;
    public string defenseReactionAnim = "ParrySuccess";
    public float defenseReactionDuration = 1f;
    [Tooltip("Fallback clásico: Si el UniversalClashManager está vacío, usa este.")]
    public string statusToApplyToEnemy = "RECOIL";

    [Header("═══ Breaker vence a Defender ═══")]
    public ClashCategory weaknessCategory = ClashCategory.Breaker;

    [Header("═══ Objeto visible durante la defensa ═══")]
    public GameObject defenseActiveObject;
    public List<GameObject> objectsToHideDuringDefense = new List<GameObject>();

    [Header("═══ Duración del Aura Defender ═══")]
    public bool keepDefenseAuraActiveForEntireCD = true;
    public float defenseAuraDuration = 0.5f;

    [Header("═══ Referencias del Player ═══")]
    public StatusC statusC;
    public PlayerStatusEffectManager playerStatusEffectManager;
    public Animator animator;
    public vThirdPersonController cc;
    public vThirdPersonInput tpInput;
    public Rigidbody controlledRigidbody;

    [Header("═══ Runtime (solo lectura) ═══")]
    public bool isHitboxActive = false;
    [SerializeField] private bool defenseSessionActive = false;
    [SerializeField] private bool hasResolvedCurrentDefense = false;
    [SerializeField] private float currentCooldown = 0f;

    private Coroutine defenseTimeoutRoutine;
    private Coroutine defenseReactionRoutine;
    private Coroutine defenseAnimationSyncRoutine;
    private Coroutine defenseAuraDurationRoutine;

    private readonly Dictionary<GameObject, bool> hiddenObjectOriginalStates = new Dictionary<GameObject, bool>();

    private bool runtimeStateCaptured;
    private bool positionLockActive;
    private Vector3 lockedWorldPosition;
    private Transform controlledRoot;

    private bool defenseImmortalityCaptured;
    private bool previousImmortalState;
    private bool breakerBypassActive;
    private bool breakerAnimationSuppressed;

    private float previousAnimatorSpeed = 1f;
    private bool previousAnimatorApplyRootMotion;

    public bool IsDefending { get { return defenseSessionActive; } }
    public float CooldownRemaining { get { return Mathf.Max(0f, currentCooldown); } }

    void Awake()
    {
        ResolveReferences();
        SetDefenseActiveObject(false);
    }

    void Update()
    {
        if (currentCooldown > 0f) currentCooldown = Mathf.Max(0f, currentCooldown - Time.deltaTime);
    }

    void OnDisable()
    {
        EndDefenseInternal(false);
    }

    void LateUpdate()
    {
        if (breakerBypassActive && statusC != null) statusC.immortal = false;

        if (!positionLockActive || controlledRoot == null) return;

        controlledRoot.position = lockedWorldPosition;

        if (controlledRigidbody != null)
        {
            controlledRigidbody.velocity = Vector3.zero;
            controlledRigidbody.angularVelocity = Vector3.zero;
        }
    }

    public bool CanBeginDefense()
    {
        return enabled && gameObject.activeInHierarchy && !defenseSessionActive && currentCooldown <= 0f;
    }

    public bool TryBeginDefense()
    {
        if (!CanBeginDefense()) return false;

        ResolveReferences();
        ClearStaleRuntimeWithoutNotification();

        defenseSessionActive = true;
        hasResolvedCurrentDefense = false;
        isHitboxActive = true;
        breakerBypassActive = false;
        breakerAnimationSuppressed = false;
        defenseImmortalityCaptured = false;
        previousImmortalState = false;
        currentCooldown = Mathf.Max(0f, cooldown);

        SetDefenseActiveObject(true);
        HideConfiguredObjectsForDefense();

        CaptureRuntimeState();
        EnterDefenseControl();

        StartHoldingDefenseAnimation();
        defenseTimeoutRoutine = StartCoroutine(DefenseTimeoutRoutine());
        return true;
    }

    public void BeginDefense() { TryBeginDefense(); }
    public void CancelDefense() { EndDefenseInternal(false); }
    public void ActivateHitbox() { }
    public void DeactivateHitbox() { }

    public bool ShouldNullifyIncomingStatus(Transform source)
    {
        if (source == null) return false;

        // IGNORAR FUEGO AMIGO EN EL DEFENDER
        BulletStatusC bullet = source.GetComponent<BulletStatusC>();
        if (bullet == null) bullet = source.GetComponentInParent<BulletStatusC>();
        if (bullet != null && (bullet.shooterTag == "Player" || bullet.shooterTag == "Ally" || bullet.shooter == gameObject)) return false;

        ProjectileClashAttribute projectile = FindProjectileClashAttribute(source);
        if (projectile == null) return false;

        if (projectile.clashCategory == weaknessCategory) return false;
        if (!defenseSessionActive || hasResolvedCurrentDefense || breakerBypassActive) return false;

        return projectile.clashCategory == incomingCategoryToDefend;
    }

    private ProjectileClashAttribute FindProjectileClashAttribute(Transform source)
    {
        if (source == null) return null;
        ProjectileClashAttribute projectile = source.GetComponent<ProjectileClashAttribute>();
        if (projectile == null) projectile = source.GetComponentInParent<ProjectileClashAttribute>();
        if (projectile == null) projectile = source.GetComponentInChildren<ProjectileClashAttribute>(true);
        return projectile;
    }

    private ProjectileClashAttribute FindProjectileClashAttribute(Collider other)
    {
        if (other == null) return null;
        ProjectileClashAttribute projectile = other.GetComponent<ProjectileClashAttribute>();
        if (projectile == null) projectile = other.GetComponentInParent<ProjectileClashAttribute>();
        if (projectile == null) projectile = other.GetComponentInChildren<ProjectileClashAttribute>(true);
        return projectile;
    }

    private IEnumerator DefenseTimeoutRoutine()
    {
        float duration = Mathf.Max(0f, maxHoldingTime);
        if (duration > 0f) yield return new WaitForSeconds(duration);
        else yield return null;

        defenseTimeoutRoutine = null;
        if (defenseSessionActive && !hasResolvedCurrentDefense) EndDefenseInternal(true);
    }

    private void ResolveReferences()
    {
        if (controlledRoot == null) controlledRoot = transform.root;
        if (statusC == null) statusC = controlledRoot.GetComponent<StatusC>();
        if (statusC == null) statusC = controlledRoot.GetComponentInChildren<StatusC>(true);

        if (playerStatusEffectManager == null) playerStatusEffectManager = controlledRoot.GetComponent<PlayerStatusEffectManager>();
        if (playerStatusEffectManager == null) playerStatusEffectManager = controlledRoot.GetComponentInChildren<PlayerStatusEffectManager>(true);

        if (animator == null) animator = controlledRoot.GetComponent<Animator>();
        if (animator == null) animator = controlledRoot.GetComponentInChildren<Animator>(true);

        if (cc == null) cc = controlledRoot.GetComponent<vThirdPersonController>();
        if (tpInput == null) tpInput = controlledRoot.GetComponent<vThirdPersonInput>();
        if (controlledRigidbody == null) controlledRigidbody = controlledRoot.GetComponent<Rigidbody>();
    }

    private void CaptureRuntimeState()
    {
        runtimeStateCaptured = true;

        if (animator != null)
        {
            previousAnimatorApplyRootMotion = animator.applyRootMotion;
            previousAnimatorSpeed = animator.speed > 0f ? animator.speed : 1f;
        }
    }

    private void EnterDefenseControl()
    {
        if (tpInput != null) tpInput.SetLockBasicInput(true);

        if (cc != null)
        {
            cc.lockMovement = true;
            cc.lockRotation = true;
            cc.input = Vector2.zero;
        }

        if (!freezePlayerPositionDuringDefense)
        {
            positionLockActive = false;
            return;
        }

        positionLockActive = true;
        lockedWorldPosition = controlledRoot.position;

        if (controlledRigidbody != null)
        {
            controlledRigidbody.velocity = Vector3.zero;
            controlledRigidbody.angularVelocity = Vector3.zero;
        }

        if (animator != null) animator.applyRootMotion = false;
    }

    private void RestoreRuntimeState()
    {
        positionLockActive = false;

        if (!runtimeStateCaptured) return;

        if (controlledRoot != null && freezePlayerPositionDuringDefense) controlledRoot.position = lockedWorldPosition;

        if (controlledRigidbody != null)
        {
            controlledRigidbody.velocity = Vector3.zero;
            controlledRigidbody.angularVelocity = Vector3.zero;
        }

        if (animator != null)
        {
            animator.speed = previousAnimatorSpeed > 0f ? previousAnimatorSpeed : 1f;
            animator.applyRootMotion = previousAnimatorApplyRootMotion;
        }

        if (tpInput != null) tpInput.SetLockBasicInput(false);
        if (cc != null)
        {
            cc.lockMovement = false;
            cc.lockRotation = false;
            cc.input = Vector2.zero;
        }

        runtimeStateCaptured = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!isHitboxActive || !defenseSessionActive) return;

        // IGNORAR FUEGO AMIGO EN EL DEFENDER
        BulletStatusC bullet = other.GetComponent<BulletStatusC>();
        if (bullet == null) bullet = other.GetComponentInParent<BulletStatusC>();
        if (bullet != null && (bullet.shooterTag == "Player" || bullet.shooterTag == "Ally" || bullet.shooter == gameObject)) return;

        ProjectileClashAttribute projectile = FindProjectileClashAttribute(other);
        if (projectile == null) return;

        if (projectile.clashCategory == weaknessCategory)
        {
            ResolveGuardBreak(projectile);
            return;
        }

        if (projectile.clashCategory == incomingCategoryToDefend)
        {
            ResolveSuccessfulDefense(projectile);
        }
    }

    private void ResolveGuardBreak(ProjectileClashAttribute breakerProjectile)
    {
        if (!defenseSessionActive) return;

        ResolveReferences();

        string effectName = breakerProjectile != null ? breakerProjectile.statusToApply : string.Empty;

        hasResolvedCurrentDefense = true;
        isHitboxActive = false;
        breakerBypassActive = true;
        ForceRemoveDefenseInvulnerabilityForBreaker();

        if (breakerProjectile != null && breakerProjectile.bonusDamageOnBreak > 0 && statusC != null)
        {
            statusC.OnDamage(breakerProjectile.bonusDamageOnBreak, breakerProjectile.elementId);
        }

        EndDefenseAfterBreaker(true);

        if (breakerProjectile == null || string.IsNullOrEmpty(effectName) || playerStatusEffectManager == null) return;

        playerStatusEffectManager.ApplyStatus(effectName);
    }

    private void ResolveSuccessfulDefense(ProjectileClashAttribute projectile)
    {
        if (!defenseSessionActive || hasResolvedCurrentDefense || breakerBypassActive) return;

        EnableDefenseInvulnerability();
        hasResolvedCurrentDefense = true;
        isHitboxActive = false;
        StopDefenseTimeout();

        if (projectile != null && negateDamageAndDestroyProjectile)
        {
            StopAndDestroyProjectile(projectile);
        }

        string punishStatus = statusToApplyToEnemy;
        if (UniversalClashManager.Instance != null && !string.IsNullOrEmpty(UniversalClashManager.Instance.strikerPunishedByDefenderStatus))
        {
            punishStatus = UniversalClashManager.Instance.strikerPunishedByDefenderStatus;
        }

        ApplyStatusToEnemy(punishStatus, projectile);

        StopDefenseAnimationSync();
        RestoreAnimatorSpeed();
        PlayDefenseAnimation(defenseReactionAnim, crossfade);

        defenseReactionRoutine = StartCoroutine(SuccessfulDefenseReactionRoutine());
    }

    private IEnumerator SuccessfulDefenseReactionRoutine()
    {
        float duration = Mathf.Max(0f, defenseReactionDuration);
        if (duration > 0f) yield return new WaitForSeconds(duration);
        else yield return null;

        defenseReactionRoutine = null;

        if (defenseSessionActive && !breakerBypassActive)
        {
            EndDefenseInternal(true);
        }
    }

    private void ApplyStatusToEnemy(string statusName, ProjectileClashAttribute projectile)
    {
        if (string.IsNullOrEmpty(statusName) || projectile == null) return;

        Transform attackerRoot = projectile.transform.root;
        EnemyStatusEffectManager esem = attackerRoot.GetComponentInChildren<EnemyStatusEffectManager>(true);

        if (esem != null) esem.ApplyStatus(statusName, transform);
    }

    private void StopAndDestroyProjectile(ProjectileClashAttribute projectile)
    {
        if (projectile == null) return;

        Rigidbody projectileBody = projectile.GetComponent<Rigidbody>();
        if (projectileBody == null) projectileBody = projectile.GetComponentInParent<Rigidbody>();

        GameObject projectileObject = projectileBody != null ? projectileBody.gameObject : projectile.gameObject;
        Collider[] projectileColliders = projectileObject.GetComponentsInChildren<Collider>(true);

        for (int i = 0; i < projectileColliders.Length; i++) projectileColliders[i].enabled = false;

        if (projectileBody != null)
        {
            projectileBody.velocity = Vector3.zero;
            projectileBody.angularVelocity = Vector3.zero;
            projectileBody.isKinematic = true;
        }

        MonoBehaviour[] projectileScripts = projectileObject.GetComponentsInChildren<MonoBehaviour>(true);
        for (int i = 0; i < projectileScripts.Length; i++)
        {
            if (projectileScripts[i] != projectile) projectileScripts[i].enabled = false;
        }

        Destroy(projectileObject);
    }

    private void ReleaseBreakerAnimationSuppression()
    {
        if (!breakerAnimationSuppressed) return;
        breakerAnimationSuppressed = false;
        RestoreAnimatorSpeed();
    }

    private void SafeResetAnimatorTrigger(string parameterName)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName)) return;
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == parameterName && parameters[i].type == AnimatorControllerParameterType.Trigger)
            {
                animator.ResetTrigger(parameterName);
                return;
            }
        }
    }

    private void SafeSetAnimatorTrigger(string parameterName)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName)) return;
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == parameterName && parameters[i].type == AnimatorControllerParameterType.Trigger)
            {
                animator.SetTrigger(parameterName);
                return;
            }
        }
    }

    private void SafeSetAnimatorBool(string parameterName, bool value)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName)) return;
        AnimatorControllerParameter[] parameters = animator.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == parameterName && parameters[i].type == AnimatorControllerParameterType.Bool)
            {
                animator.SetBool(parameterName, value);
                return;
            }
        }
    }

    private void PlayDefenseAnimation(string stateName, float fade)
    {
        if (string.IsNullOrEmpty(stateName) || breakerBypassActive) return;

        if (animator == null) return;
        animator.speed = 1f;
        animator.ResetTrigger("WeakAttack");
        animator.ResetTrigger("StrongAttack");
        animator.SetBool("IsAttacking", false);
        animator.CrossFadeInFixedTime(stateName, Mathf.Max(0f, fade), Mathf.Max(0, defenseAnimationLayer), 0f);
    }

    private void StartHoldingDefenseAnimation()
    {
        if (breakerBypassActive) return;

        StopDefenseAnimationSync();
        RestoreAnimatorSpeed();
        PlayDefenseAnimation(holdingPoseAnim, crossfade);

        if (syncHoldingAnimationToCD && animator != null && maxHoldingTime > 0f)
        {
            defenseAnimationSyncRoutine = StartCoroutine(SyncHoldingAnimationToCDRoutine());
        }
    }

    private IEnumerator SyncHoldingAnimationToCDRoutine()
    {
        yield return null;

        if (!defenseSessionActive || hasResolvedCurrentDefense || breakerBypassActive || animator == null)
        {
            defenseAnimationSyncRoutine = null;
            yield break;
        }

        int layer = Mathf.Max(0, defenseAnimationLayer);
        float enterTimeout = 0.5f;

        while (enterTimeout > 0f)
        {
            AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(layer);
            if (state.IsName(holdingPoseAnim)) break;
            enterTimeout -= Time.deltaTime;
            yield return null;
        }

        if (!defenseSessionActive || hasResolvedCurrentDefense || breakerBypassActive || animator == null)
        {
            defenseAnimationSyncRoutine = null;
            yield break;
        }

        AnimatorStateInfo holdingState = animator.GetCurrentAnimatorStateInfo(layer);

        if (holdingState.IsName(holdingPoseAnim) && holdingState.length > 0.001f && maxHoldingTime > 0.001f)
        {
            animator.speed = Mathf.Max(0.01f, holdingState.length / maxHoldingTime);
        }

        while (defenseSessionActive && !hasResolvedCurrentDefense && !breakerBypassActive)
        {
            yield return null;
        }

        defenseAnimationSyncRoutine = null;
    }

    private void StopDefenseAnimationSync()
    {
        if (defenseAnimationSyncRoutine != null)
        {
            StopCoroutine(defenseAnimationSyncRoutine);
            defenseAnimationSyncRoutine = null;
        }
    }

    private void RestoreAnimatorSpeed()
    {
        if (animator != null) animator.speed = previousAnimatorSpeed > 0f ? previousAnimatorSpeed : 1f;
    }

    private void ExitDefenseAnimation()
    {
        StopDefenseAnimationSync();
        RestoreAnimatorSpeed();

        if (animator == null) return;

        SafeResetAnimatorTrigger("WeakAttack");
        SafeResetAnimatorTrigger("StrongAttack");
        SafeSetAnimatorBool("IsAttacking", false);
        SafeSetAnimatorBool("IsBlocking", false);

        int layer = Mathf.Max(0, defenseAnimationLayer);
        bool exitedByState = false;

        if (!string.IsNullOrEmpty(defenseExitStateName))
        {
            int stateHash = Animator.StringToHash(defenseExitStateName);
            if (animator.HasState(layer, stateHash))
            {
                animator.CrossFadeInFixedTime(stateHash, Mathf.Max(0f, defenseExitCrossfade), layer, 0f);
                exitedByState = true;
            }
        }

        if (!exitedByState && !string.IsNullOrEmpty(defenseExitTrigger)) SafeSetAnimatorTrigger(defenseExitTrigger);

        animator.Update(0f);
    }

    private void EnableDefenseInvulnerability()
    {
        if (statusC == null) return;

        if (breakerBypassActive)
        {
            statusC.immortal = false;
            return;
        }

        if (!defenseImmortalityCaptured)
        {
            previousImmortalState = statusC.immortal;
            defenseImmortalityCaptured = true;
        }

        statusC.immortal = true;
    }

    private void ReleaseDefenseInvulnerability()
    {
        if (statusC != null && defenseImmortalityCaptured) statusC.immortal = previousImmortalState;
        defenseImmortalityCaptured = false;
    }

    private void ForceRemoveDefenseInvulnerabilityForBreaker()
    {
        if (statusC != null) statusC.immortal = false;
        previousImmortalState = false;
        defenseImmortalityCaptured = false;
    }

    private void SetDefenseActiveObject(bool visible)
    {
        if (defenseActiveObject != null && defenseActiveObject.activeSelf != visible) defenseActiveObject.SetActive(visible);
    }

    private void HideConfiguredObjectsForDefense()
    {
        RestoreConfiguredObjectsAfterDefense();

        if (objectsToHideDuringDefense == null) return;

        for (int i = 0; i < objectsToHideDuringDefense.Count; i++)
        {
            GameObject target = objectsToHideDuringDefense[i];
            if (target == null || hiddenObjectOriginalStates.ContainsKey(target)) continue;

            hiddenObjectOriginalStates.Add(target, target.activeSelf);
            if (target.activeSelf) target.SetActive(false);
        }
    }

    private void RestoreConfiguredObjectsAfterDefense()
    {
        if (hiddenObjectOriginalStates.Count == 0) return;

        foreach (KeyValuePair<GameObject, bool> pair in hiddenObjectOriginalStates)
        {
            if (pair.Key != null && pair.Key.activeSelf != pair.Value) pair.Key.SetActive(pair.Value);
        }

        hiddenObjectOriginalStates.Clear();
    }

    private void StopDefenseTimeout()
    {
        if (defenseTimeoutRoutine != null)
        {
            StopCoroutine(defenseTimeoutRoutine);
            defenseTimeoutRoutine = null;
        }
    }

    private void StopDefenseAuraDuration()
    {
        if (defenseAuraDurationRoutine != null)
        {
            StopCoroutine(defenseAuraDurationRoutine);
            defenseAuraDurationRoutine = null;
        }
    }

    private void ClearStaleRuntimeWithoutNotification()
    {
        StopDefenseTimeout();
        StopDefenseAuraDuration();
        StopDefenseAnimationSync();

        if (defenseReactionRoutine != null)
        {
            StopCoroutine(defenseReactionRoutine);
            defenseReactionRoutine = null;
        }

        ReleaseBreakerAnimationSuppression();
        breakerBypassActive = false;
        ReleaseDefenseInvulnerability();
        SetDefenseActiveObject(false);
        RestoreConfiguredObjectsAfterDefense();

        RestoreAnimatorSpeed();
        RestoreRuntimeState();
        defenseSessionActive = false;
        isHitboxActive = false;
        hasResolvedCurrentDefense = false;
    }

    private void EndDefenseAfterBreaker(bool notifyBrain)
    {
        StopDefenseTimeout();
        StopDefenseAuraDuration();
        StopDefenseAnimationSync();

        if (defenseReactionRoutine != null)
        {
            StopCoroutine(defenseReactionRoutine);
            defenseReactionRoutine = null;
        }

        isHitboxActive = false;
        hasResolvedCurrentDefense = false;

        breakerAnimationSuppressed = false;
        RestoreAnimatorSpeed();

        breakerBypassActive = false;
        ForceRemoveDefenseInvulnerabilityForBreaker();
        SetDefenseActiveObject(false);
        RestoreConfiguredObjectsAfterDefense();

        RestoreRuntimeStateAfterBreaker();
        defenseSessionActive = false;
    }

    private void RestoreRuntimeStateAfterBreaker()
    {
        positionLockActive = false;

        if (!runtimeStateCaptured) return;

        if (controlledRigidbody != null)
        {
            controlledRigidbody.velocity = Vector3.zero;
            controlledRigidbody.angularVelocity = Vector3.zero;
        }

        if (animator != null)
        {
            animator.speed = previousAnimatorSpeed > 0f ? previousAnimatorSpeed : 1f;
            animator.applyRootMotion = previousAnimatorApplyRootMotion;
        }

        runtimeStateCaptured = false;
    }

    private void EndDefenseInternal(bool notifyBrain)
    {
        if (breakerBypassActive)
        {
            EndDefenseAfterBreaker(notifyBrain);
            return;
        }

        StopDefenseTimeout();
        StopDefenseAuraDuration();
        StopDefenseAnimationSync();

        if (defenseReactionRoutine != null)
        {
            StopCoroutine(defenseReactionRoutine);
            defenseReactionRoutine = null;
        }

        isHitboxActive = false;
        hasResolvedCurrentDefense = false;
        ReleaseBreakerAnimationSuppression();
        breakerBypassActive = false;
        ReleaseDefenseInvulnerability();
        SetDefenseActiveObject(false);
        RestoreConfiguredObjectsAfterDefense();

        ExitDefenseAnimation();
        RestoreRuntimeState();
        defenseSessionActive = false;
    }
}