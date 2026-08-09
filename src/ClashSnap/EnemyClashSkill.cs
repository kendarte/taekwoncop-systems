using UnityEngine;
using MalbersAnimations.Cards;

[CreateAssetMenu(fileName = "NewEnemyClashSkill", menuName = "CLASH/Enemy Clash Skill")]
public class EnemyClashSkill : ScriptableObject
{
    [Header("═══ Cooldown Base ═══")]
    public float cooldown = 5f;
    // NOTA: El cooldown actual en tiempo real ahora lo maneja el Cerebro, no el Asset.

    [Header("═══ Control del Aura (Elija una opción) ═══")]
    [Tooltip("OPCIÓN A: Ponga el PREFAB del aura aquí. El cerebro lo instanciará y destruirá.")]
    public GameObject fxPrefab;

    [Tooltip("OPCIÓN B: Si el aura YA ESTÁ en los huesos del modelo, escriba el NOMBRE EXACTO aquí. El cerebro la buscará y la encenderá.")]
    public string auraNameInModel = "";

    [Header("═══ Tiempos de Animación ═══")]
    [Tooltip("Tiempo con el FX encendido ANTES de que inicie el movimiento del ataque.")]
    public float anticipationTime = 0.5f;

    [Tooltip("Nombre del estado (pose de carga). Déjelo vacío si no quiere usar ninguno.")]
    public string anticipationStateName = "";

    [Tooltip("Si es TRUE, el FX se apagará apenas termine la anticipación (antes de golpear).")]
    public bool disableFxAfterAnticipation = true;

    [Tooltip("Tiempo total que dura el ataque DESPUÉS de la anticipación.")]
    public float totalAttackTime = 2f;

    [Header("═══ Identidad del Choque ═══")]
    public ClashCategory clashCategory = ClashCategory.Strike;
    public int powerStat = 10;

    [Header("═══ Ejecución Física ═══")]
    public string animatorStateName = "Attack_Strike_01";

    [Header("═══ Snap de Rotación hacia el Player ═══")]
    [Tooltip("Activa la alineación horizontal de este ataque hacia el player. No mueve al enemigo.")]
    public bool useRotationSnap = true;

    [Tooltip("Rota hacia el player durante el tiempo de anticipación del ataque.")]
    public bool rotateDuringAnticipation = true;

    [Tooltip("Velocidad de giro durante la anticipación, en grados por segundo.")]
    [Min(0f)]
    public float anticipationRotationSpeed = 720f;

    [Tooltip("Ángulo máximo inicial desde el frente en el que este ataque puede usar snap. 180 permite girar hacia cualquier dirección.")]
    [Range(0f, 180f)]
    public float maximumSnapAngle = 180f;

    [Tooltip("Al comenzar el golpe, corrige de inmediato cualquier ángulo restante hacia el player.")]
    public bool hardAlignAtAttackStart = true;

    [Tooltip("Permite continuar girando hacia el player durante el inicio de la animación de ataque.")]
    public bool trackTargetDuringAttack = false;

    [Tooltip("Segundos durante los que el ataque puede continuar siguiendo al player. 0 desactiva el seguimiento aunque Track Target esté activo.")]
    [Min(0f)]
    public float attackTrackingDuration = 0.15f;

    [Tooltip("Velocidad de giro durante el seguimiento del ataque, en grados por segundo.")]
    [Min(0f)]
    public float attackTrackingRotationSpeed = 360f;

    [Header("═══ Consecuencias al Ganar (Payoff) ═══")]
    public string statusToApplyToLoser = "Stagger";
    public bool triggerVTGAOnWin = false;
    public string vtgaActionTag = "Action";
}