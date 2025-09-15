using BepInEx;
using BepInEx.Configuration;
using EntityStates;
using EntityStates.VoidSurvivor;
using RiskOfOptions;
using RiskOfOptions.OptionConfigs;
using RiskOfOptions.Options;
using RoR2;
using System;
using UnityEngine;

namespace VoidBlinkHoldToFloat
{
    [BepInPlugin("com.yourname.voidblinkhold", "TresspassTweaks", "1.0.0")]
    [BepInDependency("com.rune580.riskofoptions")]
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
        private const float INITIAL_DASH_TIME = 0.25f;
        private const float TRANSITION_DURATION = 0.3f;

        // Configuration
        public static ConfigEntry<bool> DisableCancellation;
        public static ConfigEntry<KeyboardShortcut> HoldKey;

        // State tracking dictionaries
        private static System.Collections.Generic.Dictionary<UnityEngine.Networking.NetworkInstanceId, BlinkStateData> stateData =
            new System.Collections.Generic.Dictionary<UnityEngine.Networking.NetworkInstanceId, BlinkStateData>();

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
            // Create configuration
            HoldKey = Config.Bind("Keybinds", "Hold To Float Key", new KeyboardShortcut(KeyCode.LeftShift),
                "The key you must hold to activate the 'float' portion of the blink.");

            DisableCancellation = Config.Bind("Tweaks", "Disable Cancellation", false,
                "If enabled, the ability cannot be cancelled by releasing the hold key - it will always complete its full duration");

            // Set up Risk of Options
            SetupRiskOfOptions();

            // Hook into the game's update loop
            On.RoR2.CharacterBody.FixedUpdate += CharacterBody_FixedUpdate;

            // Replace the VoidBlinkBase state with our custom implementation
            On.EntityStates.VoidSurvivor.VoidBlinkBase.OnEnter += VoidBlinkBase_OnEnter;
            On.EntityStates.VoidSurvivor.VoidBlinkBase.GetVelocity += VoidBlinkBase_GetVelocity;
            On.EntityStates.VoidSurvivor.VoidBlinkBase.FixedUpdate += VoidBlinkBase_FixedUpdate;
        }

        private void SetupRiskOfOptions()
        {
            try
            {
                // Register the option with Risk of Options
                ModSettingsManager.AddOption(new KeyBindOption(HoldKey));
                ModSettingsManager.AddOption(new CheckBoxOption(DisableCancellation));

                // Set mod description
                ModSettingsManager.SetModDescription("Hold the configured key during Void Blink to float upward instead of dashing forward. Tap for dash, hold for float!");

                // Set mod icon if available
                SetModIcon();
            }
            catch (Exception ex)
            {
                Logger.LogError($"Error setting up Risk of Options: {ex}");
            }
        }

        private void SetModIcon()
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                string resourceName = null;
                foreach (var name in asm.GetManifestResourceNames())
                {
                    if (name.EndsWith("icon.png", StringComparison.OrdinalIgnoreCase))
                    {
                        resourceName = name;
                        break;
                    }
                }

                if (resourceName == null)
                {
                    Logger.LogWarning("Mod icon resource not found. Make sure icon.png is embedded.");
                    return;
                }

                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return;

                    byte[] data = new byte[stream.Length];
                    stream.Read(data, 0, data.Length);

                    Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                    if (!UnityEngine.ImageConversion.LoadImage(texture, data)) return;

                    Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f));
                    ModSettingsManager.SetModIcon(sprite);
                    Logger.LogInfo("Mod icon set successfully.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to set mod icon: {ex.Message}");
            }
        }

        private void CharacterBody_FixedUpdate(On.RoR2.CharacterBody.orig_FixedUpdate orig, RoR2.CharacterBody self)
        {
            orig(self);

            // Clean up state data for characters that no longer exist
            var idsToRemove = new System.Collections.Generic.List<UnityEngine.Networking.NetworkInstanceId>();
            foreach (var kvp in stateData)
            {
                if (kvp.Value.lastUpdatedTime < Time.time - 10f) // Remove after 10 seconds of inactivity
                {
                    idsToRemove.Add(kvp.Key);
                }
            }

            foreach (var id in idsToRemove)
            {
                stateData.Remove(id);
            }
        }

        private void VoidBlinkBase_OnEnter(On.EntityStates.VoidSurvivor.VoidBlinkBase.orig_OnEnter orig, VoidBlinkBase self)
        {
            orig(self);

            // Initialize state data for this character
            var character = self.characterBody;
            if (character && character.netId != null)
            {
                stateData[character.netId] = new BlinkStateData
                {
                    isHoldingMode = false,
                    modeDetermined = false,
                    transitionStartTime = 0f,
                    transitionStartVelocity = Vector3.zero,
                    lastUpdatedTime = Time.time
                };

                // Start with tap mode by default
                self.duration = DOWN_DURATION;
            }
        }

        private Vector3 VoidBlinkBase_GetVelocity(On.EntityStates.VoidSurvivor.VoidBlinkBase.orig_GetVelocity orig, VoidBlinkBase self)
        {
            var character = self.characterBody;
            if (!character || character.netId == null || !stateData.TryGetValue(character.netId, out var data))
            {
                return orig(self);
            }

            data.lastUpdatedTime = Time.time;

            // Use fixed timing for mode determination - always dash for first 0.25s
            if (!data.modeDetermined && self.fixedAge >= INITIAL_DASH_TIME)
            {
                // After initial dash time, check if key is still held to determine mode
                data.isHoldingMode = IsHoldKeyDown();
                data.modeDetermined = true;

                if (data.isHoldingMode)
                {
                    data.transitionStartTime = self.fixedAge;
                    data.transitionStartVelocity = self.characterMotor?.velocity ?? Vector3.zero;
                    self.duration = UP_DURATION;
                }
            }

            // Calculate base velocity based on the determined mode
            float targetDuration = data.isHoldingMode ? UP_DURATION : DOWN_DURATION;
            AnimationCurve targetUpSpeed = data.isHoldingMode ? UP_UP_SPEED : DOWN_UP_SPEED;
            AnimationCurve targetForwardSpeed = data.isHoldingMode ? UP_FORWARD_SPEED : DOWN_FORWARD_SPEED;
            float targetSpeedCoefficient = data.isHoldingMode ? UP_SPEED_COEFFICIENT : DOWN_SPEED_COEFFICIENT;

            float time = Mathf.Clamp01(self.fixedAge / targetDuration);
            Vector3 targetVelocity = (targetForwardSpeed.Evaluate(time) * self.forwardVector +
                                      targetUpSpeed.Evaluate(time) * Vector3.up) *
                                      targetSpeedCoefficient * self.moveSpeedStat;

            // If we're transitioning to hold mode, create a smooth U-shaped arc
            if (data.isHoldingMode && self.fixedAge < data.transitionStartTime + TRANSITION_DURATION)
            {
                float transitionProgress = Mathf.Clamp01((self.fixedAge - data.transitionStartTime) / TRANSITION_DURATION);

                // Use a quadratic ease-out for smoother upward curve
                float upwardBlend = Mathf.SmoothStep(0f, 1f, transitionProgress);
                float forwardBlend = Mathf.Lerp(1f, 0.8f, transitionProgress); // Slightly reduce forward speed

                // Get the current dash velocity components
                float currentForwardSpeed = Vector3.Dot(data.transitionStartVelocity, self.forwardVector);
                float currentUpwardSpeed = data.transitionStartVelocity.y;

                // Create a U-shaped transition: maintain forward momentum while gradually adding upward lift
                Vector3 forwardComponent = self.forwardVector * Mathf.Lerp(currentForwardSpeed, targetVelocity.magnitude * forwardBlend, upwardBlend);
                Vector3 upwardComponent = Vector3.up * Mathf.Lerp(currentUpwardSpeed, targetVelocity.y, upwardBlend * upwardBlend); // Quadratic for smoother curve

                return forwardComponent + upwardComponent;
            }

            return targetVelocity;
        }

        private void VoidBlinkBase_FixedUpdate(On.EntityStates.VoidSurvivor.VoidBlinkBase.orig_FixedUpdate orig, VoidBlinkBase self)
        {
            orig(self);

            var character = self.characterBody;
            if (!character || character.netId == null || !stateData.TryGetValue(character.netId, out var data))
            {
                return;
            }

            data.lastUpdatedTime = Time.time;
            bool keyHeld = IsHoldKeyDown();

            // If we're in hold mode but key is released, cancel the ability (unless disabled)
            if (self.isAuthority && data.modeDetermined && data.isHoldingMode && !keyHeld &&
                self.fixedAge > 0.1f && !DisableCancellation.Value)
            {
                self.duration = self.fixedAge;
            }
        }

        // Helper to check if the configured hold key is down
        private static bool IsHoldKeyDown()
        {
            if (HoldKey?.Value != null && HoldKey.Value.MainKey != KeyCode.None)
            {
                return Input.GetKey(HoldKey.Value.MainKey);
            }
            return false;
        }
        }

        // Helper class to track state per character
        public class BlinkStateData
        {
            public bool isHoldingMode;
            public bool modeDetermined;
            public float transitionStartTime;
            public Vector3 transitionStartVelocity;
            public float lastUpdatedTime;
        }
    }
