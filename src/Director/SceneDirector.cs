using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using AC;

public class SceneDirector : MonoBehaviour
{
    public List<MovieTrack> tracks = new List<MovieTrack>();
    public bool playOnStart = false;
    public bool loop = false;

    private void Start() { if (playOnStart) PlayScene(); }

    [ContextMenu("Play Action!")]
    public void PlayScene() { StartCoroutine(DirectorRoutine()); }

    private IEnumerator DirectorRoutine()
    {
        if (KickStarter.stateHandler) KickStarter.stateHandler.EnforceCutsceneMode = true;

        foreach (var t in tracks) if (t.actor) t.actor.SetDirectorControl(true);

        List<Coroutine> allCoroutines = new List<Coroutine>();

        foreach (var t in tracks)
        {
            if (t.moveClips.Count > 0) allCoroutines.Add(StartCoroutine(RunClipList(t, t.moveClips)));
            if (t.animClips.Count > 0) allCoroutines.Add(StartCoroutine(RunClipList(t, t.animClips)));
            if (t.audioClips.Count > 0) allCoroutines.Add(StartCoroutine(RunClipList(t, t.audioClips)));
            if (t.fxClips.Count > 0) allCoroutines.Add(StartCoroutine(RunClipList(t, t.fxClips)));
            if (t.emoteClips.Count > 0) allCoroutines.Add(StartCoroutine(RunClipList(t, t.emoteClips)));
        }

        foreach (var c in allCoroutines) yield return c;

        foreach (var t in tracks) if (t.actor) t.actor.SetDirectorControl(false);
        if (KickStarter.stateHandler) KickStarter.stateHandler.EnforceCutsceneMode = false;
    }

    private IEnumerator RunClipList(MovieTrack track, List<MovieClip> clipsToRun)
    {
        MovieActor actor = track.actor;

        for (int i = 0; i < clipsToRun.Count; i++)
        {
            var clip = clipsToRun[i];

            if (clip.startDelay > 0) yield return new WaitForSeconds(clip.startDelay);

            float actionDuration = 0f;

            switch (clip.type)
            {
                case ActionType.MoveTo:
                    // PASAMOS TODOS LOS PARÁMETROS NUEVOS
                    if (actor) actor.Cmd_Move(clip.targetPosition, clip.targetRotation, clip.lookAtTarget, clip.movementType, clip.animVertical, clip.animHorizontal, clip.useAnimMove, clip.lockRotation);
                    break;

                case ActionType.Animation:
                    if (actor)
                    {
                        string state = clip.animationClip ? clip.animationClip.name : clip.animationState;
                        actor.Cmd_Anim(clip.animationClip, state, clip.animationLayer);
                    }
                    break;

                case ActionType.Talk:
                    if (actor && clip.dialogueAudio) actionDuration = actor.Cmd_Speak(clip.dialogueAudio);
                    else if (clip.dialogueAudio) actionDuration = clip.dialogueAudio.length;
                    break;

                case ActionType.SpawnFX:
                    if (actor) actor.Cmd_VFX(clip.fxPrefab, clip.fxBone, clip.customTransform, clip.duration);
                    else if (clip.fxPrefab)
                    {
                        Vector3 pos = clip.customTransform ? clip.customTransform.position : transform.position;
                        GameObject g = Instantiate(clip.fxPrefab, pos, Quaternion.identity);
                        if (clip.duration > 0) Destroy(g, clip.duration);
                    }
                    break;

                case ActionType.Emote:
                    if (actor) actor.Cmd_Emote(clip.emoteName, clip.duration);
                    break;
            }

            if (clip.type == ActionType.MoveTo && clip.waitToFinish && actor)
            {
                yield return null;
                yield return new WaitUntil(() => actor.CheckArrived());
            }
            else if (clip.duration > 0 && clip.type != ActionType.SpawnFX)
            {
                yield return new WaitForSeconds(clip.duration);
            }
            else if (clip.waitToFinish && actionDuration > 0)
            {
                yield return new WaitForSeconds(actionDuration);
            }

            if (clip.type == ActionType.Animation && actor)
            {
                if (clip.idleAfterAnim != null) actor.Cmd_Anim(clip.idleAfterAnim, clip.idleAfterAnim.name, clip.animationLayer);
                else actor.Cmd_Anim(null, actor.locomotionState, clip.animationLayer);
            }
        }
    }
}