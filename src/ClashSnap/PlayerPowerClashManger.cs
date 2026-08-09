using UnityEngine;

/// <summary>
/// Manager exclusivo para Power Clash.
/// Hereda todo el comportamiento de PlayerClashManager para mantener
/// compatibilidad con IClashCaster, AuraClashCollider y PlayerClashSkill.
/// Mantiene su propio arsenal, cooldowns y runtime porque es una instancia
/// independiente añadida al Player.
/// </summary>
[DisallowMultipleComponent]
public class PlayerPowerClashManger : PlayerClashManager
{
}
