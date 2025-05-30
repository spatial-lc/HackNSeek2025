using UnityEngine.XR;
using UnityEngine;
using System.Collections;

namespace Cognitive3D.Components
{
    /// <summary>
    /// Sends events when controller loses tracking and events when controller regains tracking <br/>
    /// Each lost event should have a corresponding regained event <br/>
    /// Also sends sensors representing controller height from HMD
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Cognitive3D/Components/Controller Tracking")]
    public class ControllerTracking : AnalyticsComponentBase
    {
        private readonly float ControllerTrackingInterval = 1;

        /// <summary>
        /// Counts up the deltatime to determine when the interval ends
        /// </summary>
        private float currentTime;

        /// <summary>
        /// Angle between left controller and hmd; used as a property for custom event
        /// </summary>
        float leftAngle;

        /// <summary>
        /// Angle between right controller and hmd; used as a property for custom event
        /// </summary>
        float rightAngle;

        /// <summary>
        /// Vector from left controller to HMD
        /// </summary>
        Vector3 leftControllerToHMD;

        /// <summary>
        /// Vector from right controller to HMD
        /// </summary>
        Vector3 rightControllerToHMD;

        /// <summary>
        /// How long to wait before sending "left controller tracking lost" events
        /// </summary>
        private const float LEFT_TRACKING_COOLDOWN_TIME_IN_SECONDS = 5;

        /// <summary>
        /// How long to wait before sending "right controller tracking lost" events
        /// </summary>
        private const float RIGHT_TRACKING_COOLDOWN_TIME_IN_SECONDS = 5;

        /// <summary>
        /// Whether right controller tracking is in cooldown
        /// </summary>
        bool inRightCooldown;

        /// <summary>
        /// Whether right controller tracking is in cooldown
        /// </summary>
        bool inLeftCooldown;

        /// <summary>
        /// Internal clock to measure how long has elapsed since last left controller lost tracking event
        /// </summary>
        float leftCooldownTimer;

        /// <summary>
        /// Internal clock to measure how long has elapsed since last right controller lost tracking event
        /// </summary>
        float rightCooldownTimer;

        /// <summary>
        /// If left controller sent a lost tracking event and hasn't yet sent a regained tracking event
        /// </summary>
        bool leftControllerLostTracking;

        /// <summary>
        /// If right controller sent a lost tracking event and hasn't yet sent a regained tracking event
        /// </summary>
        bool rightControllerLostTracking;

        /// <summary>
        /// Event triggered when controllers are registered as dynamics
        /// </summary>
        public static event System.Action OnControllerRegistered;

        /// <summary>
        /// Flags to track whether the left controller has been registered
        /// </summary>
        private bool leftControllerRegistered;

        /// <summary>
        /// Flags to track whether the right controller has been registered
        /// </summary>
        private bool rightControllerRegistered;

        protected override void OnSessionBegin()
        {
#if XRPF
             if (XRPF.PrivacyFramework.Agreement.IsAgreementComplete && XRPF.PrivacyFramework.Agreement.IsHardwareDataAllowed)
#endif
            {
                InputTracking.trackingLost += OnTrackingLost;
                InputTracking.trackingAcquired += OnTrackingAcquired;
            }

            Cognitive3D_Manager.OnUpdate += Cognitive3D_Manager_OnUpdate;
            Cognitive3D_Manager.OnPreSessionEnd += Cognitive3D_Manager_OnPreSessionEnd;
            
            // Registering controllers
            Cognitive3D_Manager.OnLevelLoaded += OnLevelLoaded;
            RegisterControllers();
        }

        void OnLevelLoaded(UnityEngine.SceneManagement.Scene scene, UnityEngine.SceneManagement.LoadSceneMode mode, bool didChangeSceneId)
        {
            if (didChangeSceneId && Cognitive3D_Manager.TrackingScene != null)
            {
                leftControllerRegistered = false;
                rightControllerRegistered = false;

                // Registering new controllers
                RegisterControllers();
            }
        }

        void RegisterControllers()
        {
            if (!Cognitive3D_Manager.autoInitializeInput) return;

            // Check if the left controller is valid and not yet registered
            if (!leftControllerRegistered && InputUtil.TryGetInputDevice(XRNode.LeftHand, out InputDevice leftController))
            {
                DynamicManager.RegisterController(XRNode.LeftHand, false);
                leftControllerRegistered = true;
            }

            // Check if the right controller is valid and not yet registered
            if (!rightControllerRegistered && InputUtil.TryGetInputDevice(XRNode.RightHand, out InputDevice rightController))
            {
                DynamicManager.RegisterController(XRNode.RightHand, true);
                rightControllerRegistered = true;
            }

            // Invoke event if both controllers are registered
            if (leftControllerRegistered && rightControllerRegistered)
            {
                OnControllerRegistered?.Invoke();
            }

            // If hand tracking is enabled, update the input state based on the current tracked device
            // Otherwise, set the controllers' enabled state to true by specifying the input type as Controller
            if (gameObject.GetComponent<HandTracking>())
            {
                DynamicManager.UpdateDynamicInputEnabledState(InputUtil.GetCurrentTrackedDevice());
            }
            else
            {
                DynamicManager.UpdateDynamicInputEnabledState(InputUtil.InputType.Controller);
            }
        }
        
        /// <summary>
        /// Function to execute when xrNodeState.nodeType loses tracking
        /// We have a cooldown timer to prevent multiple back-to-back events from being sent
        /// </summary>
        /// <param name="xrNodeState">The state of the device</param>
        public void OnTrackingLost(XRNodeState xrNodeState)
        {
            if (xrNodeState.nodeType == XRNode.LeftHand && !inLeftCooldown)
            {
                new CustomEvent("c3d.Left Controller Lost tracking")
                    .SetProperty("Angle from HMD", leftAngle)
                    .SetProperty("Height from HMD", leftControllerToHMD.y)
                    .Send();
                inLeftCooldown = true;
                leftControllerLostTracking = true;
                leftCooldownTimer = 0;
            }

            if (xrNodeState.nodeType == XRNode.RightHand && !inRightCooldown)
            {
                new CustomEvent("c3d.Right Controller Lost tracking")
                    .SetProperty("Angle from HMD", rightAngle)
                    .SetProperty("Height from HMD", rightControllerToHMD.y)
                    .Send();
                inRightCooldown = true;
                rightControllerLostTracking = true;
                rightCooldownTimer = 0;
            }
        }

        /// <summary>
        /// Function to execute when xrNodeState.nodeType regains tracking
        /// </summary>
        /// <param name="xrNodeState">The state of the device</param>
        private void OnTrackingAcquired(XRNodeState xrNodeState)
        {
            // When a device becomes tracked, check if we need to register controllers
            if (xrNodeState.nodeType == XRNode.LeftHand || xrNodeState.nodeType == XRNode.RightHand)
            {
                RegisterControllers();
            }

            if (xrNodeState.nodeType == XRNode.LeftHand && leftControllerLostTracking)
            {
                new CustomEvent("c3d.Left Controller regained tracking")
                    .Send();
                leftControllerLostTracking = false;
            }

            if (xrNodeState.nodeType == XRNode.RightHand && rightControllerLostTracking)
            {
                new CustomEvent("c3d.Right Controller regained tracking")
                    .Send();
                rightControllerLostTracking = false;
            }
        }

        private void Cognitive3D_Manager_OnUpdate(float deltaTime)
        {
            // We don't want these lines to execute if component disabled
            // Without this condition, these lines will execute regardless
            //      of component being disabled since this function is bound to C3D_Manager.Update on SessionBegin()  
            if (isActiveAndEnabled)
            {
                currentTime += deltaTime;
                UpdateCooldownClock(deltaTime);
                bool wasLeftControllerFound = InputUtil.TryGetControllerPosition(XRNode.LeftHand, out var leftHandPosition);
                bool wasRightControllerFound = InputUtil.TryGetControllerPosition(XRNode.RightHand, out var rightHandPosition);
                
                if (wasLeftControllerFound)
                {
                    leftControllerToHMD = leftHandPosition - GameplayReferences.HMD.position;
                    leftAngle = Vector3.Angle(leftControllerToHMD, GameplayReferences.HMD.forward);
                }

                if (wasRightControllerFound)
                {
                    rightControllerToHMD = rightHandPosition - GameplayReferences.HMD.position;
                    rightAngle = Vector3.Angle(rightControllerToHMD, GameplayReferences.HMD.forward);
                }

                if (currentTime > ControllerTrackingInterval)
                {
                    if (wasLeftControllerFound)
                    {
                        SensorRecorder.RecordDataPoint("c3d.controller.left.height.fromHMD", leftControllerToHMD.y);
                    }

                    if (wasRightControllerFound)
                    {
                        SensorRecorder.RecordDataPoint("c3d.controller.right.height.fromHMD", rightControllerToHMD.y);
                    }
                    currentTime = 0;
                }
            }
            else
            {
                Debug.LogWarning("Controller Tracking component is disabled. Please enable in inspector.");
            }
        }

        /// <summary>
        /// Increments cooldown timer and checks if cooldown time has elapsed
        /// </summary>
        /// <param name="deltaTime">Time elapsed since last frame</param>
        private void UpdateCooldownClock(float deltaTime)
        {
            if (inRightCooldown) { rightCooldownTimer += deltaTime; }
            if (rightCooldownTimer >= RIGHT_TRACKING_COOLDOWN_TIME_IN_SECONDS) { inRightCooldown = false; }
            if (inLeftCooldown) { leftCooldownTimer += deltaTime; }
            if (leftCooldownTimer >= LEFT_TRACKING_COOLDOWN_TIME_IN_SECONDS) { inLeftCooldown = false; }
        }

        private void Cognitive3D_Manager_OnPreSessionEnd()
        {
#if XRPF
             if (XRPF.PrivacyFramework.Agreement.IsAgreementComplete && XRPF.PrivacyFramework.Agreement.IsHardwareDataAllowed)
#endif
            {
                InputTracking.trackingLost -= OnTrackingLost;
                InputTracking.trackingAcquired -= OnTrackingAcquired;
            }

            Cognitive3D_Manager.OnUpdate -= Cognitive3D_Manager_OnUpdate;
            Cognitive3D_Manager.OnPreSessionEnd -= Cognitive3D_Manager_OnPreSessionEnd;
            Cognitive3D_Manager.OnLevelLoaded -= OnLevelLoaded;
        }

        private void OnDestroy()
        {
#if XRPF
             if (XRPF.PrivacyFramework.Agreement.IsAgreementComplete && XRPF.PrivacyFramework.Agreement.IsHardwareDataAllowed)
#endif
            {
                InputTracking.trackingLost -= OnTrackingLost;
                InputTracking.trackingAcquired -= OnTrackingAcquired;
            }

            Cognitive3D_Manager.OnUpdate -= Cognitive3D_Manager_OnUpdate;
            Cognitive3D_Manager.OnPreSessionEnd -= Cognitive3D_Manager_OnPreSessionEnd;
            Cognitive3D_Manager.OnLevelLoaded -= OnLevelLoaded;
        }

        public override string GetDescription()
        {
            return "Sends events related to controllers such as tracking and height";
        }
    }
}