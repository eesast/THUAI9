using THUAI9.Unity.CameraControlNS;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace THUAI9.Unity.Playback
{
    public class PlaybackInputHotkeys : MonoBehaviour
    {
        public PlaybackController playbackController;
        public CameraControl cameraControl;
        public float speedStep = 0.5f;
        private void Awake(){ playbackController ??= FindObjectOfType<PlaybackController>(); cameraControl ??= FindObjectOfType<CameraControl>(); }
        private void Update(){ if(IsTypingIntoUI())return; if(Input.GetKeyDown(KeyCode.Space)){ClearNonTextUiSelection();playbackController?.TogglePlayPause();} else if(Input.GetKeyDown(KeyCode.RightArrow))playbackController?.StepForward(); else if(Input.GetKeyDown(KeyCode.LeftArrow))playbackController?.StepBackward(); else if(Input.GetKeyDown(KeyCode.Home))playbackController?.SeekToFrame(0); else if(Input.GetKeyDown(KeyCode.End)&&playbackController!=null)playbackController.SeekToFrame(Mathf.Max(playbackController.TotalFrameCount-1,0)); else if(Input.GetKeyDown(KeyCode.F))cameraControl?.FitToMap(); else if(Input.GetKeyDown(KeyCode.Equals)||Input.GetKeyDown(KeyCode.KeypadPlus))AdjustSpeed(speedStep); else if(Input.GetKeyDown(KeyCode.Minus)||Input.GetKeyDown(KeyCode.KeypadMinus))AdjustSpeed(-speedStep); }
        private void AdjustSpeed(float delta){ if(playbackController!=null)playbackController.SetSpeed(playbackController.playSpeed+delta); }
        private static void ClearNonTextUiSelection(){ GameObject selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null; if(selected==null||selected.GetComponent<InputField>()!=null||selected.GetComponentInChildren<InputField>()!=null)return; EventSystem.current.SetSelectedGameObject(null); }
        private static bool IsTypingIntoUI(){ GameObject selected=EventSystem.current!=null?EventSystem.current.currentSelectedGameObject:null; return selected!=null&&(selected.GetComponent<InputField>()!=null||selected.GetComponentInChildren<InputField>()!=null); }
    }
}
