using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public enum BodyPart { Head, LeftArm, RightArm, LeftLeg, RightLeg, Torso }

[System.Serializable]
public struct ComboHit
{
    public float damage;    // overlap circle damage (slice damage is shared via sliceDamage field)
    public float range;
    public float knockback;
    public float startup;
    public float recovery;
}

[System.Serializable]
public struct BodyPartDebuff
{
    [Range(0f, 1f)] public float speedMultiplier;       // movement speed scale
    [Range(0f, 1f)] public float jumpMultiplier;        // jump force scale (0 = unable to jump)
    [Range(0f, 1f)] public float attackDamageMultiplier;// light/heavy damage scale
    [Range(0f, 1f)] public float staminaCostMultiplier; // stamina cost scale (higher = costs more)
    [Range(0f, 1f)] public float hpMaxMultiplier;       // max HP scale
    [Range(0f, 3f)] public float hpDrainMultiplier;     // HP drain rate scale
    [Range(1f, 5f)] public float attackDelayMultiplier; // attack animation time scale
    [Range(1f, 5f)] public float dodgeDelayMultiplier;  // dodge duration scale
    [Range(1f, 5f)] public float stunMultiplier;        // stun duration scale when this part is slayed
}

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerControl : MonoBehaviour, IDamageable
{
    public static PlayerControl Instance { get; private set; }

    // ���� Movement ����������������������������������������������������������������������������������������������������������������������������

    [Header("Movement")]
    public float speed = 5f;
    public float jumpForce = 10f;
    public float coyoteTime = 0.12f;
    public Transform groundCheck;
    public LayerMask groundLayer;

    // ���� Health ��������������������������������������������������������������������������������������������������������������������������������

    [Header("Health")]
    public float maxHp = 100f;
    public float CurrentHp { get; private set; }
    public bool IsDead { get; set; }

    // ���� Stamina ������������������������������������������������������������������������������������������������������������������������������

    [Header("Stamina")]
    public float maxStamina = 100f;
    public float staminaRegen = 20f;
    public float staminaRegenDelay = 1.2f;
    public float CurrentStamina { get; private set; }

    // ���� Blood ��������������������������������������������������������������������������������������������������������������������������������

    [Header("BloodGage")]
    public float maxBloodGage = 100f;
    public float CurrentBloodGage { get; private set; }

    [Header("BloodMoney")]
    public int CurrentBloodMoney { get; private set; }

    // ── Body Part Debuffs ─────────────────────────────────────────────────────

    [Header("Body Part Debuffs")]
    [Tooltip("One entry per BodyPart enum value (Head, LeftArm, RightArm, LeftLeg, RightLeg, Torso).")]
    public BodyPartDebuff[] bodyPartDebuffs = new BodyPartDebuff[6];

    // tracks which parts have been slayed this run
    readonly bool[] _slayedParts = new bool[6];

    // priority order used when blood gauge heals a part
    static readonly BodyPart[] HealPriority =
    {
        BodyPart.Torso,
        BodyPart.Head,
        BodyPart.LeftArm,
        BodyPart.RightArm,
        BodyPart.LeftLeg,
        BodyPart.RightLeg,
    };

    // ── Leg Debuff Computed ───────────────────────────────────────────────────
    int SlayedLegCount =>
        (_slayedParts[(int)BodyPart.LeftLeg] ? 1 : 0) +
        (_slayedParts[(int)BodyPart.RightLeg] ? 1 : 0);

    float EffectiveSpeed => speed * BasicStatMultiplier * (_isRunning ? runSpeedMultiplier : 1f) * (SlayedLegCount == 2 ? 0.05f : SlayedLegCount == 1 ? 0.50f : 1f);
    float EffectiveJumpForce => jumpForce * (SlayedLegCount == 2 ? 0f : SlayedLegCount == 1 ? 0.50f : 1f);
    bool CanJump => SlayedLegCount < 2;
    bool AllLimbsCut => SlayedLegCount == 2 && !CanWeakAttack && !CanStrongAttack;
    float AttackDelayMultiplier => (SlayedLegCount == 2 ? 2.2f : SlayedLegCount == 1 ? 1.5f : 1f) * AttackSpeedTimeScale;
    float DodgeDelayMultiplier => SlayedLegCount == 2 ? 2.0f : 1f;
    float EffectiveDodgeForce => dodgeForce * (SlayedLegCount == 2 ? 0.5f : 1f);

    // ── Arm Debuff Computed ───────────────────────────────────────────────────
    bool CanWeakAttack => !_slayedParts[(int)BodyPart.LeftArm];
    bool CanStrongAttack => !_slayedParts[(int)BodyPart.RightArm];

    float ArmStunMultiplier
    {
        get
        {
            float m = 1f;
            if (_slayedParts[(int)BodyPart.LeftArm]) m *= bodyPartDebuffs[(int)BodyPart.LeftArm].stunMultiplier;
            if (_slayedParts[(int)BodyPart.RightArm]) m *= bodyPartDebuffs[(int)BodyPart.RightArm].stunMultiplier;
            return m;
        }
    }

    // ── Stun ──────────────────────────────────────────────────────────────────

    [Header("Stun")]
    [Tooltip("Base stun duration in seconds. Scaled up by each slayed arm's stunMultiplier.")]
    public float stunDuration = 0.6f;
    [Tooltip("Accumulated stun needed to enter downed state.")]
    public float downThreshold = 2.5f;
    [Tooltip("How fast the stun accumulator decays per second while upright.")]
    public float stunDecayRate = 1f;

    // ── HP Drain ──────────────────────────────────────────────────────────────

    [Header("HP Drain")]
    [Tooltip("HP lost per second passively. Set to 0 to disable.")]
    public float hpDrainPerSecond = 2f;
    [Tooltip("HP drain cannot reduce HP below this value.")]
    public float hpDrainFloor = 1f;

    // ── Sunproof Guard ────────────────────────────────────────────────────────

    [Header("Sunproof Guard")]
    [Tooltip("Maximum sunproof guard. Set to 0 to disable.")]
    public float maxSunproofGuard = 0f;
    public float CurrentSunproofGuard { get; private set; }

    // ���� Light Attack ��������������������������������������������������������������������������������������������������������������������

    [Header("Combo")]
    public float lightStaminaCost = 20f;
    public float comboWindowDuration = 0.5f;
    [Tooltip("Each entry is one combo step — fires overlap + slice simultaneously.")]
    public ComboHit[] comboHits = new ComboHit[3];

    // ── Run ───────────────────────────────────────────────────────────────────

    [Header("Run")]
    [Tooltip("Speed multiplier applied while running.")]
    public float runSpeedMultiplier = 1.6f;
    [Tooltip("Seconds of holding Shift while moving before running starts.")]
    public float runHoldTime = 0.3f;

    bool _isRunning;
    float _runHoldTimer;
    bool _shiftDown;

    // ── Bloodloss ─────────────────────────────────────────────────────────────

    [Header("Bloodloss")]
    public float bleedDps = 5f;
    public float bleedDuration = 4f;

    // ── Deathblow ─────────────────────────────────────────────────────────────

    [Header("Deathblow")]
    public float deathblowRange = 2f;
    public float deathblowBloodGain = 50f;
    public float deathblowSliceForce = 10f;

    // ── Quickdraw ─────────────────────────────────────────────────────────────

    [Header("Quickdraw")]
    public float quickdrawRange = 6f;
    public float quickdrawDamage = 30f;
    public float quickdrawStaminaCost = 30f;
    public float quickdrawCooldown = 1.0f;
    public float quickdrawBleedDps = 5f;
    public float quickdrawBleedDuration = 3f;

    // ── Smashdown ─────────────────────────────────────────────────────────────

    [Header("Smashdown")]
    public float smashdownDamage = 25f;
    public float smashdownRange = 0.9f;
    public float smashdownKnockback = 6f;
    public float smashdownStaminaCost = 20f;
    public float smashdownStartup = 0.1f;
    public float smashdownRecovery = 0.35f;
    public float smashdownCooldown = 1.0f;

    // ── Body Slam ─────────────────────────────────────────────────────────────

    [Header("Body Slam")]
    public float bodySlamStaminaCost = 30f;
    public float bodySlamSlipSpeed = 10f;
    public float bodySlamDuration = 0.45f;
    public float bodySlamHitRadius = 0.55f;
    public float bodySlamDamage = 20f;
    public float bodySlamKnockback = 8f;
    public float bodySlamSelfDamage = 10f;
    public float bodySlamSelfKnockback = 12f;
    public float bodySlamCooldown = 1.5f;

    // ── Grab Throw ────────────────────────────────────────────────────────────

    [Header("Grab Throw")]
    public float grabThrowStaminaCost = 30f;
    public float grabThrowCooldown = 1.0f;
    public float grabThrowStartup = 0.3f;
    public float grabRange = 1.5f;
    public float throwForce = 14f;
    public float throwCollisionDamage = 25f;
    public float throwDuration = 0.8f;

    // ── Berserker Mode ────────────────────────────────────────────────────────

    [Header("Berserker Mode")]
    public float berserkerDuration = 10f;
    [Tooltip("Seconds of holding Gather button to activate manually.")]
    public float berserkerHoldTime = 1.0f;
    public float berserkerActivationRange = 5f;
    public GameObject swordAuraPrefab;
    public float swordAuraDamage = 40f;
    public float swordAuraSpeed = 18f;
    public float swordAuraLifetime = 1.5f;

    // ── Rising Attack ─────────────────────────────────────────────────────────

    [Header("Rising Attack")]
    public float risingDamage = 20f;
    public float risingRange = 1.0f;
    public float risingKnockback = 12f;
    public float risingStaminaCost = 20f;
    public float risingStartup = 0.08f;
    public float risingRecovery = 0.3f;

    // ���� Ladder ��������������������������������������������������������������������������������������������������������������������������������

    [Header("Ladder")]
    public float climbSpeed = 3f;

    // ���� Dodge ����������������������������������������������������������������������������������������������������������������������������������

    [Header("Dodge")]
    public float dodgeForce = 10f;
    public float dodgeStaminaCost = 25f;
    public float dodgeDuration = 0.3f;
    public float iFrameDuration = 0.25f;

    // ���� Guard / Parry ������������������������������������������������������������������������������������������������������������������

    [Header("Guard")]
    public float guardDamageReduction = 0.6f;
    public float parryWindowDuration = 0.2f;
    public float parryStaminaCost = 15f;

    // ���� Slice ����������������������������������������������������������������������������������������������������������������������������������

    [Header("Slice")]
    public float sliceRange = 5f;
    public float sliceDamage = 999f;
    public float sliceCooldown = 0.6f;
    public float sliceStaminaCost = 30f;
    public float sliceForce;  // multiplied by damage to get actual slice force

    [Header("Slash Visual")]
    public Color slashColor = new Color(1f, 1f, 0.3f, 0.85f);
    public float slashDuration = 0.07f;
    public float slashWidth = 0.05f;

    // ���� Layers / References ������������������������������������������������������������������������������������������������������

    [Header("Layers")]
    public LayerMask enemyLayer;
    [Tooltip("Platform layers the player should pass through while on a ladder.")]
    public LayerMask passthroughPlatformLayer;

    [Header("Blood Sphere")]
    [SerializeField] private float bloodSphereCooldown = 3f;

    [Header("Interaction")]
    public float interactRange = 1.5f;
    public LayerMask interactLayer;
    public GameObject interactPrompt;

    [Header("References")]
    public Transform hitPoint;
    [SerializeField] private GameObject bloodSpherePrefab;
    [SerializeField] private Skillwheel skillwheel;

    // ���� State ����������������������������������������������������������������������������������������������������������������������������������

    public bool IsInCutscene { get; private set; }
    public bool IsInvincible { get; private set; }
    public bool IsGuarding { get; private set; }
    public bool IsStunned { get; private set; }
    public bool IsDown { get; private set; }
    float _stunAccumulator;

    bool isParryActive;
    bool isAttacking;
    bool isDodging;
    bool isSlamming;

    public bool IsBerserker { get; private set; }
    float _berserkerTimer;
    bool _berserkerUsed;
    bool _gatherHeld;
    float _gatherHoldTimer;

    float _quickdrawCooldownTimer;
    float _smashdownCooldownTimer;
    float _bodySlamCooldownTimer;
    float _grabThrowCooldownTimer;
    float _bloodSphereCooldownTimer;

    int comboStep;
    float comboTimer;
    bool comboInputQueued;

    float staminaRegenTimer;
    float moveInput;
    float climbInput;
    bool jumpQueued;
    float _coyoteTimer;
    int lastFacingDir = 1;

    bool isOnSlope;
    Vector2 slopeNormal;

    bool isNearLadder;
    bool isOnLadder;
    Ladder _currentLadder;

    bool _interactEnabled = true;

    LineRenderer slashLine;

    Rigidbody2D rb;
    SpriteRenderer sr;
    _2DActions actions;
    IInteractable _interactTarget;
    EffectGenerator effects;
    public BloodPuddleMaker bloodPuddleMaker;
    PlayerMagicSkill playerMagicSkill;
    ImsiIlustChanger _illustChanger;
    ItemConsumer _itemConsumer;
    PlayerBasicStatContainer _basicStats;

    // ���� Unity ����������������������������������������������������������������������������������������������������������������������������������

    void Awake()
    {
        if (Instance != null) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        rb = GetComponent<Rigidbody2D>();
        rb.freezeRotation = true;
        sr = GetComponent<SpriteRenderer>();

        // PlayerBasicStatContainer is optional — when present, its stats are authoritative
        // and override the Inspector defaults; when absent, the Inspector defaults stand.
        _basicStats = GetComponent<PlayerBasicStatContainer>();
        if (_basicStats != null)
        {
            maxHp = _basicStats.HealthPoint;
            maxStamina = _basicStats.StaminaPoint;
            speed = _basicStats.MoveSpeedPoint;
        }

        CurrentHp = maxHp;
        CurrentStamina = maxStamina;
        CurrentSunproofGuard = maxSunproofGuard;

        actions = new _2DActions();
        slashLine = BuildSlashLine();
        effects = GetComponent<EffectGenerator>();
        bloodPuddleMaker = GetComponent<BloodPuddleMaker>();
        playerMagicSkill = GetComponent<PlayerMagicSkill>();
        _illustChanger = GetComponent<ImsiIlustChanger>();
        _itemConsumer = GetComponent<ItemConsumer>();
        if (skillwheel == null)
            skillwheel = FindFirstObjectByType<Skillwheel>();
    }

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    void OnEnable()
    {
        actions.Player2D.Move.performed += OnMove;
        actions.Player2D.Move.canceled += OnMove;
        actions.Player2D.Jump.performed += OnJump;
        actions.Player2D.LightAttack.performed += OnAttack;
        actions.Player2D.Dodge.performed += OnDodge;
        actions.Player2D.Dodge.canceled += OnDodgeRelease;
        actions.Player2D.Guard.performed += OnGuardStart;
        actions.Player2D.Guard.canceled += OnGuardEnd;
        actions.Player2D.Gather.started += OnGatherDown;
        actions.Player2D.Gather.performed += OnGather;
        actions.Player2D.Gather.canceled += OnGatherUp;
        actions.Player2D.Skill.started += OnSkillPress;
        actions.Player2D.Skill.canceled += OnSkillRelease;
        actions.Player2D.SkillChange.started += OnSkillWheelOpen;
        actions.Player2D.SkillChange.canceled += OnSkillWheelClose;
        actions.Player2D.GrabThrow.performed += OnGrabThrow;
        actions.Player2D.Interact.performed += OnInteract;
        actions.Player2D.UseItem1.performed += OnUseItem1;
        actions.Player2D.UseItem2.performed += OnUseItem2;
        actions.Player2D.UseItem3.performed += OnUseItem3;
        actions.Player2D.Enable();
        GameManager.OnStateChanged += OnGameStateChanged;
        if (GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Boot)
            SetInputEnabled(false);
    }

    void OnDisable()
    {
        GameManager.OnStateChanged -= OnGameStateChanged;
        actions.Player2D.Move.performed -= OnMove;
        actions.Player2D.Move.canceled -= OnMove;
        actions.Player2D.Jump.performed -= OnJump;
        actions.Player2D.LightAttack.performed -= OnAttack;
        actions.Player2D.Dodge.performed -= OnDodge;
        actions.Player2D.Dodge.canceled -= OnDodgeRelease;
        actions.Player2D.Guard.performed -= OnGuardStart;
        actions.Player2D.Guard.canceled -= OnGuardEnd;
        actions.Player2D.Gather.started -= OnGatherDown;
        actions.Player2D.Gather.performed -= OnGather;
        actions.Player2D.Gather.canceled -= OnGatherUp;
        actions.Player2D.Skill.started -= OnSkillPress;
        actions.Player2D.Skill.canceled -= OnSkillRelease;
        actions.Player2D.SkillChange.started -= OnSkillWheelOpen;
        actions.Player2D.SkillChange.canceled -= OnSkillWheelClose;
        actions.Player2D.GrabThrow.performed -= OnGrabThrow;
        actions.Player2D.Interact.performed -= OnInteract;
        actions.Player2D.UseItem1.performed -= OnUseItem1;
        actions.Player2D.UseItem2.performed -= OnUseItem2;
        actions.Player2D.UseItem3.performed -= OnUseItem3;
        actions.Player2D.Disable();
    }

    void Start()
    {
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
    }

    void Update()
    {
        TickEffectiveCaps();
        TickComboWindow();
        TickStaminaRegen();
        TickHpDrain();
        TickRun();
        TickBerserker();
        TickStunAccumulator();
        TickSkillCooldowns();
        TickCoyoteTime();
        TickDeathblowIndicator();
        TickInteraction();
        TickRender();
        CheckSlope();
    }

    void FixedUpdate()
    {
        if (IsDead) return;
        if (isDodging || isSlamming) return;

        if (isOnLadder)
        {
            rb.gravityScale = 0f;
            rb.linearVelocity = new Vector2(0f, climbInput * climbSpeed);

            // Snap horizontal position to ladder
            if (_currentLadder != null)
                rb.position = new Vector2(_currentLadder.LadderX, rb.position.y);

            // Auto-exit at bottom
            if (_currentLadder != null && rb.position.y <= _currentLadder.BottomY)
            {
                ExitLadder();
                return;
            }

            // Auto-exit at top — place player above the ladder
            if (_currentLadder != null && rb.position.y >= _currentLadder.TopY)
            {
                rb.position = new Vector2(_currentLadder.LadderX, _currentLadder.TopY);
                ExitLadder();
                return;
            }

            return;
        }

        if (jumpQueued)
        {
            rb.gravityScale = 1f;
            rb.linearVelocity = new Vector2(moveInput * EffectiveSpeed, EffectiveJumpForce);
            jumpQueued = false;
            return;
        }

        if (isOnSlope && moveInput != 0)
        {
            Vector2 slopeDir = new(slopeNormal.y, -slopeNormal.x);
            rb.gravityScale = 0f;
            rb.linearVelocity = moveInput * EffectiveSpeed * slopeDir;
        }
        else if (isOnSlope && moveInput == 0)
        {
            rb.gravityScale = 0f;
            rb.linearVelocity = Vector2.zero;
        }
        else
        {
            rb.gravityScale = 1f;
            rb.linearVelocity = new Vector2(moveInput * EffectiveSpeed, rb.linearVelocity.y);
        }
    }

    // ���� Input Callbacks ��������������������������������������������������������������������������������������������������������������
    void CheckSlope()
    {
        if (IsGrounded())
        {
            RaycastHit2D hit = Physics2D.Raycast(groundCheck.position, Vector2.down, 0.3f, groundLayer);
            if (hit.collider != null)
            {
                float slopeAngle = Vector2.Angle(hit.normal, Vector2.up);
                isOnSlope = slopeAngle > 0f && slopeAngle <= 45f;
                slopeNormal = hit.normal;
                return;
            }
        }
        isOnSlope = false;
        slopeNormal = Vector2.up;
    }

    void OnMove(InputAction.CallbackContext ctx)
    {
        Vector2 input = ctx.ReadValue<Vector2>();
        float prevClimbInput = climbInput;
        moveInput = input.x;
        climbInput = input.y;
        if (moveInput != 0f)
            lastFacingDir = (int)Mathf.Sign(moveInput);

        if (_isRunning && climbInput > 0.5f && prevClimbInput <= 0.5f && !isDodging && !IsGuarding && !IsStunned)
            BodySlamSkill();

        if (isNearLadder && !isOnLadder && _currentLadder != null && !AllLimbsCut && climbInput != 0f && prevClimbInput == 0f)
            GrabLadder();
    }

    void OnJump(InputAction.CallbackContext ctx)
    {
        if (IsDown)
        {
            RecoverFromDown();
            return;
        }
        if (isOnLadder)
        {
            ExitLadder();
            rb.linearVelocity = new Vector2(-lastFacingDir * EffectiveSpeed, EffectiveJumpForce * 0.6f);
            return;
        }
        if (!CanJump) return;
        if (IsGrounded() || _coyoteTimer > 0f)
        {
            jumpQueued = true;
            _coyoteTimer = 0f;
        }
    }

    void OnAttack(InputAction.CallbackContext ctx)
    {
        if (UnityEngine.EventSystems.EventSystem.current != null &&
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject()) return;
        if (isDodging || IsGuarding || IsStunned || IsDown) return;

        if (IsBerserker) { ShootSwordAura(); return; }

        if (_isRunning) { QuickdrawSkill(); return; }

        if (climbInput > 0.5f && CanWeakAttack) { RisingAttackSkill(); return; }
        if (climbInput < -0.5f && CanWeakAttack && !IsGrounded()) { SmashdownSkill(); return; }

        if (isAttacking) { comboInputQueued = true; return; }

        if (!CanWeakAttack && !CanStrongAttack) return;
        if (!SpendStamina(lightStaminaCost)) return;

        comboStep = 0;
        StartCoroutine(ComboCoroutine());
    }



    void OnDodge(InputAction.CallbackContext ctx)
    {
        _shiftDown = true;
        _runHoldTimer = 0f;
        if (moveInput == 0f)
            TryDodge();
    }

    void OnDodgeRelease(InputAction.CallbackContext ctx)
    {
        if (_shiftDown && !_isRunning)
            TryDodge();
        _shiftDown = false;
        _isRunning = false;
        _runHoldTimer = 0f;
    }

    void TryDodge()
    {
        if (isDodging || AllLimbsCut || !SpendStamina(dodgeStaminaCost)) return;
        StartCoroutine(DodgeCoroutine());
    }

    void OnGuardStart(InputAction.CallbackContext ctx)
    {
        if (isDodging || isAttacking || (!CanWeakAttack && !CanStrongAttack)) return;
        IsGuarding = true;
        StartCoroutine(ParryWindowCoroutine());
    }

    void OnGuardEnd(InputAction.CallbackContext ctx)
    {
        IsGuarding = false;
        isParryActive = false;
    }

    void OnGameStateChanged(GameManager.GameState state)
    {
        SetInputEnabled(state != GameManager.GameState.Boot);
    }

    void OnGather(UnityEngine.InputSystem.InputAction.CallbackContext ctx)
    {
        if (!TryDeathblow())
            SphereSummon();
    }

    void SphereSummon()
    {
        if (_bloodSphereCooldownTimer > 0f) return;
        _bloodSphereCooldownTimer = bloodSphereCooldown;
        Instantiate(bloodSpherePrefab, transform.position, Quaternion.identity);
    }

    void TickDeathblowIndicator()
    {
        if (effects == null) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, deathblowRange, enemyLayer);

        Collider2D best = null;
        float bestDist = float.MaxValue;
        foreach (var col in hits)
        {
            var mm = col.GetComponentInParent<IMonsterCore>();
            if (mm == null || mm.MonsterType == IMonsterCore.Type.boss || (!mm.IsBleeding && mm.IsAwareOfPlayer)) continue;
            float d = Vector2.Distance(transform.position, col.bounds.center);
            if (d < bestDist) { bestDist = d; best = col; }
        }

        if (best != null)
            effects.SetTargetOfDeathBlowIndicator(best.transform);
        else
            effects.TurnDeathBlowIndicator(false);
    }

    bool TryDeathblow()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, deathblowRange, enemyLayer);
        Debug.Log($"[Deathblow] hits={hits.Length}, range={deathblowRange}, layer={enemyLayer.value}");

        Collider2D target = null;
        float bestDist = float.MaxValue;
        foreach (var col in hits)
        {
            var mm = col.GetComponentInParent<IMonsterCore>();
            Debug.Log($"[Deathblow] col={col.name} mm={mm != null} bleeding={mm?.IsBleeding} aware={mm?.IsAwareOfPlayer}");
            if (mm == null || mm.MonsterType == IMonsterCore.Type.boss || (!mm.IsBleeding && mm.IsAwareOfPlayer)) continue;
            float d = Vector2.Distance(transform.position, col.bounds.center);
            if (d < bestDist) { bestDist = d; target = col; }
        }

        if (target == null) { Debug.Log("[Deathblow] no eligible target"); return false; }

        var sliceable2 = target.GetComponentInParent<EnemySliceable>();
        if (sliceable2 != null)
        {
            Vector2 toEnemy = ((Vector2)target.bounds.center - (Vector2)transform.position).normalized;
            sliceable2.SetPendingSlice(toEnemy, target.bounds.center, deathblowSliceForce, transform.position);
            SpawnBlood((Vector2)target.bounds.center, -toEnemy, sliceable2.Money, sliceable2.HpHeal);
            if (bloodPuddleMaker != null)
                bloodPuddleMaker.SpawnStrongPuddle((Vector2)target.bounds.center, sliceable2.Money, sliceable2.HpHeal);
        }

        var damageable2 = target.GetComponentInParent<IDamageable>();
        if (damageable2 != null)
            damageable2.TakeDamage(ScaleOutgoingDamage(99999f));
        else
            Debug.LogWarning("[Deathblow] no IDamageable found on target or parent");

        AddBloodGage(deathblowBloodGain);
        return true;
    }

    void OnSkillPress(InputAction.CallbackContext ctx) => playerMagicSkill?.OnMagicSkillPress();
    void OnSkillRelease(InputAction.CallbackContext ctx) => playerMagicSkill?.OnMagicSkillRelease();

    void OnSkillWheelOpen(InputAction.CallbackContext ctx)
    {
        if (IsDead || IsStunned || IsDown || isDodging || isAttacking) return;
        skillwheel?.Open();
    }

    void OnSkillWheelClose(InputAction.CallbackContext ctx) => skillwheel?.CommitClose();

    void OnGatherDown(InputAction.CallbackContext ctx) => _gatherHeld = true;
    void OnGatherUp(InputAction.CallbackContext ctx) { _gatherHeld = false; _gatherHoldTimer = 0f; }

    // ── Berserker Mode ────────────────────────────────────────────────────────

    void TickBerserker()
    {
        if (IsDead) return;

        // HP condition (combat only) is triggered directly from TakeDamage/TakeSpecialDamage,
        // so the tick HP drain reaching hpDrainFloor never spuriously activates Berserker here.

        // Hold condition: hold Gather button for berserkerHoldTime
        if (!IsBerserker && !_berserkerUsed && _gatherHeld)
        {
            _gatherHoldTimer += Time.deltaTime;
            if (_gatherHoldTimer >= berserkerHoldTime)
            {
                _gatherHoldTimer = 0f;
                ActivateBerserker();
            }
        }

        // Countdown while active
        if (IsBerserker)
        {
            CurrentStamina = EffectiveMaxStamina;
            _berserkerTimer -= Time.deltaTime;
            if (_berserkerTimer <= 0f)
                DeactivateBerserker();
        }
    }

    void ActivateBerserker()
    {
        Debug.Log("Berserker Mode Activated!");

        IsBerserker = true;
        _berserkerUsed = true;
        _berserkerTimer = berserkerDuration;
        RestoreAllLimbs();
        BerserkerActivationSlaughter();
        CurrentHp = 1f;

        if (IsStunned || IsDown)
        {
            StopCoroutine(nameof(StunCoroutine));
            IsStunned = false;
            IsDown = false;
            _stunAccumulator = 0f;
            SetInputEnabled(true);
        }
    }

    public void RestoreBerserker()
    {
        _berserkerUsed = false;
    }

    public void DeactivateBerserker()
    {
        IsBerserker = false;
        CurrentHp = 1f;
        CurrentStamina = 0f;
        CurrentBloodGage = 0f;
        // effects added later
    }

    void ShootSwordAura()
    {
        if (swordAuraPrefab == null) return;
        int dir = FacingDir();
        Vector2 spawnPos = (Vector2)transform.position + Vector2.right * dir * 0.3f;
        GameObject obj = Instantiate(swordAuraPrefab, spawnPos, Quaternion.identity);
        var aura = obj.GetComponent<SwordAura>();
        if (aura != null)
        {
            aura.speed = swordAuraSpeed;
            aura.lifetime = swordAuraLifetime;
            aura.Init(dir, ScaleOutgoingDamage(swordAuraDamage), enemyLayer, bloodPuddleMaker);
        }
    }

    void BerserkerActivationSlaughter()
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, berserkerActivationRange, enemyLayer);
        foreach (var col in hits)
        {
            if (col.TryGetComponent<EnemySliceable>(out var sliceable))
            {
                Vector2 toEnemy = ((Vector2)col.bounds.center - (Vector2)transform.position).normalized;
                float randomAngle = Random.Range(-60f, 60f);
                Vector2 rotated = Quaternion.AngleAxis(randomAngle, Vector3.forward) * toEnemy;
                sliceable.SetPendingSlice(rotated, col.bounds.center, deathblowSliceForce, transform.position);
                if (bloodPuddleMaker != null)
                    bloodPuddleMaker.SpawnStrongPuddle(col.bounds.center, sliceable.Money, sliceable.HpHeal);
            }

            if (col.TryGetComponent<IDamageable>(out var damageable))
                damageable.TakeDamage(ScaleOutgoingDamage(99999f));
        }
    }

    //���� Slice ����������������������������������������������������������������������������������������������������������������������������������

    void DoSlice(float forcePower = 0f, Vector2? attackDir = null)
    {
        Vector2 origin = (Vector2)transform.position + Vector2.up * 0.3f;

        Vector2 sliceDir;
        if (attackDir.HasValue)
        {
            sliceDir = attackDir.Value;
        }
        else
        {
            Vector2 mouseWorld = CameraFollow2D.GameCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            sliceDir = mouseWorld - origin;
            if (sliceDir.sqrMagnitude < 0.001f) sliceDir = Vector2.right * FacingDir();
            sliceDir.Normalize();
        }

        Vector2 endpoint = origin + sliceDir * sliceRange;

        // Penetrating ray — hits every enemy along the line
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, sliceDir, sliceRange, enemyLayer);

        Vector2 sliceNormal = new(-sliceDir.y, sliceDir.x);

        foreach (var hit in hits)
        {
            if (hit.collider.TryGetComponent<EnemySliceable>(out var sliceable))
            {
                sliceable.SetPendingSlice(sliceNormal, hit.point, forcePower, transform.position);
                SpawnBlood(hit.point, hit.normal, sliceable.Money / 10, sliceable.HpHeal / 10f);

                bool alreadyDead = hit.collider.TryGetComponent<IDamageable>(out var preCheck) && preCheck.IsDead;
                if (!alreadyDead && hit.collider.TryGetComponent<IDamageable>(out var d))
                    d.TakeDamage(ScaleOutgoingDamage(sliceDamage));

                bool isDead = alreadyDead || !hit.collider.TryGetComponent<IDamageable>(out var m) || m.IsDead;
                if (isDead && bloodPuddleMaker != null)
                    bloodPuddleMaker.SpawnStrongPuddle(hit.point, sliceable.Money, sliceable.HpHeal);
            }
            else
            {
                SpawnBlood(hit.point, hit.normal);
                if (hit.collider.TryGetComponent<IDamageable>(out var damageable))
                    damageable.TakeDamage(ScaleOutgoingDamage(sliceDamage));
            }
        }

        StartCoroutine(ShowSlashLine(origin, endpoint));
    }

    IEnumerator ShowSlashLine(Vector2 start, Vector2 end)
    {
        slashLine.enabled = true;
        slashLine.SetPosition(0, start);
        slashLine.SetPosition(1, end);

        yield return new WaitForSeconds(slashDuration);

        slashLine.enabled = false;
    }

    LineRenderer BuildSlashLine()
    {
        var lr = gameObject.AddComponent<LineRenderer>();
        lr.positionCount = 2;
        lr.startWidth = slashWidth;
        lr.endWidth = slashWidth * 0.4f;
        lr.useWorldSpace = true;
        lr.enabled = false;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = slashColor;
        lr.endColor = new Color(slashColor.r, slashColor.g, slashColor.b, 0f);
        lr.sortingOrder = 10;
        return lr;
    }

    // ���� Light Attack Combo ��������������������������������������������������������������������������������������������������������

    IEnumerator ComboCoroutine()
    {
        isAttacking = true;
        comboInputQueued = false;
        float ad = AttackDelayMultiplier;

        var hit = comboHits[comboStep];

        yield return new WaitForSeconds(hit.startup * ad);

        MarkSliceDir(HitOrigin(hit.range), hit.range, MouseSliceDir());
        HitEnemies(HitOrigin(hit.range), hit.range, hit.damage, hit.knockback, FacingDir(), bleedDps, bleedDuration);
        DoSlice(sliceDamage * sliceForce);

        yield return new WaitForSeconds(hit.recovery * ad);

        isAttacking = false;

        if (comboInputQueued && comboStep < comboHits.Length - 1)
        {
            comboStep++;
            comboTimer = comboWindowDuration;
            comboInputQueued = false;
            if (SpendStamina(lightStaminaCost))
                StartCoroutine(ComboCoroutine());
        }
        else
        {
            comboStep = 0;
            comboTimer = 0f;
            comboInputQueued = false;
        }
    }

    void TickComboWindow()
    {
        if (comboStep == 0) return;

        comboTimer -= Time.deltaTime;
        if (comboTimer <= 0f)
        {
            comboStep = 0;
            comboInputQueued = false;
        }
    }



    static readonly WaitForSeconds s_quickdrawRecovery = new(0.15f);

    void TickRender()
    {
        if (_illustChanger == null) return;
        if (isAttacking)
            _illustChanger.Attack();
        else if (!IsGrounded())
            _illustChanger.Jump();
        else
            _illustChanger.Idle();
    }

    void TickCoyoteTime()
    {
        if (IsGrounded())
            _coyoteTimer = coyoteTime;
        else if (_coyoteTimer > 0f)
            _coyoteTimer -= Time.deltaTime;
    }

    void TickSkillCooldowns()
    {
        float dt = Time.deltaTime;
        if (_quickdrawCooldownTimer > 0f) _quickdrawCooldownTimer -= dt;
        if (_smashdownCooldownTimer > 0f) _smashdownCooldownTimer -= dt;
        if (_bodySlamCooldownTimer > 0f) _bodySlamCooldownTimer -= dt;
        if (_grabThrowCooldownTimer > 0f) _grabThrowCooldownTimer -= dt;
        if (_bloodSphereCooldownTimer > 0f) _bloodSphereCooldownTimer -= dt;
    }

    // ── Quickdraw Skill ───────────────────────────────────────────────────────
    void QuickdrawSkill()
    {
        if (_quickdrawCooldownTimer > 0f && !IsBerserker) return;
        if (!SpendStamina(quickdrawStaminaCost)) return;
        if (!IsBerserker) _quickdrawCooldownTimer = quickdrawCooldown;
        StartCoroutine(QuickdrawCoroutine());
    }

    IEnumerator QuickdrawCoroutine()
    {
        isAttacking = true;
        IsInvincible = true;

        Vector2 origin = transform.position;
        Vector2 dir = Vector2.right * FacingDir();

        // Stop at ground-layer walls
        RaycastHit2D wallHit = Physics2D.Raycast(origin, dir, quickdrawRange, groundLayer);
        Vector2 destination = wallHit.collider != null
            ? wallHit.point - dir * 0.15f
            : origin + dir * quickdrawRange;

        float dist = Vector2.Distance(origin, destination);

        // Slice every enemy along the path
        RaycastHit2D[] hits = Physics2D.RaycastAll(origin, dir, dist, enemyLayer);
        Vector2 sliceNormal = dir;
        foreach (var hit in hits)
        {
            SpawnBlood(hit.point, hit.normal);
            if (hit.collider.TryGetComponent<EnemySliceable>(out var sliceable))
            {
                bool alreadyDead = hit.collider.TryGetComponent<IDamageable>(out var preCheck) && preCheck.IsDead;
                if (!alreadyDead && hit.collider.TryGetComponent<IDamageable>(out var d)) d.TakeDamage(ScaleOutgoingDamage(quickdrawDamage));
                if (!alreadyDead) ApplyBleedToCollider(hit.collider);
                bool isDead = alreadyDead || !hit.collider.TryGetComponent<IDamageable>(out var m) || m.IsDead;
                if (isDead)
                {
                    sliceable.Slice(sliceNormal, hit.point, deathblowSliceForce, transform.position);
                    if (bloodPuddleMaker != null) bloodPuddleMaker.SpawnStrongPuddle(hit.point, sliceable.Money, sliceable.HpHeal);
                }
            }
            else if (hit.collider.TryGetComponent<IDamageable>(out var damageable))
            {
                damageable.TakeDamage(ScaleOutgoingDamage(quickdrawDamage));
                ApplyBleedToCollider(hit.collider);
            }
        }

        // Draw slash line along the full travel path then teleport
        StartCoroutine(ShowSlashLine(origin, destination));
        transform.position = destination;

        yield return s_quickdrawRecovery;

        IsInvincible = false;
        isAttacking = false;
    }

    void ApplyBleedToCollider(Collider2D col)
    {
        if (col.TryGetComponent<IMonsterCore>(out var mm)) mm.ApplyBloodloss(quickdrawBleedDps, quickdrawBleedDuration);
    }

    // ── Rising Attack Skill ───────────────────────────────────────────────────

    void RisingAttackSkill()
    {
        if (!SpendStamina(risingStaminaCost)) return;
        StartCoroutine(RisingAttackCoroutine());
    }

    IEnumerator RisingAttackCoroutine()
    {
        isAttacking = true;
        float ad = AttackDelayMultiplier;

        yield return new WaitForSeconds(risingStartup * ad);

        Vector2 origin = (Vector2)transform.position + Vector2.up * 0.4f;
        MarkSliceDir(origin, risingRange, new Vector2(FacingDir(), 1f).normalized);
        HitEnemies(origin, risingRange, risingDamage, new Vector2(FacingDir() * 2f, risingKnockback), bleedDps, bleedDuration);
        DoSlice(risingDamage * sliceForce, new Vector2(FacingDir(), 1f).normalized);

        yield return new WaitForSeconds(risingRecovery * ad);
        isAttacking = false;
    }

    // ── Smashdown Skill ───────────────────────────────────────────────────────

    void SmashdownSkill()
    {
        if (_smashdownCooldownTimer > 0f && !IsBerserker) return;
        if (!SpendStamina(smashdownStaminaCost)) return;
        if (!IsBerserker) _smashdownCooldownTimer = smashdownCooldown;
        StartCoroutine(SmashdownCoroutine());
    }

    IEnumerator SmashdownCoroutine()
    {
        isAttacking = true;
        float ad = AttackDelayMultiplier;

        yield return new WaitForSeconds(smashdownStartup * ad);

        Vector2 origin = (Vector2)transform.position + Vector2.down * 0.4f;
        MarkSliceDir(origin, smashdownRange, new Vector2(FacingDir(), -1f).normalized);
        HitEnemies(origin, smashdownRange, smashdownDamage, smashdownKnockback, FacingDir(), bleedDps, bleedDuration);
        DoSlice(smashdownDamage * sliceForce, new Vector2(FacingDir(), -1f).normalized);

        yield return new WaitForSeconds(smashdownRecovery * ad);
        isAttacking = false;
    }

    // ── Grab Throw Skill ──────────────────────────────────────────────────────

    void OnGrabThrow(InputAction.CallbackContext ctx)
    {
        if (isDodging || IsGuarding || IsStunned || IsDown) return;
        GrabThrowSkill();
    }

    void GrabThrowSkill()
    {
        if (_grabThrowCooldownTimer > 0f && !IsBerserker) return;

        Collider2D target = FindNearestInRange((Vector2)transform.position, grabRange, enemyLayer);
        if (target == null) return;

        if (!SpendStamina(grabThrowStaminaCost)) return;
        if (!IsBerserker) _grabThrowCooldownTimer = grabThrowCooldown;
        StartCoroutine(GrabThrowCoroutine(target));
    }

    IEnumerator GrabThrowCoroutine(Collider2D targetCol)
    {
        isAttacking = true;

        if (!targetCol.TryGetComponent<SmallEnemy>(out var small)) { isAttacking = false; yield break; }

        small.StartGrab();

        yield return new WaitForSeconds(grabThrowStartup);

        int dir = FacingDir();
        Vector2 throwVel = new Vector2(dir * throwForce, throwForce * 0.35f);

        small.Throw(throwVel, ScaleOutgoingDamage(throwCollisionDamage), throwDuration, enemyLayer);

        yield return new WaitForSeconds(0.2f);
        isAttacking = false;
    }

    Collider2D FindNearestInRange(Vector2 origin, float range, LayerMask layer)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, range, layer);
        Collider2D best = null;
        float bestDist = float.MaxValue;
        foreach (var col in hits)
        {
            float d = Vector2.Distance(origin, col.bounds.center);
            if (d < bestDist) { bestDist = d; best = col; }
        }
        return best;
    }

    // ── Body Slam Skill ───────────────────────────────────────────────────────

    void BodySlamSkill()
    {
        if (isSlamming) return;
        if (_bodySlamCooldownTimer > 0f && !IsBerserker) return;
        if (!SpendStamina(bodySlamStaminaCost)) return;
        if (!IsBerserker) _bodySlamCooldownTimer = bodySlamCooldown;
        StartCoroutine(BodySlamCoroutine());
    }

    IEnumerator BodySlamCoroutine()
    {
        _isRunning = false;
        isAttacking = true;
        isSlamming = true;

        int dir = FacingDir();
        float elapsed = 0f;
        bool collided = false;

        while (elapsed < bodySlamDuration && !collided)
        {
            rb.linearVelocity = new Vector2(dir * bodySlamSlipSpeed, rb.linearVelocity.y);

            yield return new WaitForFixedUpdate();
            elapsed += Time.fixedDeltaTime;

            Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, bodySlamHitRadius, enemyLayer);
            if (hits.Length > 0)
            {
                collided = true;
                foreach (var col in hits)
                {
                    if (col.TryGetComponent<IDamageable>(out var target))
                    {
                        target.TakeDamage(ScaleOutgoingDamage(bodySlamDamage));
                        if (target.IsDead && col.TryGetComponent<EnemySliceable>(out var sliceable))
                        {
                            Vector2 slamDir = rb.linearVelocity.sqrMagnitude > 0.01f ? rb.linearVelocity.normalized : Vector2.right * FacingDir();
                            sliceable.Slice(slamDir, col.bounds.center, deathblowSliceForce, transform.position);
                            if (bloodPuddleMaker != null)
                                bloodPuddleMaker.SpawnStrongPuddle((Vector2)col.bounds.center);
                        }
                    }

                    var kbForce = new Vector2(dir * bodySlamKnockback, 2f);
                    if (col.TryGetComponent<IMonsterCore>(out var mm))
                        mm.ApplyKnockback(kbForce, smashdownRecovery);
                    else if (col.TryGetComponent<Rigidbody2D>(out var targetRb))
                        targetRb.AddForce(kbForce, ForceMode2D.Impulse);
                }

                TakeDamage(bodySlamSelfDamage);
                rb.linearVelocity = new Vector2(-dir * bodySlamSelfKnockback, rb.linearVelocity.y);
            }
        }

        if (!collided)
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);

        // Hold isSlamming during recovery so FixedUpdate doesn't cancel the rebound velocity.
        yield return new WaitForSeconds(smashdownRecovery);

        isSlamming = false;
        isAttacking = false;
    }

    // ── Stun ──────────────────────────────────────────────────────────────────

    public void ApplyStun()
    {
        TryApplyStun(stunDuration * ArmStunMultiplier);
    }

    void TryApplyStun(float scaled)
    {
        _stunAccumulator += scaled;
        if (IsStunned || IsDown || _stunAccumulator >= downThreshold)
            EnterDown();
        else
            StartCoroutine(StunCoroutine(scaled));
    }

    IEnumerator StunCoroutine(float duration)
    {
        IsStunned = true;
        SetInputEnabled(false);
        yield return new WaitForSeconds(duration);
        if (!IsDown)
        {
            IsStunned = false;
            SetInputEnabled(true);
        }
    }

    void TickStunAccumulator()
    {
        if (_stunAccumulator > 0f && !IsDown)
            _stunAccumulator = Mathf.Max(0f, _stunAccumulator - stunDecayRate * Time.deltaTime);
    }

    void EnterDown()
    {
        StopCoroutine(nameof(StunCoroutine));
        IsStunned = false;
        IsDown = true;
        SetInputEnabled(false);
        actions.Player2D.Jump.Enable();
    }

    void RecoverFromDown()
    {
        IsDown = false;
        _stunAccumulator = 0f;
        SetInputEnabled(true);
    }

    // ───── Dodge ──────────────────────────────────────────────────────────────

    IEnumerator DodgeCoroutine()
    {
        isDodging = true;
        isAttacking = false;
        float dd = DodgeDelayMultiplier;

        int dodgeDir = moveInput != 0f ? (int)Mathf.Sign(moveInput) : lastFacingDir;

        int playerLayer = gameObject.layer;
        int enemyLayerIndex = Mathf.RoundToInt(Mathf.Log(enemyLayer.value, 2));
        Physics2D.IgnoreLayerCollision(playerLayer, enemyLayerIndex, true);

        IsInvincible = true;
        if (isOnSlope)
        {
            rb.gravityScale = 0f;
            Vector2 slopeDir = new Vector2(slopeNormal.y, -slopeNormal.x);  // tangent along slope
            rb.linearVelocity = dodgeDir * EffectiveDodgeForce * slopeDir;
        }
        else
        {
            rb.gravityScale = 1f;
            rb.linearVelocity = new Vector2(dodgeDir * EffectiveDodgeForce, rb.linearVelocity.y);
        }
        yield return new WaitForSeconds(iFrameDuration * dd);

        IsInvincible = false;

        yield return new WaitForSeconds((dodgeDuration - iFrameDuration) * dd);

        Physics2D.IgnoreLayerCollision(playerLayer, enemyLayerIndex, false);
        rb.gravityScale = 1f;
        isDodging = false;
    }

    // ���� Guard / Parry ������������������������������������������������������������������������������������������������������������������

    IEnumerator ParryWindowCoroutine()
    {
        isParryActive = true;
        yield return new WaitForSeconds(parryWindowDuration);
        isParryActive = false;
    }

    // ���� IDamageable ����������������������������������������������������������������������������������������������������������������������

    public void TakeSpecialDamage(float amount)
    {
        if (IsInvincible || IsBerserker) return;
        amount = ApplyDefense(amount);
        CurrentHp -= amount;
        StartCoroutine(HitFlash(Color.red));
        SlayRandomBodyPart();
        if (CurrentHp <= 1f && !_berserkerUsed) { ActivateBerserker(); return; }
        if (CurrentHp <= 0f) Die();
    }

    public void TakeDamage(float amount, float stunDuration = 0f)
    {
        if (IsDead || IsInvincible || IsBerserker) return;

        if (IsDown) EnterDown();

        if (IsGuarding)
        {
            if (isParryActive)
            {
                SpendStamina(parryStaminaCost);
                StartCoroutine(HitFlash(Color.yellow));
                return;
            }

            amount *= (1f - guardDamageReduction);
        }

        amount = ApplyDefense(amount);

        CurrentHp -= amount;
        StartCoroutine(HitFlash(Color.red));

        if (stunDuration > 0f)
            TryApplyStun(stunDuration * ArmStunMultiplier);

        if (CurrentHp <= 1f && !_berserkerUsed)
        {
            ActivateBerserker();
            return;
        }

        if (CurrentHp <= 0f)
            Die();
    }

    void SlayRandomBodyPart()
    {
        if (IsBerserker) return;

        var available = new System.Collections.Generic.List<BodyPart>();
        foreach (BodyPart part in System.Enum.GetValues(typeof(BodyPart)))
            if (!IsSlayed(part)) available.Add(part);

        if (available.Count == 0) return;
        SlayBodyPart(available[Random.Range(0, available.Count)]);
    }

    void Die()
    {
        CurrentHp = 0f;
        IsDead = true;
        SetInputEnabled(false);
        rb.gravityScale = 1f;
        rb.linearVelocity = Vector2.zero;
        Debug.Log("Player died.");
    }

    // ���� Stamina ������������������������������������������������������������������������������������������������������������������������������

    public bool SpendStamina(float cost)
    {
        if (CurrentStamina < cost) return false;
        CurrentStamina -= cost;
        staminaRegenTimer = staminaRegenDelay;
        return true;
    }

    void TickStaminaRegen()
    {
        if (staminaRegenTimer > 0f)
        {
            staminaRegenTimer -= Time.deltaTime;
            return;
        }

        CurrentStamina = Mathf.Min(CurrentStamina + staminaRegen * Time.deltaTime, EffectiveMaxStamina);
    }

    void TickRun()
    {
        if (_shiftDown && moveInput != 0f && !isDodging && !isAttacking && !IsDown)
        {
            _runHoldTimer += Time.deltaTime;
            if (_runHoldTimer >= runHoldTime && IsGrounded())
                _isRunning = true;
        }
    }

    // Clamps CurrentHp/CurrentStamina down immediately when day/night shrinks the effective
    // max (e.g. daylight halving max HP mid-fight costs HP right away, not just on future gains).
    void TickEffectiveCaps()
    {
        if (IsDead) return;
        if (CurrentHp > EffectiveMaxHp) CurrentHp = EffectiveMaxHp;
        if (CurrentStamina > EffectiveMaxStamina) CurrentStamina = EffectiveMaxStamina;
    }

    void TickHpDrain()
    {
        if (IsDead || IsInCutscene || hpDrainPerSecond <= 0f) return;
        if (GameManager.Instance != null && GameManager.Instance.State == GameManager.GameState.Boot) return;
        if (PlayerDebuffer.Instance != null && !PlayerDebuffer.Instance.TickDamageActive) return;

        float drain = hpDrainPerSecond * Time.deltaTime;

        if (CurrentSunproofGuard > 0f)
        {
            CurrentSunproofGuard = Mathf.Max(CurrentSunproofGuard - drain, 0f);
            if (CurrentSunproofGuard <= 0f)
                _itemConsumer?.TryAutoUseSunCream();
            return;
        }

        if (CurrentHp <= hpDrainFloor) return;
        CurrentHp = Mathf.Max(CurrentHp - drain, hpDrainFloor);
    }

    public void AddSunproofGuard(float amount)
    {
        CurrentSunproofGuard = Mathf.Min(CurrentSunproofGuard + amount, maxSunproofGuard);
    }

    public void SetSunproofGuard(float amount)
    {
        CurrentSunproofGuard = Mathf.Clamp(amount, 0f, maxSunproofGuard);
    }

    // ���� BloodGage ����������������������������������������������������������������������������������������������������������������������������������

    public void Teleport(Vector2 position)
    {
        rb.linearVelocity = Vector2.zero;
        rb.position = position;
        transform.position = position;
    }

    public void Heal(float amount)
    {
        if (IsDead) return;
        CurrentHp = Mathf.Min(CurrentHp + amount, EffectiveMaxHp);
    }

    public void RestoreStamina(float amount)
    {
        CurrentStamina = Mathf.Min(CurrentStamina + amount, EffectiveMaxStamina);
    }

    public void AddBloodGage(float amount)
    {
        CurrentBloodGage = Mathf.Min(CurrentBloodGage + amount, maxBloodGage);
        if (CurrentBloodGage >= maxBloodGage && HasSlayedPart())
        {
            CurrentBloodGage = 0f;
            HealNextBodyPart();
        }
    }

    bool HasSlayedPart()
    {
        for (int i = 0; i < _slayedParts.Length; i++)
            if (_slayedParts[i]) return true;
        return false;
    }

    void HealNextBodyPart()
    {
        foreach (BodyPart part in HealPriority)
        {
            if (!_slayedParts[(int)part]) continue;
            _slayedParts[(int)part] = false;
            if (part == BodyPart.Head) ApplyHeadEffects(false);
            return;
        }
    }

    public void RestoreAllLimbs()
    {
        bool headWasSlayed = _slayedParts[(int)BodyPart.Head];
        for (int i = 0; i < _slayedParts.Length; i++)
            _slayedParts[i] = false;
        if (headWasSlayed) ApplyHeadEffects(false);
    }

    void ApplyHeadEffects(bool slayed)
    {
        if (ScreenFader.Instance != null)
        {
            if (slayed) ScreenFader.Instance.GameFadeIn();
            else ScreenFader.Instance.GameFadeOut();
        }
        SoundManager.SetHeadMuffle(slayed);
    }
    public bool ConsumeBloodGage(float amount)
    {
        if (CurrentBloodGage < amount)
            return false;
        CurrentBloodGage = Mathf.Max(CurrentBloodGage - amount, 0f);
        return true;
    }

    public void AddBloodMoney(int amount)
    {
        CurrentBloodMoney += amount;
    }

    public bool SpendBloodMoney(int amount)
    {
        if (CurrentBloodMoney < amount) return false;
        CurrentBloodMoney -= amount;
        return true;
    }

    public void SetBloodMoney(int amount)
    {
        CurrentBloodMoney = Mathf.Max(amount, 0);
    }

    public void SetBloodGage(float amount)
    {
        CurrentBloodGage = Mathf.Clamp(amount, 0f, maxBloodGage);
    }

    // ���� Helpers ������������������������������������������������������������������������������������������������������������������������������

    // Every PlayerBasicStatContainer stat is scaled by this before use (50% day / 100% night
    // by default via PlayerDebuffer). No PlayerDebuffer present leaves stats unscaled.
    float BasicStatMultiplier => PlayerDebuffer.Instance != null ? PlayerDebuffer.Instance.BasicStatMultiplier : 1f;

    float EffectiveMaxHp => maxHp * BasicStatMultiplier;
    float EffectiveMaxStamina => maxStamina * BasicStatMultiplier;

    float ScaleOutgoingDamage(float baseDamage)
    {
        float result = baseDamage;
        if (PlayerDebuffer.Instance != null) result *= PlayerDebuffer.Instance.OutgoingDamageMultiplier;
        if (_basicStats != null)
        {
            result *= 1f + (_basicStats.AttackPoint * BasicStatMultiplier) / 100f;
            if (Random.Range(0f, 100f) < _basicStats.LuckPoint * BasicStatMultiplier) result *= 2f;
        }
        return result;
    }

    // DefendPoint is subtracted flat from incoming damage (not a percentage); no container
    // present leaves damage unchanged.
    float ApplyDefense(float amount) =>
        _basicStats != null ? Mathf.Max(0f, amount - _basicStats.DefendPoint * BasicStatMultiplier) : amount;

    // Each AttackSpeedPoint shortens attack startup/recovery by 1%, floored at 80% reduction
    // so swings can never hit a zero/negative duration.
    float AttackSpeedTimeScale =>
        _basicStats != null ? Mathf.Max(0.2f, 1f - (_basicStats.AttackSpeedPoint * BasicStatMultiplier) / 100f) : 1f;

    void HitEnemies(Vector2 origin, float radius, float damage, Vector2 knockbackForce, float bleedDps = 0f, float bleedDuration = 0f/*, Vector2? sliceDir = null*/)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, radius, enemyLayer);
        foreach (var col in hits)
        {
            if (col.TryGetComponent<IDamageable>(out var target))
            {
                target.TakeDamage(ScaleOutgoingDamage(damage));
                if (target.IsDead)
                {
                    if (bloodPuddleMaker != null)
                        bloodPuddleMaker.SpawnStrongPuddle((Vector2)col.bounds.center);
                    if (!col.TryGetComponent<IMonsterCore>(out _) && col.TryGetComponent<EnemySliceable>(out var s))
                        s.Slice(s.pendingSliceNormal, s.pendingSliceContact, s.pendingSliceForcePower, s.pendingSlicePlayerPos);
                }
            }

            if (bleedDps > 0f)
            {
                if (col.TryGetComponent<IMonsterCore>(out var mm)) mm.ApplyBloodloss(bleedDps, bleedDuration);
            }

            if (col.TryGetComponent<Rigidbody2D>(out var targetRb))
                targetRb.AddForce(knockbackForce, ForceMode2D.Impulse);

            Vector2 contactPt = col.ClosestPoint(origin);
            Vector2 normal = (contactPt - (Vector2)col.bounds.center).normalized;
            SpawnBlood(contactPt, normal);
        }
    }

    void HitEnemies(Vector2 origin, float radius, float damage, float knockback, int dir, float bleedDps = 0f, float bleedDuration = 0f)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(origin, radius, enemyLayer);
        foreach (var col in hits)
        {
            if (col.TryGetComponent<IDamageable>(out var target))
            {
                target.TakeDamage(ScaleOutgoingDamage(damage));
                if (target.IsDead)
                {
                    if (bloodPuddleMaker != null)
                        bloodPuddleMaker.SpawnStrongPuddle((Vector2)col.bounds.center);
                    if (!col.TryGetComponent<IMonsterCore>(out _) && col.TryGetComponent<EnemySliceable>(out var s))
                        s.Slice(s.pendingSliceNormal, s.pendingSliceContact, s.pendingSliceForcePower, s.pendingSlicePlayerPos);
                }
            }

            if (bleedDps > 0f)
            {
                if (col.TryGetComponent<IMonsterCore>(out var mm)) mm.ApplyBloodloss(bleedDps, bleedDuration);
            }

            if (col.TryGetComponent<Rigidbody2D>(out var targetRb))
                targetRb.AddForce(new Vector2(dir * knockback, 2f), ForceMode2D.Impulse);

            Vector2 contactPt = col.ClosestPoint(origin);
            Vector2 normal = (contactPt - (Vector2)col.bounds.center).normalized;
            SpawnBlood(contactPt, normal);
        }
    }

    void SpawnBlood(Vector2 point, Vector2 normal, int moneyValue = 0, float hpHeal = 0f)
    {
        effects?.SpawnBlood(point, normal);
        if (bloodPuddleMaker != null)
        {
            Vector2 _randomPoint = new Vector2(point.x + Random.Range(-0.5f, 0.5f), point.y);
            bloodPuddleMaker.SpawnPuddle(_randomPoint, moneyValue, hpHeal);
        }
    }


    Vector2 HitOrigin(float range)
    {
        return hitPoint != null
            ? (Vector2)hitPoint.position
            : (Vector2)transform.position + Vector2.right * FacingDir() * range * 0.5f;
    }

    Vector2 MouseSliceDir()
    {
        Vector2 origin = (Vector2)transform.position + Vector2.up * 0.3f;
        Vector2 mouseWorld = CameraFollow2D.GameCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        Vector2 dir = (mouseWorld - origin).normalized;
        return dir.sqrMagnitude < 0.001f ? Vector2.right * FacingDir() : dir;
    }

    void MarkSliceDir(Vector2 hitOrigin, float radius, Vector2 cutDir)
    {
        Vector2 sliceNormal = new(-cutDir.y, cutDir.x);
        Collider2D[] hits = Physics2D.OverlapCircleAll(hitOrigin, radius, enemyLayer);
        foreach (var col in hits)
            if (col.TryGetComponent<EnemySliceable>(out var s))
                s.SetPendingSlice(sliceNormal, col.bounds.center, deathblowSliceForce, transform.position);
    }

    public void SetNearLadder(bool near, Ladder ladder)
    {
        isNearLadder = near;
        _currentLadder = ladder;
        if (!near) ExitLadder();
    }

    void GrabLadder()
    {
        isOnLadder = true;
        jumpQueued = false;
        rb.gravityScale = 0f;
        rb.linearVelocity = Vector2.zero;
        rb.position = new Vector2(_currentLadder.LadderX, rb.position.y);
        SetLadderPassthrough(true);
    }

    public void ExitLadder()
    {
        if (!isOnLadder) return;
        isOnLadder = false;
        rb.gravityScale = 1f;
        SetLadderPassthrough(false);
    }

    void SetLadderPassthrough(bool ignore)
    {
        int playerLayer = gameObject.layer;
        int mask = passthroughPlatformLayer.value;
        for (int i = 0; i < 32; i++)
            if ((mask & (1 << i)) != 0)
                Physics2D.IgnoreLayerCollision(playerLayer, i, ignore);
    }

    public void SetInputEnabled(bool enabled)
    {
        if (enabled)
            actions.Player2D.Enable();
        else
        {
            skillwheel?.ForceClose();
            actions.Player2D.Disable();
            actions.Player2D.Interact.Enable();
            moveInput = 0f;
            jumpQueued = false;
            rb.linearVelocity = new Vector2(0f, rb.linearVelocity.y);
        }
    }

    public void SetCutsceneMode(bool active)
    {
        IsInCutscene = active;
        SetInputEnabled(!active);
    }

    public void SetInteractEnabled(bool enabled) => _interactEnabled = enabled;

    void OnInteract(InputAction.CallbackContext _)
    {
        if (DialogSystem.Instance != null && DialogSystem.Instance.IsOpen)
        {
            DialogSystem.Instance.Advance();
            return;
        }
        if (!_interactEnabled) return;
        _interactTarget?.Interact(gameObject);
    }

    void OnUseItem1(InputAction.CallbackContext _) => _itemConsumer?.UseSlot(0);
    void OnUseItem2(InputAction.CallbackContext _) => _itemConsumer?.UseSlot(1);
    void OnUseItem3(InputAction.CallbackContext _) => _itemConsumer?.UseSlot(2);

    void TickInteraction()
    {
        if (!_interactEnabled)
        {
            _interactTarget = null;
            if (interactPrompt != null) interactPrompt.SetActive(false);
            return;
        }
        Collider2D hit = Physics2D.OverlapCircle(transform.position, interactRange, interactLayer);
        _interactTarget = hit != null && hit.TryGetComponent(out IInteractable interactable) ? interactable : null;
        if (interactPrompt != null)
            interactPrompt.SetActive(_interactTarget != null && !DialogSystem.Instance.IsOpen);
    }

    // ── Body Part Slay ────────────────────────────────────────────────────────

    public void SlayBodyPart(BodyPart part)
    {
        int i = (int)part;
        if (_slayedParts[i]) return;
        _slayedParts[i] = true;
        if (effects != null) effects.SpawnSlicedLimb(part, transform.position);
        if (part == BodyPart.Head) ApplyHeadEffects(true);

        if (part == BodyPart.Torso)
        {
            SlayBodyPart(BodyPart.LeftLeg);
            SlayBodyPart(BodyPart.RightLeg);
        }
    }

    public bool IsSlayed(BodyPart part) => _slayedParts[(int)part];

    // Returns the combined multiplier for a stat across all slayed parts.
    // Usage example: float effectiveSpeed = speed * GetCombinedMultiplier(d => d.speedMultiplier);
    public float GetCombinedMultiplier(System.Func<BodyPartDebuff, float> selector)
    {
        float result = 1f;
        for (int i = 0; i < _slayedParts.Length; i++)
            if (_slayedParts[i]) result *= selector(bodyPartDebuffs[i]);
        return result;
    }

    public int FacingDir() => lastFacingDir;

    bool IsGrounded()
    {
        return Physics2D.OverlapCircle(groundCheck.position, 0.1f, groundLayer);
    }

    IEnumerator HitFlash(Color flashColor)
    {
        if (sr == null) yield break;
        sr.color = flashColor;
        yield return new WaitForSeconds(0.1f);
        sr.color = Color.white;
    }

    // ���� Gizmos ��������������������������������������������������������������������������������������������������������������������������������

    void OnDrawGizmosSelected()
    {
        float gizmoRange = comboHits != null && comboHits.Length > 0 ? comboHits[0].range : 1f;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(HitOrigin(gizmoRange), gizmoRange * 0.6f);

        // Slice ray — direction follows mouse at runtime; show a range circle in editor
        Vector2 origin = (Vector2)transform.position + Vector2.up * 0.3f;
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(origin, sliceRange);
    }
}
