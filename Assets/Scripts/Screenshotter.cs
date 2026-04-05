using UnityEngine;
using UnityEngine.InputSystem;

public class Screenshotter : MonoBehaviour
{
    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.pKey.isPressed)
            ScreenCapture.CaptureScreenshot("Screenshots/New.png");
    }
}
