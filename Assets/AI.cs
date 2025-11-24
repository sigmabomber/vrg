using UnityEngine;
using System.Collections.Generic;

public class AI : MonoBehaviour, IAIPlayer, IPlayerStats
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

    [Header("Visual Feedback")]
    [SerializeField] private Renderer npcRenderer;
    [SerializeField] private Color aliveColor = Color.green;
    [SerializeField] private Color deadColor = Color.red;
    [SerializeField] private GameObject eliminatedEffect;
    [SerializeField] private GameObject aimIndicator;

    private int currentHealth;
    private bool isAlive = true;

    public int Health => currentHealth;
    public string PlayerName => playerName;
    public bool IsAlive => isAlive;
    public int ID => playerID;

    public float Aggression => aggression;
    public float Fear => fear;
    public float Confidence => confidence;

    private int observedSelfShots = 0;
    private int observedOpponentShots = 0;
    private int observedSurvivalCount = 0;
    private float dynamicRiskLevel = 0.5f;

    private Quaternion originalRotation;
    private Quaternion targetRotation;
    private bool isAiming = false;
    private float aimProgress = 0f;
    private Target currentTargetDecision;
    private Transform targetTransform;

    private GameManager gameManager;
    private Revolver revolver;

    void Awake()
    {
        currentHealth = maxHealth;
        UpdateVisuals();
        originalRotation = transform.rotation;
        gameManager = Object.FindAnyObjectByType<GameManager>();
        revolver = Object.FindAnyObjectByType<Revolver>();
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
        if (isAiming)
        {
            UpdateAiming();
        }
    }

    public void TakeTurn()
    {
    }

    public void TakeDamage(int damage)
    {
        currentHealth -= damage;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Eliminate();
        }
    }

    public Target DecideTarget(int chambersLeft)
    {
        float bulletRisk = CalculateBulletRisk(chambersLeft);
        float selfShootChance = CalculateSelfShootProbability(bulletRisk, chambersLeft);
        float roll = Random.Range(0f, 1f);

        Target decision = roll < selfShootChance ? Target.Self : Target.Opponent;
        StartAiming(decision);
        return decision;
    }

    private void StartAiming(Target target)
    {
        currentTargetDecision = target;
        isAiming = true;
        aimProgress = 0f;
        targetTransform = GetTargetTransform(target);

        if (aimIndicator != null)
        {
            aimIndicator.SetActive(true);
        }
    }

    private void UpdateAiming()
    {
        if (targetTransform == null)
        {
            isAiming = false;
            return;
        }

        aimProgress += Time.deltaTime / aimDuration;
        aimProgress = Mathf.Clamp01(aimProgress);
        Vector3 targetDirection = GetAimDirection(currentTargetDecision, targetTransform);

        if (targetDirection != Vector3.zero)
        {
            targetRotation = Quaternion.LookRotation(targetDirection);
            float curveValue = aimCurve.Evaluate(aimProgress);
            transform.rotation = Quaternion.Slerp(originalRotation, targetRotation, curveValue);
            if (aimPoint != transform)
            {
                aimPoint.rotation = Quaternion.Slerp(originalRotation, targetRotation, curveValue);
            }
        }

        UpdateAimIndicator();

        if (aimProgress >= 1f)
        {
            isAiming = false;
        }
    }

    private Vector3 GetAimDirection(Target target, Transform targetTransform)
    {
        if (target == Target.Self)
        {
            Vector3 selfAimPoint = transform.position + Vector3.up * 1.5f;
            return (selfAimPoint - aimPoint.position).normalized;
        }
        else if (targetTransform != null)
        {
            Vector3 opponentAimPoint = targetTransform.position + Vector3.up * 1.2f;
            return (opponentAimPoint - aimPoint.position).normalized;
        }

        return transform.forward;
    }

    private Transform GetTargetTransform(Target target)
    {
        if (target == Target.Self) return transform;

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

    private void UpdateAimIndicator()
    {
        if (aimIndicator == null) return;

        aimIndicator.transform.position = aimPoint.position;
        Vector3 targetDirection = GetAimDirection(currentTargetDecision, targetTransform);
        if (targetDirection != Vector3.zero)
        {
            aimIndicator.transform.rotation = Quaternion.LookRotation(targetDirection);
        }

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

    public void StopAiming()
    {
        isAiming = false;
        aimProgress = 0f;

        if (aimIndicator != null)
        {
            aimIndicator.SetActive(false);
        }

        transform.rotation = originalRotation;
        if (aimPoint != transform)
        {
            aimPoint.rotation = originalRotation;
        }
    }

    public bool IsAiming() => isAiming;

    public Target GetCurrentTarget() => currentTargetDecision;

    private float CalculateBulletRisk(int chambersLeft)
    {
        if (chambersLeft <= 0) return 1f;
        float baseProbability = 1f / chambersLeft;
        float perceivedRisk = baseProbability * (1f + fear * 0.5f);
        return Mathf.Clamp01(perceivedRisk);
    }

    private float CalculateSelfShootProbability(float bulletRisk, int chambersLeft)
    {
        float baseChance = 0.5f;
        float confidenceFactor = confidence * 0.3f;
        float aggressionPenalty = aggression * 0.4f;
        float riskPenalty = bulletRisk * (1f + fear);
        float chamberBonus = Mathf.Clamp01(chambersLeft / 6f) * 0.2f;
        float observationFactor = 0f;

        if (adaptToObservations && (observedSelfShots + observedOpponentShots) > 0)
        {
            float survivalRate = observedSurvivalCount / (float)(observedSelfShots + observedOpponentShots);
            observationFactor = survivalRate * 0.15f;
        }

        float finalChance = baseChance + confidenceFactor + chamberBonus + observationFactor
                           - aggressionPenalty - riskPenalty;
        finalChance = Mathf.Lerp(finalChance, riskThreshold, dynamicRiskLevel);
        return Mathf.Clamp01(finalChance);
    }

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

        if (chambersLeft <= 2) dynamicRiskLevel = Mathf.Max(0.3f, dynamicRiskLevel - 0.1f);
        if (playerChoice == Target.Self && chambersLeft < 3) confidence = Mathf.Min(1f, confidence + 0.05f);
    }

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

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = isAlive ? Color.green : Color.red;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.3f);
        Gizmos.color = Color.Lerp(Color.blue, Color.red, aggression);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * (1f + aggression));

        if (isAiming && aimPoint != null)
        {
            Vector3 aimDirection = GetAimDirection(currentTargetDecision, targetTransform);
            Gizmos.color = currentTargetDecision == Target.Self ? Color.yellow : Color.red;
            Gizmos.DrawRay(aimPoint.position, aimDirection * 2f);
            Gizmos.DrawWireSphere(aimPoint.position + aimDirection * 2f, 0.1f);
        }
    }

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
}
