using UnityEngine;
using System.Collections.Generic;

// ENUMS
public enum CineTransitionType { Cut, FadeToColor, Flash }
public enum CineShotPreset { Empty, CloseUp, MediumShot, CowboyShot, FullShot, LowAngle, HighAngle, Overhead, OverTheShoulderLeft, OverTheShoulderRight, EstablishingShot, DutchAngle }
public enum CineMotionType { Static, Pan, DollyIn, DollyOut, Orbit, CraneUp, CraneDown, Follow }
public enum ChronosPreset { Custom, MatrixStop, SnyderPunch, SpeedRampUp, SpeedRampDown, ChaosTwitch }
public enum FocusMode { AutoTarget, FixedDistance, ManualCurve }
public enum ImpactPreset { Custom, HandheldIdle, LightHit, HeavyPunch, ShotgunKick, Explosion, Heartbeat }
public enum MasterShotType { Custom, Action_SnyderFinisher, Action_MatrixRun, Action_MichaelBayOrbit, Action_Shellshock, Talk_RPG_OverShoulder, Talk_Tense_Handheld, Talk_Drama_PushIn, Talk_WalkAndTalk_Side, Drama_TheRevelation }

[System.Serializable]
public class CameraClip
{
    public string shotName = "Toma FX";
    [Header("🎬 PRESET MAESTRO")]
    public MasterShotType masterPreset = MasterShotType.Custom;

    [Header("TIEMPOS")]
    public float waitTime = 0f;
    public float shotDuration = 3f;

    [Header("TRANSICIÓN")]
    public CineTransitionType transition = CineTransitionType.Cut;
    public Color effectColor = Color.black;
    [Range(0.1f, 5f)] public float transitionDuration = 0.5f;

    // --- MÓDULO 4: AUDIO (NUEVO) ---
    [Header("SONIDO (AUDIO FX)")]
    [Tooltip("Sonido que suena al INICIO de la toma (Impacto, Swoosh, etc).")]
    public AudioClip shotSFX;
    [Range(0f, 1f)] public float sfxVolume = 1.0f;
    [Tooltip("Si TRUE, el Pitch del audio global bajará junto con la cámara lenta.")]
    public bool warpAudioPitch = true;

    [Header("ÓPTICA: ENFOQUE")]
    public FocusMode focusMode = FocusMode.AutoTarget;
    public float focusDistance = 3.0f;
    [Range(0.1f, 32f)] public float aperture = 5.6f;
    public AnimationCurve focusCurve = new AnimationCurve(new Keyframe(0, 3), new Keyframe(1, 3));

    [Header("ÓPTICA: LENTE")]
    [Range(10, 150)] public float startFOV = 60f;
    [Range(10, 150)] public float endFOV = 60f;
    [Range(-180, 180)] public float dutchTilt = 0f;

    [Header("FÍSICA: RECOIL")]
    public bool useImpulse = false;
    public Vector3 impulseDirection = new Vector3(0, 0, -1);
    public float impulseForce = 10f;
    public float impulseStiffness = 120f;
    public float impulseDamping = 15f;
    public float punchZoomAmount = 0f;

    [Header("FX: TRAUMA & STRESS")]
    [Range(0f, 1f)] public float traumaImpact = 0f;
    public float traumaDecay = 1.5f;
    public bool useOpticalStress = true;
    [Range(0f, 2f)] public float idleSwayAmount = 0.5f;
    public float idleSwaySpeed = 1.0f;

    [Header("CHRONOS")]
    public bool enableChronos = false;
    public AnimationCurve timeCurve = new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 1));
    public bool syncPhysics = true;

    [HideInInspector] public ChronosPreset chronosPreset = ChronosPreset.Custom;
    [HideInInspector] public ImpactPreset impactPreset = ImpactPreset.Custom;

    [Header("NAVEGACIÓN")]
    public Transform target;
    public List<Transform> waypoints = new List<Transform>();
    public bool bucleRiel = false;

    [HideInInspector] public CineShotPreset presetToGenerate = CineShotPreset.MediumShot;
    [HideInInspector] public CineMotionType motionToGenerate = CineMotionType.Static;
}