using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace VoidBlinkUpUsesDownValues
{
    [BepInPlugin("com.yourname.voidblinkupusesdown", "Void Blink Up Uses Down Values", "1.0.0")]
    public class Main : BaseUnityPlugin
    {
        // Hardcoded values from VoidBlinkDown
        private const float TARGET_DURATION = 0.5f;
        private const float TARGET_SPEED_COEFFICIENT = 2f;
        private static readonly AnimationCurve TARGET_UP_SPEED;
        private static readonly AnimationCurve TARGET_FORWARD_SPEED;

        static Main()
        {
            // Build the hardcoded "upSpeed" curve from the provided JSON string
            TARGET_UP_SPEED = new AnimationCurve(
                new Keyframe(0.0f, 1.0f, -2.3116774559020998f, -2.3116774559020998f),
                new Keyframe(1.0f, 0.0f, -0.22838349640369416f, -0.22838349640369416f)
            );
            TARGET_UP_SPEED.preWrapMode = WrapMode.ClampForever;
            TARGET_UP_SPEED.postWrapMode = WrapMode.ClampForever;

            // Build the hardcoded "forwardSpeed" curve from the provided JSON string
            TARGET_FORWARD_SPEED = new AnimationCurve(
                new Keyframe(0.0f, 0.0f, 3.399319887161255f, 3.399319887161255f),
                new Keyframe(1.0f, 2.0f, 0.0f, 0.0f)
            );
            TARGET_FORWARD_SPEED.preWrapMode = WrapMode.ClampForever;
            TARGET_FORWARD_SPEED.postWrapMode = WrapMode.ClampForever;
        }

        public void Awake()
        {
            new Harmony("com.yourname.voidblinkupusesdown").PatchAll();
        }

        [HarmonyPatch(typeof(EntityStates.VoidSurvivor.VoidBlinkBase), "GetVelocity")]
        class GetVelocityPatch
        {
            static bool Prefix(EntityStates.VoidSurvivor.VoidBlinkBase __instance, ref Vector3 __result)
            {
                if (__instance is EntityStates.VoidSurvivor.VoidBlinkBase.VoidBlinkUp)
                {
                    float time = __instance.fixedAge / TARGET_DURATION;
                    Vector3 horizontal = TARGET_FORWARD_SPEED.Evaluate(time) * __instance.forwardVector;
                    Vector3 vertical = TARGET_UP_SPEED.Evaluate(time) * Vector3.up;
                    __result = (horizontal + vertical) * TARGET_SPEED_COEFFICIENT * __instance.moveSpeedStat;
                    return false;
                }
                return true;
            }
        }

        // NEW PATCH: Fix the duration check in FixedUpdate to also use the target duration
        [HarmonyPatch(typeof(EntityStates.VoidSurvivor.VoidBlinkBase), "FixedUpdate")]
        class FixedUpdatePatch
        {
            static void Postfix(EntityStates.VoidSurvivor.VoidBlinkBase __instance)
            {
                // Only override for VoidBlinkUp instances
                if (__instance is EntityStates.VoidSurvivor.VoidBlinkBase.VoidBlinkUp)
                {
                    // Check if the time spent in the state is greater than our TARGET_DURATION
                    if (__instance.fixedAge >= TARGET_DURATION && __instance.isAuthority)
                    {
                        // If it is, tell the state machine to exit immediately
                        __instance.outer.SetNextStateToMain();
                    }
                }
            }
        }
    }
}