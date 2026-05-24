using Doody.GameEvents;
using System.Collections;
using UnityEngine;

public class AI : EventListener, IAIPlayer, IPlayerStats
{
    [Header("Player Info")]
    [SerializeField] private string playerName = "NPC";
    [SerializeField] private int playerID = 1;
    [SerializeField] private int maxHealth = 3;

    [Header("AI Personality")]
    [SerializeField][Range(0f, 1f)] private float aggression = 0.5f;
    [SerializeField][Range(0f, 1f)] private float fear = 0.5f;
    [SerializeField][Range(0f, 1f)] private float confidence = 0.5f;

    [Header("AI Behavior")]
    [SerializeField] private float riskThreshold = 0.5f;
    [SerializeField] private bool adaptToObservations = true;

    [Header("Aiming Settings")]
    [SerializeField] private Transform aimPoint;
    [SerializeField] private float aimDuration = 1.0f;
    [SerializeField] private AnimationCurve aimCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
    [SerializeField] private Transform playerCameraTransform; // Assign player's camera to aim directly at it

    [Header("Character Rotation Settings")]
    [SerializeField] private float selfAimAngle = 90f;
    [SerializeField] private float opponentAimAngle = -90f;

    [Header("Revolver Rotation Settings")]
    [SerializeField] private float revolverSelfAimAngle = 45f;
    [SerializeField] private float revolverOpponentAimAngle = 0f;

    [Header("Shooting Settings")]
    [SerializeField] private float shootDelayAfterAim = 0.5f;
    [SerializeField] private GameObject muzzleFlashEffect;
    [SerializeField] private AudioClip shootSound;

    [Header("Dramatic Aiming (Self-Shot Sequence)")]
    [SerializeField] private bool enableDramaticAiming = true;
    [SerializeField] private float dramaticPauseDuration = 0.8f;
    [SerializeField] private float dramaticAimDuration = 0.5f;

    [Header("Visual Feedback")]
    [SerializeField] private Renderer npcRenderer;
    [SerializeField] private Color aliveColor = Color.green;
    [SerializeField] private Color deadColor = Color.red;
    [SerializeField] private GameObject eliminatedEffect;
    [SerializeField] private GameObject aimIndicator;

    // Core game state
    private int currentHealth;
    private bool isAlive = true;

    // Personality tracking and learning
    private int observedSelfShots = 0;
    private int observedOpponentShots = 0;
    private int observedSurvivalCount = 0;
    private float dynamicRiskLevel = 0.5f;

    // Current computed personality values
    private float dynamicAggression;
    private float dynamicFear;
    private float dynamicConfidence;

    // Aiming system state
    private Quaternion originalRotation;
    private Quaternion targetRotation;
    private Quaternion revolverOriginalRotation;
    private Quaternion revolverTargetRotation;
    private bool isAiming = false;
    private bool isShooting = false;
    private float aimProgress = 0f;
    private Target currentTargetDecision;
    private Transform targetTransform;

    // External dependencies
    private GameManager gameManager;
    public Revolver revolver;
    private AudioSource audioSource;

    // Coroutine handles
    private Coroutine currentTurnCoroutine;
    private Coroutine dramaticAimCoroutine;
    private System.Action onTurnComplete;

    // Public properties for interfaces
    public int Health => currentHealth;
    public string PlayerName => playerName;
    public bool IsAlive => isAlive;
    public int ID => playerID;
    public float Aggression => dynamicAggression;
    public float Fear => dynamicFear;
    public float Confidence => dynamicConfidence;

    void Awake()
    {
        currentHealth = maxHealth;
        dynamicAggression = aggression;
        dynamicFear = fear;
        dynamicConfidence = confidence;
        UpdateVisuals();
        originalRotation = transform.rotation;
        gameManager = FindAnyObjectByType<GameManager>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        if (revolver != null)
            revolverOriginalRotation = revolver.transform.rotation;
    }

    void Start()
    {
        if (!gameObject.CompareTag("NPC"))
            gameObject.tag = "NPC";

        if (aimPoint == null)
            aimPoint = transform;

        if (aimIndicator != null)
            aimIndicator.SetActive(false);
    }

    void Update()
    {
        if (isAiming && dramaticAimCoroutine == null)
            UpdateAiming();
    }

    // ─────────────────────────────────────────────
    //  TURN LOGIC
    // ─────────────────────────────────────────────

    /// <summary>Main turn handler - decides target and executes shooting sequence.</summary>
    public void TakeTurn(System.Action onComplete = null)
    {
        onTurnComplete = onComplete;
        if (isAlive && !isAiming && !isShooting && currentTurnCoroutine == null)
            currentTurnCoroutine = StartCoroutine(AITurnSequence());
    }

    // Satisfies IPlayer.TakeTurn()
    public void TakeTurn() => TakeTurn(null);

    private IEnumerator AITurnSequence()
    {
        yield return new WaitForSeconds(1f);
        if (!isAlive) yield break;

        int chambersLeft = GetChambersLeft();
        Target decision = DecideTarget(chambersLeft);

        if (decision == Target.Self && enableDramaticAiming)
        {
            yield return StartCoroutine(DramaticSelfShotSequence());
        }
        else
        {
            StartAiming(decision);
            while (isAiming) yield return null;
            yield return new WaitForSeconds(shootDelayAfterAim);
        }

        revolver.Fire();

        currentTurnCoroutine = null;
        onTurnComplete?.Invoke();
    }

    // ─────────────────────────────────────────────
    //  DRAMATIC SEQUENCE
    // ─────────────────────────────────────────────

    /// <summary>Aim at opponent, pause for tension, then quickly snap to self.</summary>
    private IEnumerator DramaticSelfShotSequence()
    {
        StartAiming(Target.Opponent);
        while (isAiming) yield return null;

        yield return new WaitForSeconds(dramaticPauseDuration);

        dramaticAimCoroutine = StartCoroutine(DramaticAimTransition(Target.Self));
        yield return dramaticAimCoroutine;

        yield return new WaitForSeconds(shootDelayAfterAim * 0.5f);
    }

    /// <summary>Fast aim transition used for the dramatic self-shot snap.</summary>
    private IEnumerator DramaticAimTransition(Target finalTarget)
    {
        LockRevolverPhysics();

        isAiming = true;
        aimProgress = 0f;
        currentTargetDecision = finalTarget;
        targetTransform = GetTargetTransform(finalTarget);

        Quaternion startRotation        = transform.rotation;
        Quaternion startRevolverRotation = revolver != null ? revolver.transform.rotation : Quaternion.identity;

        targetRotation        = CalculateTargetRotation(finalTarget);
        revolverTargetRotation = revolver != null ? CalculateRevolverTargetRotation(finalTarget) : Quaternion.identity;

        float elapsed = 0f;
        while (elapsed < dramaticAimDuration)
        {
            elapsed += Time.deltaTime;
            float t          = Mathf.Clamp01(elapsed / dramaticAimDuration);
            float curveValue = aimCurve.Evaluate(t);

            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, curveValue);
            if (aimPoint != transform)
                aimPoint.rotation = Quaternion.Slerp(startRotation, targetRotation, curveValue);
            if (revolver != null)
                revolver.transform.rotation = Quaternion.Slerp(startRevolverRotation, revolverTargetRotation, curveValue);

            UpdateAimIndicator();
            yield return null;
        }

        // Snap to exact final rotations
        transform.rotation = targetRotation;
        if (aimPoint != transform) aimPoint.rotation = targetRotation;
        if (revolver != null)     revolver.transform.rotation = revolverTargetRotation;

        isAiming              = false;
        dramaticAimCoroutine  = null;
    }

    // ─────────────────────────────────────────────
    //  AIMING
    // ─────────────────────────────────────────────

    private void StartAiming(Target target)
    {
        if (revolver == null)
        {
            Debug.LogWarning($"AI {playerName}: Revolver is null, cannot aim!");
            return;
        }

        if (gameManager != null)
            gameManager.AllowRevolverRotation(true);

        currentTargetDecision  = target;
        isAiming               = true;
        isShooting             = false;
        aimProgress            = 0f;
        targetTransform        = GetTargetTransform(target);

        originalRotation        = transform.rotation;
        revolverOriginalRotation = revolver.transform.rotation;

        LockRevolverPhysics();

        targetRotation        = CalculateTargetRotation(target);
        revolverTargetRotation = CalculateRevolverTargetRotation(target);

        if (aimIndicator != null) aimIndicator.SetActive(true);
    }

    /// <summary>
    /// Body rotation toward camera (opponent) or angle-based fallback (self).
    /// </summary>
    private Quaternion CalculateTargetRotation(Target target)
    {
        if (target == Target.Opponent && playerCameraTransform != null)
        {
            Vector3 dir = (playerCameraTransform.position - transform.position).normalized;
            dir.y = 0f; // Keep body upright — only rotate on Y axis
            return Quaternion.LookRotation(dir);
        }

        float targetAngle = target == Target.Self ? selfAimAngle : opponentAimAngle;
        return originalRotation * Quaternion.Euler(0f, targetAngle, 0f);
    }

    /// <summary>
    /// Revolver rotation so the barrel points directly at the target.
    /// Uses muzzlePoint's forward (Z axis) as the barrel direction — make sure
    /// muzzlePoint's blue arrow points straight out of the barrel in the editor.
    /// </summary>
private Quaternion CalculateRevolverTargetRotation(Target target)
{
    if (target == Target.Opponent && playerCameraTransform != null && revolver != null)
    {
        Transform muzzle = revolver.muzzlePoint ?? revolver.transform;
        Vector3 desiredDirection = (playerCameraTransform.position - muzzle.position).normalized;

        Quaternion worldRotation = Quaternion.LookRotation(desiredDirection, Vector3.up);
        Quaternion muzzleToRevolver = Quaternion.Inverse(
            Quaternion.LookRotation(muzzle.forward, muzzle.up)
        ) * revolver.transform.rotation;

        return worldRotation * muzzleToRevolver;
    }

    // For self-shot: base off the body's target rotation for this turn, not a stale one
    Quaternion bodyRotation = CalculateTargetRotation(target);
    float revolverAngle = target == Target.Self ? revolverSelfAimAngle : revolverOpponentAimAngle;
    return bodyRotation * Quaternion.Euler(0f, revolverAngle, 0f);
}
    /// <summary>Smoothly interpolates rotation towards target during aiming.</summary>
    private void UpdateAiming()
    {
        aimProgress += Time.deltaTime / aimDuration;
        aimProgress  = Mathf.Clamp01(aimProgress);

        float curveValue = aimCurve.Evaluate(aimProgress);

        transform.rotation = Quaternion.Slerp(originalRotation, targetRotation, curveValue);
        if (aimPoint != transform)
            aimPoint.rotation = Quaternion.Slerp(originalRotation, targetRotation, curveValue);

        if (revolver != null)
        {
            Quaternion newRot = Quaternion.Slerp(revolverOriginalRotation, revolverTargetRotation, curveValue);
            revolver.transform.rotation = newRot;

            Rigidbody rb = revolver.GetComponent<Rigidbody>();
            if (rb != null && !rb.isKinematic)
                rb.MoveRotation(newRot);
        }

        UpdateAimIndicator();

        if (aimProgress >= 1f && !isShooting)
            OnAimingComplete();
    }

    private void OnAimingComplete()
    {
        isAiming = false;
    }

    // ─────────────────────────────────────────────
    //  AI DECISION MAKING
    // ─────────────────────────────────────────────

    /// <summary>Core AI decision: self or opponent?</summary>
    public Target DecideTarget(int chambersLeft)
    {
        RefreshDynamicPersonality(chambersLeft);

        float bulletRisk      = CalculateBulletRisk(chambersLeft);
        float selfShootChance = CalculateSelfShootProbability(bulletRisk, chambersLeft);
        float roll            = Random.Range(0f, 1f);
        return roll < selfShootChance ? Target.Self : Target.Opponent;
    }

    private void RefreshDynamicPersonality(int chambersLeft)
    {
        if (maxHealth <= 0)
        {
            dynamicAggression = aggression;
            dynamicFear = fear;
            dynamicConfidence = confidence;
            return;
        }

        float selfHealthRatio = currentHealth / (float)maxHealth;
        float opponentHealthRatio = GetOpponentHealthRatio();
        float healthDifference = selfHealthRatio - opponentHealthRatio;

        int bulletsRemaining = revolver != null ? revolver.BulletPositions.Count : 0;
        int maxChambers = revolver != null ? revolver.MaxChambers : 6;
        float bulletDensity = maxChambers > 0 ? bulletsRemaining / (float)maxChambers : 0f;
        float chamberPressure = maxChambers > 0 ? 1f - (chambersLeft / (float)maxChambers) : 0f;
        float roundPressure = gameManager != null ? Mathf.Clamp01(gameManager.CurrentTurn / 10f) : 0f;

        float advantageFactor = healthDifference * 0.25f;
        float weaknessFactor = 1f - opponentHealthRatio;
        float selfRiskFactor = 1f - selfHealthRatio;

        dynamicAggression = Mathf.Clamp01(aggression + advantageFactor + weaknessFactor * 0.2f - bulletDensity * 0.15f - roundPressure * 0.1f);
        dynamicFear       = Mathf.Clamp01(fear + selfRiskFactor * 0.3f + bulletDensity * 0.2f + chamberPressure * 0.15f + roundPressure * 0.1f - advantageFactor * 0.1f);
        dynamicConfidence = Mathf.Clamp01(confidence + advantageFactor * 0.3f + (1f - bulletDensity) * 0.2f - roundPressure * 0.05f);
    }

    private float GetOpponentHealthRatio()
    {
        if (gameManager == null) return 1f;

        foreach (var player in gameManager.GetAllPlayers())
        {
            if ((Object)player != this)
            {
                return Mathf.Clamp01(player.Health / (float)Mathf.Max(1, maxHealth));
            }
        }

        return 1f;
    }

    private float CalculateBulletRisk(int chambersLeft)
    {
        if (chambersLeft <= 0) return 1f;
        float baseProbability = 1f / chambersLeft;
        float perceivedRisk   = baseProbability * (1f + dynamicFear * 0.5f);
        return Mathf.Clamp01(perceivedRisk);
    }

    private float CalculateSelfShootProbability(float bulletRisk, int chambersLeft)
    {
        float baseChance        = 0.5f;
        float confidenceFactor  = dynamicConfidence * 0.3f;
        float aggressionPenalty = dynamicAggression * 0.4f;
        float riskPenalty       = bulletRisk * (1f + dynamicFear);
        float chamberBonus      = Mathf.Clamp01(chambersLeft / 6f) * 0.2f;

        float observationFactor = 0f;
        if (adaptToObservations && (observedSelfShots + observedOpponentShots) > 0)
        {
            float survivalRate  = observedSurvivalCount / (float)(observedSelfShots + observedOpponentShots);
            observationFactor   = survivalRate * 0.15f;
        }

        float finalChance = baseChance + confidenceFactor + chamberBonus + observationFactor
                            - aggressionPenalty - riskPenalty;
        finalChance = Mathf.Lerp(finalChance, riskThreshold, dynamicRiskLevel);
        return Mathf.Clamp01(finalChance);
    }

    /// <summary>Learn from other players' actions to adjust future behavior.</summary>
    public void ObservePlayerAction(Target playerChoice, int chambersLeft, bool npcShotSelfLastTurn)
    {
        if (!adaptToObservations) return;

        if (playerChoice == Target.Self)
        {
            observedSelfShots++;
            observedSurvivalCount++;
        }
        else
        {
            observedOpponentShots++;
        }

        if (chambersLeft <= 2)
            dynamicRiskLevel = Mathf.Max(0.3f, dynamicRiskLevel - 0.1f);

        if (playerChoice == Target.Self && chambersLeft < 3)
            dynamicConfidence = Mathf.Min(1f, dynamicConfidence + 0.05f);
    }

    // ─────────────────────────────────────────────
    //  DAMAGE / ELIMINATION
    // ─────────────────────────────────────────────

    public void TakeDamage(int damage)
    {
        currentHealth -= damage;
        Events.Publish(new PlayerDamagedEvent { Player = this, Damage = damage });

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Eliminate();
        }
        UpdateVisuals();
    }

    public void Eliminate()
    {
        if (!isAlive) return;

        isAlive       = false;
        currentHealth = 0;
        UpdateVisuals();
        StopAiming();

        if (eliminatedEffect != null)
            Instantiate(eliminatedEffect, transform.position, Quaternion.identity);

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Events.Publish(new PlayerEliminatedEvent { Player = this });
    }

    public void Reset()
    {
        currentHealth         = maxHealth;
        isAlive               = true;
        observedSelfShots     = 0;
        observedOpponentShots = 0;
        observedSurvivalCount = 0;
        dynamicRiskLevel      = 0.5f;
        dynamicAggression     = aggression;
        dynamicFear           = fear;
        dynamicConfidence     = confidence;

        StopAiming();
        transform.rotation = originalRotation;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        UpdateVisuals();
    }

    // ─────────────────────────────────────────────
    //  FORCE SHOOT (debug / special events)
    // ─────────────────────────────────────────────

    /// <summary>Force an immediate shot at the specified target.</summary>
    public void ForceShoot(Target target)
    {
        if (!isAlive || isAiming || isShooting) return;

        if (target == Target.Self && enableDramaticAiming)
            StartCoroutine(DramaticForceShootSequence());
        else
        {
            StartAiming(target);
            StartCoroutine(ForceShootSequence(target));
        }
    }

    private IEnumerator DramaticForceShootSequence()
    {
        yield return StartCoroutine(DramaticSelfShotSequence());
        ExecuteShotEffects(Target.Self);
    }

    private IEnumerator ForceShootSequence(Target target)
    {
        while (isAiming) yield return null;
        yield return new WaitForSeconds(shootDelayAfterAim);
        ExecuteShotEffects(target);
    }

    /// <summary>Visual/audio effects only — GameManager handles actual shot logic.</summary>
    private void ExecuteShotEffects(Target target)
    {
        if (muzzleFlashEffect != null && aimPoint != null)
            Instantiate(muzzleFlashEffect, aimPoint.position, aimPoint.rotation);

        if (shootSound != null && audioSource != null)
            audioSource.PlayOneShot(shootSound);

        IPlayer targetPlayer = GetTargetPlayer(target);
        if (targetPlayer != null && gameManager != null)
            Events.Publish(new RevolverFiredEvent());
    }

    // ─────────────────────────────────────────────
    //  HELPERS
    // ─────────────────────────────────────────────

    private void LockRevolverPhysics()
    {
        if (revolver == null) return;
        Rigidbody rb = revolver.GetComponent<Rigidbody>();
        if (rb == null) return;
        rb.isKinematic      = true;
        rb.useGravity       = false;
        rb.linearVelocity   = Vector3.zero;
        rb.angularVelocity  = Vector3.zero;
    }

    private int GetChambersLeft()
    {
        return revolver != null ? revolver.MaxChambers - revolver.CurrentChamber : 6;
    }

    private IPlayer GetTargetPlayer(Target target)
    {
        if (target == Target.Self) return this;

        if (gameManager != null)
        {
            foreach (var player in gameManager.GetAllPlayers())
            {
                if ((Object)player != this && player.IsAlive)
                    return player;
            }
        }
        return null;
    }

    private Transform GetTargetTransform(Target target)
    {
        if (target == Target.Self) return transform;

        if (gameManager != null)
        {
            foreach (var player in gameManager.GetAllPlayers())
            {
                if ((Object)player != this && player.IsAlive)
                {
                    MonoBehaviour mono = player as MonoBehaviour;
                    if (mono != null) return mono.transform;
                }
            }
        }
        return null;
    }

    private Vector3 GetAimDirection(Target target, Transform t)
    {
        if (target == Target.Self)
            return (transform.position + Vector3.up * 1.5f - aimPoint.position).normalized;

        if (t != null)
            return (t.position + Vector3.up * 1.2f - aimPoint.position).normalized;

        return transform.forward;
    }

    private void UpdateAimIndicator()
    {
        if (aimIndicator == null) return;

        aimIndicator.transform.position = aimPoint.position;

        Vector3 dir = GetAimDirection(currentTargetDecision, targetTransform);
        if (dir != Vector3.zero)
            aimIndicator.transform.rotation = Quaternion.LookRotation(dir);

        Renderer r = aimIndicator.GetComponent<Renderer>();
        if (r != null)
        {
            Color c = currentTargetDecision == Target.Self ? Color.yellow : Color.red;
            c.a = 0.7f;
            MaterialPropertyBlock props = new MaterialPropertyBlock();
            r.GetPropertyBlock(props);
            props.SetColor("_Color", c);
            r.SetPropertyBlock(props);
        }
    }

    private void UpdateVisuals()
    {
        if (npcRenderer == null) return;
        MaterialPropertyBlock props = new MaterialPropertyBlock();
        npcRenderer.GetPropertyBlock(props);
        props.SetColor("_Color", isAlive ? aliveColor : deadColor);
        npcRenderer.SetPropertyBlock(props);
    }

    // ─────────────────────────────────────────────
    //  STOP / CLEANUP
    // ─────────────────────────────────────────────

    /// <summary>Emergency stop for all AI activities.</summary>
    public void StopAiming()
    {
        isAiming    = false;
        isShooting  = false;
        aimProgress = 0f;

        if (dramaticAimCoroutine != null)
        {
            StopCoroutine(dramaticAimCoroutine);
            dramaticAimCoroutine = null;
        }

        if (aimIndicator != null) aimIndicator.SetActive(false);

        transform.rotation = originalRotation;
        if (aimPoint != transform) aimPoint.rotation = originalRotation;
        if (revolver != null)     revolver.transform.rotation = revolverOriginalRotation;

        if (gameManager != null) gameManager.AllowRevolverRotation(false);

        if (currentTurnCoroutine != null)
        {
            StopCoroutine(currentTurnCoroutine);
            currentTurnCoroutine = null;
        }
    }

    // ─────────────────────────────────────────────
    //  PUBLIC GETTERS / SETTERS
    // ─────────────────────────────────────────────

    public bool IsAiming()         => isAiming;
    public bool IsShooting()       => isShooting;
    public Target GetCurrentTarget() => currentTargetDecision;

    public void SetPersonality(float newAggression, float newFear, float newConfidence)
    {
        aggression  = Mathf.Clamp01(newAggression);
        fear        = Mathf.Clamp01(newFear);
        confidence  = Mathf.Clamp01(newConfidence);
    }

    public void SetRiskThreshold(float threshold)   => riskThreshold = Mathf.Clamp01(threshold);
    public void SetAimPoint(Transform newAimPoint)  => aimPoint = newAimPoint;

    public void SetAimAngles(float selfAngle, float opponentAngle)
    {
        selfAimAngle     = selfAngle;
        opponentAimAngle = opponentAngle;
    }

    public void SetRevolverAimAngles(float selfAngle, float opponentAngle)
    {
        revolverSelfAimAngle     = selfAngle;
        revolverOpponentAimAngle = opponentAngle;
    }

    // ─────────────────────────────────────────────
    //  GIZMOS
    // ─────────────────────────────────────────────

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = isAlive ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.3f);

        Gizmos.color = Color.Lerp(Color.blue, Color.red, aggression);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * (1f + aggression));

        if ((isAiming || isShooting) && aimPoint != null)
        {
            Vector3 dir = GetAimDirection(currentTargetDecision, targetTransform);
            Gizmos.color = currentTargetDecision == Target.Self ? Color.yellow : Color.red;
            Gizmos.DrawRay(aimPoint.position, dir * 2f);
            Gizmos.DrawWireSphere(aimPoint.position + dir * 2f, 0.1f);
        }
    }
}