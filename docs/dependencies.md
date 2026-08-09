# Dependencies and integration boundaries

This code comes from a larger Unity prototype. The following packages and project configuration are required to reproduce the original behavior.

| Dependency | Used by | Role |
| --- | --- | --- |
| Unity Engine | All source files | Components, coroutines, animation, audio, transforms, and serialization |
| Unity AI Navigation | `MovieActor.cs` | `NavMeshAgent` actor movement |
| Adventure Creator | `SceneDirector.cs`, `CameraSequencer.cs`, `CinemaSmartRig.cs` | Cutscene state, camera switching, and time scaling |
| Invector Third Person Controller | `MovieData.cs` | Generic interaction trigger reference |
| SALSA LipSync Suite | `MovieActor.cs` | Speech-driven facial animation and emotes |
| Post Processing Stack v2 | `CinemaSmartRig.cs` | Depth of field and chromatic aberration |

## Not included

- Commercial package source or binaries
- Unity scenes, prefabs, ScriptableObjects, and project settings
- Art, animation, audio, dialogue, and VFX assets
- The complete game or a playable build
- Custom editor tooling from the original project

## Integration notes

- `SceneDirector` expects Adventure Creator's `KickStarter` services to exist in the scene.
- `CameraSequencer` and `CinemaSmartRig` expect an Adventure Creator `_Camera` on the cinematic rig.
- `CinemaSmartRig` expects a configured Post Processing v2 volume when focus or optical-stress effects are used.
- `MovieActor` expects a compatible Animator controller and may use `NavMeshAgent`, `Salsa`, `Emoter`, and `AudioSource` components.
- `MovieData` stores an Invector `vTriggerGenericAction` reference for project interaction clips.

The third-party packages remain subject to their own licenses. No third-party implementation is copied into this repository.
