using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public sealed class PlayerController : BaseController<Define.PlayerAnimState>
{
    [Header("Actions (Player Map)")]
    [SerializeField] private string moveActionName = "Move";
    [SerializeField] private string jumpActionName = "Jump";

    [Header("Movement")]
    [SerializeField] private float floorMoveSpeed = 6f;
    [SerializeField] private float airMoveSpeed = 5f;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Jump / Gravity")]
    [SerializeField] private float jumpSpeed = 6f;
    [SerializeField] private float gravityAcceleration = 9.81f;
    [SerializeField] private float jumpGroundIgnoreDuration = 0.12f;
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Jump Relative Speed Clamp")]
    [SerializeField] private float maxUpwardWorldYSpeed = 1f;
    [SerializeField] private float maxDownwardWorldYSpeed = 11f;

    [Header("Ground Check")]
    [SerializeField] private float groundCheckPadding = 0.1f;
    [SerializeField] private float groundSnapMaxDistance = 0.1f;
    [SerializeField] private float groundSnapSurfaceGap = 0.03f;

    [Header("Shadow")]
    [SerializeField] private float shadowDiameter = 0.9f;
    [SerializeField] private float shadowSurfaceOffset = 0.02f;
    [SerializeField] private int shadowTextureSize = 128;
    [SerializeField] private float shadowRayDistance = 20f;

    private const int FloorLayer = 7;

    private CharacterController _characterController;
    private CameraController _cameraController;
    private InputAction _moveAction;
    private InputAction _jumpAction;

    private Vector2 _moveInput;
    private bool _jumpPressedThisFrame;
    private bool _isGrounded;
    private bool _groundedByCollision;
    private float _jumpGroundIgnoreTimer;
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private Vector3 _verticalVelocity;
    private Transform _groundFloorTransform;
    private Vector3 _groundFloorLastPosition;
    private Quaternion _groundFloorLastRotation;
    private Transform _collisionFloorTransform;
    private Vector3 _lastRadialOutward = Vector3.right;
    private Vector3 _lastMoveDirection = Vector3.up;
    private SpriteRenderer _shadowRenderer;
    private Texture2D _shadowTexture;
    private Sprite _shadowSprite;
    private float _groundReferenceDistance;
    private readonly RaycastHit[] _groundHitBuffer = new RaycastHit[16];

    protected override void Init()
    {
        base.Init();
        WorldObjectType = Define.WorldObject.Player;

        _characterController = GetComponent<CharacterController>();
        _cameraController = GetMainCameraController();

        InputActionMap playerMap = Managers.Input.PlayerMap;
        _moveAction = playerMap.FindAction(moveActionName, true);
        _jumpAction = playerMap.FindAction(jumpActionName, true);

        _isGrounded = CheckGroundedByFloorLayer();
        AnimState = Define.PlayerAnimState.IDLE;

        CreateShadowRenderer();
    }

    protected override void UpdateState()
    {
        if (Managers.Input.Mode != Define.InputMode.Player)
        {
            _moveInput = Vector2.zero;
            _jumpPressedThisFrame = false;
            return;
        }

        UpdateInput();
        _groundedByCollision = false;
        _collisionFloorTransform = null;

        if (_jumpPressedThisFrame)
            _jumpBufferTimer = jumpBufferTime;
        else if (_jumpBufferTimer > 0f)
            _jumpBufferTimer -= Time.deltaTime;

        if (_jumpGroundIgnoreTimer > 0f)
            _jumpGroundIgnoreTimer -= Time.deltaTime;

        Vector3 gravityDirection = GetGravityDirectionAwayFromYAxis();
        bool hitByCastBeforeMove = TryGetFloorHit(gravityDirection, out RaycastHit floorHitBeforeMove);
        _isGrounded = CanApplyGrounding() && hitByCastBeforeMove;

        if (hitByCastBeforeMove)
            UpdateGroundFloor(floorHitBeforeMove.collider.transform);

        if (_isGrounded)
            _coyoteTimer = coyoteTime;
        else if (_coyoteTimer > 0f)
            _coyoteTimer -= Time.deltaTime;

        if (_isGrounded && !_jumpPressedThisFrame)
            MoveWithGroundFloor();

        if (_isGrounded && hitByCastBeforeMove && !_jumpPressedThisFrame)
            SnapToFloor(floorHitBeforeMove, gravityDirection);

        Vector3 moveDirection = _isGrounded
            ? GetGroundedCylinderDirection(_moveInput, gravityDirection)
            : GetCameraRelativeDirectionOnPlane(_moveInput, gravityDirection);
        float moveSpeed = _isGrounded ? floorMoveSpeed : airMoveSpeed;
        Vector3 planarVelocity = moveDirection * moveSpeed;

        bool canConsumeJump = CanApplyGrounding() && (_isGrounded || _coyoteTimer > 0f);
        if (canConsumeJump && _jumpBufferTimer > 0f)
        {
            Vector3 floorVelocity = GetCurrentGroundFloorVelocity();
            _isGrounded = false;
            _verticalVelocity = (-gravityDirection * jumpSpeed) + floorVelocity;
            _verticalVelocity.y = Mathf.Clamp(_verticalVelocity.y, -maxDownwardWorldYSpeed, maxUpwardWorldYSpeed);
            _jumpGroundIgnoreTimer = jumpGroundIgnoreDuration;
            _jumpBufferTimer = 0f;
            _coyoteTimer = 0f;
            ClearGroundFloor();
        }
        else if (_isGrounded)
        {
            _verticalVelocity = Vector3.zero;
        }

        if (!_isGrounded)
            _verticalVelocity += gravityDirection * (gravityAcceleration * Time.deltaTime);

        if (!_isGrounded)
            _verticalVelocity.y = Mathf.Clamp(_verticalVelocity.y, -maxDownwardWorldYSpeed, maxUpwardWorldYSpeed);

        Vector3 totalVelocity = planarVelocity + _verticalVelocity;
        _characterController.Move(totalVelocity * Time.deltaTime);

        bool hitByCastAfterMove = TryGetFloorHit(gravityDirection, out RaycastHit floorHitAfterMove);
        _isGrounded = CanApplyGrounding() && (_groundedByCollision || hitByCastAfterMove);

        if (_isGrounded)
        {
            _verticalVelocity = Vector3.zero;
            _coyoteTimer = coyoteTime;

            if (hitByCastAfterMove)
                UpdateGroundFloor(floorHitAfterMove.collider.transform);
            else if (_collisionFloorTransform != null)
                UpdateGroundFloor(_collisionFloorTransform);
        }
        else
        {
            ClearGroundFloor();
        }

        if (_isGrounded && hitByCastAfterMove && !_jumpPressedThisFrame)
            SnapToFloor(floorHitAfterMove, gravityDirection);

        UpdateRotation(moveDirection, gravityDirection);
        UpdateAnimation(moveDirection);
        UpdateShadow(gravityDirection);

        _jumpPressedThisFrame = false;
    }

    private void OnDestroy()
    {
        if (_shadowSprite != null)
            Destroy(_shadowSprite);

        if (_shadowTexture != null)
            Destroy(_shadowTexture);
    }

    private bool CheckGroundedByFloorLayer()
    {
        Vector3 gravityDirection = GetGravityDirectionAwayFromYAxis();
        return TryGetFloorHit(gravityDirection, out _);
    }

    private bool TryGetFloorHit(Vector3 gravityDirection, out RaycastHit hit)
    {
        Vector3 center = transform.TransformPoint(_characterController.center);
        float castDistance = (_characterController.height * 0.5f) + groundCheckPadding;

        int hitCount = Physics.SphereCastNonAlloc(
            center,
            _characterController.radius,
            gravityDirection,
            _groundHitBuffer,
            castDistance,
            1 << FloorLayer,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
        {
            hit = default;
            return false;
        }

        int nearestHitIndex = -1;
        float nearestHitDistance = float.MaxValue;
        int preferredHitIndex = -1;
        float preferredHitDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit candidateHit = _groundHitBuffer[i];
            Collider candidateCollider = candidateHit.collider;
            if (candidateCollider == null || candidateCollider.gameObject.layer != FloorLayer)
                continue;

            if (candidateHit.distance < nearestHitDistance)
            {
                nearestHitDistance = candidateHit.distance;
                nearestHitIndex = i;
            }

            if (_groundFloorTransform == null || !candidateCollider.transform.IsChildOf(_groundFloorTransform))
                continue;

            if (candidateHit.distance < preferredHitDistance)
            {
                preferredHitDistance = candidateHit.distance;
                preferredHitIndex = i;
            }
        }

        if (preferredHitIndex >= 0)
        {
            hit = _groundHitBuffer[preferredHitIndex];
            return true;
        }

        if (nearestHitIndex >= 0)
        {
            hit = _groundHitBuffer[nearestHitIndex];
            return true;
        }

        hit = default;
        return false;
    }

    private void SnapToFloor(RaycastHit floorHit, Vector3 gravityDirection)
    {
        float snapDistance = Mathf.Min(floorHit.distance, groundSnapMaxDistance);
        snapDistance = Mathf.Max(0f, snapDistance - groundSnapSurfaceGap);
        if (snapDistance <= Define.epsilon)
            return;

        _characterController.Move(gravityDirection * snapDistance);
    }

    private void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (hit.collider.gameObject.layer != FloorLayer)
            return;

        if (!CanApplyGrounding())
            return;

        Vector3 objectUp = -GetGravityDirectionAwayFromYAxis();
        if (Vector3.Dot(hit.normal, objectUp) <= 0.25f)
            return;

        _groundedByCollision = true;
        _collisionFloorTransform = hit.collider.transform;
        _verticalVelocity = Vector3.zero;
    }

    private void MoveWithGroundFloor()
    {
        if (_groundFloorTransform == null)
            return;

        Vector3 currentFloorPosition = _groundFloorTransform.position;
        Quaternion currentFloorRotation = _groundFloorTransform.rotation;

        Vector3 floorDelta = currentFloorPosition - _groundFloorLastPosition;
        Quaternion floorRotationDelta = currentFloorRotation * Quaternion.Inverse(_groundFloorLastRotation);

        Vector3 rotatedPlayerPosition = floorRotationDelta * (transform.position - _groundFloorLastPosition) + _groundFloorLastPosition;
        Vector3 rotationDelta = rotatedPlayerPosition - transform.position;
        Vector3 attachDelta = floorDelta + rotationDelta;
        if (attachDelta.sqrMagnitude > Define.epsilon)
            _characterController.Move(attachDelta);

        _groundFloorLastPosition = _groundFloorTransform.position;
        _groundFloorLastRotation = _groundFloorTransform.rotation;
    }

    private Vector3 GetCurrentGroundFloorVelocity()
    {
        if (_groundFloorTransform == null)
            return Vector3.zero;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);
        Vector3 currentFloorPosition = _groundFloorTransform.position;
        return (currentFloorPosition - _groundFloorLastPosition) / dt;
    }

    private void UpdateGroundFloor(Transform floorTransform)
    {
        if (floorTransform == null)
            return;

        Transform attachFloorTransform = ResolveAttachFloorTransform(floorTransform);
        if (attachFloorTransform == null)
            return;

        if (_groundFloorTransform != attachFloorTransform)
        {
            _groundFloorTransform = attachFloorTransform;
            _groundFloorLastPosition = attachFloorTransform.position;
            _groundFloorLastRotation = attachFloorTransform.rotation;
        }
    }

    private static Transform ResolveAttachFloorTransform(Transform floorTransform)
    {
        FloorController floorController = floorTransform.GetComponentInParent<FloorController>();
        return floorController != null ? floorController.transform : floorTransform;
    }

    private void ClearGroundFloor()
    {
        _groundFloorTransform = null;
    }

    private bool CanApplyGrounding()
    {
        return _jumpGroundIgnoreTimer <= 0f;
    }

    private void CreateShadowRenderer()
    {
        GameObject shadowObject = new("PlayerShadow");
        shadowObject.transform.SetParent(transform, false);
        shadowObject.transform.localPosition = Vector3.zero;
        shadowObject.transform.localRotation = Quaternion.identity;

        _shadowRenderer = shadowObject.AddComponent<SpriteRenderer>();
        _shadowRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _shadowRenderer.receiveShadows = false;
        _shadowRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        _shadowRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        _shadowTexture = BuildCircleShadowTexture(shadowTextureSize);
        _shadowSprite = Sprite.Create(
            _shadowTexture,
            new Rect(0f, 0f, _shadowTexture.width, _shadowTexture.height),
            new Vector2(0.5f, 0.5f),
            _shadowTexture.width);

        _shadowRenderer.sprite = _shadowSprite;
        _shadowRenderer.color = new Color(0f, 0f, 0f, 1f);
        _shadowRenderer.enabled = false;
    }

    private static Texture2D BuildCircleShadowTexture(int textureSize)
    {
        int size = Mathf.Max(16, textureSize);
        Texture2D texture = new(size, size, TextureFormat.RGBA32, false);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;

        float half = (size - 1) * 0.5f;
        float edgeStart = 0.88f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - half) / half;
                float dy = (y - half) / half;
                float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                float alpha = 1f - Mathf.InverseLerp(edgeStart, 1f, distance);
                alpha = Mathf.Clamp01(alpha);
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        texture.Apply(false, false);
        return texture;
    }

    private void UpdateShadow(Vector3 gravityDirection)
    {
        if (_shadowRenderer == null)
            return;

        if (!TryGetShadowFloorHit(gravityDirection, out RaycastHit floorHit))
        {
            _shadowRenderer.enabled = false;
            return;
        }

        _shadowRenderer.enabled = true;

        Vector3 shadowPosition = floorHit.point + (floorHit.normal * shadowSurfaceOffset);
        _shadowRenderer.transform.position = shadowPosition;
        _shadowRenderer.transform.rotation = Quaternion.FromToRotation(Vector3.forward, floorHit.normal);
        _shadowRenderer.transform.localScale = new Vector3(shadowDiameter, shadowDiameter, 1f);

        if (_isGrounded)
            _groundReferenceDistance = floorHit.distance;
        else if (_groundReferenceDistance <= 0f)
            _groundReferenceDistance = floorHit.distance;

        float currentHeight = Mathf.Max(0f, floorHit.distance - _groundReferenceDistance);
        float maxJumpHeight = (jumpSpeed * jumpSpeed) / (2f * Mathf.Max(0.01f, gravityAcceleration));
        maxJumpHeight = Mathf.Max(0.01f, maxJumpHeight);
        float alpha = 1f - Mathf.Clamp01(currentHeight / maxJumpHeight);

        _shadowRenderer.color = new Color(0f, 0f, 0f, alpha);
    }

    private bool TryGetShadowFloorHit(Vector3 gravityDirection, out RaycastHit hit)
    {
        Vector3 center = transform.TransformPoint(_characterController.center);
        float distance = Mathf.Max(0.5f, shadowRayDistance);

        return Physics.Raycast(
            center,
            gravityDirection,
            out hit,
            distance,
            1 << FloorLayer,
            QueryTriggerInteraction.Ignore);
    }

    private Vector3 GetGravityDirectionAwayFromYAxis()
    {
        Vector3 radial = new(transform.position.x, 0f, transform.position.z);
        if (radial.sqrMagnitude > Define.epsilon)
        {
            _lastRadialOutward = radial.normalized;
            return _lastRadialOutward;
        }

        return _lastRadialOutward;
    }

    private void UpdateInput()
    {
        _moveInput = _moveAction != null ? _moveAction.ReadValue<Vector2>() : Vector2.zero;
        _jumpPressedThisFrame = _jumpAction != null && _jumpAction.WasPressedThisFrame();
    }

    private void UpdateRotation(Vector3 moveDirection, Vector3 gravityDirection)
    {
        Vector3 objectUp = -gravityDirection;
        Vector3 lookDirection = moveDirection;

        if (lookDirection.sqrMagnitude <= Define.epsilon)
            lookDirection = Vector3.ProjectOnPlane(_lastMoveDirection, objectUp);

        if (lookDirection.sqrMagnitude <= Define.epsilon)
            lookDirection = Vector3.ProjectOnPlane(transform.forward, objectUp);

        if (lookDirection.sqrMagnitude <= Define.epsilon)
            return;

        lookDirection.Normalize();
        _lastMoveDirection = lookDirection;

        Quaternion targetRotation = Quaternion.LookRotation(lookDirection, objectUp);
        transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationSpeed * Time.deltaTime);
    }

    private void UpdateAnimation(Vector3 moveDirection)
    {
        if (!_isGrounded)
        {
            AnimState = Define.PlayerAnimState.JUMP;
            return;
        }

        AnimState = moveDirection.sqrMagnitude > Define.epsilon
            ? Define.PlayerAnimState.RUN
            : Define.PlayerAnimState.IDLE;
    }

    private static CameraController GetMainCameraController()
    {
        CameraController cameraController = Object.FindFirstObjectByType<CameraController>();
        if (cameraController != null)
            return cameraController;

        Camera mainCamera = Camera.main;
        return mainCamera != null ? mainCamera.GetComponent<CameraController>() : null;
    }

    private Vector3 GetCameraRelativeDirectionOnPlane(Vector2 moveInput, Vector3 planeNormal)
    {
        if (_cameraController == null)
            _cameraController = GetMainCameraController();

        Vector3 forward;
        Vector3 right;

        if (_cameraController != null)
        {
            forward = _cameraController.transform.forward;
            right = _cameraController.transform.right;
        }
        else
        {
            forward = transform.forward;
            right = transform.right;
        }

        Vector3 direction = (right * moveInput.x) + (forward * moveInput.y);
        direction = Vector3.ProjectOnPlane(direction, planeNormal);
        return Vector3.ClampMagnitude(direction, 1f);
    }

    private static Vector3 GetGroundedCylinderDirection(Vector2 moveInput, Vector3 gravityDirection)
    {
        Vector3 radialOutward = gravityDirection.normalized;
        Vector3 tangentAroundYAxis = Vector3.Cross(Vector3.up, radialOutward);

        if (tangentAroundYAxis.sqrMagnitude <= Define.epsilon)
            tangentAroundYAxis = Vector3.forward;
        else
            tangentAroundYAxis.Normalize();

        Vector3 direction = (tangentAroundYAxis * moveInput.x) + (Vector3.up * moveInput.y);
        return Vector3.ClampMagnitude(direction, 1f);
    }
}
