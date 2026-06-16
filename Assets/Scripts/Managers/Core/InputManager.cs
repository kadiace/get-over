using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager
{
    private InputActionAsset _asset;
    private string _playerMapName = "Player";
    private string _uiMapName = "UI";

    public Define.InputMode Mode { get; private set; } = Define.InputMode.Player;

    public InputActionMap PlayerMap;
    public InputActionMap UIMap;

    public void Init()
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        WebGLInput.captureAllKeyboardInput = true;
#endif

        _asset = Managers.Resource.Load<InputActionAsset>("InputSystem_Actions");
        PlayerMap = _asset.FindActionMap(_playerMapName, throwIfNotFound: true);
        UIMap = _asset.FindActionMap(_uiMapName, throwIfNotFound: true);
        SetMode(Define.InputMode.Player);
    }

    public void SetMode(Define.InputMode mode)
    {
        Mode = mode;

        // Disable everything first (clean slate)
        PlayerMap.Disable();
        UIMap.Disable();

        switch (mode)
        {
            case Define.InputMode.Player:
                PlayerMap.Enable();
#if UNITY_WEBGL && !UNITY_EDITOR
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
#else
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
#endif
                break;

            case Define.InputMode.UI:
                UIMap.Enable();
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;

            case Define.InputMode.Cutscene:
                // none enabled
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                break;
        }
    }
}
