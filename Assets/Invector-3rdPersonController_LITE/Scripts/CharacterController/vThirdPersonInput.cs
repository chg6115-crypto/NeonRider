using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Invector.vCharacterController
{
    public class vThirdPersonInput : MonoBehaviour
    {
        #region Variables

#if ENABLE_INPUT_SYSTEM
        // 이름을 바꿔서 예전 KeyCode 값(예: LeftShift=304)이 섞여 들어오지 않게 함
        [Header("Controller Input (New Input System)")]
        public Key jumpKey = Key.Space;
        public Key strafeKey = Key.Tab;
        public Key sprintKey = Key.LeftShift;
#else
        [Header("Controller Input")]
        public string horizontalInput = "Horizontal";
        public string verticallInput = "Vertical";
        public KeyCode jumpKey = KeyCode.Space;
        public KeyCode strafeKey = KeyCode.Tab;
        public KeyCode sprintKey = KeyCode.LeftShift;
#endif

        [Header("Camera Input")]
#if ENABLE_INPUT_SYSTEM
        [Tooltip("마우스 감도 (구 Input Manager의 Mouse X/Y와 비슷하게 0.1 기준)")]
        public float mouseSensitivity = 0.1f;
        [Tooltip("게임패드 오른쪽 스틱 감도")]
        public float gamepadLookSensitivity = 3f;
#else
        public string rotateCameraXInput = "Mouse X";
        public string rotateCameraYInput = "Mouse Y";
#endif

        [HideInInspector] public vThirdPersonController cc;
        [HideInInspector] public vThirdPersonCamera tpCamera;
        [HideInInspector] public Camera cameraMain;

        #endregion

        protected virtual void Start()
        {
            InitilizeController();
            InitializeTpCamera();
        }

        protected virtual void FixedUpdate()
        {
            cc.UpdateMotor();               // updates the ThirdPersonMotor methods
            cc.ControlLocomotionType();     // handle the controller locomotion type and movespeed
            cc.ControlRotationType();       // handle the controller rotation type
        }

        protected virtual void Update()
        {
            InputHandle();                  // update the input methods
            cc.UpdateAnimator();            // updates the Animator Parameters
        }

        public virtual void OnAnimatorMove()
        {
            cc.ControlAnimatorRootMotion(); // handle root motion animations 
        }

        #region Basic Locomotion Inputs

        protected virtual void InitilizeController()
        {
            cc = GetComponent<vThirdPersonController>();

            if (cc != null)
                cc.Init();
        }

        protected virtual void InitializeTpCamera()
        {
            if (tpCamera == null)
            {
                tpCamera = FindFirstObjectByType<vThirdPersonCamera>();
                if (tpCamera == null)
                    return;
                if (tpCamera)
                {
                    tpCamera.SetMainTarget(this.transform);
                    tpCamera.Init();
                }
            }
        }

        protected virtual void InputHandle()
        {
            MoveInput();
            CameraInput();
            SprintInput();
            StrafeInput();
            JumpInput();
        }

        public virtual void MoveInput()
        {
#if ENABLE_INPUT_SYSTEM
            Vector2 move = Vector2.zero;
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.wKey.isPressed || kb.upArrowKey.isPressed) move.y += 1f;
                if (kb.sKey.isPressed || kb.downArrowKey.isPressed) move.y -= 1f;
                if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) move.x += 1f;
                if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) move.x -= 1f;
            }
            var pad = Gamepad.current;
            if (pad != null)
            {
                Vector2 stick = pad.leftStick.ReadValue();
                if (stick.sqrMagnitude > move.sqrMagnitude) move = stick;
            }
            move = Vector2.ClampMagnitude(move, 1f);
            cc.input.x = move.x;
            cc.input.z = move.y;
#else
            cc.input.x = Input.GetAxis(horizontalInput);
            cc.input.z = Input.GetAxis(verticallInput);
#endif
        }

        protected virtual void CameraInput()
        {
            if (!cameraMain)
            {
                if (!Camera.main) Debug.Log("Missing a Camera with the tag MainCamera, please add one.");
                else
                {
                    cameraMain = Camera.main;
                    cc.rotateTarget = cameraMain.transform;
                }
            }

            if (cameraMain)
            {
                cc.UpdateMoveDirection(cameraMain.transform);
            }

            if (tpCamera == null)
                return;

#if ENABLE_INPUT_SYSTEM
            Vector2 look = Vector2.zero;
            if (Mouse.current != null)
                look += Mouse.current.delta.ReadValue() * mouseSensitivity;
            if (Gamepad.current != null)
                look += Gamepad.current.rightStick.ReadValue() * gamepadLookSensitivity;
            var X = look.x;
            var Y = look.y;
#else
            var Y = Input.GetAxis(rotateCameraYInput);
            var X = Input.GetAxis(rotateCameraXInput);
#endif

            tpCamera.RotateCamera(X, Y);
        }

        protected virtual void StrafeInput()
        {
            if (KeyDown(strafeKey))
                cc.Strafe();
        }

        protected virtual void SprintInput()
        {
            if (KeyDown(sprintKey))
                cc.Sprint(true);
            else if (KeyUp(sprintKey))
                cc.Sprint(false);
        }

        /// <summary>
        /// Conditions to trigger the Jump animation & behavior
        /// </summary>
        protected virtual bool JumpConditions()
        {
            return cc.isGrounded && cc.GroundAngle() < cc.slopeLimit && !cc.isJumping && !cc.stopMove;
        }

        /// <summary>
        /// Input to trigger the Jump 
        /// </summary>
        protected virtual void JumpInput()
        {
            if (KeyDown(jumpKey) && JumpConditions())
                cc.Jump();
        }

        #endregion

        #region Key helpers (old/new Input 공용)

#if ENABLE_INPUT_SYSTEM
        // 잘못된 Key 값이 들어와도 예외 없이 false 반환
        static bool IsValid(Key key) => key > Key.None && key < Key.IMESelected;

        protected bool KeyDown(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && IsValid(key) && kb[key].wasPressedThisFrame;
        }

        protected bool KeyUp(Key key)
        {
            var kb = Keyboard.current;
            return kb != null && IsValid(key) && kb[key].wasReleasedThisFrame;
        }
#else
        protected bool KeyDown(KeyCode key) => Input.GetKeyDown(key);
        protected bool KeyUp(KeyCode key)   => Input.GetKeyUp(key);
#endif

        #endregion
    }
}