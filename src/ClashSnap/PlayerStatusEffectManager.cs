using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Invector.vCharacterController;
using MalbersAnimations.Cards;

[DisallowMultipleComponent]
[RequireComponent(typeof(vThirdPersonController))]
[RequireComponent(typeof(vThirdPersonInput))]
public class PlayerStatusEffectManager : MonoBehaviour
{
    [Header("═══ Estados del Player ═══")]
    public List<PlayerStatusEffectDefinition> possibleEffects = new List<PlayerStatusEffectDefinition>();

    [Header("═══ Referencias (auto-detectadas) ═══")]
    public vThirdPersonController cc;
    public vThirdPersonInput tpInput;
    public Animator animator;
    public vSnapAttackHook snapAttackHook;

    private StatusC statusC;

    [Header("═══ Runtime (Solo Lectura) ═══")]
    public bool isInvulnerable = false;

    [SerializeField] private List<string> _activeNames = new List<string>();

    private Dictionary<string, Coroutine> activeRoutines = new Dictionary<string, Coroutine>();
    private Dictionary<string, GameObject> activeVFX = new Dictionary<string, GameObject>();
    private Dictionary<string, PlayerStatusEffectDefinition> activeDefinitions = new Dictionary<string, PlayerStatusEffectDefinition>();
    private Dictionary<string, float> effectTimers = new Dictionary<string, float>();

    private bool isPlayerLocked = false;
    private bool wasImmortalBefore = false;

    void Awake()
    {
        cc = GetComponent<vThirdPersonController>();
        tpInput = GetComponent<vThirdPersonInput>();
        animator = GetComponent<Animator>();
        statusC = GetComponent<StatusC>();

        ResolveSnapAttackHook();
    }

    void OnDisable()
    {
        StopAllCoroutines();

        if (!IsDead())
        {
            foreach (var kvp in activeDefinitions)
            {
                if (kvp.Value != null) RestoreDebuffs(kvp.Value);
            }
        }

        foreach (var kvp in activeVFX)
        {
            if (kvp.Value != null) Destroy(kvp.Value);
        }

        activeRoutines.Clear();
        activeVFX.Clear();
        activeDefinitions.Clear();
        effectTimers.Clear();
        _activeNames.Clear();

        isInvulnerable = false;
        ForceReleasePlayerControls();
        SetSnapAttackHookLocked(false);
    }

    public bool ApplyStatus(string effectName, Transform source = null)
    {
        return ApplyStatusInternal(effectName, source, false, Vector3.zero);
    }

    public bool ApplyStatusAtPoint(string effectName, Vector3 hitPoint)
    {
        return ApplyStatusInternal(effectName, null, true, hitPoint);
    }

    private bool ApplyStatusInternal(string effectName, Transform source, bool hasHitPoint, Vector3 hitPoint)
    {
        if (IsDead()) return false;

        ProjectileClashAttribute incomingPca = FindProjectileClashAttribute(source);
        ClashCategory? myAttackCategory = UniversalClashManager.GetActiveAttackCategory(gameObject);

        PlayerClashDefender cd = GetComponent<PlayerClashDefender>();
        if (cd == null) cd = GetComponentInParent<PlayerClashDefender>();
        if (cd == null) cd = GetComponentInChildren<PlayerClashDefender>(true);

        // ════════════════════════════════════════════════════════════════
        // EVALUACIÓN DE EFECTOS CLASH (STRIKER / BREAKER / DEFENDER)
        // ════════════════════════════════════════════════════════════════

        // 1. REGLA: STRIKER VS BREAKER (El Player casteaba Breaker, Enemigo le pegó con Striker)
        if (myAttackCategory == ClashCategory.Breaker && incomingPca != null && incomingPca.clashCategory == ClashCategory.Strike)
        {
            UniversalClashManager.CancelAttack(gameObject);
            string punishStatus = UniversalClashManager.Instance != null ? UniversalClashManager.Instance.breakerPunishedByStrikerStatus : "Stagger";

            if (!string.IsNullOrEmpty(incomingPca.punishStatusToBreaker))
                punishStatus = incomingPca.punishStatusToBreaker;

            effectName = punishStatus;
        }

        // 2. REGLA: BREAKER VS DEFENDER (El Player estaba Defendiendo, Enemigo le pegó con Breaker)
        if (cd != null && cd.IsDefending && incomingPca != null && incomingPca.clashCategory == ClashCategory.Breaker)
        {
            if (!string.IsNullOrEmpty(incomingPca.statusToApply))
            {
                effectName = incomingPca.statusToApply;
            }
        }

        // 3. REGLA: STRIKER VS DEFENDER (El Player estaba Defendiendo, Enemigo le pegó con Striker)
        if (cd != null && cd.ShouldNullifyIncomingStatus(source))
        {
            ReflectPunishToAttacker(source);
            return false;
        }

        // ════════════════════════════════════════════════════════════════
        // AHORA revisamos si effectName está vacío
        // ════════════════════════════════════════════════════════════════
        if (string.IsNullOrEmpty(effectName)) return false;

        PlayerStatusEffectDefinition def = FindDefinition(effectName);
        if (def == null)
        {
            Debug.LogWarning("[PlayerStatusEffectManager] No existe definición para el efecto: " + effectName, this);
            return false;
        }

        if (def.interruptOtherEffects)
        {
            List<string> keys = new List<string>(activeRoutines.Keys);
            foreach (string key in keys) CleanupEffect(key, true);
        }

        if (activeRoutines.ContainsKey(effectName))
        {
            CleanupEffect(effectName, false);
        }

        Coroutine co = StartCoroutine(EffectRoutine(def, source, hasHitPoint, hitPoint));
        activeRoutines[effectName] = co;
        activeDefinitions[effectName] = def;

        if (!_activeNames.Contains(effectName))
            _activeNames.Add(effectName);

        return true;
    }

    private void ReflectPunishToAttacker(Transform source)
    {
        if (source == null) return;

        BulletStatusC bullet = source.GetComponent<BulletStatusC>();
        if (bullet == null) bullet = source.GetComponentInParent<BulletStatusC>();
        if (bullet == null) bullet = source.GetComponentInChildren<BulletStatusC>(true);

        Transform shooterTransform = null;
        if (bullet != null && bullet.shooter != null)
        {
            shooterTransform = bullet.shooter.transform; // Referencia EXACTA al tirador
        }
        else
        {
            shooterTransform = source.root;
        }

        if (shooterTransform == null) return;

        string punishStatus = UniversalClashManager.Instance != null ? UniversalClashManager.Instance.strikerPunishedByDefenderStatus : "Stun";

        if (shooterTransform.CompareTag("Player") || shooterTransform.GetComponentInChildren<PlayerStatusEffectManager>(true) != null)
        {
            PlayerStatusEffectManager psem = shooterTransform.GetComponentInChildren<PlayerStatusEffectManager>(true);
            if (psem == null && shooterTransform.parent != null) psem = shooterTransform.GetComponentInParent<PlayerStatusEffectManager>();

            if (psem != null) psem.ApplyStatus(punishStatus, transform);
        }
        else
        {
            EnemyStatusEffectManager esem = shooterTransform.GetComponentInChildren<EnemyStatusEffectManager>(true);
            if (esem == null && shooterTransform.parent != null) esem = shooterTransform.GetComponentInParent<EnemyStatusEffectManager>();

            if (esem != null) esem.ApplyStatus(punishStatus, transform);
        }
    }

    public bool HasStatus(string effectName)
    {
        if (string.IsNullOrEmpty(effectName)) return false;
        return activeRoutines.ContainsKey(effectName);
    }

    public void RemoveStatus(string effectName)
    {
        if (!activeRoutines.ContainsKey(effectName)) return;
        CleanupEffect(effectName, true);
    }

    public void RemoveAllStatuses()
    {
        List<string> keys = new List<string>(activeRoutines.Keys);
        foreach (string key in keys) CleanupEffect(key, true);
    }

    IEnumerator EffectRoutine(PlayerStatusEffectDefinition def, Transform source, bool hasHitPoint, Vector3 hitPoint)
    {
        string effectName = def.effectName;

        ApplyDebuffs(def);

        Vector3 pushDirection = ResolvePushDirection(source, hasHitPoint, hitPoint);

        if (def.vfxPrefab != null)
        {
            Vector3 vfxPos = transform.position + def.vfxOffset;
            GameObject vfx = Instantiate(def.vfxPrefab, vfxPos, transform.rotation);
            vfx.transform.SetParent(transform);
            activeVFX[effectName] = vfx;
        }

        if (def.lockControls)
        {
            LockPlayerControls();
        }

        if (def.lockVSnapAttackHook)
        {
            SetSnapAttackHookLocked(true);
        }

        if (def.grantIFrames)
        {
            wasImmortalBefore = this.isInvulnerable;
            this.isInvulnerable = true;
        }

        if (def.forceAnimation && !string.IsNullOrEmpty(def.animationStateName))
        {
            PlayForcedAnimation(def);
        }

        if (!def.lockPosition && (def.knockbackForce > 0 || def.knockupForce > 0))
        {
            StartCoroutine(SlidePlayerRoutine(def.knockbackForce, def.knockupForce, pushDirection));
        }

        float timer = def.duration;
        effectTimers[effectName] = timer;
        Vector3 lockedPosition = transform.position;

        while (timer > 0f)
        {
            if (IsDead())
            {
                effectTimers.Remove(effectName);
                CleanupEffect(effectName, false);
                yield break;
            }

            if (def.lockPosition && cc != null)
            {
                transform.position = lockedPosition;
                cc.GetComponent<Rigidbody>().velocity = Vector3.zero;
            }

            timer -= Time.deltaTime;
            effectTimers[effectName] = Mathf.Max(0f, timer);
            yield return null;
        }

        effectTimers.Remove(effectName);
        CleanupEffect(effectName, true);
    }

    private Vector3 ResolvePushDirection(Transform source, bool hasHitPoint, Vector3 hitPoint)
    {
        Vector3 origin;

        if (hasHitPoint)
        {
            origin = hitPoint;
        }
        else if (source != null)
        {
            origin = source.position;
        }
        else
        {
            origin = transform.position + transform.forward;
        }

        Vector3 pushDirection = transform.position - origin;
        pushDirection.y = 0f;

        if (pushDirection.sqrMagnitude > 0.01f)
            pushDirection.Normalize();
        else
            pushDirection = -transform.forward;

        return pushDirection;
    }

    IEnumerator SlidePlayerRoutine(float knockback, float knockup, Vector3 pushDirection)
    {
        float duration = 0.25f;
        float elapsed = 0f;

        CharacterController characterController = GetComponent<CharacterController>();

        Vector3 direction = (pushDirection * knockback) + (Vector3.up * knockup);

        while (elapsed < duration)
        {
            if (IsDead()) yield break;

            if (characterController != null && characterController.enabled)
            {
                Vector3 moveVec = direction;
                if (!characterController.isGrounded && knockup <= 0) moveVec.y -= 9.8f;
                characterController.Move(moveVec * Time.deltaTime);
            }
            else
            {
                transform.position += direction * Time.deltaTime;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    private void PlayForcedAnimation(PlayerStatusEffectDefinition def)
    {
        if (animator == null) return;

        ResetAnimatorCombatParams();

        if (def.crossfadeDuration > 0f)
        {
            animator.CrossFadeInFixedTime(def.animationStateName, def.crossfadeDuration, def.animatorLayer, 0f);
        }
        else
        {
            animator.Play(def.animationStateName, def.animatorLayer, 0f);
        }
    }

    private void LockPlayerControls()
    {
        if (isPlayerLocked) return;
        isPlayerLocked = true;

        if (tpInput != null)
        {
            tpInput.SetLockBasicInput(true);
            tpInput.SetLockCameraInput(false);
        }

        if (cc != null)
        {
            cc.lockMovement = true;
            cc.lockRotation = true;

            cc.input = Vector2.zero;
            Rigidbody rb = cc.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
            }
        }
    }

    private void RefreshPlayerControlLock()
    {
        bool stillNeedsLock = false;

        foreach (var kvp in activeDefinitions)
        {
            if (kvp.Value != null && kvp.Value.lockControls)
            {
                stillNeedsLock = true;
                break;
            }
        }

        if (stillNeedsLock || IsDead()) return;

        ForceReleasePlayerControls();
    }

    private void ForceReleasePlayerControls()
    {
        if (tpInput != null)
        {
            tpInput.SetLockBasicInput(false);
        }

        if (cc != null)
        {
            cc.lockMovement = false;
            cc.lockRotation = false;
            cc.input = Vector2.zero;

            Rigidbody rb = cc.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.velocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
            }
        }

        isPlayerLocked = false;
    }

    private void ResolveSnapAttackHook()
    {
        if (snapAttackHook != null) return;

        snapAttackHook = GetComponent<vSnapAttackHook>();
        if (snapAttackHook == null)
            snapAttackHook = GetComponentInParent<vSnapAttackHook>();
        if (snapAttackHook == null)
            snapAttackHook = GetComponentInChildren<vSnapAttackHook>(true);
    }

    private void SetSnapAttackHookLocked(bool locked)
    {
        ResolveSnapAttackHook();

        if (snapAttackHook != null)
        {
            snapAttackHook.SetExternalLock(locked);
        }
    }

    private void RefreshSnapAttackHookLock()
    {
        bool stillNeedsSnapLock = false;

        foreach (var kvp in activeDefinitions)
        {
            if (kvp.Value != null && kvp.Value.lockVSnapAttackHook)
            {
                stillNeedsSnapLock = true;
                break;
            }
        }

        SetSnapAttackHookLocked(stillNeedsSnapLock);
    }

    private void CleanupEffect(string effectName, bool restoreStats)
    {
        PlayerStatusEffectDefinition removedDefinition = null;
        activeDefinitions.TryGetValue(effectName, out removedDefinition);

        if (activeRoutines.ContainsKey(effectName))
        {
            StopCoroutine(activeRoutines[effectName]);
            activeRoutines.Remove(effectName);
        }

        if (activeVFX.ContainsKey(effectName))
        {
            if (activeVFX[effectName] != null) Destroy(activeVFX[effectName]);
            activeVFX.Remove(effectName);
        }

        effectTimers.Remove(effectName);

        activeDefinitions.Remove(effectName);
        _activeNames.Remove(effectName);

        if (restoreStats && removedDefinition != null && !IsDead())
        {
            RestoreDebuffs(removedDefinition);

            if (removedDefinition.grantIFrames)
            {
                this.isInvulnerable = wasImmortalBefore;
            }
        }

        RefreshPlayerControlLock();
        RefreshSnapAttackHookLock();
    }

    void ApplyDebuffs(PlayerStatusEffectDefinition def)
    {
        if (statusC == null) return;
        if (def.atkReduction != 0) statusC.addAtk -= def.atkReduction;
        if (def.defReduction != 0) statusC.addDef -= def.defReduction;
    }

    void RestoreDebuffs(PlayerStatusEffectDefinition def)
    {
        if (statusC == null) return;
        if (def.atkReduction != 0) statusC.addAtk += def.atkReduction;
        if (def.defReduction != 0) statusC.addDef += def.defReduction;
    }

    void ResetAnimatorCombatParams()
    {
        if (animator == null) return;
        foreach (var param in animator.parameters)
        {
            if (param.name == "WeakAttack" || param.name == "StrongAttack" || param.name == "ResetState" || param.name == "Action")
            {
                if (param.type == AnimatorControllerParameterType.Trigger)
                    animator.ResetTrigger(param.name);
            }
        }
        animator.SetBool("IsAttacking", false);
        animator.SetBool("IsBlocking", false);
    }

    private ProjectileClashAttribute FindProjectileClashAttribute(Transform source)
    {
        if (source == null) return null;

        ProjectileClashAttribute projectile = source.GetComponent<ProjectileClashAttribute>();

        if (projectile == null)
            projectile = source.GetComponentInParent<ProjectileClashAttribute>();

        if (projectile == null)
            projectile = source.GetComponentInChildren<ProjectileClashAttribute>(true);

        return projectile;
    }

    PlayerStatusEffectDefinition FindDefinition(string effectName)
    {
        for (int i = 0; i < possibleEffects.Count; i++)
        {
            if (possibleEffects[i].effectName == effectName)
                return possibleEffects[i];
        }
        return null;
    }

    bool IsDead()
    {
        if (statusC != null && statusC.health <= 0) return true;
        if (cc != null && cc.isDead) return true;
        return false;
    }
}

[System.Serializable]
public class PlayerStatusEffectDefinition
{
    [Header("─── Identificación ───")]
    public string effectName = "GuardBreak";
    public float duration = 2f;

    public bool interruptOtherEffects = true;

    [Header("─── Impacto Físico (Empuje) ───")]
    public float knockbackForce = 0f;
    public float knockupForce = 0f;

    [Header("─── Control del Player ───")]
    public bool lockControls = true;
    public bool lockPosition = true;
    public bool lockVSnapAttackHook = true;
    public bool grantIFrames = true;

    [Header("─── Animación Forzada ───")]
    public bool forceAnimation = true;
    public string animationStateName = "Stunned";
    public int animatorLayer = 0;
    public float crossfadeDuration = 0.15f;

    [Header("─── Debuffs de Stats ───")]
    public int atkReduction = 0;
    public int defReduction = 0;

    [Header("─── Visual ───")]
    public GameObject vfxPrefab;
    public Vector3 vfxOffset = Vector3.up;
}