# Dependencies and integration boundaries

This code comes from a larger Unity prototype. The following packages and project configuration are required to reproduce the original behavior.

| Dependency | Used by | Role |
| --- | --- | --- |
| Unity Engine | All source files | Components, coroutines, animation, audio, transforms, and serialization |
| Unity AI Navigation | `MovieActor.cs` | `NavMeshAgent` actor movement |
| Adventure Creator | `SceneDirector.cs`, `CameraSequencer.cs`, `CinemaSmartRig.cs` | Cutscene state, camera switching, and time scaling |
| Invector Third Person Controller | `MovieData.cs`, `vSnapToTarget.cs`, `vSnapAttackHook.cs`, `PlayerClashDefender.cs`, `PlayerStatusEffectManager.cs` | Interaction references, input, lock-on, locomotion, and control locking |
| SALSA LipSync Suite | `MovieActor.cs` | Speech-driven facial animation and emotes |
| Post Processing Stack v2 | `CinemaSmartRig.cs` | Depth of field and chromatic aberration |
| `MalbersAnimations.Cards` namespace | Clash Snap skill, manager, defender, and status files | Clash categories, cards, and caster contracts used by the original project |

## Not included

- Commercial package source or binaries
- Unity scenes, prefabs, ScriptableObjects, and project settings
- Art, animation, audio, dialogue, and VFX assets
- The complete game or a playable build
- Custom editor tooling from the original project
- Project-level combat contracts: `StatusC`, `BulletStatusC`, `ProjectileClashAttribute`, `UniversalClashManager`, `EnemyStatusEffectManager`, and `ARPG_EnemyStatusEffectManager`

## Integration notes

- `SceneDirector` expects Adventure Creator's `KickStarter` services to exist in the scene.
- `CameraSequencer` and `CinemaSmartRig` expect an Adventure Creator `_Camera` on the cinematic rig.
- `CinemaSmartRig` expects a configured Post Processing v2 volume when focus or optical-stress effects are used.
- `MovieActor` expects a compatible Animator controller and may use `NavMeshAgent`, `Salsa`, `Emoter`, and `AudioSource` components.
- `MovieData` stores an Invector `vTriggerGenericAction` reference for project interaction clips.
- `vSnapToTarget` expects Invector lock-on and third-person controller components, plus the project's `StatusC` enemy root/health component.
- `PlayerClashManager` creates a runtime `ClashCardTemplate` and queries the project's enemy status managers when evaluating special skills.
- `PlayerClashDefender` and `PlayerStatusEffectManager` collaborate with `ProjectileClashAttribute` and `UniversalClashManager` to resolve Strike, Breaker, and Defender outcomes.
- `PlayerPowerClashManger` preserves the spelling used by the original Unity component so existing serialized references remain valid.

The third-party packages remain subject to their own licenses. No third-party implementation is copied into this repository.
