﻿using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Cognitive3D;
using UnityEngine.XR;
#if C3D_STEAMVR2
using Valve.VR;
#endif

//replaced everything to use InputFeature
//automatically get left/right controllers at runtime from gameplayreferences
//adds controller input properties to dynamic object snapshot for display on sceneexplorer

namespace Cognitive3D.Components
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Cognitive3D/Components/Controller Input Tracker")]
    public class ControllerInputTracker : AnalyticsComponentBase
    {
        const float UpdateRate = 0.1f;
        float nextUpdateTime;
        //records analogue inputs at this interval

        DynamicData LeftHand;
        DynamicData RightHand;

        protected override void OnSessionBegin()
        {
            ControllerTracking.OnControllerRegistered += Init;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ControllerTracking.OnControllerRegistered -= Init;
        }

#if C3D_STEAMVR2

        List<ButtonState> CurrentLeftButtonStates = new List<ButtonState>();
        List<ButtonState> CurrentRightButtonStates = new List<ButtonState>();

        public SteamVR_Action_Boolean gripAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("c3d_input", "grip");
        public SteamVR_Action_Single triggerAction = SteamVR_Input.GetAction<SteamVR_Action_Single>("c3d_input", "trigger");
        public SteamVR_Action_Boolean menuAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("c3d_input", "menu");
        public SteamVR_Action_Vector2 touchpadAction = SteamVR_Input.GetAction<SteamVR_Action_Vector2>("c3d_input", "touchpad");
        public SteamVR_Action_Boolean touchAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("c3d_input", "touchpad_touch");
        public SteamVR_Action_Boolean pressAction = SteamVR_Input.GetAction<SteamVR_Action_Boolean>("c3d_input", "touchpad_press");
        public SteamVR_ActionSet C3D_ActionSet = SteamVR_Input.GetActionSet("C3D_Input");

        int Trigger;
        int TouchForce;
        Vector2 lastAxis;
        float sqrMag = 0.05f;

        protected override void OnEnable()
        {
            base.OnEnable();
            //register actions
            if (gripAction != null)
            {
                gripAction.AddOnChangeListener(OnGripActionChange, SteamVR_Input_Sources.LeftHand);
                gripAction.AddOnChangeListener(OnGripActionChange, SteamVR_Input_Sources.RightHand);
            }
            if (touchAction != null)
            {
                touchAction.AddOnChangeListener(OnTouchActionChange, SteamVR_Input_Sources.LeftHand);
                touchAction.AddOnChangeListener(OnTouchActionChange, SteamVR_Input_Sources.RightHand);
            }
            if (pressAction != null)
            {
                pressAction.AddOnChangeListener(OnPressActionChange, SteamVR_Input_Sources.LeftHand);
                pressAction.AddOnChangeListener(OnPressActionChange, SteamVR_Input_Sources.RightHand);
            }
            if (menuAction != null)
            {
                menuAction.AddOnChangeListener(OnMenuActionChange, SteamVR_Input_Sources.LeftHand);
                menuAction.AddOnChangeListener(OnMenuActionChange, SteamVR_Input_Sources.RightHand);
            }
            if (C3D_ActionSet != null)
            {
                C3D_ActionSet.Activate(SteamVR_Input_Sources.LeftHand);
                C3D_ActionSet.Activate(SteamVR_Input_Sources.RightHand);
            }
        }

        private void OnGripActionChange(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool newState)
        {
            if (fromSource == SteamVR_Input_Sources.RightHand)
            {
                OnButtonChanged(
                RightHand,
                true,
                "vive_grip",
                newState,
                CurrentRightButtonStates);
            }
            else
            {
                OnButtonChanged(
                LeftHand,
                false,
                "vive_grip",
                newState,
                CurrentLeftButtonStates);
            }
        }

        private void OnTouchActionChange(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool newState)
        {
            ButtonState buttonstate;
            if (fromSource == SteamVR_Input_Sources.RightHand)
            {
                buttonstate = CurrentRightButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == "vive_touchpad"; });
            }
            else
            {
                buttonstate = CurrentLeftButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == "vive_touchpad"; });
            }

            if (buttonstate != null)
            {
                if (newState)
                    TouchForce = 50;
                else if (pressAction.state)
                    TouchForce = 100;
                else
                    TouchForce = 0;
                buttonstate.ButtonPercent = TouchForce;
                lastAxis.x = buttonstate.X;
                lastAxis.y = buttonstate.Y;
            }
            else
            {
                var axis = touchpadAction.GetAxis(fromSource);
                if (newState)
                    TouchForce = 50;
                else if (pressAction.state)
                    TouchForce = 100;
                else
                    TouchForce = 0;

                if (fromSource == SteamVR_Input_Sources.RightHand)
                {
                    OnVectorChanged(
                        RightHand,
                        true,
                        "vive_touchpad",
                        TouchForce,
                        axis,
                        CurrentRightButtonStates);
                }
                else
                {
                    OnVectorChanged(
                        LeftHand,
                        false,
                        "vive_touchpad",
                        TouchForce,
                        axis,
                        CurrentLeftButtonStates);
                }

                lastAxis = axis;
            }
        }

        private void OnPressActionChange(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool newState)
        {
            ButtonState buttonstate;
            if (fromSource == SteamVR_Input_Sources.RightHand)
            {
                buttonstate = CurrentRightButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == "vive_touchpad"; });
            }
            else
            {
                buttonstate = CurrentLeftButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == "vive_touchpad"; });
            }
            if (buttonstate != null)
            {
                if (newState)
                    TouchForce = 100;
                else if (touchAction.state)
                    TouchForce = 50;
                else
                    TouchForce = 0;
                buttonstate.ButtonPercent = TouchForce;
                lastAxis.x = buttonstate.X;
                lastAxis.y = buttonstate.Y;
            }
            else
            {
                var axis = touchpadAction.GetAxis(fromSource);
                if (newState)
                    TouchForce = 100;
                else if (touchAction.state)
                    TouchForce = 50;
                else
                    TouchForce = 0;

                if (fromSource == SteamVR_Input_Sources.RightHand)
                {
                    OnVectorChanged(
                        RightHand,
                        true,
                        "vive_touchpad",
                        TouchForce,
                        axis,
                        CurrentRightButtonStates);
                }
                else
                {
                    OnVectorChanged(
                        LeftHand,
                        false,
                        "vive_touchpad",
                        TouchForce,
                        axis,
                        CurrentLeftButtonStates);
                }

                lastAxis = axis;
            }
        }

        private void OnMenuActionChange(SteamVR_Action_Boolean fromAction, SteamVR_Input_Sources fromSource, bool newState)
        {
            if (fromSource == SteamVR_Input_Sources.RightHand)
            {
                OnButtonChanged(
                    RightHand,
                    true,
                    "vive_menubtn",
                    newState,
                    CurrentRightButtonStates);
            }
            else
            {
                OnButtonChanged(
                    LeftHand,
                    false,
                    "vive_menubtn",
                    newState,
                    CurrentLeftButtonStates);
            }
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            //remove actions
            if (gripAction != null)
            {
                gripAction.RemoveOnChangeListener(OnGripActionChange, SteamVR_Input_Sources.LeftHand);
                gripAction.RemoveOnChangeListener(OnGripActionChange, SteamVR_Input_Sources.RightHand);
            }
            if (touchAction != null)
            {
                touchAction.RemoveOnChangeListener(OnTouchActionChange, SteamVR_Input_Sources.LeftHand);
                touchAction.RemoveOnChangeListener(OnTouchActionChange, SteamVR_Input_Sources.RightHand);
            }
            if (pressAction != null)
            {
                pressAction.RemoveOnChangeListener(OnPressActionChange, SteamVR_Input_Sources.LeftHand);
                pressAction.RemoveOnChangeListener(OnPressActionChange, SteamVR_Input_Sources.RightHand);
            }
            if (menuAction != null)
            {
                menuAction.RemoveOnChangeListener(OnMenuActionChange, SteamVR_Input_Sources.LeftHand);
                menuAction.RemoveOnChangeListener(OnMenuActionChange, SteamVR_Input_Sources.RightHand);
            }
            if (C3D_ActionSet != null)
            {
                C3D_ActionSet.Deactivate(SteamVR_Input_Sources.LeftHand);
                C3D_ActionSet.Deactivate(SteamVR_Input_Sources.RightHand);
            }
        }

        void Init()
        {
            //TODO loop this if a controller is null
            Transform tempTransform;
            if (GameplayReferences.GetControllerTransform(false,out tempTransform))
            {
                LeftHand = tempTransform.GetComponent<DynamicObject>();
            }
            if (GameplayReferences.GetControllerTransform(true, out tempTransform))
            {
                RightHand = tempTransform.GetComponent<DynamicObject>();
            }
        }

        private void LateUpdate()
        {
            if (!Cognitive3D_Manager.IsInitialized) { return; }

            Transform tempTransform;
            if (LeftHand == null && GameplayReferences.GetControllerTransform(false,out tempTransform))
            {
                LeftHand = tempTransform.GetComponent<DynamicObject>();
            }
            if (RightHand == null && GameplayReferences.GetControllerTransform(true, out tempTransform))
            {
                RightHand = tempTransform.GetComponent<DynamicObject>();
            }
            //assuming controller updates happen before/in update loop?

            if (Time.time > nextUpdateTime)
            {
                RecordAnalogInputs();
                nextUpdateTime = Time.time + UpdateRate;
            }

            if (CurrentLeftButtonStates.Count > 0)
            {
            	List<ButtonState> copy = new List<ButtonState>(CurrentLeftButtonStates.Count);
            	for(int i = 0; i< CurrentLeftButtonStates.Count;i++)
            	{
            		copy.Add(CurrentLeftButtonStates[i]);
            	}
                CurrentLeftButtonStates.Clear();
            	DynamicManager.RecordControllerEvent(false, copy);
            }
            if (CurrentRightButtonStates.Count > 0)
            {
                List<ButtonState> copy = new List<ButtonState>(CurrentRightButtonStates.Count);
                for (int i = 0; i < CurrentRightButtonStates.Count; i++)
                {
                    copy.Add(CurrentRightButtonStates[i]);
                }
                CurrentRightButtonStates.Clear();
                DynamicManager.RecordControllerEvent(true, copy);
            }
        }

        void RecordAnalogInputs()
        {
            { //left hand
                float trigger = triggerAction.GetAxis(SteamVR_Input_Sources.LeftHand);
                int tempTrigger = (int)(trigger * 100);
                if (Trigger != tempTrigger)
                {
                    var buttonstate = CurrentLeftButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == "vive_touchpad"; });
                    if (buttonstate != null)
                    {
                        buttonstate.ButtonPercent = tempTrigger;
                    }
                    else
                    {
                        OnSingleChanged(LeftHand, false, "vive_trigger", tempTrigger, CurrentLeftButtonStates);
                    }
                    Trigger = tempTrigger;
                }

                if (TouchForce != 0)
                {
                    var axis = touchpadAction.GetAxis(SteamVR_Input_Sources.LeftHand);

                    if (Vector3.SqrMagnitude(axis - lastAxis) > sqrMag)
                    {
                        var buttonstate = CurrentLeftButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == "vive_touchpad"; });
                        if (buttonstate != null)
                        {
                            buttonstate.X = axis.x;
                            buttonstate.Y = axis.y;
                        }
                        else
                        {
                            OnVectorChanged(LeftHand, false, "vive_touchpad", TouchForce, axis, CurrentLeftButtonStates);
                        }

                        lastAxis = axis;
                    }
                }
            }
            { //right hand
                float trigger = triggerAction.GetAxis(SteamVR_Input_Sources.RightHand);
                int tempTrigger = (int)(trigger * 100);
                if (Trigger != tempTrigger)
                {
                    var buttonstate = CurrentRightButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == "vive_touchpad"; });
                    if (buttonstate != null)
                    {
                        buttonstate.ButtonPercent = tempTrigger;
                    }
                    else
                    {
                        OnSingleChanged(RightHand, true, "vive_trigger", tempTrigger, CurrentRightButtonStates);
                    }
                    Trigger = tempTrigger;
                }

                if (TouchForce != 0)
                {
                    var axis = touchpadAction.GetAxis(SteamVR_Input_Sources.LeftHand);

                    if (Vector3.SqrMagnitude(axis - lastAxis) > sqrMag)
                    {
                        var buttonstate = CurrentRightButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == "vive_touchpad"; });
                        if (buttonstate != null)
                        {
                            buttonstate.X = axis.x;
                            buttonstate.Y = axis.y;
                        }
                        else
                        {
                            OnVectorChanged(RightHand, true, "vive_touchpad", TouchForce, axis, CurrentRightButtonStates);
                        }

                        lastAxis = axis;
                    }
                }
            }
        }
#else
        List<ButtonState> CurrentLeftButtonStates = new List<ButtonState>();
        List<ButtonState> CurrentRightButtonStates = new List<ButtonState>();

        Dictionary<string, ButtonState> LeftLastFrameButtonStates = new Dictionary<string, ButtonState>();
        Dictionary<string, ButtonState> RightLastFrameButtonStates = new Dictionary<string, ButtonState>();

        Vector3 LeftJoystickVector;
        Vector3 RightJoystickVector;
        Vector3 LeftTouchpadVector;
        Vector3 RightTouchpadVector;
        float minMagnitude = 0.05f;

        void Init()
        {
            LeftHand = DynamicManager.GetInputDynamicData(InputUtil.InputType.Controller, false);
            RightHand = DynamicManager.GetInputDynamicData(InputUtil.InputType.Controller, true);

            Cognitive3D_Manager.SetSessionProperty("c3d.device.controllerinputs.enabled", true);

            LeftLastFrameButtonStates = new Dictionary<string, ButtonState>();
            RightLastFrameButtonStates = new Dictionary<string, ButtonState>();

            //left hand
            if (!System.String.IsNullOrEmpty(LeftHand.Id) && InputUtil.TryParseControllerDisplayType(LeftHand.ControllerType, out InputUtil.ControllerDisplayType leftDisplay))
            {
                switch (leftDisplay)
                {
                    case InputUtil.ControllerDisplayType.vive_controller:
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("vive_touchpad", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("vive_touchpad", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxisTouch.name, new ButtonState("vive_touchpad", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("vive_trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("vive_trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("vive_grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("vive_grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.primaryButton.name, new ButtonState("vive_menubtn"));
                        break;
                    case InputUtil.ControllerDisplayType.vive_focus_controller_left:
                    case InputUtil.ControllerDisplayType.quest_pro_touch_left:
                    case InputUtil.ControllerDisplayType.quest_plus_touch_left:
                    case InputUtil.ControllerDisplayType.oculus_quest_touch_left:
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("joystick", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("joystick", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.menuButton.name, new ButtonState("menu"));
                        LeftLastFrameButtonStates.Add(CommonUsages.primaryButton.name, new ButtonState("xbtn"));
                        LeftLastFrameButtonStates.Add(CommonUsages.secondaryButton.name, new ButtonState("ybtn"));
                        break;
                    case InputUtil.ControllerDisplayType.oculus_rift_controller_left:
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("rift_joystick", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("rift_joystick", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("rift_trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("rift_trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("rift_grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("rift_grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.menuButton.name, new ButtonState("rift_start"));
                        LeftLastFrameButtonStates.Add(CommonUsages.primaryButton.name, new ButtonState("rift_xbtn"));
                        LeftLastFrameButtonStates.Add(CommonUsages.secondaryButton.name, new ButtonState("rift_ybtn"));
                        break;
                    case InputUtil.ControllerDisplayType.windows_mixed_reality_controller_left:
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("wmr_joystick", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("wmr_joystick", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.secondary2DAxis.name, new ButtonState("wmr_touchpad", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.secondary2DAxisClick.name, new ButtonState("wmr_touchpad", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("wmr_trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("wmr_trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("wmr_grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("wmr_grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.menuButton.name, new ButtonState("wmr_menubtn"));
                        break;
                    case InputUtil.ControllerDisplayType.pico_neo_2_eye_controller_left:
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("pico_joystick", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("pico_joystick", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("pico_trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("pico_trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("pico_grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("pico_grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.menuButton.name, new ButtonState("pico_menubtn"));
                        LeftLastFrameButtonStates.Add(CommonUsages.primaryButton.name, new ButtonState("pico_xbtn"));
                        LeftLastFrameButtonStates.Add(CommonUsages.secondaryButton.name, new ButtonState("pico_ybtn"));
                        break;
                    case InputUtil.ControllerDisplayType.pico_neo_3_eye_controller_left:
                    case InputUtil.ControllerDisplayType.pico_neo_4_eye_controller_left:
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("pico_joystick", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("pico_joystick", 0, 0, 0, true));
                        LeftLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("pico_trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("pico_trigger"));
                        LeftLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("pico_grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("pico_grip"));
                        LeftLastFrameButtonStates.Add(CommonUsages.menuButton.name, new ButtonState("pico_menubtn"));
                        LeftLastFrameButtonStates.Add(CommonUsages.primaryButton.name, new ButtonState("pico_xbtn"));
                        LeftLastFrameButtonStates.Add(CommonUsages.secondaryButton.name, new ButtonState("pico_ybtn"));
                        break;
                    case InputUtil.ControllerDisplayType.unknown:
                        break;
                    default:
                        Util.logDebug("Unknown Left Controller Type: " + LeftHand.Name);break;
                }
            }

            //right hand
            if (!System.String.IsNullOrEmpty(RightHand.Id) && InputUtil.TryParseControllerDisplayType(RightHand.ControllerType, out InputUtil.ControllerDisplayType rightDisplay))
            {
                switch (rightDisplay)
                {
                    case InputUtil.ControllerDisplayType.vive_controller:
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("vive_touchpad", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("vive_touchpad", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxisTouch.name, new ButtonState("vive_touchpad", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("vive_trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("vive_trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("vive_grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("vive_grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.primaryButton.name, new ButtonState("vive_menubtn"));
                        break;
                    case InputUtil.ControllerDisplayType.vive_focus_controller_right:
                    case InputUtil.ControllerDisplayType.quest_pro_touch_right:
                    case InputUtil.ControllerDisplayType.quest_plus_touch_right:
                    case InputUtil.ControllerDisplayType.oculus_quest_touch_right:
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("joystick", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("joystick", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.menuButton.name, new ButtonState("menu"));
                        RightLastFrameButtonStates.Add(CommonUsages.primaryButton.name, new ButtonState("abtn"));
                        RightLastFrameButtonStates.Add(CommonUsages.secondaryButton.name, new ButtonState("bbtn"));
                        break;
                    case InputUtil.ControllerDisplayType.oculus_rift_controller_right:
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("rift_joystick", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("rift_joystick", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("rift_trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("rift_trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("rift_grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("rift_grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.menuButton.name, new ButtonState("rift_start"));
                        RightLastFrameButtonStates.Add(CommonUsages.primaryButton.name, new ButtonState("rift_xbtn"));
                        RightLastFrameButtonStates.Add(CommonUsages.secondaryButton.name, new ButtonState("rift_ybtn"));
                        break;
                    case InputUtil.ControllerDisplayType.windows_mixed_reality_controller_right:
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("wmr_joystick", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("wmr_joystick", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.secondary2DAxis.name, new ButtonState("wmr_touchpad", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.secondary2DAxisClick.name, new ButtonState("wmr_touchpad", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("wmr_trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("wmr_trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("wmr_grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("wmr_grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.menuButton.name, new ButtonState("wmr_menubtn"));
                        break;
                    case InputUtil.ControllerDisplayType.pico_neo_2_eye_controller_right:
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("pico_joystick", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("pico_joystick", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("pico_trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("pico_trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("pico_grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("pico_grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.menuButton.name, new ButtonState("pico_menubtn"));
                        RightLastFrameButtonStates.Add(CommonUsages.primaryButton.name, new ButtonState("pico_xbtn"));
                        RightLastFrameButtonStates.Add(CommonUsages.secondaryButton.name, new ButtonState("pico_ybtn"));
                        break;
                    case InputUtil.ControllerDisplayType.pico_neo_3_eye_controller_right:
                    case InputUtil.ControllerDisplayType.pico_neo_4_eye_controller_right:
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxis.name, new ButtonState("pico_joystick", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.primary2DAxisClick.name, new ButtonState("pico_joystick", 0, 0, 0, true));
                        RightLastFrameButtonStates.Add(CommonUsages.trigger.name, new ButtonState("pico_trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.triggerButton.name, new ButtonState("pico_trigger"));
                        RightLastFrameButtonStates.Add(CommonUsages.grip.name, new ButtonState("pico_grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.gripButton.name, new ButtonState("pico_grip"));
                        RightLastFrameButtonStates.Add(CommonUsages.menuButton.name, new ButtonState("pico_menubtn"));
                        RightLastFrameButtonStates.Add(CommonUsages.primaryButton.name, new ButtonState("pico_abtn"));
                        RightLastFrameButtonStates.Add(CommonUsages.secondaryButton.name, new ButtonState("pico_bbtn"));
                        break;
                    case InputUtil.ControllerDisplayType.unknown:
                        break;
                    default:
                        Util.logDebug("Unknown Right Controller Type: " + RightHand.Name); break;
                }
            }

            //TODO set initial button states
        }

        private void Update()
        {
            if (!Cognitive3D_Manager.IsInitialized) { return; }

            InputDevice leftHandDevice;
            InputUtil.TryGetInputDevice(XRNode.LeftHand, out leftHandDevice);
            InputDevice rightHandDevice;
            InputUtil.TryGetInputDevice(XRNode.RightHand, out rightHandDevice);
            
            if (leftHandDevice.isValid)
            {
                //menu left
                bool menu;
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.menuButton, out menu) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.menuButton.name))
                {
                    if (LeftLastFrameButtonStates[CommonUsages.menuButton.name].ButtonPercent != (menu ? 100 : 0))
                    {
                        OnButtonChanged(LeftLastFrameButtonStates[CommonUsages.menuButton.name].ButtonName, menu, CurrentLeftButtonStates);
                        LeftLastFrameButtonStates[CommonUsages.menuButton.name].ButtonPercent = menu ? 100 : 0;
                    }
                }

                //left primary axis
                Vector2 primaryaxis2d;
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out primaryaxis2d) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxis.name))
                {
                    bool touchPressChanged = false;
                    int axisPower = 0;
                    //check for touch or press. if changed, write entire vector
                    bool touch;
                    if (leftHandDevice.TryGetFeatureValue(CommonUsages.primary2DAxisTouch, out touch) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxisTouch.name))
                    {
                        if (LeftLastFrameButtonStates[CommonUsages.primary2DAxisTouch.name].ButtonPercent != (touch ? 50 : 0))
                        {
                            touchPressChanged = true;
                            LeftLastFrameButtonStates[CommonUsages.primary2DAxisTouch.name].ButtonPercent = touch ? 50 : 0;
                            axisPower = touch ? 50 : 0;
                        }
                    }
                    bool press;
                    if (leftHandDevice.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out press) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxisClick.name))
                    {
                        if (LeftLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonPercent != (press ? 100 : 0))
                        {
                            touchPressChanged = true;
                            LeftLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonPercent = press ? 100 : 0;
                            axisPower = press ? 100 : 0;
                        }
                    }
                    if (touchPressChanged)
                    {
                        OnVectorChanged(LeftLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonName, axisPower, primaryaxis2d.x, primaryaxis2d.y, CurrentLeftButtonStates);
                    }
                }

                //left secondary axis
                Vector2 secondaryaxis2d;
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.secondary2DAxis, out secondaryaxis2d) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.secondary2DAxis.name))
                {
                    bool touchPressChanged = false;
                    int axisPower = 0;
                    //check for touch or press. if changed, write entire vector
                    bool touch;
                    if (leftHandDevice.TryGetFeatureValue(CommonUsages.secondary2DAxisTouch, out touch) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.secondary2DAxisTouch.name))
                    {
                        if (LeftLastFrameButtonStates[CommonUsages.secondary2DAxisTouch.name].ButtonPercent != (touch ? 50 : 0))
                        {
                            touchPressChanged = true;
                            LeftLastFrameButtonStates[CommonUsages.secondary2DAxisTouch.name].ButtonPercent = touch ? 50 : 0;
                            axisPower = touch ? 50 : 0;
                        }
                    }
                    bool press;
                    if (leftHandDevice.TryGetFeatureValue(CommonUsages.secondary2DAxisClick, out press) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.secondary2DAxisClick.name))
                    {
                        if (LeftLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonPercent != (press ? 100 : 0))
                        {
                            touchPressChanged = true;
                            LeftLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonPercent = press ? 100 : 0;
                            axisPower = press ? 100 : 0;
                        }
                    }
                    if (touchPressChanged)
                    {
                        OnVectorChanged(LeftLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonName, axisPower, secondaryaxis2d.x, secondaryaxis2d.y, CurrentLeftButtonStates);
                    }
                }

                //left trigger as button
                bool triggerButton;
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.triggerButton, out triggerButton) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.triggerButton.name))
                {
                    if (LeftLastFrameButtonStates[CommonUsages.triggerButton.name].ButtonPercent != (triggerButton ? 100 : 0))
                    {
                        OnButtonChanged(LeftLastFrameButtonStates[CommonUsages.triggerButton.name].ButtonName, triggerButton, CurrentLeftButtonStates);
                        LeftLastFrameButtonStates[CommonUsages.triggerButton.name].ButtonPercent = triggerButton ? 100 : 0;
                    }
                }



                //left grip as button
                bool gripButton;
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.gripButton, out gripButton) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.gripButton.name))
                {
                    if (LeftLastFrameButtonStates[CommonUsages.gripButton.name].ButtonPercent != (gripButton ? 100 : 0))
                    {
                        OnButtonChanged(LeftLastFrameButtonStates[CommonUsages.gripButton.name].ButtonName, gripButton, CurrentLeftButtonStates);
                        LeftLastFrameButtonStates[CommonUsages.gripButton.name].ButtonPercent = gripButton ? 100 : 0;
                    }
                }

                //left primary button
                bool primaryButton;
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.primaryButton, out primaryButton) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.primaryButton.name))
                {
                    if (LeftLastFrameButtonStates[CommonUsages.primaryButton.name].ButtonPercent != (primaryButton ? 100 : 0))
                    {
                        OnButtonChanged(LeftLastFrameButtonStates[CommonUsages.primaryButton.name].ButtonName, primaryButton, CurrentLeftButtonStates);
                        LeftLastFrameButtonStates[CommonUsages.primaryButton.name].ButtonPercent = primaryButton ? 100 : 0;
                    }
                }

                //left secondary button
                bool secondaryButton;
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out secondaryButton) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.secondaryButton.name))
                {
                    if (LeftLastFrameButtonStates[CommonUsages.secondaryButton.name].ButtonPercent != (secondaryButton ? 100 : 0))
                    {
                        OnButtonChanged(LeftLastFrameButtonStates[CommonUsages.secondaryButton.name].ButtonName, secondaryButton, CurrentLeftButtonStates);
                        LeftLastFrameButtonStates[CommonUsages.secondaryButton.name].ButtonPercent = secondaryButton ? 100 : 0;
                    }
                }
            }

            if (rightHandDevice.isValid)
            {
                //menu right
                bool menu;
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.menuButton, out menu) && RightLastFrameButtonStates.ContainsKey(CommonUsages.menuButton.name))
                {
                    if (RightLastFrameButtonStates[CommonUsages.menuButton.name].ButtonPercent != (menu ? 100 : 0))
                    {
                        OnButtonChanged(RightLastFrameButtonStates[CommonUsages.menuButton.name].ButtonName, menu, CurrentRightButtonStates);
                        RightLastFrameButtonStates[CommonUsages.menuButton.name].ButtonPercent = menu ? 100 : 0;
                    }
                }

                //right primary axis
                Vector2 primaryaxis2d;
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out primaryaxis2d) && RightLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxis.name))
                {
                    bool touchPressChanged = false;
                    int axisPower = 0;
                    //check for touch or press. if changed, write entire vector
                    bool touch;
                    if (rightHandDevice.TryGetFeatureValue(CommonUsages.primary2DAxisTouch, out touch) && RightLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxisTouch.name))
                    {
                        if (RightLastFrameButtonStates[CommonUsages.primary2DAxisTouch.name].ButtonPercent != (touch ? 50 : 0))
                        {
                            touchPressChanged = true;
                            RightLastFrameButtonStates[CommonUsages.primary2DAxisTouch.name].ButtonPercent = touch ? 50 : 0;
                            axisPower = touch ? 50 : 0;
                        }
                    }
                    bool press;
                    if (rightHandDevice.TryGetFeatureValue(CommonUsages.primary2DAxisClick, out press) && RightLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxisClick.name))
                    {
                        if (RightLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonPercent != (press ? 100 : 0))
                        {
                            touchPressChanged = true;
                            RightLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonPercent = press ? 100 : 0;
                            axisPower = press ? 100 : 0;
                        }
                    }
                    if (touchPressChanged)
                    {
                        OnVectorChanged(RightLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonName, axisPower, primaryaxis2d.x, primaryaxis2d.y, CurrentRightButtonStates);
                    }
                }

                //right secondary axis
                Vector2 secondaryaxis2d;
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.secondary2DAxis, out secondaryaxis2d) && RightLastFrameButtonStates.ContainsKey(CommonUsages.secondary2DAxis.name))
                {
                    bool touchPressChanged = false;
                    int axisPower = 0;
                    //check for touch or press. if changed, write entire vector
                    bool touch;
                    if (rightHandDevice.TryGetFeatureValue(CommonUsages.secondary2DAxisTouch, out touch) && RightLastFrameButtonStates.ContainsKey(CommonUsages.secondary2DAxisTouch.name))
                    {
                        if (RightLastFrameButtonStates[CommonUsages.secondary2DAxisTouch.name].ButtonPercent != (touch ? 50 : 0))
                        {
                            touchPressChanged = true;
                            RightLastFrameButtonStates[CommonUsages.secondary2DAxisTouch.name].ButtonPercent = touch ? 50 : 0;
                            axisPower = touch ? 50 : 0;
                        }
                    }
                    bool press;
                    if (rightHandDevice.TryGetFeatureValue(CommonUsages.secondary2DAxisClick, out press) && RightLastFrameButtonStates.ContainsKey(CommonUsages.secondary2DAxisClick.name))
                    {
                        if (RightLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonPercent != (press ? 100 : 0))
                        {
                            touchPressChanged = true;
                            RightLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonPercent = press ? 100 : 0;
                            axisPower = press ? 100 : 0;
                        }
                    }
                    if (touchPressChanged)
                    {
                        OnVectorChanged(RightLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonName, axisPower, secondaryaxis2d.x, secondaryaxis2d.y, CurrentRightButtonStates);
                    }
                }
                //right trigger button
                bool triggerButton;
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.triggerButton, out triggerButton) && RightLastFrameButtonStates.ContainsKey(CommonUsages.triggerButton.name))
                {
                    if (RightLastFrameButtonStates[CommonUsages.triggerButton.name].ButtonPercent != (triggerButton ? 100 : 0))
                    {
                        OnButtonChanged(RightLastFrameButtonStates[CommonUsages.triggerButton.name].ButtonName, triggerButton, CurrentRightButtonStates);
                        RightLastFrameButtonStates[CommonUsages.triggerButton.name].ButtonPercent = triggerButton ? 100 : 0;
                    }
                }
                //right grip button
                bool gripButton;
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.gripButton, out gripButton) && RightLastFrameButtonStates.ContainsKey(CommonUsages.gripButton.name))
                {
                    if (RightLastFrameButtonStates[CommonUsages.gripButton.name].ButtonPercent != (gripButton ? 100 : 0))
                    {
                        OnButtonChanged(RightLastFrameButtonStates[CommonUsages.gripButton.name].ButtonName, gripButton, CurrentRightButtonStates);
                        RightLastFrameButtonStates[CommonUsages.gripButton.name].ButtonPercent = gripButton ? 100 : 0;
                    }
                }
                //right primary button
                bool primaryButton;
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.primaryButton, out primaryButton) && RightLastFrameButtonStates.ContainsKey(CommonUsages.primaryButton.name))
                {
                    if (RightLastFrameButtonStates[CommonUsages.primaryButton.name].ButtonPercent != (primaryButton ? 100 : 0))
                    {
                        OnButtonChanged(RightLastFrameButtonStates[CommonUsages.primaryButton.name].ButtonName, primaryButton, CurrentRightButtonStates);
                        RightLastFrameButtonStates[CommonUsages.primaryButton.name].ButtonPercent = primaryButton ? 100 : 0;
                    }
                }
                //right secondary button
                bool secondaryButton;
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.secondaryButton, out secondaryButton) && RightLastFrameButtonStates.ContainsKey(CommonUsages.secondaryButton.name))
                {
                    if (RightLastFrameButtonStates[CommonUsages.secondaryButton.name].ButtonPercent != (secondaryButton ? 100 : 0))
                    {
                        OnButtonChanged(RightLastFrameButtonStates[CommonUsages.secondaryButton.name].ButtonName, secondaryButton, CurrentRightButtonStates);
                        RightLastFrameButtonStates[CommonUsages.secondaryButton.name].ButtonPercent = secondaryButton ? 100 : 0;
                    }
                }
            }

            if (Time.time > nextUpdateTime)
            {
                nextUpdateTime = Time.time + UpdateRate;
                RecordAnalogInputs();
            }
            if (CurrentRightButtonStates.Count > 0)
            {
                List<ButtonState> copy = new List<ButtonState>(CurrentRightButtonStates.Count);
                for (int i = 0; i < CurrentRightButtonStates.Count; i++)
                {
                    copy.Add(CurrentRightButtonStates[i]);
                }
                CurrentRightButtonStates.Clear();

                DynamicManager.RecordControllerEvent(true, copy);
            }
            if (CurrentLeftButtonStates.Count > 0)
            {
                List<ButtonState> copy = new List<ButtonState>(CurrentLeftButtonStates.Count);
                for (int i = 0; i < CurrentLeftButtonStates.Count; i++)
                {
                    copy.Add(CurrentLeftButtonStates[i]);
                }
                CurrentLeftButtonStates.Clear();

                DynamicManager.RecordControllerEvent(false, copy);
            }
        }

        void RecordAnalogInputs()
        {
            InputDevice leftHandDevice;
            InputUtil.TryGetInputDevice(XRNode.LeftHand, out leftHandDevice);
            InputDevice rightHandDevice;
            InputUtil.TryGetInputDevice(XRNode.RightHand, out rightHandDevice);

            if (leftHandDevice.isValid)
            {
                //left primary joystick
                Vector2 leftJoystickVector;
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out leftJoystickVector) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxis.name))
                {
                    int axisPower;
                    if (LeftLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxisTouch.name))
                    {
                         axisPower = Mathf.Max(LeftLastFrameButtonStates[CommonUsages.primary2DAxisTouch.name].ButtonPercent, LeftLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonPercent);
                    }
                    else
                    {
                        axisPower = LeftLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonPercent;
                    }
                    var x = leftJoystickVector.x;
                    var y = leftJoystickVector.y;

                    Vector3 currentVector = new Vector3(x, y, axisPower);
                    if (Vector3.Magnitude(LeftJoystickVector - currentVector) > minMagnitude)
                    {
                        var joystick = CurrentLeftButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == LeftLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonName; });
                        if (joystick != null)
                        {
                            joystick.X = x;
                            joystick.Y = y;
                        }
                        else
                        {
                            OnVectorChanged(LeftLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonName, axisPower, leftJoystickVector, CurrentLeftButtonStates);
                        }
                        OnVectorChanged(LeftLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonName, axisPower, x, y, CurrentLeftButtonStates);
                        LeftJoystickVector = currentVector;
                    }
                }
                //left secondary touchpad
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.secondary2DAxis, out leftJoystickVector) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.secondary2DAxis.name))
                {
                    int axisPower = LeftLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonPercent;
                    var x = leftJoystickVector.x;
                    var y = leftJoystickVector.y;

                    Vector3 currentVector = new Vector3(x, y, axisPower);
                    if (Vector3.Magnitude(LeftTouchpadVector - currentVector) > minMagnitude)
                    {
                        var joystick = CurrentLeftButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == LeftLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonName; });
                        if (joystick != null)
                        {
                            joystick.X = x;
                            joystick.Y = y;
                        }
                        else
                        {
                            OnVectorChanged(LeftLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonName, axisPower, leftJoystickVector, CurrentLeftButtonStates);
                        }
                        OnVectorChanged(LeftLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonName, axisPower, x, y, CurrentLeftButtonStates);
                        LeftTouchpadVector = currentVector;
                    }
                }
                //grip left
                float grip;
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.grip, out grip) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.grip.name))
                {
                    if (LeftLastFrameButtonStates[CommonUsages.grip.name].ButtonPercent != (int)(grip * 100))
                    {
                        OnSingleChanged(LeftLastFrameButtonStates[CommonUsages.grip.name].ButtonName, (int)(grip * 100), CurrentLeftButtonStates);
                        LeftLastFrameButtonStates[CommonUsages.grip.name].ButtonPercent = (int)(grip * 100);
                    }
                }
                //trigger left
                float trigger;
                if (leftHandDevice.TryGetFeatureValue(CommonUsages.trigger, out trigger) && LeftLastFrameButtonStates.ContainsKey(CommonUsages.trigger.name))
                {
                    if (LeftLastFrameButtonStates[CommonUsages.trigger.name].ButtonPercent != (int)(trigger * 100))
                    {
                        OnSingleChanged(LeftLastFrameButtonStates[CommonUsages.trigger.name].ButtonName, (int)(trigger * 100), CurrentLeftButtonStates);
                        LeftLastFrameButtonStates[CommonUsages.trigger.name].ButtonPercent = (int)(trigger * 100);
                    }
                }
            }
            if (rightHandDevice.isValid)
            {

                //right primary joystick
                Vector2 rightJoystickVector;
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.primary2DAxis, out rightJoystickVector) && RightLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxis.name))
                {
                    int axisPower;
                    if (RightLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxisTouch.name))
                    {
                        axisPower = Mathf.Max(RightLastFrameButtonStates[CommonUsages.primary2DAxisTouch.name].ButtonPercent, RightLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonPercent);
                    }
                    else
                    {
                        axisPower = RightLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonPercent;
                    }
                    var x = rightJoystickVector.x;
                    var y = rightJoystickVector.y;

                    Vector3 currentVector = new Vector3(x, y, axisPower);
                    if (Vector3.Magnitude(RightJoystickVector - currentVector) > minMagnitude)
                    {
                        var joystick = CurrentRightButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == RightLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonName; });
                        if (joystick != null)
                        {
                            joystick.X = x;
                            joystick.Y = y;
                        }
                        else
                        {
                            OnVectorChanged(RightLastFrameButtonStates[CommonUsages.primary2DAxisClick.name].ButtonName, axisPower, rightJoystickVector, CurrentRightButtonStates);
                        }
                        RightJoystickVector = currentVector;
                    }
                }
                //right secondary touchpad
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.secondary2DAxis, out rightJoystickVector) && RightLastFrameButtonStates.ContainsKey(CommonUsages.secondary2DAxis.name))
                {
                    int axisPower = RightLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonPercent;
                    var x = rightJoystickVector.x;
                    var y = rightJoystickVector.y;

                    Vector3 currentVector = new Vector3(x, y, axisPower);
                    if (Vector3.Magnitude(RightTouchpadVector - currentVector) > minMagnitude)
                    {
                        var joystick = CurrentRightButtonStates.Find(delegate (ButtonState obj) { return obj.ButtonName == RightLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonName; });
                        if (joystick != null)
                        {
                            joystick.X = x;
                            joystick.Y = y;
                        }
                        else
                        {
                            OnVectorChanged(RightLastFrameButtonStates[CommonUsages.secondary2DAxisClick.name].ButtonName, axisPower, rightJoystickVector, CurrentRightButtonStates);
                        }
                        RightTouchpadVector = currentVector;
                    }
                }


                //grip right
                float grip;
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.grip, out grip) && RightLastFrameButtonStates.ContainsKey(CommonUsages.primary2DAxis.name))
                {
                    if (RightLastFrameButtonStates[CommonUsages.grip.name].ButtonPercent != (int)(grip * 100))
                    {
                        OnSingleChanged(RightLastFrameButtonStates[CommonUsages.grip.name].ButtonName, (int)(grip * 100), CurrentRightButtonStates);
                        RightLastFrameButtonStates[CommonUsages.grip.name].ButtonPercent = (int)(grip * 100);
                    }
                }

                //trigger right
                float trigger;
                if (rightHandDevice.TryGetFeatureValue(CommonUsages.trigger, out trigger) && RightLastFrameButtonStates.ContainsKey(CommonUsages.trigger.name))
                {
                    if (RightLastFrameButtonStates[CommonUsages.trigger.name].ButtonPercent != (int)(trigger * 100))
                    {
                        OnSingleChanged(RightLastFrameButtonStates[CommonUsages.trigger.name].ButtonName, (int)(trigger * 100), CurrentRightButtonStates);
                        RightLastFrameButtonStates[CommonUsages.trigger.name].ButtonPercent = (int)(trigger * 100);
                    }
                }
            }
        }
#endif

        void OnButtonChanged(string name, bool down, List<ButtonState> states)
        {
            states.Add(new ButtonState(name, down ? 100 : 0));
        }

        //writes for 0-100 inputs (triggers)
        void OnSingleChanged(string name, int single, List<ButtonState> states)
        {
            states.Add(new ButtonState(name, single));
        }

        void OnVectorChanged(string name, int input, float x, float y, List<ButtonState> states)
        {
            states.Add(new ButtonState(name, input, x, y, true));
        }

        //writes for normalized inputs (touchpads)
        void OnVectorChanged(string name, int input, Vector2 vector, List<ButtonState> states)
        {
            states.Add(new ButtonState(name, input, vector.x, vector.y, true));
        }

        public override string GetDescription()
        {
            return "Records buttons, triggers, grips and joystick inputs on controllers";
        }
    }
}