using Doody.GameEvents;
using System.Collections;
using UnityEngine;

public class AI : EventListener, IAIPlayer, IPlayerStats
{
    [Header("Player Info")]
    [SerializeField] private string playerName = "NPC";
    [SerializeField] private int playerID = 1;
    [SerializeField] private int maxHealth = 1;

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
    private float dynamicRiskLevel = 0.5f; // Adjusts based on game observations

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

    // Public properties for interfaces
    public int Health => currentHealth;
    public string PlayerName => playerName;
    public bool IsAlive => isAlive;
    public int ID => playerID;
    public float Aggression => aggression;
    public float Fear => fear;
    public float Confidence => confidence;

    void Awake()
    {
        currentHealth = maxHealth;
        UpdateVisuals();
        originalRotation = transform.rotation;
        gameManager = FindAnyObjectByType<GameManager>();
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();

        if (revolver != null)
        {
            revolverOriginalRotation = revolver.transform.rotation;
        }
    }

    void Start()
    {
        if (!gameObject.CompareTag("NPC"))
        {
            gameObject.tag = "NPC";
        }

        if (aimPoint == null)
        {
            aimPoint = transform;
        }

        if (aimIndicator != null)
        {
            aimIndicator.SetActive(false);
        }
    }

    void Update()
    {
        // Only handle smooth aiming when not in dramatic sequence
        if (isAiming && dramaticAimCoroutine == null)
        {
            UpdateAiming();
        }
    }

    /// <summary>Main turn handler - decides target and executes shooting sequence</summary>
    public void TakeTurn()
    {
        if (isAlive && !isAiming && !isShooting && currentTurnCoroutine == null)
        {
            currentTurnCoroutine = StartCoroutine(AITurnSequence());
        }
    }

    private IEnumerator AITurnSequence()
    {
        yield return new WaitForSeconds(1f); // Brief pause before starting

        if (!isAlive) yield break;

        int chambersLeft = GetChambersLeft();
        Target decision = DecideTarget(chambersLeft);

        // Handle dramatic self-shot sequence (aim at opponent first, then self)
        if (decision == Target.Self && enableDramaticAiming)
        {
            yield return StartCoroutine(DramaticSelfShotSequence());
        }
        else
        {
            // Standard aiming sequence
            StartAiming(decision);
            while (isAiming) yield return null;
            yield return new WaitForSeconds(shootDelayAfterAim);
        }

        ExecuteShotThroughGameManager(decision);
        currentTurnCoroutine = null;
    }

    /// <summary>Dramatic sequence: aim at opponent, pause, then quickly aim at self</summary>
    private IEnumerator DramaticSelfShotSequence()
    {
        StartAiming(Target.Opponent);
        while (isAiming) yield return null;

        yield return new WaitForSeconds(dramaticPauseDuration); // Dramatic tension

        dramaticAimCoroutine = StartCoroutine(DramaticAimTransition(Target.Self));
        yield return dramaticAimCoroutine;

        yield return new WaitForSeconds(shootDelayAfterAim * 0.5f); // Shorter delay for drama
    }

    /// <summary>Quick aim transition for dramatic effect</summary>
    private IEnumerator DramaticAimTransition(Target finalTarget)
    {
        isAiming = true;
        aimProgress = 0f;
        currentTargetDecision = finalTarget;
        targetTransform = GetTargetTransform(finalTarget);

        Quaternion startRotation = transform.rotation;
        Quaternion startRevolverRotation = revolver != null ? revolver.transform.rotation : Quaternion.identity;

        targetRotation = CalculateTargetRotation(finalTarget);
        if (revolver != null)
        {
            revolverTargetRotation = CalculateRevolverTargetRotation(finalTarget);
        }

        // Animate the dramatic rotation
        float elapsed = 0f;
        while (elapsed < dramaticAimDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / dramaticAimDuration);
            float curveValue = aimCurve.Evaluate(t);

            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, curveValue);
            if (aimPoint != transform) aimPoint.rotation = Quaternion.Slerp(startRotation, targetRotation, curveValue);
            if (revolver != null) revolver.transform.rotation = Quaternion.Slerp(startRevolverRotation, revolverTargetRotation, curveValue);

            UpdateAimIndicator();
            yield return null;
        }

        // Finalize rotations
        transform.rotation = targetRotation;
        if (aimPoint != transform) aimPoint.rotation = targetRotation;
        if (revolver != null) revolver.transform.rotation = revolverTargetRotation;

        isAiming = false;
        dramaticAimCoroutine = null;
    }

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

    /// <summary>Core AI decision making - calculates probability of self-shot vs opponent shot</summary>
    public Target DecideTarget(int chambersLeft)
    {
        float bulletRisk = CalculateBulletRisk(chambersLeft);
        float selfShootChance = CalculateSelfShootProbability(bulletRisk, chambersLeft);
        float roll = Random.Range(0f, 1f);

        Target decision = roll < selfShootChance ? Target.Self : Target.Opponent;
        return decision;
    }

    /// <summary>Calculates perceived risk of bullet in next chamber</summary>
    private float CalculateBulletRisk(int chambersLeft)
    {
        if (chambersLeft <= 0) return 1f;
        float baseProbability = 1f / chambersLeft;
        float perceivedRisk = baseProbability * (1f + fear * 0.5f); // Fear amplifies perceived risk
        return Mathf.Clamp01(perceivedRisk);
    }

    /// <summary>Complex probability calculation combining personality, risk, and observations</summary>
    private float CalculateSelfShootProbability(float bulletRisk, int chambersLeft)
    {
        float baseChance = 0.5f;
        float confidenceFactor = confidence * 0.3f;           // Confidence increases self-shots
        float aggressionPenalty = aggression * 0.4f;          // Aggressive AIs prefer shooting opponents
        float riskPenalty = bulletRisk * (1f + fear);         // Risk and fear reduce self-shots
        float chamberBonus = Mathf.Clamp01(chambersLeft / 6f) * 0.2f; // More chambers = more self-shots

        // Learning from observations - survival rate influences decisions
        float observationFactor = 0f;
        if (adaptToObservations && (observedSelfShots + observedOpponentShots) > 0)
        {
            float survivalRate = observedSurvivalCount / (float)(observedSelfShots + observedOpponentShots);
            observationFactor = survivalRate * 0.15f;
        }

        // Combine all factors and adjust toward risk threshold
        float finalChance = baseChance + confidenceFactor + chamberBonus + observationFactor
                           - aggressionPenalty - riskPenalty;
        finalChance = Mathf.Lerp(finalChance, riskThreshold, dynamicRiskLevel);
        return Mathf.Clamp01(finalChance);
    }

    /// <summary>Learn from other players' actions to adjust behavior</summary>
    public void ObservePlayerAction(Target playerChoice, int chambersLeft, bool npcShotSelfLastTurn)
    {
        if (!adaptToObservations) return;

        // Track shot types and survival outcomes
        if (playerChoice == Target.Self)
        {
            observedSelfShots++;
            observedSurvivalCount++; // Self-shot that didn't fire counts as survival
        }
        else
        {
            observedOpponentShots++;
        }

        // Adjust risk level and confidence based on game state
        if (chambersLeft <= 2) dynamicRiskLevel = Mathf.Max(0.3f, dynamicRiskLevel - 0.1f); // More cautious near end
        if (playerChoice == Target.Self && chambersLeft < 3) confidence = Mathf.Min(1f, confidence + 0.05f); // Gain confidence from risky plays
    }

    private void StartAiming(Target target)
    {
        if (revolver == null) return;

        if (gameManager != null)
        {
            gameManager.AllowRevolverRotation(true);
        }

        // Initialize aiming state
        currentTargetDecision = target;
        isAiming = true;
        isShooting = false;
        aimProgress = 0f;
        targetTransform = GetTargetTransform(target);

        // Store starting rotations
        originalRotation = transform.rotation;
        if (revolver != null) revolverOriginalRotation = revolver.transform.rotation;

        // Calculate target rotations
        targetRotation = CalculateTargetRotation(target);
        if (revolver != null) revolverTargetRotation = CalculateRevolverTargetRotation(target);

        if (aimIndicator != null) aimIndicator.SetActive(true);
    }

    private Quaternion CalculateTargetRotation(Target target)
    {
        float targetAngle = target == Target.Self ? selfAimAngle : opponentAimAngle;
        return Quaternion.Euler(0f, targetAngle, 0f);
    }

    private Quaternion CalculateRevolverTargetRotation(Target target)
    {
        float revolverAngle = target == Target.Self ? revolverSelfAimAngle : revolverOpponentAimAngle;
        return Quaternion.Euler(0f, revolverAngle, 0f);
    }

    /// <summary> Smoothly interpolates rotation towards target during aiming</summary>
    private void UpdateAiming()
    {
        aimProgress += Time.deltaTime / aimDuration;
        aimProgress = Mathf.Clamp01(aimProgress);

        float curveValue = aimCurve.Evaluate(aimProgress);

        // Apply rotation interpolation
        transform.rotation = Quaternion.Slerp(originalRotation, targetRotation, curveValue);
        if (aimPoint != transform) aimPoint.rotation = Quaternion.Slerp(originalRotation, targetRotation, curveValue);
        if (revolver != null) revolver.transform.rotation = Quaternion.Slerp(revolverOriginalRotation, revolverTargetRotation, curveValue);

        UpdateAimIndicator();

        if (aimProgress >= 1f && !isShooting)
        {
            OnAimingComplete();
        }
    }

    private void OnAimingComplete()
    {
        isAiming = false;
    }

    /// <summary>Triggers visual/audio effects and notifies GameManager of shot</summary>
    private void ExecuteShotThroughGameManager(Target target)
    {
        // Visual and audio effects
        if (muzzleFlashEffect != null && aimPoint != null)
        {
            Instantiate(muzzleFlashEffect, aimPoint.position, aimPoint.rotation);
        }

        if (shootSound != null && audioSource != null)
        {
            audioSource.PlayOneShot(shootSound);
        }

        // Notify game manager to handle shot logic
        IPlayer targetPlayer = GetTargetPlayer(target);
        if (targetPlayer != null && gameManager != null)
        {
            Events.Publish(new RevolverFiredEvent());
           // gameManager.OnRevolverFired(targetPlayer);
        }
    }

    private IPlayer GetTargetPlayer(Target target)
    {
        if (target == Target.Self) return this;

        // Find first alive opponent
        if (gameManager != null)
        {
            var players = gameManager.GetAllPlayers();
            foreach (var player in players)
            {
                if ((Object)player != this && player.IsAlive)
                {
                    return player;
                }
            }
        }
        return null;
    }

    private int GetChambersLeft()
    {
        if (revolver != null)
        {
            return revolver.MaxChambers - revolver.CurrentChamber;
        }
        return 6; // Default revolver size
    }

    /// <summary>Calculates aim direction with appropriate height offsets</summary>
    private Vector3 GetAimDirection(Target target, Transform targetTransform)
    {
        if (target == Target.Self)
        {
            Vector3 selfAimPoint = transform.position + Vector3.up * 1.5f; // Aim at upper body/head
            return (selfAimPoint - aimPoint.position).normalized;
        }
        else if (targetTransform != null)
        {
            Vector3 opponentAimPoint = targetTransform.position + Vector3.up * 1.2f; // Aim at chest level
            return (opponentAimPoint - aimPoint.position).normalized;
        }

        return transform.forward; // Fallback direction
    }

    private Transform GetTargetTransform(Target target)
    {
        if (target == Target.Self) return transform;

        // Find first alive opponent's transform
        if (gameManager != null)
        {
            var players = gameManager.GetAllPlayers();
            foreach (var player in players)
            {
                if ((Object)player != this && player.IsAlive)
                {
                    MonoBehaviour playerMono = player as MonoBehaviour;
                    if (playerMono != null) return playerMono.transform;
                }
            }
        }

        return null;
    }

    /// <summary>Updates aim indicator position, rotation, and color based on target</summary>
    private void UpdateAimIndicator()
    {
        if (aimIndicator == null) return;

        aimIndicator.transform.position = aimPoint.position;

        Vector3 targetDirection = GetAimDirection(currentTargetDecision, targetTransform);
        if (targetDirection != Vector3.zero)
        {
            aimIndicator.transform.rotation = Quaternion.LookRotation(targetDirection);
        }

        // Set color based on target (yellow for self, red for opponent)
        Renderer indicatorRenderer = aimIndicator.GetComponent<Renderer>();
        if (indicatorRenderer != null)
        {
            Color indicatorColor = currentTargetDecision == Target.Self ? Color.yellow : Color.red;
            indicatorColor.a = 0.7f;
            MaterialPropertyBlock props = new MaterialPropertyBlock();
            indicatorRenderer.GetPropertyBlock(props);
            props.SetColor("_Color", indicatorColor);
            indicatorRenderer.SetPropertyBlock(props);
        }
    }

    /// <summary>Emergency stop for all AI activities</summary>
    public void StopAiming()
    {
        isAiming = false;
        isShooting = false;
        aimProgress = 0f;

        // Stop any running coroutines
        if (dramaticAimCoroutine != null)
        {
            StopCoroutine(dramaticAimCoroutine);
            dramaticAimCoroutine = null;
        }

        if (aimIndicator != null) aimIndicator.SetActive(false);

        // Reset rotations to original state
        transform.rotation = originalRotation;
        if (aimPoint != transform) aimPoint.rotation = originalRotation;
        if (revolver != null) revolver.transform.rotation = revolverOriginalRotation;

        if (gameManager != null) gameManager.AllowRevolverRotation(false);
        if (currentTurnCoroutine != null)
        {
            StopCoroutine(currentTurnCoroutine);
            currentTurnCoroutine = null;
        }
    }

    public bool IsAiming() => isAiming;
    public bool IsShooting() => isShooting;
    public Target GetCurrentTarget() => currentTargetDecision;

    public void Eliminate()
    {
        if (!isAlive) return;

        isAlive = false;
        currentHealth = 0;
        UpdateVisuals();
        StopAiming();

        if (eliminatedEffect != null)
        {
            Instantiate(eliminatedEffect, transform.position, Quaternion.identity);
        }

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Events.Publish(new PlayerEliminatedEvent { Player = this });
    }

    private void UpdateVisuals()
    {
        if (npcRenderer != null)
        {
            MaterialPropertyBlock props = new MaterialPropertyBlock();
            npcRenderer.GetPropertyBlock(props);
            props.SetColor("_Color", isAlive ? aliveColor : deadColor);
            npcRenderer.SetPropertyBlock(props);
        }
    }

    public void Reset()
    {
        currentHealth = maxHealth;
        isAlive = true;
        // Reset learning observations
        observedSelfShots = 0;
        observedOpponentShots = 0;
        observedSurvivalCount = 0;
        dynamicRiskLevel = 0.5f;

        StopAiming();
        transform.rotation = originalRotation;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        UpdateVisuals();
    }

    /// <summary>Debug visualization for AI state and aiming</summary>
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        // Status sphere above AI head
        Gizmos.color = isAlive ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.3f);

        // Personality indicator (aggression)
        Gizmos.color = Color.Lerp(Color.blue, Color.red, aggression);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * (1f + aggression));

        // Aiming visualization
        if ((isAiming || isShooting) && aimPoint != null)
        {
            Vector3 aimDirection = GetAimDirection(currentTargetDecision, targetTransform);
            Gizmos.color = currentTargetDecision == Target.Self ? Color.yellow : Color.red;
            Gizmos.DrawRay(aimPoint.position, aimDirection * 2f);
            Gizmos.DrawWireSphere(aimPoint.position + aimDirection * 2f, 0.1f);
        }
    }

    // Public configuration methods
    public void SetPersonality(float newAggression, float newFear, float newConfidence)
    {
        aggression = Mathf.Clamp01(newAggression);
        fear = Mathf.Clamp01(newFear);
        confidence = Mathf.Clamp01(newConfidence);
    }

    public void SetRiskThreshold(float threshold)
    {
        riskThreshold = Mathf.Clamp01(threshold);
    }

    public void SetAimPoint(Transform newAimPoint)
    {
        aimPoint = newAimPoint;
    }

    public void SetAimAngles(float selfAngle, float opponentAngle)
    {
        selfAimAngle = selfAngle;
        opponentAimAngle = opponentAngle;
    }

    public void SetRevolverAimAngles(float selfAngle, float opponentAngle)
    {
        revolverSelfAimAngle = selfAngle;
        revolverOpponentAimAngle = opponentAngle;
    }

    /// <summary>Force immediate shot at specified target (for debugging or special events)</summary>
    public void ForceShoot(Target target)
    {
        if (isAlive && !isAiming && !isShooting)
        {
            if (target == Target.Self && enableDramaticAiming)
            {
                StartCoroutine(DramaticForceShootSequence());
            }
            else
            {
                StartAiming(target);
                StartCoroutine(ForceShootSequence(target));
            }
        }
    }

    private IEnumerator DramaticForceShootSequence()
    {
        yield return StartCoroutine(DramaticSelfShotSequence());
        ExecuteShotThroughGameManager(Target.Self);
    }

    private IEnumerator ForceShootSequence(Target target)
    {
        while (isAiming) yield return null;
        yield return new WaitForSeconds(shootDelayAfterAim);
        ExecuteShotThroughGameManager(target);
    }
}