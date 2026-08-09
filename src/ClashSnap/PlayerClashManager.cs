using UnityEngine;
using UnityEngine.Events;
using System.Collections;
using System.Collections.Generic;
using MalbersAnimations.Cards;

[RequireComponent(typeof(Animator))]
public class PlayerClashManager : MonoBehaviour, IClashCaster
{
    // Clase contenedora para rastrear el cooldown de los ScriptableObjects en tiempo real
    [System.Serializable]
    public class RuntimeSkill
    {
        public PlayerClashSkill skillAsset;

        [Min(0f)]
        [Tooltip("Duración del skill después de la anticipación. Mientras este tiempo esté activo no se puede cambiar a otro skill. Si es 0, usa totalAttackTime del asset.")]
        public float duration = 0f;

        [HideInInspector] public float currentCooldown = 0f;
    }

    [System.Serializable]
    public class SpecialRuntimeSkill : RuntimeSkill
    {
        [Header("═══ Condiciones Especiales (Dejar en blanco si no aplica) ═══")]
        [Tooltip("El ataque solo saldrá si el ENEMIGO tiene este efecto activo.")]
        public string requiredEnemyStatus = "Stagger";

        [Tooltip("El ataque solo saldrá si el JUGADOR tiene este efecto activo (Ej. 'Enraged').")]
        public string requiredPlayerStatus = "";

        [Header("═══ Eventos ═══")]
        [Tooltip("Eventos que se disparan justo al ejecutar este ataque especial (Ej. Cambiar de cámara, slow motion).")]
        public UnityEvent onSpecialFired;
    }

    [Header("═══ Arsenal Normal (Drag & Drop) ═══")]
    public List<RuntimeSkill> normalSkills = new List<RuntimeSkill>();

    [Header("═══ Arsenal Especial (Drag & Drop) ═══")]
    public List<SpecialRuntimeSkill> specialSkills = new List<SpecialRuntimeSkill>();

    [Header("═══ Memoria Táctica ═══")]
    [Tooltip("Cantidad de ataques que el jugador y los enemigos recuerdan para calcular la tendencia.")]
    public int memorySize = 5;

    [Header("═══ Debug (Solo Lectura) ═══")]
    [SerializeField] private List<ClashCategory> attackHistory = new List<ClashCategory>();
    [SerializeField] private ClashCategory currentDominantTendency = ClashCategory.Strike;
    [SerializeField] private bool isExecutingClash = false;
    [SerializeField] private PlayerClashSkill activeSkillAsset;
    [SerializeField] private ClashCardTemplate activeRuntimeCard;

    private Animator animator;
    private PlayerStatusEffectManager myPsem;
    private Coroutine clashRoutine;
    private GameObject activeFx;
    private int clashExecutionId = 0;

    void Awake()
    {
        animator = GetComponent<Animator>();
        myPsem = GetComponent<PlayerStatusEffectManager>();
    }

    void OnDisable()
    {
        CancelCurrentClash();
    }

    public ClashCardTemplate GetActiveCard()
    {
        return activeRuntimeCard;
    }

    public PlayerClashSkill GetActiveSkill()
    {
        return activeSkillAsset;
    }

    void Update()
    {
        foreach (var skill in normalSkills)
        {
            if (skill.currentCooldown > 0f) skill.currentCooldown -= Time.deltaTime;
        }

        foreach (var skill in specialSkills)
        {
            if (skill.currentCooldown > 0f) skill.currentCooldown -= Time.deltaTime;
        }
    }

    /// <summary>
    /// Selecciona y ejecuta el mejor ataque de los assets asignados según el contexto actual.
    /// Mientras un skill está activo, los nuevos intentos se ignoran hasta que termine su duración.
    /// Al terminar no se ejecuta otro skill automáticamente: queda esperando una nueva orden.
    /// </summary>
    public void ExecuteClash(Transform enemyTarget)
    {
        if (isExecutingClash) return;

        RuntimeSkill chosenSkill = SelectSkill(enemyTarget);
        if (chosenSkill == null || chosenSkill.skillAsset == null || animator == null) return;

        chosenSkill.currentCooldown = chosenSkill.skillAsset.cooldown;
        PrepareRuntimeCard(chosenSkill.skillAsset);

        float skillDuration = GetSkillDuration(chosenSkill);

        clashExecutionId++;
        int thisExecutionId = clashExecutionId;

        clashRoutine = StartCoroutine(
            ClashRoutine(
                chosenSkill.skillAsset,
                skillDuration,
                thisExecutionId));

        RecordAttack(chosenSkill.skillAsset.clashCategory);
    }

    private float GetSkillDuration(RuntimeSkill runtimeSkill)
    {
        if (runtimeSkill == null || runtimeSkill.skillAsset == null)
            return 0f;

        if (runtimeSkill.duration > 0f)
            return runtimeSkill.duration;

        return Mathf.Max(0f, runtimeSkill.skillAsset.totalAttackTime);
    }

    private RuntimeSkill SelectSkill(Transform enemyTarget)
    {
        RuntimeSkill chosenSkill = null;

        // 1. Evaluar Arsenal Especial primero.
        if (enemyTarget != null)
        {
            EnemyStatusEffectManager invectorEsem =
                enemyTarget.GetComponent<EnemyStatusEffectManager>();

            ARPG_EnemyStatusEffectManager arpgEsem =
                enemyTarget.GetComponent<ARPG_EnemyStatusEffectManager>();

            foreach (var special in specialSkills)
            {
                if (special.skillAsset == null || special.currentCooldown > 0f)
                    continue;

                bool enemyConditionMet =
                    string.IsNullOrEmpty(special.requiredEnemyStatus);

                if (!enemyConditionMet)
                {
                    if (invectorEsem != null &&
                        invectorEsem.HasStatus(special.requiredEnemyStatus))
                    {
                        enemyConditionMet = true;
                    }

                    if (arpgEsem != null &&
                        arpgEsem.HasStatus(special.requiredEnemyStatus))
                    {
                        enemyConditionMet = true;
                    }
                }

                bool playerConditionMet =
                    string.IsNullOrEmpty(special.requiredPlayerStatus);

                if (!playerConditionMet && myPsem != null &&
                    myPsem.HasStatus(special.requiredPlayerStatus))
                {
                    playerConditionMet = true;
                }

                if (enemyConditionMet &&
                    playerConditionMet &&
                    (!string.IsNullOrEmpty(special.requiredEnemyStatus) ||
                     !string.IsNullOrEmpty(special.requiredPlayerStatus)))
                {
                    chosenSkill = special;
                    special.onSpecialFired?.Invoke();
                    break;
                }
            }
        }

        // 2. Si no salió una especial, escoger una normal disponible.
        if (chosenSkill == null)
        {
            List<RuntimeSkill> availableNormal =
                new List<RuntimeSkill>();

            foreach (var normal in normalSkills)
            {
                if (normal.skillAsset != null &&
                    normal.currentCooldown <= 0f)
                {
                    availableNormal.Add(normal);
                }
            }

            if (availableNormal.Count > 0)
            {
                chosenSkill =
                    availableNormal[Random.Range(0, availableNormal.Count)];
            }
        }

        return chosenSkill;
    }

    private void PrepareRuntimeCard(PlayerClashSkill skillAsset)
    {
        DestroyRuntimeCard();

        activeSkillAsset = skillAsset;
        if (skillAsset == null) return;

        activeRuntimeCard =
            ScriptableObject.CreateInstance<ClashCardTemplate>();

        activeRuntimeCard.clashCategory = skillAsset.clashCategory;
        activeRuntimeCard.powerStat = skillAsset.powerStat;
        activeRuntimeCard.animatorStateName = skillAsset.animatorStateName;
        activeRuntimeCard.statusToApplyToLoser =
            skillAsset.statusToApplyToLoser;
        activeRuntimeCard.triggerVTGAOnWin = skillAsset.triggerVTGAOnWin;
        activeRuntimeCard.vtgaActionTag = skillAsset.vtgaActionTag;
    }

    private void DestroyRuntimeCard()
    {
        activeSkillAsset = null;

        if (activeRuntimeCard != null)
        {
            Destroy(activeRuntimeCard);
            activeRuntimeCard = null;
        }
    }

    private IEnumerator ClashRoutine(
        PlayerClashSkill skillAsset,
        float skillDuration,
        int executionId)
    {
        isExecutingClash = true;

        animator.SetBool("IsAttacking", true);
        animator.ResetTrigger("WeakAttack");
        animator.ResetTrigger("StrongAttack");

        if (skillAsset.fxPrefab != null)
        {
            activeFx = Instantiate(
                skillAsset.fxPrefab,
                transform.position,
                transform.rotation,
                transform);
        }

        if (skillAsset.anticipationTime > 0f)
        {
            if (!string.IsNullOrEmpty(skillAsset.anticipationStateName))
            {
                animator.CrossFadeInFixedTime(
                    skillAsset.anticipationStateName,
                    0.15f);
            }

            yield return new WaitForSeconds(skillAsset.anticipationTime);

            if (executionId != clashExecutionId)
                yield break;
        }

        if (skillAsset.disableFxAfterAnticipation)
        {
            DestroyActiveFx();
        }

        animator.CrossFadeInFixedTime(
            skillAsset.animatorStateName,
            skillAsset.crossfade);

        float timer = skillDuration;

        while (timer > 0f)
        {
            if (executionId != clashExecutionId)
                yield break;

            timer -= Time.deltaTime;
            yield return null;
        }

        FinishClashState(executionId);
    }

    private void FinishClashState(int executionId)
    {
        if (executionId != clashExecutionId) return;

        DestroyActiveFx();

        if (animator != null)
        {
            animator.speed = 1f;
            animator.SetBool("IsAttacking", false);
            animator.ResetTrigger("WeakAttack");
            animator.ResetTrigger("StrongAttack");
        }

        DestroyRuntimeCard();
        isExecutingClash = false;
        clashRoutine = null;
    }

    private void DestroyActiveFx()
    {
        if (activeFx == null) return;

        Destroy(activeFx);
        activeFx = null;
    }

    public void CancelCurrentClash()
    {
        clashExecutionId++;

        if (clashRoutine != null)
        {
            StopCoroutine(clashRoutine);
            clashRoutine = null;
        }

        DestroyActiveFx();

        if (animator != null)
        {
            animator.speed = 1f;
            animator.SetBool("IsAttacking", false);
            animator.ResetTrigger("WeakAttack");
            animator.ResetTrigger("StrongAttack");
        }

        DestroyRuntimeCard();
        isExecutingClash = false;
    }

    public bool IsExecutingClash()
    {
        return isExecutingClash;
    }

    private void RecordAttack(ClashCategory cat)
    {
        attackHistory.Add(cat);

        if (attackHistory.Count > memorySize)
        {
            attackHistory.RemoveAt(0);
        }

        RecalculateTendency();
    }

    private void RecalculateTendency()
    {
        if (attackHistory.Count == 0) return;

        Dictionary<ClashCategory, int> counts =
            new Dictionary<ClashCategory, int>();

        foreach (var cat in attackHistory)
        {
            if (!counts.ContainsKey(cat))
                counts[cat] = 0;

            counts[cat]++;
        }

        int max = 0;

        foreach (var kvp in counts)
        {
            if (kvp.Value > max)
            {
                max = kvp.Value;
                currentDominantTendency = kvp.Key;
            }
        }
    }

    public ClashCategory GetDominantTendency()
    {
        return currentDominantTendency;
    }
}