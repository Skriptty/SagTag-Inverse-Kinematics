using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using GameNetcodeStuff;
using UnityEngine;

[BepInPlugin("com.sagtags.lethalcompany.IKFootsteps", "SagTags Inverse Kinematics Footsteps", "0.0.1")]
public class FootIKPlugin : BaseUnityPlugin
{
    internal static ManualLogSource Log;
    private readonly Harmony harmony = new Harmony("com.sagtags.lethalcompany.IKFootsteps");

    private void Awake()
    {
        Log = base.Logger;
        harmony.PatchAll();
        Log.LogInfo("SagTags IK Footsteps initialized properly");
    }
}

[HarmonyPatch(typeof(PlayerControllerB), "Awake")]
public class PlayerControllerB_AwakePatch
{
    static void Postfix(PlayerControllerB __instance)
    {
        Animator anim = __instance.playerBodyAnimator;
        if (anim == null) return;

        if (anim.GetComponent<FootIKController>() == null)
            anim.gameObject.AddComponent<FootIKController>();
    }
}