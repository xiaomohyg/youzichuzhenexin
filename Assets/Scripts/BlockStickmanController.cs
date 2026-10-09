using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class BlockStickmanController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 5f;
    [SerializeField] private float sprintSpeed = 6.2f;
    [SerializeField] private float crouchSpeed = 2.8f;
    [SerializeField] private float jumpHeight = 1.5f;
    [SerializeField] private float gravity = -20f;

    [Header("Camera")]
    [SerializeField] private Transform cameraPivot;
    [SerializeField] private Camera playerCamera;
    [SerializeField] private bool isFirstPerson = true;
    [SerializeField] private Vector3 firstPersonCameraOffset = new Vector3(0f, 0.52f, 0.36f);
    [SerializeField] private Vector3 thirdPersonCameraOffset = new Vector3(0f, 1f, -7f);
    [SerializeField] private float firstPersonFieldOfView = 94f;
    [SerializeField] private float thirdPersonFieldOfView = 60f;
    [SerializeField] private float mouseSensitivity = 0.15f;
    [SerializeField] private float maximumLookAngle = 85f;

    [Header("Crouch")]
    [SerializeField] private Transform visualsRoot;
    [SerializeField] private Transform upperBody;
    [SerializeField] private float standingHeight = 3.8f;
    [SerializeField] private float crouchingHeight = 3f;
    [SerializeField] private float standingEyeHeight = 2.95f;
    [SerializeField] private float crouchingEyeHeight = 2.25f;
    [SerializeField] private float thirdPersonStandingPivotHeight = 2.1f;
    [SerializeField] private float thirdPersonCrouchingPivotHeight = 1.4f;
    [SerializeField] private float crouchTransitionSpeed = 10f;
    [SerializeField] private float crouchBodyDrop = 0.7f;
    [SerializeField] private float crouchLegAngle = 49f;
    [SerializeField] private float standingHipHeight = 1.42f;
    [SerializeField] private float crouchingHipHeight = 0.92f;

    [Header("Block Animation")]
    [SerializeField] private Transform head;
    [SerializeField] private float headLookSmoothness = 14f;
    [SerializeField] private Transform leftArm;
    [SerializeField] private Transform rightArm;
    [SerializeField] private Transform leftLeg;
    [SerializeField] private Transform rightLeg;
    [SerializeField] private Transform leftKnee;
    [SerializeField] private Transform rightKnee;
    [SerializeField] private float limbSwingAngle = 28f;
    [SerializeField] private float limbSwingSpeed = 9f;
    [SerializeField] private float waveDuration = 2f;

    private CharacterController characterController;
    private float verticalVelocity;
    private float cameraPitch;
    private float walkCycle;
    private float waveTimer;
    private float currentCrouchAmount;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        playerCamera = GetComponentInChildren<Camera>(true);
        if (head == null && visualsRoot != null)
        {
            head = FindDescendant(visualsRoot, "Head Pivot");
            if (head == null)
            {
                head = FindDescendant(visualsRoot, "Head");
            }
        }
        FindCrouchRig();
        ApplyViewMode();
    }

    private void OnEnable()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null)
        {
            return;
        }

        UpdateLook(mouse);
        UpdateHeadAnimation();

        if (keyboard.vKey.wasPressedThisFrame)
        {
            TogglePerspective();
        }

        bool isCrouching = keyboard.leftCtrlKey.isPressed || keyboard.cKey.isPressed;
        UpdateCrouch(isCrouching);

        Vector2 movementInput = ReadMovement(keyboard);
        bool isSprinting = keyboard.leftShiftKey.isPressed && !isCrouching && movementInput.y > 0f;
        float moveSpeed = isCrouching ? crouchSpeed : isSprinting ? sprintSpeed : walkSpeed;

        bool isGrounded = characterController.isGrounded;
        if (isGrounded && verticalVelocity < 0f)
        {
            verticalVelocity = -2f;
        }

        if (isGrounded && !isCrouching && keyboard.spaceKey.wasPressedThisFrame)
        {
            verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        verticalVelocity += gravity * Time.deltaTime;
        Vector3 planarMovement = transform.right * movementInput.x + transform.forward * movementInput.y;
        Vector3 velocity = planarMovement * moveSpeed + Vector3.up * verticalVelocity;
        characterController.Move(velocity * Time.deltaTime);

        if (keyboard.eKey.wasPressedThisFrame)
        {
            waveTimer = waveDuration;
        }

        waveTimer = Mathf.Max(0f, waveTimer - Time.deltaTime);
        AnimateLimbs(movementInput.sqrMagnitude, isGrounded, waveTimer > 0f);
        UpdateCursorLock(keyboard, mouse);
    }

    public void Configure(
        Transform pivot,
        Transform newVisualsRoot,
        Transform newLeftArm,
        Transform newRightArm,
        Transform newLeftLeg,
        Transform newRightLeg)
    {
        cameraPivot = pivot;
        visualsRoot = newVisualsRoot;
        head = FindDescendant(newVisualsRoot, "Head Pivot");
        if (head == null)
        {
            head = FindDescendant(newVisualsRoot, "Head");
        }
        leftArm = newLeftArm;
        rightArm = newRightArm;
        leftLeg = newLeftLeg;
        rightLeg = newRightLeg;
        FindCrouchRig();
        cameraPitch = NormalizeAngle(pivot.localEulerAngles.x);
        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>(true);
        }
        ApplyViewMode();
    }

    public void TogglePerspective()
    {
        isFirstPerson = !isFirstPerson;
        ApplyViewMode();
    }

    public void SetFirstPerson(bool enabled)
    {
        isFirstPerson = enabled;
        ApplyViewMode();
    }

    public void SetLookPitch(float pitch)
    {
        cameraPitch = Mathf.Clamp(pitch, -maximumLookAngle, maximumLookAngle);
        if (cameraPivot != null)
        {
            cameraPivot.localRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        }
    }

    private void UpdateLook(Mouse mouse)
    {
        if (mouse == null || Cursor.lockState != CursorLockMode.Locked)
        {
            return;
        }

        Vector2 lookDelta = mouse.delta.ReadValue() * mouseSensitivity;
        transform.Rotate(0f, lookDelta.x, 0f);
        SetLookPitch(cameraPitch - lookDelta.y);
    }

    private void UpdateHeadAnimation()
    {
        if (head == null)
        {
            return;
        }

        float blend = 1f - Mathf.Exp(-headLookSmoothness * Time.deltaTime);
        Quaternion targetRotation = Quaternion.Euler(cameraPitch, 0f, 0f);
        head.localRotation = Quaternion.Slerp(head.localRotation, targetRotation, blend);
    }

    private void UpdateCrouch(bool isCrouching)
    {
        float targetHeight = isCrouching ? crouchingHeight : standingHeight;
        characterController.height = Mathf.MoveTowards(
            characterController.height,
            targetHeight,
            crouchTransitionSpeed * Time.deltaTime);
        characterController.center = Vector3.up * (characterController.height * 0.5f);

        currentCrouchAmount = Mathf.InverseLerp(standingHeight, crouchingHeight, characterController.height);
        if (cameraPivot != null)
        {
            Vector3 pivotPosition = cameraPivot.localPosition;
            float standingPivotHeight = isFirstPerson ? standingEyeHeight : thirdPersonStandingPivotHeight;
            float crouchingPivotHeight = isFirstPerson ? crouchingEyeHeight : thirdPersonCrouchingPivotHeight;
            pivotPosition.y = Mathf.Lerp(standingPivotHeight, crouchingPivotHeight, currentCrouchAmount);
            cameraPivot.localPosition = pivotPosition;
        }

        if (visualsRoot != null)
        {
            visualsRoot.localScale = Vector3.one;
        }
        if (upperBody != null)
        {
            upperBody.localPosition = Vector3.down * (crouchBodyDrop * currentCrouchAmount);
        }
        UpdateHipHeight(leftLeg);
        UpdateHipHeight(rightLeg);
    }

    private static Vector2 ReadMovement(Keyboard keyboard)
    {
        Vector2 movementInput = Vector2.zero;
        if (keyboard.wKey.isPressed) movementInput.y += 1f;
        if (keyboard.sKey.isPressed) movementInput.y -= 1f;
        if (keyboard.dKey.isPressed) movementInput.x += 1f;
        if (keyboard.aKey.isPressed) movementInput.x -= 1f;
        return Vector2.ClampMagnitude(movementInput, 1f);
    }

    private void AnimateLimbs(float movementAmount, bool isGrounded, bool isWaving)
    {
        float targetSwing = 0f;
        if (movementAmount > 0.01f && isGrounded)
        {
            walkCycle += Time.deltaTime * limbSwingSpeed;
            targetSwing = Mathf.Sin(walkCycle) * limbSwingAngle;
        }

        SetLimbRotation(leftArm, Quaternion.Euler(targetSwing, 0f, 0f));

        float walkingBlend = 1f - currentCrouchAmount;
        float hipBend = -crouchLegAngle * currentCrouchAmount;
        float kneeBend = crouchLegAngle * 2f * currentCrouchAmount;
        SetLimbRotation(leftLeg, Quaternion.Euler(hipBend - targetSwing * walkingBlend, 0f, 0f));
        SetLimbRotation(rightLeg, Quaternion.Euler(hipBend + targetSwing * walkingBlend, 0f, 0f));
        SetLimbRotation(leftKnee, Quaternion.Euler(kneeBend, 0f, 0f));
        SetLimbRotation(rightKnee, Quaternion.Euler(kneeBend, 0f, 0f));

        Quaternion rightArmRotation = isWaving
            ? Quaternion.Euler(0f, 0f, 150f + Mathf.Sin(Time.time * 10f) * 18f)
            : Quaternion.Euler(-targetSwing, 0f, 0f);
        SetLimbRotation(rightArm, rightArmRotation);
    }

    private static void SetLimbRotation(Transform limb, Quaternion rotation)
    {
        if (limb != null)
        {
            limb.localRotation = Quaternion.Slerp(limb.localRotation, rotation, 12f * Time.deltaTime);
        }
    }

    private void UpdateHipHeight(Transform hip)
    {
        if (hip == null)
        {
            return;
        }

        Vector3 position = hip.localPosition;
        position.y = Mathf.Lerp(standingHipHeight, crouchingHipHeight, currentCrouchAmount);
        hip.localPosition = position;
    }

    private static void UpdateCursorLock(Keyboard keyboard, Mouse mouse)
    {
        if (keyboard.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void ApplyViewMode()
    {
        if (cameraPivot == null || playerCamera == null)
        {
            return;
        }

        float crouchAmount = characterController == null
            ? 0f
            : Mathf.InverseLerp(standingHeight, crouchingHeight, characterController.height);
        float standingPivotHeight = isFirstPerson ? standingEyeHeight : thirdPersonStandingPivotHeight;
        float crouchingPivotHeight = isFirstPerson ? crouchingEyeHeight : thirdPersonCrouchingPivotHeight;
        cameraPivot.localPosition = new Vector3(
            0f,
            Mathf.Lerp(standingPivotHeight, crouchingPivotHeight, crouchAmount),
            0f);

        Vector3 cameraOffset = isFirstPerson ? firstPersonCameraOffset : thirdPersonCameraOffset;
        playerCamera.transform.localPosition = cameraOffset;
        playerCamera.transform.localRotation = Quaternion.identity;
        playerCamera.fieldOfView = isFirstPerson ? firstPersonFieldOfView : thirdPersonFieldOfView;

    }

    private void FindCrouchRig()
    {
        if (visualsRoot == null)
        {
            return;
        }

        upperBody = FindDescendant(visualsRoot, "Upper Body");
        leftKnee = FindDescendant(visualsRoot, "Left Knee Pivot");
        rightKnee = FindDescendant(visualsRoot, "Right Knee Pivot");
    }

    private static Transform FindDescendant(Transform root, string objectName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == objectName)
            {
                return child;
            }
        }
        return null;
    }

    private static float NormalizeAngle(float angle)
    {
        return angle > 180f ? angle - 360f : angle;
    }
}
