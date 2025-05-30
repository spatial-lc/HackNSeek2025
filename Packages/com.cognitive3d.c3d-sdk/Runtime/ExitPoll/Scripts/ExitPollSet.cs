using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR;
using System.Threading.Tasks;


namespace Cognitive3D
{
    public class ExitPollSet
    {
        /// <summary>
        /// The current exit poll panel being displayed
        /// </summary>
        public ExitPollPanel CurrentExitPollPanel;

        /// <summary>
        /// Parameters related to the exit poll
        /// </summary>
        public ExitPollParameters myparameters;

        /// <summary>
        /// A countdown timer used to track when tracking data is lost (e.g., controllers not detected).
        /// </summary>
        private float noTrackingCountdown;

        /// <summary>
        /// The maximum duration in seconds before fallback is triggered when tracking is lost.
        /// </summary>
        private const float NO_TRACKING_COUNTDOWN_LIMIT = 35;

        /// <summary>
        /// The message displayed when fallback to the HMD pointer is triggered due to missing controller or hand tracking.
        /// </summary>
        private readonly string FALLBACK_TO_HMD_POINTER = $"Controller or hands not found for {NO_TRACKING_COUNTDOWN_LIMIT}! Using HMD Pointer.";

        /// <summary>
        /// A list containing information on each panel <br/>
        /// The kv pairs in the dictionary are the keys and values of each of the fields on the panel
        /// </summary>
        private readonly List<Dictionary<string, string>> panelProperties = new List<Dictionary<string, string>>();

        /// <summary>
        /// The set of questions for the exit poll,
        /// </summary>
        private ExitPollData questionSet;

        /// <summary>
        /// Tracks the index of the currently active panel in the exit poll sequence.
        /// </summary>
        private int currentPanelIndex;

        // A temporary dictionary to store answers for each panel
        // This is used to keep track of the user's progress and pre-fill their answers if they revisit a panel.
        private readonly Dictionary<int, object> tempAnswers = new Dictionary<int, object>();

#region Begin
        public void BeginExitPoll(ExitPollParameters parameters)
        {
            if (!Cognitive3D_Manager.IsInitialized)
            {
                Util.logDebug("Cannot display exitpoll. Session has not begun");
                Cleanup(false);
                return;
            }

            Cognitive3D_Manager.OnUpdate += Cognitive3D_Manager_OnUpdate;
            Cognitive3D_Manager.OnPreSessionEnd += Cognitive3D_Manager_OnPreSessionEnd;
            InputTracking.trackingLost += ExitPollPointer.OnTrackingLost;
            InputTracking.trackingAcquired += ExitPollPointer.OnTrackingRegained;
            
            myparameters = ExitPollPointer.currentExitPollParameters = parameters;
            noTrackingCountdown = 0;
            switch (parameters.PointerType)
            {
                case ExitPollManager.PointerType.HMD:
                    ExitPollPointer.SetUpHMDAsPointer(parameters.HMDPointerPrefab);
                    break;

                case ExitPollManager.PointerType.ControllersAndHands:
                    ExitPollPointer.SetupControllerAsPointer(parameters.PointerControllerPrefab, parameters.PointerActivationButton, parameters.PointerLineWidth, parameters.PointerGradient);
                    break;

                case ExitPollManager.PointerType.Custom:
                    // Refer to the documentation for best practices
                    break;
                default:
                    break;
            }

            currentPanelIndex = 0;
            if (string.IsNullOrEmpty(myparameters.Hook))
            {
                Cleanup(false);
                Util.logDebug("Cognitive3D Exit Poll. You haven't specified a question hook to request!");
                return;
            }

            if (Cognitive3D_Manager.Instance != null)
            {
                GetQuestionSet();
            }
            else
            {
                Util.logDebug("Cannot display exitpoll. Cognitive3DManager not present in scene");
                Cleanup(false);
            }
        }
#endregion

#region Update
        private void Cognitive3D_Manager_OnUpdate(float deltaTime)
        {
            // Increment counter if controller pointer exists and tracking type is none
            if (GameplayReferences.PointerController != null && InputUtil.GetCurrentTrackedDevice() == InputUtil.InputType.None)
            {
                noTrackingCountdown += deltaTime;
                
                // Limit reached: destroy controller pointer and fallback to HMD pointer
                if (noTrackingCountdown >= NO_TRACKING_COUNTDOWN_LIMIT)
                {
                    noTrackingCountdown = 0;
                    GameObject.Destroy(GameplayReferences.PointerController);
                    ExitPollPointer.SetUpHMDAsPointer(myparameters.HMDPointerPrefab);
                    ExitPollPointer.DisplayControllerError(true, FALLBACK_TO_HMD_POINTER);
                }
            }
        }
#endregion

#region End
        /// <summary>
        /// When you manually need to close the Exit Poll question set manually OR <br/>
        /// when requesting a new exit poll question set when one is already active
        /// </summary>
        public void EndQuestionSet(int timeToWait)
        {
            panelProperties.Clear();
            if (CurrentExitPollPanel != null)
            {
                CurrentExitPollPanel.CloseError(timeToWait);
            }
            OnPanelError();
        }

        private void Cognitive3D_Manager_OnPreSessionEnd()
        {
            Cognitive3D_Manager.OnUpdate -= Cognitive3D_Manager_OnUpdate;
            Cognitive3D_Manager.OnPreSessionEnd -= Cognitive3D_Manager_OnPreSessionEnd;
            InputTracking.trackingLost -= ExitPollPointer.OnTrackingLost;
            InputTracking.trackingAcquired -= ExitPollPointer.OnTrackingRegained;
        }
#endregion

        /// <summary>
        /// Retrieves a set of questions for the exit poll, processes each question, and prepares the required data for display.
        /// </summary>
        async void GetQuestionSet()
        {
            Task<ExitPollData> exitPollDataTask = ExitPollManager.GetExitPollQuestionSets();

            questionSet = await exitPollDataTask;

            // Process each question
            foreach (var question in questionSet.questions)
            {
                var questionVariables = BuildQuestionVariables(questionSet.title, question);
                panelProperties.Add(questionVariables);
            }

            myparameters.OnBegin?.Invoke();
            IterateToNextQuestion();
        }

        /// <summary>
        /// Constructs a dictionary of variables (properties) for a given question, containing its attributes and optional properties.
        /// </summary>
        /// <param name="title">The title of the question set.</param>
        /// <param name="question">The question data entry containing details about the question.</param>
        private Dictionary<string, string> BuildQuestionVariables(string title, ExitPollData.ExitPollDataEntry question)
        {
            var variables = new Dictionary<string, string>
            {
                { "title", title },
                { "question", question.title },
                { "type", question.type },
                { "maxResponseLength", question.maxResponseLength.ToString() }
            };

            if (!string.IsNullOrEmpty(question.minLabel))
                variables["minLabel"] = question.minLabel;

            if (!string.IsNullOrEmpty(question.maxLabel))
                variables["maxLabel"] = question.maxLabel;

            if (question.range != null)
            {
                variables["start"] = question.range.start.ToString();
                variables["end"] = question.range.end.ToString();
            }

            if (question.answers != null)
            {
                string csvAnswers = "";
                foreach (var answer in question.answers)
                {
                    if (!string.IsNullOrEmpty(answer.answer))
                    {
                        csvAnswers += answer.answer + "|";
                    }
                }

                if (csvAnswers.Length > 0)
                {
                    // Remove the trailing pipe
                    csvAnswers = csvAnswers.Substring(0, csvAnswers.Length - 1);
                    variables["csvanswers"] = csvAnswers;
                }
            }

            return variables;
        }

        /// <summary>
        /// Handles the event when a panel closes, triggered by timeout, manual close, or answering.
        /// Updates the temporary answers dictionary, records the answer, and proceeds to the next question.
        /// </summary>
        public void OnPanelClosed(int panelId, int objectValue)
        {
            if (tempAnswers.ContainsKey(panelId))
            {
                tempAnswers[panelId] = objectValue;
            }
            else
            {
                tempAnswers.Add(panelId, objectValue);
            }

            ExitPollManager.RecordAnswer(panelId, objectValue);
            currentPanelIndex++;
            IterateToNextQuestion();
        }

        /// <summary>
        /// Handles the event when a voice response panel closes.
        /// Updates the temporary answers dictionary with the base64-encoded voice response, records the response, and proceeds to the next question.
        /// </summary>
        public void OnPanelClosedVoice(int panelId, string base64voice)
        {
            if (tempAnswers.ContainsKey(panelId))
            {
                tempAnswers[panelId] = base64voice;
            }
            else
            {
                tempAnswers.Add(panelId, base64voice);
            }

            ExitPollManager.RecordMicrophoneAnswer(panelId, base64voice);
            currentPanelIndex++;
            IterateToNextQuestion();
        }

        /// <summary>
        /// This method is called when a panel is being closed. It updates or adds the answer (objectValue) for current panel
        /// in the tempAnswers dictionary. The current panel index is decremented and the function proceeds to the previous question.
        /// </summary>
        /// <param name="panelId">The ID of the panel being closed.</param>
        /// <param name="objectValue">The answer value to be stored for the panel.</param>
        public void OnPanelReopen(int panelId, object objectValue)
        {
            // Update or add value in tempAnswers
            if (tempAnswers.ContainsKey(panelId))
            {
                tempAnswers[panelId] = objectValue;
            }
            else
            {
                tempAnswers.Add(panelId, objectValue);
            }
            
            currentPanelIndex--;
            IterateToNextQuestion();
        }

        /// <summary>
        /// Use EndQuestionSet to close the active panel and immediately end the question set
        /// this sets the current panel to null and calls endaction. it assumes the panel closes itself
        /// </summary>
        public void OnPanelError()
        {
            CurrentExitPollPanel = null;
            Cleanup(false);
            Util.logDebug("Exit poll OnPanelError - HMD is null, manually closing question set or new exit poll while one is active");
        }

        /// <summary>
        /// Advances to the next question in the exit poll sequence, handling panel transitions and completion logic.
        /// </summary>
        void IterateToNextQuestion()
        {
            if (GameplayReferences.HMD == null)
            {
                Cleanup(false);
                return;
            }

            bool useLastPanelPosition = false;
            Vector3 lastPanelPosition = Vector3.zero;
            Quaternion lastPanelRotation = Quaternion.identity;

            //close current panel
            if (CurrentExitPollPanel != null)
            {
                lastPanelPosition = CurrentExitPollPanel.transform.position;
                lastPanelRotation = CurrentExitPollPanel.transform.rotation;
                useLastPanelPosition = true;
            }

            if (!useLastPanelPosition)
            {
                if (!ExitPollUtil.GetSpawnPosition(out lastPanelPosition, myparameters))
                {
                    Cognitive3D.Util.logDebug("no last position set. invoke endaction");
                    Cleanup(false);
                    return;
                }
            }

            //if next question, display that
            if (panelProperties.Count > 0 && currentPanelIndex < panelProperties.Count)
            {
                var prefab = ExitPollUtil.GetPrefab(myparameters, panelProperties[currentPanelIndex]);
                if (prefab == null)
                {
                    Util.logError("couldn't find prefab " + panelProperties[currentPanelIndex]);
                    Cleanup(false);
                    return;
                }

                Vector3 spawnPosition = lastPanelPosition;
                Quaternion spawnRotation = lastPanelRotation;

                if (currentPanelIndex == 0)
                {
                    //figure out world spawn position
                    if (myparameters.UseOverridePosition || myparameters.ExitpollSpawnType == ExitPollManager.SpawnType.WorldSpace)
                        spawnPosition = myparameters.OverridePosition;
                    if (myparameters.UseOverrideRotation || myparameters.ExitpollSpawnType == ExitPollManager.SpawnType.WorldSpace)
                        spawnRotation = myparameters.OverrideRotation;
                }

                // Skip voice response if microphone not detected
                var currentPanelProperties = panelProperties[currentPanelIndex];
#if UNITY_WEBGL
                //skip voice questions on webgl - microphone is not supported
                if (currentPanelProperties["type"].Equals("VOICE"))
                {
                    int tempPanelID = currentPanelIndex; // OnPanelClosed takes in PanelID, but since panel isn't initialized yet, we use currentPanelIndex
                                                         // because that is what PanelID gets set to
                    new Cognitive3D.CustomEvent("c3d.ExitPoll detected no microphones")
                        .SetProperty("Panel ID", tempPanelID)
                        .Send();
                    OnPanelClosed(tempPanelID, "Answer" + tempPanelID, short.MinValue);
                    return;
                }
#else
                if (currentPanelProperties["type"].Equals("VOICE") && Microphone.devices.Length == 0)
                {
                    int tempPanelID = currentPanelIndex; // OnPanelClosed takes in PanelID, but since panel isn't initialized yet, we use currentPanelIndex
                                                         // because that is what PanelID gets set to
                    new Cognitive3D.CustomEvent("c3d.ExitPoll detected no microphones")
                        .SetProperty("Panel ID", tempPanelID)
                        .Send();
                    OnPanelClosed(tempPanelID, short.MinValue);
                    return;
                }
#endif

                var newPanelGo = GameObject.Instantiate<GameObject>(prefab,spawnPosition,spawnRotation);
                CurrentExitPollPanel = ExitPollPointer.currentExitPollPanel = newPanelGo.GetComponent<ExitPollPanel>();

                if (CurrentExitPollPanel == null)
                {
                    Debug.LogError(newPanelGo.gameObject.name + " does not have ExitPollPanel component!");
                    GameObject.Destroy(newPanelGo);
                    Cleanup(false);
                    return;
                }
                CurrentExitPollPanel.Initialize(panelProperties[currentPanelIndex], currentPanelIndex, this, questionSet.questions.Length, tempAnswers);

                if (myparameters.ExitpollSpawnType == ExitPollManager.SpawnType.WorldSpace && myparameters.UseAttachTransform)
                {
                    if (myparameters.AttachTransform != null)
                    {
                        newPanelGo.transform.SetParent(myparameters.AttachTransform);
                    }
                }
            }
            else //finished everything format and send
            {
                ExitPollManager.SubmitAllAnswers(questionSet);
                CurrentExitPollPanel = null;
                Cleanup(true);
            }
        }

        //calls end actions
        //calls parameter end events
        //destroys spawned pointers
        void Cleanup(bool completedSuccessfully)
        {
            tempAnswers.Clear();

            if (ExitPollPointer.pointerInstance != null)
            {
                GameObject.Destroy(ExitPollPointer.pointerInstance);
            }

            if (myparameters.OnComplete != null && completedSuccessfully == true)
                myparameters.OnComplete.Invoke();

            if (myparameters.OnClose != null)
                myparameters.OnClose.Invoke();

            if (myparameters.EndAction != null)
            {
                myparameters.EndAction.Invoke(completedSuccessfully);
            }
        }
    }
}
