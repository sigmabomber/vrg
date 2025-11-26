using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class GameManager : MonoBehaviour, ITurnBased, IGameRules, IGameManager
{
    [Header("Game References")]
    [SerializeField] private Revolver revolver;
    [SerializeField] private UIDisplay uiDisplayScript;
    private IUIDisplay uiDisplay;

    [Header("Scene Positions")]
    [SerializeField] private Transform playerPosition;
    [SerializeField] private Transform npcPosition;
    [SerializeField] private Transform tableCenterPoint;
    [SerializeField] private Transform tableEdgePlayerSide;
    [SerializeField] private Transform tableEdgeNPCSide;

    [Header("Players")]
    [SerializeField] private GameObject playerPrefab;
    [SerializeField] private GameObject npcPrefab;
    [SerializeField] private List<GameObject> playerObjects = new List<GameObject>();
    private List<IPlayer> players = new List<IPlayer>();
    private IPlayer humanPlayer;

    [Header("Turn Settings")]
    [SerializeField] private float turnDuration = 30f;
    [SerializeField] private float aiThinkTime = 2f;
    [SerializeField] private float revolverPassTime = 1.5f;
    [SerializeField] private float ragdollDuration = 2f;
    [SerializeField] private float screenBlackDuration = 1f;

    [Header("Visual Settings")]
    [SerializeField] private bool animateRevolverPass = true;
    [SerializeField] private float revolverFloatHeight = 0.3f;

    [Header("Screen Fade")]
    [SerializeField] private CanvasGroup screenFadeCanvas;
    [SerializeField] private float fadeSpeed = 2f;

    [Header("Ragdoll Settings")]
    [SerializeField] private float ragdollForce = 10f;
    [SerializeField] private float ragdollUpwardForce = 3f;

    [Header("Collider Settings")]
    [SerializeField] private bool disableCollidersDuringReset = true;
    [SerializeField] private float colliderReenableDelay = 0.1f;

    [Header("Revolver Physics")]
    [SerializeField] private bool lockRevolverAtDestination = true;
    [SerializeField] private bool enableGravityOnBlankShots = false;

    private int currentTurn = 0;
    private float currentTurnTime = 0f;
    private int currentPlayerIndex = 0;

    public int CurrentTurn => currentTurn;
    public float TimeSpan => currentTurnTime;
    public int currentIDsTurn => players.Count > 0 ? players[currentPlayerIndex].ID : -1;

    public IReadOnlyList<IPlayer> Players => players.AsReadOnly();
    public IRevolverMechanic Revolver => revolver as IRevolverMechanic;
    public ITurnBased TurnSystem => this;
    public IGameRules Rules => this;

    private bool gameActive = false;
    private bool waitingForPlayerAction = false;
    private IPlayer winner = null;
    private bool isPassingRevolver = false;
    private bool isResettingScene = false;
    private bool isProcessingShot = false;

    private XRGrabInteractable revolverGrabInteractable;
    private Rigidbody revolverRigidbody;
    private bool originalRevolverKinematic;
    private bool originalRevolverGravity;

    private Dictionary<IPlayer, (Vector3 position, Quaternion rotation, Vector3 scale, bool wasKinematic, bool useGravity, bool hasRigidbody, bool[] colliderStates, RigidbodyConstraints originalConstraints)> originalTransforms = new Dictionary<IPlayer, (Vector3, Quaternion, Vector3, bool, bool, bool, bool[], RigidbodyConstraints)>();

    void Start()
    {
        ValidatePositions();
        InitializePlayers();

        if (revolver != null)
        {
            revolverGrabInteractable = revolver.GetComponent<XRGrabInteractable>();
            revolverRigidbody = revolver.GetComponent<Rigidbody>();

            if (revolverGrabInteractable == null)
            {
                Debug.LogError("Revolver doesn't have XRGrabInteractable component!");
            }

            if (revolverRigidbody != null)
            {
                originalRevolverKinematic = revolverRigidbody.isKinematic;
                originalRevolverGravity = revolverRigidbody.useGravity;
            }
        }

        if (uiDisplay == null)
        {
            uiDisplay = uiDisplayScript as IUIDisplay;

            if (uiDisplay == null)
            {
                Debug.LogWarning("No IUIDisplay found in scene!");
            }
        }

        if (screenFadeCanvas != null)
        {
            screenFadeCanvas.alpha = 0f;
            screenFadeCanvas.blocksRaycasts = false;
        }

        PositionRevolver(tableCenterPoint);
        StartGame();
    }

    void Update()
    {
        if (!gameActive || isPassingRevolver || isResettingScene) return;

        currentTurnTime += Time.deltaTime;

        if (uiDisplay != null && waitingForPlayerAction)
        {
            float timeLeft = Mathf.Max(0f, turnDuration - currentTurnTime);
            uiDisplay.UpdateTurnTimer(timeLeft);

            if (timeLeft < 5f && timeLeft > 0f)
            {
                uiDisplay.ShowWarning($"Time running out: {timeLeft:F1}s");
            }
        }

        if (currentTurnTime >= turnDuration && waitingForPlayerAction)
        {
            Debug.Log("Turn timeout - ending turn");
            ForcePlayerShot();
        }
    }

    void ValidatePositions()
    {
        if (playerPosition == null) Debug.LogError("Player Position not assigned!");
        if (npcPosition == null) Debug.LogError("NPC Position not assigned!");
        if (tableCenterPoint == null) Debug.LogError("Table Center Point not assigned!");
        if (tableEdgePlayerSide == null) Debug.LogError("Table Edge (Player Side) not assigned!");
        if (tableEdgeNPCSide == null) Debug.LogError("Table Edge (NPC Side) not assigned!");
    }

    void InitializePlayers()
    {
        players.Clear();
        originalTransforms.Clear();

        if (playerObjects.Count == 0)
        {
            if (playerPrefab != null && playerPosition != null)
            {
                GameObject player = Instantiate(playerPrefab, playerPosition.position, playerPosition.rotation);
                player.name = "Human Player";
                playerObjects.Add(player);

                Rigidbody rb = player.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                }
            }

            if (npcPrefab != null && npcPosition != null)
            {
                GameObject npc = Instantiate(npcPrefab, npcPosition.position, npcPosition.rotation);
                npc.name = "NPC Opponent";
                playerObjects.Add(npc);

                Rigidbody rb = npc.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                }
            }
        }
        else
        {
            for (int i = 0; i < playerObjects.Count; i++)
            {
                if (playerObjects[i] != null)
                {
                    if (i == 0 && playerPosition != null)
                    {
                        playerObjects[i].transform.position = playerPosition.position;
                        playerObjects[i].transform.rotation = playerPosition.rotation;
                    }
                    else if (i == 1 && npcPosition != null)
                    {
                        playerObjects[i].transform.position = npcPosition.position;
                        playerObjects[i].transform.rotation = npcPosition.rotation;
                    }
                }
            }
        }

        foreach (var playerObj in playerObjects)
        {
            if (playerObj != null)
            {
                IAIPlayer npcPlayer = playerObj.GetComponent<IAIPlayer>();
                if (npcPlayer != null)
                {
                    players.Add(npcPlayer as IPlayer);
                    StoreOriginalTransform(npcPlayer as IPlayer, playerObj.transform);
                    continue;
                }

                IPlayer humanPlayerComponent = playerObj.GetComponent<IPlayer>();
                if (humanPlayerComponent != null)
                {
                    players.Add(humanPlayerComponent);
                    humanPlayer = humanPlayerComponent;
                    StoreOriginalTransform(humanPlayerComponent, playerObj.transform);
                    continue;
                }

                if (playerObj.CompareTag("Player"))
                {
                    var playerComponents = playerObj.GetComponents<MonoBehaviour>().Where(mb => mb is IPlayer);
                    if (playerComponents.Any())
                    {
                        IPlayer foundPlayer = playerComponents.First() as IPlayer;
                        players.Add(foundPlayer);
                        if (foundPlayer is not IAIPlayer)
                        {
                            humanPlayer = foundPlayer;
                        }
                        StoreOriginalTransform(foundPlayer, playerObj.transform);
                    }
                }
            }
        }

        if (players.Count < 2)
        {
            Debug.LogWarning($"Only {players.Count} players found. Need at least 2 players.");
            return;
        }
    }

    void StoreOriginalTransform(IPlayer player, Transform playerTransform)
    {
        Rigidbody rb = playerTransform.GetComponent<Rigidbody>();
        bool hasRigidbody = rb != null;
        bool wasKinematic = hasRigidbody ? rb.isKinematic : true;
        bool useGravity = hasRigidbody ? rb.useGravity : false;
        RigidbodyConstraints originalConstraints = hasRigidbody ? rb.constraints : RigidbodyConstraints.FreezeAll;

        Collider[] colliders = playerTransform.GetComponentsInChildren<Collider>();
        bool[] colliderStates = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
        {
            colliderStates[i] = colliders[i].enabled;
        }

        originalTransforms[player] = (playerTransform.position, playerTransform.rotation, playerTransform.localScale, wasKinematic, useGravity, hasRigidbody, colliderStates, originalConstraints);
    }

    void PositionRevolver(Transform targetPoint)
    {
        if (revolver != null && targetPoint != null)
        {
            Vector3 targetPosition = targetPoint.position + Vector3.up * revolverFloatHeight;
            revolver.transform.position = targetPosition;

            Vector3 r = targetPoint.rotation.eulerAngles;
            revolver.transform.rotation = Quaternion.Euler(91f, r.y, r.z);

            LockRevolverAtPosition();
        }
    }

    private void LockRevolverAtPosition()
    {
        if (revolverRigidbody == null || !lockRevolverAtDestination)
            return;

        revolverRigidbody.isKinematic = false;
        revolverRigidbody.linearVelocity = Vector3.zero;
        revolverRigidbody.angularVelocity = Vector3.zero;

        revolverRigidbody.isKinematic = true;
        revolverRigidbody.useGravity = false;
    }

    private void EnableRevolverPhysics()
    {
        if (revolverRigidbody != null)
        {
            revolverRigidbody.isKinematic = false;
            revolverRigidbody.useGravity = enableGravityOnBlankShots;
            revolverRigidbody.constraints = RigidbodyConstraints.None;
            revolverRigidbody.linearVelocity = Vector3.zero;
            revolverRigidbody.angularVelocity = Vector3.zero;
        }

        if (revolverGrabInteractable != null)
        {
            revolverGrabInteractable.enabled = true;
        }
    }

    private void DisableRevolverPhysics()
    {
        if (revolverRigidbody != null)
        {
            revolverRigidbody.isKinematic = false;
            revolverRigidbody.linearVelocity = Vector3.zero;
            revolverRigidbody.angularVelocity = Vector3.zero;

            revolverRigidbody.isKinematic = true;
            revolverRigidbody.useGravity = false;
        }
    }

    private void SetRevolverGrabbable(bool grabbable)
    {
        if (revolverGrabInteractable != null)
        {
            revolverGrabInteractable.enabled = grabbable;

            if (!grabbable && revolverGrabInteractable.isSelected)
            {
                var interactors = revolverGrabInteractable.interactorsSelecting.ToArray();
                foreach (var interactor in interactors)
                {
                    revolverGrabInteractable.interactionManager.SelectExit(interactor, revolverGrabInteractable);
                }

                DisableRevolverPhysics();
            }

            if (grabbable)
            {
                EnableRevolverPhysics();
            }
        }
    }

    public void StartGame()
    {
        if (players.Count < 2)
        {
            Debug.LogWarning("Not enough players to start game!");
            return;
        }

        gameActive = true;
        currentTurn = 0;

        currentPlayerIndex = Random.Range(0, players.Count);
        winner = null;

        PositionRevolver(tableCenterPoint);


        if (revolver != null)
        {
            revolver.Reload(revolver.GenerateBulletPositions());
            revolver.Spin();
        }

        if (uiDisplay != null)
        {
            uiDisplay.UpdateGameState("Game Started - Good Luck!");
            uiDisplay.UpdateBulletCount(revolver.BulletPositions.Count, revolver.MaxChambers);
            uiDisplay.ShowSpinAnimation();
        }

        StartCoroutine(StartGameSequence());
    }

    IEnumerator StartGameSequence()
    {
        yield return new WaitForSeconds(1f);

        if (uiDisplay != null)
        {
            uiDisplay.UpdateGameState($"Starting with {players[currentPlayerIndex].PlayerName}");
        }

        yield return StartCoroutine(PassRevolverToPlayer(currentPlayerIndex));
        StartTurn();
    }

    IEnumerator PassRevolverToPlayer(int playerIndex)
    {
        if (!animateRevolverPass || revolver == null) yield break;

        isPassingRevolver = true;

        IPlayer targetPlayer = players[playerIndex];
        Transform targetPoint = targetPlayer is IAIPlayer ? tableEdgeNPCSide : tableEdgePlayerSide;

        if (targetPoint == null)
        {
            isPassingRevolver = false;
            yield break;
        }

        DisableRevolverPhysics();

        Vector3 startPos = revolver.transform.position;
        Vector3 endPos = targetPoint.position + Vector3.up * revolverFloatHeight;
        Quaternion startRot = revolver.transform.rotation;
        Quaternion endRot = Quaternion.Euler(91f, targetPoint.rotation.eulerAngles.y, targetPoint.rotation.eulerAngles.z);

        float elapsed = 0f;
        while (elapsed < revolverPassTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / revolverPassTime);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            revolver.transform.position = Vector3.Lerp(startPos, endPos, smoothT);
            revolver.transform.rotation = Quaternion.Slerp(startRot, endRot, smoothT);

            yield return null;
        }

        revolver.transform.position = endPos;
        revolver.transform.rotation = endRot;

        DisableRevolverPhysics();

        isPassingRevolver = false;
    }

    public void StartTurn()
    {
        if (!gameActive) return;

        currentTurnTime = 0f;
        IPlayer currentPlayer = players[currentPlayerIndex];

        if (revolver != null)
        {

            var revolverMono = revolver as MonoBehaviour;
            if (revolverMono != null)
            {
                var resetMethod = revolverMono.GetType().GetMethod("ResetShotTracking");
                resetMethod?.Invoke(revolverMono, null);
            }
        }

        if (uiDisplay != null)
        {
            uiDisplay.UpdateTurnIndicator(currentPlayer);
            uiDisplay.UpdateTurnTimer(turnDuration);
            uiDisplay.UpdateGameState($"{currentPlayer.PlayerName}'s Turn - Take Aim!");
            uiDisplay.UpdateBulletCount(revolver.BulletPositions.Count, revolver.MaxChambers);
            uiDisplay.UpdateChamberInfo(revolver.CurrentChamber, revolver.MaxChambers);

            foreach (var player in players)
            {
                uiDisplay.UpdatePlayerStatus(player, player.IsAlive);
            }
        }

        bool isPlayerTurn = !(currentPlayer is IAIPlayer);
        SetRevolverGrabbable(isPlayerTurn);

        if (currentPlayer is IAIPlayer aiPlayer)
        {
            StartCoroutine(HandleAITurn(aiPlayer));
        }
        else
        {
            waitingForPlayerAction = true;
            currentPlayer.TakeTurn(); 

        }
    }

    IEnumerator HandleAITurn(IAIPlayer aiPlayer)
    {
        if (uiDisplay != null)
        {
            uiDisplay.UpdateGameState($"{aiPlayer.PlayerName} is thinking...");
        }

        yield return new WaitForSeconds(aiThinkTime);

        if (!gameActive) yield break;

        int chambersLeft = revolver.MaxChambers - revolver.CurrentChamber;

        Target decision = aiPlayer.DecideTarget(chambersLeft);

        if (uiDisplay != null)
        {
            uiDisplay.UpdateGameState($"{aiPlayer.PlayerName} aims at {decision}");
            uiDisplay.ShowEffect(UIEffect.DangerWarning);
        }

        yield return new WaitForSeconds(0.5f);

        IPlayer target = null;
        if (decision == Target.Self)
        {
            target = aiPlayer as IPlayer;
        }
        else
        {
            target = players.FirstOrDefault(p => p != aiPlayer && p.IsAlive);
        }

        if (target != null)
        {
            if (isProcessingShot) yield break;
            isProcessingShot = true;

            FireResult result = revolver.Fire();

            if (result == FireResult.Bullet)
            {
                DealDamageToTarget(target, aiPlayer as IPlayer);
            }
            else
            {
                ProcessShotResult(aiPlayer as IPlayer, target, result);
            }
        }
        else
        {
            EndTurn();
        }
    }

    private void ForcePlayerShot()
    {
        if (!waitingForPlayerAction) return;

        waitingForPlayerAction = false;
        IPlayer currentPlayer = players[currentPlayerIndex];

        if (uiDisplay != null)
        {
            uiDisplay.ShowWarning("Time's up! Shooting randomly...");
        }

        var alivePlayers = players.Where(p => p.IsAlive).ToList();
        if (alivePlayers.Count > 0)
        {
            IPlayer randomTarget = alivePlayers[Random.Range(0, alivePlayers.Count)];

            if (isProcessingShot) return;
            isProcessingShot = true;

            FireResult result = revolver.Fire();

            if (result == FireResult.Bullet)
            {
                DealDamageToTarget(randomTarget, currentPlayer);
            }
            else
            {
                ProcessShotResult(currentPlayer, randomTarget, result);
            }
        }
        else
        {
            EndTurn();
        }
    }

    public void OnRevolverFired(IPlayer target)
    {
        if (!waitingForPlayerAction || isProcessingShot) return;

        waitingForPlayerAction = false;
        isProcessingShot = true;
        IPlayer currentPlayer = players[currentPlayerIndex];

        FireResult result = revolver.Fire(); 
        ProcessShotResult(currentPlayer, target, result);
    }

    private void ProcessShotResult(IPlayer shooter, IPlayer target, FireResult result)
    {
        if (uiDisplay != null)
        {
            uiDisplay.ShowResult(result);
            uiDisplay.UpdateGameState($"{shooter.PlayerName} shot {target.PlayerName} - {result}");
            uiDisplay.UpdateBulletCount(revolver.BulletPositions.Count, revolver.MaxChambers);
            uiDisplay.UpdateChamberInfo(revolver.CurrentChamber, revolver.MaxChambers);

            if (result == FireResult.Bullet)
            {
                uiDisplay.ShowEffect(UIEffect.PlayerEliminated);
            }
            else
            {
                uiDisplay.ShowEffect(UIEffect.SafeShot);
            }
        }

        if (result == FireResult.Bullet)
        {
            DealDamageToTarget(target, shooter);
        }
        else
        {
            bool shotSelf = shooter == target;
            int chambersLeft = revolver.MaxChambers - revolver.CurrentChamber;

            foreach (var player in players.OfType<IAIPlayer>().ToArray())
            {
                if (player != shooter)
                {
                    Target choice = shotSelf ? Target.Self : Target.Opponent;
                    player.ObservePlayerAction(choice, chambersLeft, shotSelf);
                }
            }

            bool getsAnotherTurn = shotSelf && result == FireResult.Blank;

            if (getsAnotherTurn)
            {
                if (uiDisplay != null)
                {
                    uiDisplay.ShowWarning($"{shooter.PlayerName} gets another turn!");
                }

                StartCoroutine(ReturnRevolverForSameTurn());
            }
            else
            {
                StartCoroutine(ReturnRevolverAndEndTurn());
            }
        }
    }

    private void DealDamageToTarget(IPlayer target, IPlayer shooter)
    {
        StartCoroutine(HitSequence(target, shooter));
    }

    IEnumerator HitSequence(IPlayer target, IPlayer shooter)
    {
        isResettingScene = true;

        MakeAllPlayersImmovable();

        bool ragdollSuccess = TriggerRagdoll(target, shooter);

        yield return new WaitForSeconds(ragdollDuration);

        yield return StartCoroutine(FadeToBlack());

        yield return StartCoroutine(ResetCharacterPositions());

        yield return new WaitForFixedUpdate();
        yield return new WaitForSeconds(0.1f);

        int damage = 1;
        target.TakeDamage(damage);

        if (uiDisplay != null)
        {
            uiDisplay.UpdatePlayerStatus(target, target.IsAlive);
        }

        if (IsGameOver())
        {
            EndGame();
            yield break;
        }

        int chambersLeft = revolver.MaxChambers - revolver.CurrentChamber;
        foreach (var player in players.OfType<IAIPlayer>().ToArray())
        {
            if (player != target && player != shooter)
            {
                player.ObservePlayerAction(Target.Opponent, chambersLeft, false);
            }
        }

        yield return StartCoroutine(FadeFromBlack());

        isResettingScene = false;

        StartCoroutine(ReturnRevolverAndEndTurn());
    }

    void MakeAllPlayersImmovable()
    {
        foreach (var player in players)
        {
            MonoBehaviour playerMono = player as MonoBehaviour;
            if (playerMono != null)
            {
                GameObject playerObj = playerMono.gameObject;
                Rigidbody rb = playerObj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                    rb.isKinematic = true;
                    rb.useGravity = false;
                    rb.constraints = RigidbodyConstraints.FreezeAll;
                }

                Rigidbody[] childRigidbodies = playerObj.GetComponentsInChildren<Rigidbody>();
                foreach (Rigidbody childRb in childRigidbodies)
                {
                    if (childRb != rb)
                    {
                        childRb.linearVelocity = Vector3.zero;
                        childRb.angularVelocity = Vector3.zero;
                        childRb.isKinematic = true;
                        childRb.useGravity = false;
                        childRb.constraints = RigidbodyConstraints.FreezeAll;
                    }
                }
            }
        }
    }

    bool TriggerRagdoll(IPlayer target, IPlayer shooter)
    {
        MonoBehaviour targetMono = target as MonoBehaviour;

        if (targetMono != null)
        {
            GameObject targetObj = targetMono.gameObject;

            Rigidbody rb = targetObj.GetComponent<Rigidbody>();
            if (rb == null)
            {
                rb = targetObj.AddComponent<Rigidbody>();
            }

            rb.constraints = RigidbodyConstraints.None;

            Collider collider = targetObj.GetComponent<Collider>();
            if (collider == null)
            {
                CapsuleCollider capsule = targetObj.AddComponent<CapsuleCollider>();
                capsule.height = 2f;
                capsule.radius = 0.3f;
                capsule.center = new Vector3(0, 1f, 0);
            }

            rb.isKinematic = false;
            rb.useGravity = true;

            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;

            Vector3 shotDirection = (targetObj.transform.position - GetShooterPosition(shooter)).normalized;
            if (shotDirection == Vector3.zero)
            {
                shotDirection = -targetObj.transform.forward;
            }

            Vector3 shotForce = shotDirection * ragdollForce + Vector3.up * ragdollUpwardForce;
            rb.AddForce(shotForce, ForceMode.Impulse);

            rb.AddTorque(new Vector3(
                Random.Range(-2f, 2f),
                Random.Range(-1f, 1f),
                Random.Range(-2f, 2f)
            ), ForceMode.Impulse);

            Animator animator = targetObj.GetComponent<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
            }
            return true;
        }

        return false;
    }

    Vector3 GetShooterPosition(IPlayer shooter)
    {
        MonoBehaviour shooterMono = shooter as MonoBehaviour;
        if (shooterMono != null)
        {
            return shooterMono.transform.position;
        }
        return Vector3.zero;
    }

    IEnumerator ResetCharacterPositions()
    {
        foreach (var player in players)
        {
            MonoBehaviour playerMono = player as MonoBehaviour;
            if (playerMono != null && originalTransforms.ContainsKey(player))
            {
                GameObject playerObj = playerMono.gameObject;
                var originalTransform = originalTransforms[player];

                if (disableCollidersDuringReset)
                {
                    Collider[] allColliders = playerObj.GetComponentsInChildren<Collider>();
                    foreach (Collider collider in allColliders)
                    {
                        collider.enabled = false;
                    }
                }

                Rigidbody[] allRigidbodies = playerObj.GetComponentsInChildren<Rigidbody>();

                foreach (Rigidbody rb in allRigidbodies)
                {
                    if (rb != null)
                    {
                        if (!rb.isKinematic)
                        {
                            rb.linearVelocity = Vector3.zero;
                            rb.angularVelocity = Vector3.zero;
                        }

                        rb.ResetCenterOfMass();
                        rb.ResetInertiaTensor();

                        rb.isKinematic = true;

                        if (rb.gameObject == playerObj)
                        {
                            playerObj.transform.position = originalTransform.position;
                            playerObj.transform.rotation = originalTransform.rotation;
                            playerObj.transform.localScale = originalTransform.scale;
                        }

                        rb.isKinematic = originalTransform.wasKinematic;
                        rb.useGravity = originalTransform.useGravity;
                        rb.constraints = originalTransform.originalConstraints;
                    }
                }

                if (allRigidbodies.Length == 0)
                {
                    playerObj.transform.position = originalTransform.position;
                    playerObj.transform.rotation = originalTransform.rotation;
                    playerObj.transform.localScale = originalTransform.scale;
                }

                Animator animator = playerObj.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.enabled = true;
                    animator.Rebind();
                    animator.Update(0f);
                }
            }
        }

        yield return new WaitForFixedUpdate();
        yield return new WaitForSeconds(0.05f);

        if (disableCollidersDuringReset)
        {
            yield return new WaitForSeconds(colliderReenableDelay);

            foreach (var player in players)
            {
                MonoBehaviour playerMono = player as MonoBehaviour;
                if (playerMono != null && originalTransforms.ContainsKey(player))
                {
                    GameObject playerObj = playerMono.gameObject;
                    var originalTransform = originalTransforms[player];

                    Collider[] allColliders = playerObj.GetComponentsInChildren<Collider>();
                    bool[] originalColliderStates = originalTransform.Item7;

                    for (int i = 0; i < Mathf.Min(allColliders.Length, originalColliderStates.Length); i++)
                    {
                        allColliders[i].enabled = originalColliderStates[i];
                    }
                }
            }
        }
    }

    IEnumerator FadeToBlack()
    {
        if (screenFadeCanvas == null) yield break;

        float elapsed = 0f;
        while (elapsed < 1f)
        {
            elapsed += Time.deltaTime * fadeSpeed;
            screenFadeCanvas.alpha = Mathf.Clamp01(elapsed);
            yield return null;
        }
        screenFadeCanvas.alpha = 1f;

        yield return new WaitForSeconds(screenBlackDuration);
    }

    IEnumerator FadeFromBlack()
    {
        if (screenFadeCanvas == null) yield break;

        float elapsed = 0f;
        while (elapsed < 1f)
        {
            elapsed += Time.deltaTime * fadeSpeed;
            screenFadeCanvas.alpha = Mathf.Clamp01(1f - elapsed);
            yield return null;
        }
        screenFadeCanvas.alpha = 0f;
    }

    IEnumerator ReturnRevolverForSameTurn()
    {
        yield return StartCoroutine(ReturnRevolverSequence());
        isProcessingShot = false;

        yield return StartCoroutine(PassRevolverToPlayer(currentPlayerIndex));
        StartTurn();
    }

    IEnumerator ReturnRevolverAndEndTurn()
    {
        yield return StartCoroutine(ReturnRevolverSequence());
        isProcessingShot = false;
        EndTurn();
    }

    IEnumerator ReturnRevolverSequence()
    {
        EnableRevolverPhysics();

        yield return new WaitForSeconds(0.1f);

        if (revolverGrabInteractable != null && revolverGrabInteractable.isSelected)
        {
            var interactors = revolverGrabInteractable.interactorsSelecting.ToArray();
            foreach (var interactor in interactors)
            {
                revolverGrabInteractable.interactionManager.SelectExit(interactor, revolverGrabInteractable);
            }
        }

        yield return new WaitForSeconds(0.2f);

        DisableRevolverPhysics();

        yield return StartCoroutine(PassRevolverToPoint(tableCenterPoint));
        yield return new WaitForSeconds(0.5f);
    }

    public void EndTurn()
    {
        if (!gameActive) return;

        currentTurn++;

        int startIndex = currentPlayerIndex;
        do
        {
            currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;

            if (currentPlayerIndex == startIndex) break;
        }
        while (!players[currentPlayerIndex].IsAlive && GetActivePlayers() > 1);

        if (revolver.BulletPositions.Count == 0 || revolver.CurrentChamber >= revolver.MaxChambers)
        {
            if (uiDisplay != null)
            {
                uiDisplay.UpdateGameState("Reloading revolver...");
                uiDisplay.ShowReloadAnimation();
            }
            StartCoroutine(ReloadSequence());
        }
        else
        {
            StartCoroutine(PassAndStartNextTurn());
        }
    }

    IEnumerator ReloadSequence()
    {
        yield return StartCoroutine(PassRevolverToPoint(tableCenterPoint));
        yield return new WaitForSeconds(0.5f);

        revolver.Reload(revolver.GenerateBulletPositions());
        revolver.Spin();
        Debug.Log("Revolver reloaded and spun!");

        if (uiDisplay != null)
        {
            uiDisplay.UpdateBulletCount(revolver.BulletPositions.Count, revolver.MaxChambers);
            uiDisplay.UpdateGameState("Revolver reloaded and spun!");
            uiDisplay.ShowEffect(UIEffect.BulletLoaded);
        }

        yield return new WaitForSeconds(1f);
        yield return StartCoroutine(PassRevolverToPlayer(currentPlayerIndex));
        StartTurn();
    }

    IEnumerator PassAndStartNextTurn()
    {
        yield return StartCoroutine(PassRevolverToPlayer(currentPlayerIndex));
        StartTurn();
    }

    IEnumerator PassRevolverToPoint(Transform point)
    {
        if (!animateRevolverPass || revolver == null || point == null)
        {
            yield break;
        }

        isPassingRevolver = true;

        DisableRevolverPhysics();

        Vector3 startPos = revolver.transform.position;
        Vector3 endPos = point.position + Vector3.up * revolverFloatHeight;
        Quaternion startRot = revolver.transform.rotation;
        Quaternion endRot = point.rotation;

        float elapsed = 0f;
        float duration = revolverPassTime * 0.7f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float smoothT = Mathf.SmoothStep(0f, 1f, t);

            revolver.transform.position = Vector3.Lerp(startPos, endPos, smoothT);
            revolver.transform.rotation = Quaternion.Slerp(startRot, endRot, smoothT);

            yield return null;
        }

        revolver.transform.position = endPos;
        revolver.transform.rotation = endRot;

        DisableRevolverPhysics();
        isPassingRevolver = false;
    }


    public bool CheckWinCondition()
    {
        return GetActivePlayers() <= 1;
    }

    public bool IsGameOver()
    {
        return CheckWinCondition();
    }

    public IPlayer GetWinner()
    {
        if (!IsGameOver()) return null;
        return players.FirstOrDefault(p => p.IsAlive);
    }

    public int GetActivePlayers()
    {
        return players.Count(p => p.IsAlive);
    }

    private void EndGame()
    {
        gameActive = false;
        winner = GetWinner();

        SetRevolverGrabbable(false);

        if (winner != null)
        {
            if (uiDisplay != null)
            {
                uiDisplay.DisplayWinner(winner);
                uiDisplay.UpdateGameState($"Game Over - {winner.PlayerName} Wins!");
            }
        }
        else
        {
            if (uiDisplay != null)
            {
                uiDisplay.UpdateGameState("Game Over - Draw!");
            }
        }

        StartCoroutine(PassRevolverToPoint(tableCenterPoint));
    }

    public void NextTurn()
    {
        EndTurn();
    }

    public void EliminatePlayer(IPlayer player)
    {
        player.Eliminate();

        if (uiDisplay != null)
        {
            uiDisplay.UpdatePlayerStatus(player, false);
        }

        if (IsGameOver())
        {
            EndGame();
        }
    }

    public void ResetRound()
    {
        RestartGame();
    }

    public void RestartGame()
    {
        foreach (var playerObj in playerObjects)
        {
            var player = playerObj.GetComponent<IPlayer>();
            if (player != null)
            {
                var resetMethod = playerObj.GetType().GetMethod("Reset");
                resetMethod?.Invoke(playerObj, null);
            }
        }

        InitializePlayers();
        StartGame();
    }

    public IPlayer GetCurrentPlayer()
    {
        return players.Count > 0 ? players[currentPlayerIndex] : null;
    }

    public List<IPlayer> GetAllPlayers()
    {
        return new List<IPlayer>(players);
    }

    public bool IsPlayerTurn()
    {
        return waitingForPlayerAction;
    }

    public Transform GetPlayerPosition() => playerPosition;
    public Transform GetNPCPosition() => npcPosition;
    public Transform GetTableCenter() => tableCenterPoint;
}