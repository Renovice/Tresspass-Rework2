using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace VoidBlinkHoldToFloat
{
    [BepInPlugin("com.yourname.voidblinkhold", "Void Blink Hold to Float", "1.0.0")]
    public class Main : BaseUnityPlugin
    {
        // Hardcoded values from VoidBlinkDown (for tap behavior)
        private const float DOWN_DURATION = 0.5f;
        private const float DOWN_SPEED_COEFFICIENT = 2f;
        private static readonly AnimationCurve DOWN_UP_SPEED;
        private static readonly AnimationCurve DOWN_FORWARD_SPEED;

        // Hardcoded values from VoidBlinkUp (for hold behavior)
        private const float UP_DURATION = 1.0f;
        private const float UP_SPEED_COEFFICIENT = 2.2f;
        private static readonly AnimationCurve UP_UP_SPEED;
        private static readonly AnimationCurve UP_FORWARD_SPEED;

        // Timing settings
        private const float INITIAL_DASH_TIME = 0.25f; // Fixed time before mode determination
        private const float TRANSITION_DURATION = 0.3f; // Smooth transition time
        private static bool isHoldingMode = false;
        private static bool modeDetermined = false;
        private static float transitionStartTime = 0f;
        private static Vector3 transitionStartVelocity = Vector3.zero;

        static Main()
        {
            // Build curves for VoidBlinkDown (aggressive hop)
            DOWN_UP_SPEED = new AnimationCurve(
                new Keyframe(0.0f, 1.0f, -2.3116774559020998f, -2.3116774559020998f),
                new Keyframe(1.0f, 0.0f, -0.22838349640369416f, -0.22838349640369416f)
            );
            DOWN_UP_SPEED.preWrapMode = WrapMode.ClampForever;

            DOWN_FORWARD_SPEED = new AnimationCurve(
                new Keyframe(0.0f, 0.0f, 3.399319887161255f, 3.399319887161255f),
                new Keyframe(1.0f, 2.0f, 0.0f, 0.0f)
            );
            DOWN_FORWARD_SPEED.preWrapMode = WrapMode.ClampForever;

            // Build curves for VoidBlinkUp (smooth float)
            UP_UP_SPEED = new AnimationCurve(
                new Keyframe(0.0f, 0.0f, 1.0f, 1.0f),
                new Keyframe(1.0f, 1.0f, 1.0f, 1.0f)
            );
            UP_UP_SPEED.preWrapMode = WrapMode.ClampForever;

            UP_FORWARD_SPEED = new AnimationCurve(
                new Keyframe(0.0f, 1.0f, -1.0f, -1.0f),
                new Keyframe(1.0f, 0.0f, -1.0f, -1.0f)
            );
            UP_FORWARD_SPEED.preWrapMode = WrapMode.ClampForever;
        }

        public void Awake()
        {
            new Harmony("com.yourname.voidblinkhold").PatchAll();
        }

        private static bool IsShiftKeyHeld()
        {
            return Input.GetKey(KeyCode.LeftShift);
        }

        [HarmonyPatch(typeof(EntityStates.VoidSurvivor.VoidBlinkBase), "OnEnter")]
        class OnEnterPatch
        {
            static void Postfix(EntityStates.VoidSurvivor.VoidBlinkBase __instance)
            {
                // Reset state for new blink
                isHoldingMode = false;
                modeDetermined = false;
                transitionStartTime = 0f;
                transitionStartVelocity = Vector3.zero;

                // Start with tap mode by default
                Traverse.Create(__instance).Field("duration").SetValue(DOWN_DURATION);
            }
        }

        [HarmonyPatch(typeof(EntityStates.VoidSurvivor.VoidBlinkBase), "GetVelocity")]
        class GetVelocityPatch
        {
            static bool Prefix(EntityStates.VoidSurvivor.VoidBlinkBase __instance, ref Vector3 __result)
            {
                // Use fixed timing for mode determination - always dash for first 0.25s
                if (!modeDetermined && __instance.fixedAge >= INITIAL_DASH_TIME)
                {
                    // After initial dash time, check if shift is still held to determine mode
                    isHoldingMode = IsShiftKeyHeld();
                    modeDetermined = true;

                    if (isHoldingMode)
                    {
                        transitionStartTime = __instance.fixedAge;
                        transitionStartVelocity = __instance.characterMotor?.velocity ?? Vector3.zero;
                        Traverse.Create(__instance).Field("duration").SetValue(UP_DURATION);
                    }
                }

                // Calculate base velocity based on the determined mode
                float targetDuration = isHoldingMode ? UP_DURATION : DOWN_DURATION;
                AnimationCurve targetUpSpeed = isHoldingMode ? UP_UP_SPEED : DOWN_UP_SPEED;
                AnimationCurve targetForwardSpeed = isHoldingMode ? UP_FORWARD_SPEED : DOWN_FORWARD_SPEED;
                float targetSpeedCoefficient = isHoldingMode ? UP_SPEED_COEFFICIENT : DOWN_SPEED_COEFFICIENT;

                float time = Mathf.Clamp01(__instance.fixedAge / targetDuration);
                Vector3 targetVelocity = (targetForwardSpeed.Evaluate(time) * __instance.forwardVector +
                                         targetUpSpeed.Evaluate(time) * Vector3.up) *
                                         targetSpeedCoefficient * __instance.moveSpeedStat;

                // If we're transitioning to hold mode, create a smooth U-shaped arc
                if (isHoldingMode && __instance.fixedAge < transitionStartTime + TRANSITION_DURATION)
                {
                    float transitionProgress = Mathf.Clamp01((__instance.fixedAge - transitionStartTime) / TRANSITION_DURATION);

                    // Use a quadratic ease-out for smoother upward curve
                    float upwardBlend = Mathf.SmoothStep(0f, 1f, transitionProgress);
                    float forwardBlend = Mathf.Lerp(1f, 0.8f, transitionProgress); // Slightly reduce forward speed

                    // Get the current dash velocity components
                    float currentForwardSpeed = Vector3.Dot(transitionStartVelocity, __instance.forwardVector);
                    float currentUpwardSpeed = transitionStartVelocity.y;

                    // Create a U-shaped transition: maintain forward momentum while gradually adding upward lift
                    Vector3 forwardComponent = __instance.forwardVector * Mathf.Lerp(currentForwardSpeed, targetVelocity.magnitude * forwardBlend, upwardBlend);
                    Vector3 upwardComponent = Vector3.up * Mathf.Lerp(currentUpwardSpeed, targetVelocity.y, upwardBlend * upwardBlend); // Quadratic for smoother curve

                    __result = forwardComponent + upwardComponent;
                }
                else
                {
                    __result = targetVelocity;
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(EntityStates.VoidSurvivor.VoidBlinkBase), "FixedUpdate")]
        class FixedUpdatePatch
        {
            static void Postfix(EntityStates.VoidSurvivor.VoidBlinkBase __instance)
            {
                bool shiftHeld = IsShiftKeyHeld();

                // If we're in hold mode but shift is released, cancel the ability
                if (__instance.isAuthority && modeDetermined && isHoldingMode && !shiftHeld && __instance.fixedAge > 0.1f)
                {
                    Traverse.Create(__instance).Field("duration").SetValue(__instance.fixedAge);
                }
            }
        }
    }
}