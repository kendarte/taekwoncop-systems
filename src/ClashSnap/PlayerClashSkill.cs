using UnityEngine;
using MalbersAnimations.Cards;

[CreateAssetMenu(fileName = "NewPlayerClashSkill", menuName = "CLASH/Player Clash Skill")]
public class PlayerClashSkill : ScriptableObject
{
    [Header("═══ Cooldown Base ═══")]
    public float cooldown = 5f;

    [Header("═══ FX y Tiempos ═══")]
    [Tooltip("Prefab del efecto visual. Al ser un Asset, el Manager lo instanciará sobre el jugador.")]
    public GameObject fxPrefab;

    [Tooltip("Nombre del hueso o transform donde aparecerá el FX (ej. 'RightHand', 'Spine'). Déjelo vacío para aparecer en la base (root) del personaje.")]
    public string fxSpawnSocketName = "";

    public float anticipationTime = 0.5f;
    public string anticipationStateName = "";
    public bool disableFxAfterAnticipation = true;
    public float totalAttackTime = 2f;

    [Header("═══ Identidad del Choque ═══")]
    public ClashCategory clashCategory = ClashCategory.Strike;
    public int powerStat = 10;

    [Header("═══ Ejecución Física ═══")]
    public string animatorStateName = "Attack_Strike_01";
    public float crossfade = 0.1f;

    [Header("═══ Consecuencias al Ganar ═══")]
    public string statusToApplyToLoser = "Stagger";
    public bool triggerVTGAOnWin = false;
    public string vtgaActionTag = "Action";


    [Header("═══ Power Attack contra Defender ═══")]
    [Tooltip("Marca esta Skill como Power Attack. Solo tiene efecto especial cuando realmente gana o alcanza a un Defender activo.")]
    public bool isPowerAttack = false;

    [Tooltip("Si está activo, el Power Attack termina completamente el ClashDefender del enemigo.")]
    public bool breakActiveDefender = true;

    [Tooltip("Estado del ESEM que se fuerza después de terminar por completo la defensa. Debe existir con el mismo nombre en possibleEffects.")]
    public string forcedStatusOnDefender = "GuardBreak";

    // ════════════════════════════════════════════════════════════════
    // LOGICA DE SPAWN DIRECTAMENTE EN EL SCRIPTABLE OBJECT
    // ════════════════════════════════════════════════════════════════
    public GameObject SpawnFX(Transform playerRoot)
    {
        if (fxPrefab == null || playerRoot == null) return null;

        Transform spawnPoint = playerRoot; // Por defecto lo tira en la raíz del jugador

        // Si usted definió un nombre de socket, lo buscamos en todos los hijos/huesos
        if (!string.IsNullOrEmpty(fxSpawnSocketName))
        {
            Transform[] allChildren = playerRoot.GetComponentsInChildren<Transform>(true);
            foreach (Transform child in allChildren)
            {
                if (child.name == fxSpawnSocketName)
                {
                    spawnPoint = child;
                    break;
                }
            }
        }

        // Instanciar el prefab en la posición y rotación del socket encontrado
        GameObject spawnedFX = Instantiate(fxPrefab, spawnPoint.position, spawnPoint.rotation);

        // Emparentar el FX al hueso para que no se quede botado en el aire si el personaje se mueve
        spawnedFX.transform.SetParent(spawnPoint);

        return spawnedFX;
    }
}