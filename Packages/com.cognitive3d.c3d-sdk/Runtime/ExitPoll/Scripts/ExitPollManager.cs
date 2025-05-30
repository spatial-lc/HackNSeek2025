using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.XR;

namespace Cognitive3D
{
    public static class ExitPollManager
    {
        /// <summary>
        /// The hook (set on the dashboard) to access
        /// </summary>
        public static string HookName;

        /// <summary>
        /// Representing the different spawn types for exit poll panels:
        /// - <c>WorldSpace</c>: Spawned in the world space.
        /// - <c>PlayerRelativeSpace</c>: Spawned relative to the player's position.
        /// </summary>
        public enum SpawnType
        {
            WorldSpace,
            PlayerRelativeSpace
        }

        /// <summary>
        /// Representing the different pointer types for user interaction:
        /// - <c>HMD</c>: HMD or gaze for pointing.
        /// - <c>ControllersAndHands</c>: Interaction with controllers and hands for pointing.
        /// - <c>Custom</c>: Custom pointer interaction defined by the user.
        /// </summary>
        public enum PointerType
        {
            HMD,
            ControllersAndHands,
            Custom
        }

        /// <summary>
        /// Representing the input buttons available for user interaction:
        /// - <c>Trigger</c>: The trigger button on the controller.
        /// - <c>Grip</c>: The grip button on the controller.
        /// - <c>PrimaryButton</c>: The primary action button (e.g., A or X).
        /// - <c>SecondaryButton</c>: The secondary action button (e.g., B or Y).
        /// - <c>Primary2DAxisClick</c>: The click action on the 2D axis of the controller.
        /// </summary>
        public enum PointerInputButton
        {
            Trigger,
            Grip,
            PrimaryButton,
            SecondaryButton,
            Primary2DAxisClick
        }

        /// <summary>
        /// called when getting the questions fails for any reason
        /// </summary>
        public static UnityEngine.Events.UnityEvent OnSetupFailed = new UnityEngine.Events.UnityEvent();

        /// <summary>
        /// called when questions returned and parsed. Accessible in QuestionProperties
        /// </summary>
        public static UnityEngine.Events.UnityEvent OnSetupComplete = new UnityEngine.Events.UnityEvent();

        /// <summary>
        /// called when the answers are recorded and sent
        /// </summary>
        public static UnityEngine.Events.UnityEvent OnSurveyComplete = new UnityEngine.Events.UnityEvent();

        /// <summary>
        /// called when the microphone begins recording
        /// </summary>
        public static UnityEngine.Events.UnityEvent OnMicrophoneRecordingBegin = new UnityEngine.Events.UnityEvent();

        /// <summary>
        /// called when the microphone recording time has expired
        /// </summary>
        public static UnityEngine.Events.UnityEvent OnMicrophoneRecordingTimeUp = new UnityEngine.Events.UnityEvent();

        public static ExitPollParameters NewExitPoll(string hookName, ExitPollParameters parameters)
        {
            parameters.Hook = hookName;
            return parameters;
        }

#region Internal Variables
        private static double StartTime;
        private static ExitPollData currentExitpollData;

        //User answers
        static List<ResponseContext> exitpollResponseProperties = new List<ResponseContext>();
        static Dictionary<string, object> exitpollEventProperties = new Dictionary<string, object>();
#endregion
        
        /// <summary>
        /// Asynchronously fetches the exit poll question sets. 
        /// This function should be called during OnSessionBegin if the user wants to immediately enable the exit poll upon Awake or OnEnable.
        /// Returns the exit poll data after waiting for the network response.
        /// </summary>
        /// <returns>Task<ExitPollData> - Returns the exit poll data once the asynchronous request is complete.</returns>
        public static async Task<ExitPollData> GetExitPollQuestionSets()
        {
            if (Cognitive3D_Manager.Instance == null)
            {
                Util.logDebug("Cannot display exitpoll. Cognitive3DManager not present in scene");
                return null;
            }

            // Using a TaskCompletionSource to wait for the asynchronous callback to complete
            var tcs = new TaskCompletionSource<ExitPollData>();

            Cognitive3D.NetworkManager.GetExitPollQuestions(HookName, (responseCode, error, text) =>
            {
                var exitPollData = ProcessExitPollResponse(responseCode, error, text);
                tcs.SetResult(exitPollData);
            }, 3);

            // Waiting for the result to be available and return it
            return await tcs.Task;
        }

        /// <summary>
        /// Records a user's answer, to be submitted later
        /// boolean - answerValue is 0 false, 1 true
        /// multiple choice - answerValue starts at 1 for the first option, 2 for the second option, etc to a maximum of 4
        /// scale - answerValue matches the number displayed (scale can allow '0' values)
        /// </summary>
        /// <param name="questionIndex"></param>
        /// <param name="answerValue"></param>
        public static void RecordAnswer(int questionIndex, int answerValue)
        {
            string key = "Answer" + questionIndex;

            if (exitpollEventProperties.ContainsKey(key))
            {
                exitpollEventProperties[key] = answerValue;
            }
            else
            {
                exitpollEventProperties.Add(key, answerValue);
            }

            if (questionIndex < exitpollResponseProperties.Count)
            {
                exitpollResponseProperties[questionIndex].ResponseValue = answerValue;
            }
            else
            {
                Util.logError("Exitpoll expected response not defined for question index " + questionIndex);
            }
        }

        /// <summary>
        /// Records a user's microphone answer using a Base64 WAV string
        /// Use with CompleteMicrophoneRecording to get the audioclip format
        /// </summary>
        /// <param name="questionIndex"></param>
        /// <param name="base64voice"></param>
        public static void RecordMicrophoneAnswer(int questionIndex, string base64voice)
        {
            string key = "Answer" + questionIndex;

            if (exitpollEventProperties.ContainsKey(key))
            {
                exitpollEventProperties[key] = 0;
            }
            else
            {
                exitpollEventProperties.Add(key, 0);
            }

            if (questionIndex < exitpollResponseProperties.Count)
            {
                exitpollResponseProperties[questionIndex].ResponseValue = base64voice;
            }
            else
            {
                Util.logError("Exitpoll expected response not defined for question index " + questionIndex);
            }
        }

        /// <summary>
        /// Records a user's microphone answer using an AudioClip
        /// Converts the AudioClip to a Base64 WAV string
        /// </summary>
        /// <param name="questionIndex">Index of the question</param>
        /// <param name="audioClip">Recorded AudioClip</param>
        public static void RecordMicrophoneAnswer(int questionIndex, AudioClip audioClip)
        {
            byte[] bytes;
            MicrophoneUtility.Save(audioClip, out bytes);
            string encodedWav = MicrophoneUtility.EncodeWav(bytes);
            RecordMicrophoneAnswer(questionIndex, encodedWav);
        }

        /// <summary>
        /// Submits all the answers collected for an exit poll, formatted correctly for transmission.
        /// It also clears the QuestionProperties after submission to prepare for any future responses.
        /// </summary>
        /// <param name="questionSet">The exit poll data containing the questions and poll details.</param>
        public static void SubmitAllAnswers(ExitPollData questionSet)
        {
            SendResponsesAsCustomEvents(HookName, questionSet);

            string responseBody = CoreInterface.SerializeExitpollAnswers(exitpollResponseProperties, questionSet.id, HookName);
            NetworkManager.PostExitpollAnswers(responseBody, questionSet.name, questionSet.version);

            Cleanup();
        }

        /// <summary>
        /// Submits all the answers collected for the current exit poll, formatted correctly for transmission.
        /// If no specific question set is provided, it uses the currentExitpollData stored in memory.
        /// It also clears the QuestionProperties after submission to prepare for any future responses.
        /// </summary>
        public static void SubmitAllAnswers()
        {
            if (currentExitpollData != null)
            {
                SendResponsesAsCustomEvents(HookName, currentExitpollData);

                string responseBody = CoreInterface.SerializeExitpollAnswers(exitpollResponseProperties, currentExitpollData.id, HookName);
                NetworkManager.PostExitpollAnswers(responseBody, currentExitpollData.name, currentExitpollData.version);

                Cleanup();
            }
        }

        /// <summary>
        /// Processes the response from the exit poll request, parsing the JSON data and checking for errors.
        /// If the data is valid, it stores the parsed exit poll data and invokes the setup completion events.
        /// If there are any issues, it logs an error and invokes the setup failure event.
        /// </summary>
        private static ExitPollData ProcessExitPollResponse(int responseCode, string error, string text)
        {
            if (responseCode != 200)
            {
                Util.logError("Failed to fetch ExitPoll data. Response code is " + responseCode);
            }

            if (string.IsNullOrEmpty(text))
            {
                Debug.LogError("C3D Exitpoll returned empty text");
                OnSetupFailed?.Invoke();
                return null;
            }

            ExitPollData _exitPollData;
            try
            {
                _exitPollData = JsonUtility.FromJson<ExitPollData>(text);
            }
            catch
            {
                Debug.LogError("C3D Exitpoll questions not formatted correctly");
                OnSetupFailed?.Invoke();
                return null;
            }

            if (_exitPollData.questions == null || _exitPollData.questions.Length == 0)
            {
                Debug.LogError("C3D Exitpoll has no questions");
                OnSetupFailed?.Invoke();
                return null;
            }

            for (int i = 0; i < _exitPollData.questions.Length; i++)
            {
                exitpollResponseProperties.Add(new ResponseContext(_exitPollData.questions[i].type));
            }

            currentExitpollData = _exitPollData;

            OnSetupComplete?.Invoke();
            StartTime = Util.Timestamp();

            return _exitPollData;
        }

        /// <summary>
        /// Sends the collected exit poll responses as custom events.
        /// </summary>
        /// <param name="questionSet">The exit poll data containing the questions and poll details to be sent with the custom event.</param>
        internal static void SendResponsesAsCustomEvents(string hookName, ExitPollData questionSet)
        {
            var exitpollEvent = new CustomEvent("cvr.exitpoll");
            exitpollEvent.SetProperty("userId", Cognitive3D_Manager.DeviceId);
            if (!string.IsNullOrEmpty(Cognitive3D_Manager.ParticipantId))
            {
                exitpollEvent.SetProperty("participantId", Cognitive3D_Manager.ParticipantId);
            }
            exitpollEvent.SetProperty("questionSetId", questionSet.id);
            exitpollEvent.SetProperty("hook", hookName);
            exitpollEvent.SetProperty("duration", Util.Timestamp() - StartTime);

            var scenesettings = Cognitive3D_Manager.TrackingScene;
            if (scenesettings != null && !string.IsNullOrEmpty(scenesettings.SceneId))
            {
                exitpollEvent.SetProperty("sceneId", scenesettings.SceneId);
            }

            foreach (var property in exitpollEventProperties)
            {
                exitpollEvent.SetProperty(property.Key, property.Value);
            }
            exitpollEvent.Send();
            Cognitive3D_Manager.FlushData();
        }

        private static void Cleanup()
        {
            currentExitpollData = null;
            exitpollResponseProperties.Clear();
            exitpollEventProperties.Clear();
        }

        //after a panel has been answered, the responses from each panel in a format to be sent to exitpoll microservice
        public class ResponseContext
        {
            public string QuestionType;
            public object ResponseValue;
            public ResponseContext(string questionType)
            {
                QuestionType = questionType;
            }
        }
    }
}