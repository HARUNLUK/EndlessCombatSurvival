using UnityEngine;

/// <summary>
/// Animator parameter calls that do nothing when the Animator has no controller (2026-11-18: every animation
/// clip and controller was removed so the user can rebuild them for the Survivor body). Without a controller
/// Unity logs "Animator is not playing an AnimatorController" on every SetX call, every frame. With a
/// controller these behave exactly like the plain calls. A null animator still throws, as before.
/// </summary>
public static class AnimatorSafe
{
    public static bool HasController(this Animator a) => a.runtimeAnimatorController != null;

    public static void SetBoolSafe(this Animator a, string name, bool v) { if (a.HasController()) a.SetBool(name, v); }
    public static void SetBoolSafe(this Animator a, int id, bool v) { if (a.HasController()) a.SetBool(id, v); }
    public static void SetFloatSafe(this Animator a, string name, float v) { if (a.HasController()) a.SetFloat(name, v); }
    public static void SetFloatSafe(this Animator a, int id, float v) { if (a.HasController()) a.SetFloat(id, v); }
    public static void SetFloatSafe(this Animator a, int id, float v, float damp, float dt) { if (a.HasController()) a.SetFloat(id, v, damp, dt); }
    public static void SetIntegerSafe(this Animator a, string name, int v) { if (a.HasController()) a.SetInteger(name, v); }
    public static void SetIntegerSafe(this Animator a, int id, int v) { if (a.HasController()) a.SetInteger(id, v); }
    public static void SetTriggerSafe(this Animator a, string name) { if (a.HasController()) a.SetTrigger(name); }
    public static void SetTriggerSafe(this Animator a, int id) { if (a.HasController()) a.SetTrigger(id); }
    public static bool GetBoolSafe(this Animator a, string name) => a.HasController() && a.GetBool(name);
}
