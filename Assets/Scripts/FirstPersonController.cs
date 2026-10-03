using UnityEngine;

/// <summary>
/// CharacterController first-person movement: WASD, mouse look, jump, sprint. Landing on springy
/// matter relaunches the player, and walking into loose matter shoves it.
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("References")]
    public Transform cameraPivot;

    [Header("Movement")]
    public float walkSpeed = 4.5f;
    public float sprintSpeed = 7.5f;
    public float acceleration = 40f;
    [Range(0f, 1f)] public float airControl = 0.4f;
    public float jumpHeight = 1.25f;
    public float gravity = -18f;

    [Header("Look")]
    [Tooltip("Degrees per pixel of mouse movement.")]
    public float lookSensitivity = 0.1f;
    public float maxPitch = 85f;

    [Header("Matter interaction")]
    public float pushForce = 900f;
    [Tooltip("Surfaces whose PhysicsMaterial is at least this bouncy relaunch the player on landing.")]
    public float bounceThreshold = 0.5f;
    public float bounceLaunchSpeed = 15f;

    CharacterController controller;
    Vector3 velocity;
    float yaw, pitch;
    bool bouncedThisMove, shocked;
    Vector3 checkpoint;
    float checkpointYaw;

    public Bounds Bounds => controller ? controller.bounds : new Bounds(transform.position, Vector3.one);
    public Vector3 Velocity => velocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        yaw = transform.eulerAngles.y;
        SetCheckpoint(transform.position, yaw);
    }

    void Start() => LockCursor(true);

    void Update()
    {
        HandleCursor();
        Look();
        Move();
        if (shocked) Shock();
        if (transform.position.y < -40f) Respawn();
    }

    void Shock()
    {
        shocked = false;
        ImpactAudio.Play(ImpactAudio.Zap, transform.position, 1f, 0.9f);
        CameraShake.AddTrauma(0.7f);
        Respawn();
        if (MatterHUD.Instance) MatterHUD.Instance.Flash("ZAPPED", new Color(0.5f, 0.8f, 1f), 2f);
    }

    void HandleCursor()
    {
        if (GameInput.CancelPressed) LockCursor(false);
        else if (GameInput.ClickPressed && Cursor.lockState != CursorLockMode.Locked) LockCursor(true);
    }

    static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    void Look()
    {
        if (Cursor.lockState != CursorLockMode.Locked) return;
        Vector2 delta = GameInput.LookDelta * lookSensitivity;
        yaw += delta.x;
        pitch = Mathf.Clamp(pitch - delta.y, -maxPitch, maxPitch);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (cameraPivot) cameraPivot.localRotation = Quaternion.Euler(pitch, 0f, 0f);
    }

    void Move()
    {
        bool grounded = controller.isGrounded;
        Vector2 input = Vector2.ClampMagnitude(GameInput.Move, 1f);
        float speed = GameInput.SprintHeld ? sprintSpeed : walkSpeed;
        Vector3 wish = (transform.right * input.x + transform.forward * input.y) * speed;
        float accel = acceleration * (grounded ? 1f : airControl);
        Vector3 horizontal = Vector3.MoveTowards(new Vector3(velocity.x, 0f, velocity.z), wish, accel * Time.deltaTime);
        velocity.x = horizontal.x;
        velocity.z = horizontal.z;

        if (grounded && velocity.y < 0f) velocity.y = -2f;
        if (grounded && GameInput.JumpPressed) velocity.y = Mathf.Sqrt(2f * jumpHeight * -gravity);
        velocity.y += gravity * Time.deltaTime;

        bouncedThisMove = false;
        CollisionFlags flags = controller.Move(velocity * Time.deltaTime);
        if ((flags & CollisionFlags.Above) != 0 && velocity.y > 0f && !bouncedThisMove) velocity.y = 0f;
    }

    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (ElectrifiedFloor.IsLive(hit.collider))
        {
            shocked = true; // handled after Move: the controller can't be teleported mid-move
            return;
        }

        if (hit.normal.y > 0.6f && velocity.y < -3f)
        {
            PhysicsMaterial surface = hit.collider.sharedMaterial;
            if (surface != null && surface.bounciness >= bounceThreshold)
            {
                velocity.y = Mathf.Max(-velocity.y * surface.bounciness, bounceLaunchSpeed * surface.bounciness);
                bouncedThisMove = true;
                ImpactAudio.Play(ImpactAudio.Boing, hit.point, 0.8f, 1.15f);
                return;
            }
        }

        // shove loose matter, but not the raft or plank you're standing on
        Rigidbody body = hit.collider.attachedRigidbody;
        if (body == null || body.isKinematic || hit.moveDirection.y < -0.3f || hit.normal.y > 0.7f) return;
        var push = new Vector3(hit.moveDirection.x, 0f, hit.moveDirection.z);
        body.AddForceAtPosition(push * pushForce, hit.point, ForceMode.Force);
    }

    public void SetCheckpoint(Transform spawn) => SetCheckpoint(spawn.position, spawn.eulerAngles.y);

    public void SetCheckpoint(Vector3 position, float yawDegrees)
    {
        checkpoint = position;
        checkpointYaw = yawDegrees;
    }

    public void Respawn() => Teleport(checkpoint, checkpointYaw);

    public void Teleport(Vector3 position, float yawDegrees)
    {
        controller.enabled = false;
        transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yawDegrees, 0f));
        controller.enabled = true;
        yaw = yawDegrees;
        pitch = 0f;
        if (cameraPivot) cameraPivot.localRotation = Quaternion.identity;
        velocity = Vector3.zero;
    }
}
