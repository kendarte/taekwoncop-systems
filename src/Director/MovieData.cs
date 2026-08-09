using UnityEngine;
using AC;
using Invector.vCharacterController.vActions;
using System.Collections.Generic;

public enum ActionType { MoveTo, Talk, Animation, Interaction, CameraShot, SpawnFX, Wait, Emote }

// --- NUEVO ENUM: VELOCIDADES ---
public enum MovementSpeed { Walk, Run, Sprint }

[System.Serializable]
public class MovieClip
{
    public string actionName = "Action";
    public ActionType type;

    [Header("TIEMPOS")]
    public float startDelay = 0f;
    public float duration = 0f;

    [Header("Actuación")]
    public AnimationClip animationClip;
    public string animationState;
    public int animationLayer = 0;
    public AnimationClip idleAfterAnim;

    [Header("Movimiento")]
    public Vector3 targetPosition;

    // 1. Variable para guardar la rotación del fantasma (Respaldo)
    public Vector3 targetRotation;

    // 2. Objeto físico para mirar (Prioridad)
    [Tooltip("Arrastra un objeto aquí. Al llegar, el personaje girará para mirarlo.")]
    public Transform lookAtTarget;

    // 3. SELECCIÓN DE VELOCIDAD (Reemplaza a 'run')
    public MovementSpeed movementType = MovementSpeed.Walk;

    public bool waitToFinish = true;
    public bool useAnimMove = true;
    public bool lockRotation = false;
    public float animVertical = 1.0f;
    public float animHorizontal = 0f;

    [Header("Audio / Dialogo")]
    public AudioClip dialogueAudio;

    // FX
    public GameObject fxPrefab;
    public HumanBodyBones fxBone = HumanBodyBones.RightHand;
    public Transform customTransform;
    public vTriggerGenericAction triggerObject;

    [Header("Emoter")]
    public string emoteName;
}

[System.Serializable]
public class MovieTrack
{
    public string trackName;
    public MovieActor actor;

    public List<MovieClip> moveClips = new List<MovieClip>();
    public List<MovieClip> animClips = new List<MovieClip>();
    public List<MovieClip> audioClips = new List<MovieClip>();
    public List<MovieClip> fxClips = new List<MovieClip>();
    public List<MovieClip> emoteClips = new List<MovieClip>();

    public List<MovieClip> clips = new List<MovieClip>();
}