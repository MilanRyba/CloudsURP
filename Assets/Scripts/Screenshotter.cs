using UnityEngine;
using UnityEngine.InputSystem;

public class Screenshotter : MonoBehaviour
{
    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.pKey.wasPressedThisFrame)
            ScreenCapture.CaptureScreenshot("Screenshots/New.png");
    }
}
