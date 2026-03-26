using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using Doody.GameEvents;
using System;

public class GameManager : EventListener, ITurnBased, IGameRules, IGameManager, IRagdoll
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
    [SerializeField] private float screenBlackDuration = 1f;

    [Header("Visual Settings")]
    [SerializeField] private bool animateRevolverPass = true;
    [SerializeField] private float revolverFloatHeight = 0.3f;

    [Header("Screen Fade")]
    [SerializeField] private CanvasGroup screenFadeCanvas;
    [SerializeField] private float fadeSpeed = 2f;

    [Header("Collider Settings")]
    [SerializeField] private bool disableCollidersDuringReset = true;
    [SerializeField] private float colliderReenableDelay = 0.1f;

    [Header("Revolver Physics")]
    [SerializeField] private bool lockRevolverAtDestination = true;
    [SerializeField] private bool enableGravityOnBlankShots = false;

    [Header("Ragdoll Settings")]
    [SerializeField] private float ragdollDuration = 2f;
    [SerializeField] private float ragdollForce = 10f;
    [SerializeField] private float ragdollUpwardForce = 3f;

    // Core game state
    private int currentTurn = 0;
    private float currentTurnTime = 0f;
    private int currentPlayerIndex = 0;
    private bool gameActive = false;
    private bool waitingForPlayerAction = false;
    private IPlayer winner = null;
    private bool isPassingRevolver = false;
    private bool isResettingScene = false;
    private bool isProcessingShot = false;
    private GameState currentState = GameState.WaitingForStart;

    // Revolver physics control
    private XRGrabInteractable revolverGrabInteractable;
    private Rigidbody revolverRigidbody;
    private bool originalRevolverKinematic;
    private bool originalRevolverGravity;

    // Player state storage for resetting positions
    private Dictionary<IPlayer, (Vector3 position, Quaternion rotation, Vector3 scale, bool wasKinematic, bool useGravity, bool hasRigidbody, bool[] colliderStates, RigidbodyConstraints originalConstraints)> originalTransforms = new Dictionary<IPlayer, (Vector3, Quaternion, Vector3, bool, bool, bool, bool[], RigidbodyConstraints)>();

    // Public interface properties
    public float RagdollForce => ragdollForce;
    public float RagdollDuration => ragdollDuration;
    public float RagdollUpwardForce => ragdollUpwardForce;
    public int CurrentTurn => currentTurn;
    public float TimeSpan => currentTurnTime;
    public int currentIDsTurn => players.Count > 0 ? players[currentPlayerIndex].ID : -1;
    public IReadOnlyList<IPlayer> Players => players.AsReadOnly();
    public IRevolverMechanic Revolver => revolver as IRevolverMechanic;
    public ITurnBased TurnSystem => this;
    public IGameRules Rules => this;

    void Start()
    {
        ValidatePositions();
        InitializePlayers();

        // Setup revolver physics components
        if (revolver != null)
        {
            revolverGrabInteractable = revolver.GetComponent<XRGrabInteractable>();
            revolverRigidbody = revolver.GetComponent<Rigidbody>();

            if (revolverRigidbody != null)
            {
                originalRevolverKinematic = revolverRigidbody.isKinematic;
                originalRevolverGravity = revolverRigidbody.useGravity;
            }
        }

        // Setup UI display interface
        if (uiDisplay == null)
        {
            uiDisplay = uiDisplayScript as IUIDisplay;
        }

        // Subscribe to game events
        SubscribeToEvents();

        // Initialize screen fade
        if (screenFadeCanvas != null)
        {
            screenFadeCanvas.alpha = 0f;
            screenFadeCanvas.blocksRaycasts = false;
        }

        PositionRevolver(tableCenterPoint);
        StartGame();
    }

    private void SubscribeToEvents()
    {
        Listen<RevolverFiredEvent>(OnRevolverFiredEvent);
        Listen<RevolverReloadedEvent>(OnRevolverReloaded);
        Listen<RevolverSpunEvent>(OnRevolverSpun);
        Listen<PlayerEliminatedEvent>(OnPlayerEliminated);
        Listen<PlayerDamagedEvent>(OnPlayerDamaged);
        Listen<GameErrorEvent>(OnGameError);
        Listen<GameResetEvent>(OnGameReset);
    }

    private void ChangeState(GameState newState)
    {
        GameState previousState = currentState;
        currentState = newState;

        Events.Publish(new GameStateChangedEvent { NewState = newState, PreviousState = previousState });

        Debug.Log($"Game state changed: {previousState} -> {newState}");
    }

    void Update()
    {
        if (currentState != GameState.TurnInProgress) return;

        currentTurnTime += Time.deltaTime;

        // Handle player turn timeout
        if (uiDisplay != null && waitingForPlayerAction)
        {
            float timeLeft = Mathf.Max(0f, turnDuration - currentTurnTime);
            uiDisplay.UpdateTurnTimer(timeLeft);

            if (timeLeft < 5f && timeLeft > 0f)
            {
                uiDisplay.ShowWarning($"Time running out: {timeLeft:F1}s");
            }
        }

        // Force player action if timeout reached
        if (currentTurnTime >= turnDuration && waitingForPlayerAction)
        {
            ForcePlayerShot();
        }
    }

    /// <summary>Validates all required scene positions are assigned</summary>
    void ValidatePositions()
    {
        if (playerPosition == null) Debug.LogError("Player Position not assigned!");
        if (npcPosition == null) Debug.LogError("NPC Position not assigned!");
        if (tableCenterPoint == null) Debug.LogError("Table Center Point not assigned!");
        if (tableEdgePlayerSide == null) Debug.LogError("Table Edge (Player Side) not assigned!");
        if (tableEdgeNPCSide == null) Debug.LogError("Table Edge (NPC Side) not assigned!");
    }

    /// <summary>Initializes players and stores their original transforms for resetting</summary>
    void InitializePlayers()
    {
        players.Clear();
        originalTransforms.Clear();

        // Create players if none assigned
        if (playerObjects.Count == 0)
        {

            GameObject playerObject = GameObject.Find("Player");
            GameObject npcObject = GameObject.Find("NPC");

            if (playerObject != null)
                playerObjects.Add(playerObject);

            if (npcObject != null)
                playerObjects.Add(npcObject);
            if (playerPrefab != null && playerPosition != null && playerObject == null)
            {

                GameObject player = Instantiate(playerPrefab, playerPosition.position, playerPosition.rotation);
                player.name = "Human Player";
                playerObjects.Add(player);
            }

            if (npcPrefab != null && npcPosition != null && npcObject == null)
            {
                GameObject npc = Instantiate(npcPrefab, npcPosition.position, npcPosition.rotation);
                npc.name = "NPC Opponent";
                playerObjects.Add(npc);
            }
        }

        // Position existing players
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

        // Register players and store original transforms
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
                }
            }
        }

        if (players.Count < 2)
        {
            Debug.LogWarning($"Only {players.Count} players found. Need at least 2 players.");
        }
    }

    /// <summary>Stores player's original state for position resetting</summary>
    void StoreOriginalTransform(IPlayer player, Transform playerTransform)
    {
        Rigidbody rb = playerTransform.GetComponent<Rigidbody>();
        bool hasRigidbody = rb != null;
        bool wasKinematic = hasRigidbody ? rb.isKinematic : true;
        bool useGravity = hasRigidbody ? rb.useGravity : false;
        RigidbodyConstraints originalConstraints = hasRigidbody ? rb.constraints : RigidbodyConstraints.FreezeAll;

        // Store collider states for re-enabling later
        Collider[] colliders = playerTransform.GetComponentsInChildren<Collider>();
        bool[] colliderStates = new bool[colliders.Length];
        for (int i = 0; i < colliders.Length; i++)
        {
            colliderStates[i] = colliders[i].enabled;
        }

        originalTransforms[player] = (playerTransform.position, playerTransform.rotation, playerTransform.localScale, wasKinematic, useGravity, hasRigidbody, colliderStates, originalConstraints);
    }

    /// <summary>Positions revolver at specified point with proper orientation</summary>
    void PositionRevolver(Transform targetPoint)
    {
        if (revolver != null && targetPoint != null)
        {
            Vector3 targetPosition = targetPoint.position + Vector3.up * revolverFloatHeight;
            revolver.transform.position = targetPosition;
            revolver.transform.rotation = Quaternion.Euler(91f, targetPoint.rotation.eulerAngles.y, targetPoint.rotation.eulerAngles.z);
            LockRevolverAtPosition();
        }
    }

    /// <summary>Locks revolver in place at current position</summary>
    private void LockRevolverAtPosition()
    {
        if (revolverRigidbody == null || !lockRevolverAtDestination) return;

        revolverRigidbody.isKinematic = false;
        revolverRigidbody.linearVelocity = Vector3.zero;
        revolverRigidbody.angularVelocity = Vector3.zero;
        revolverRigidbody.isKinematic = true;
        revolverRigidbody.useGravity = false;
    }

    /// <summary>Enables physics on revolver for interaction</summary>
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

    /// <summary>Disables physics and locks revolver</summary>
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

    /// <summary>Controls whether revolver can be grabbed by player</summary>
    private void SetRevolverGrabbable(bool grabbable)
    {
        if (revolverGrabInteractable != null)
        {
            // Can only grab if it's player's turn AND not passing the revolver
            bool canGrab = grabbable && !isPassingRevolver;
            revolverGrabInteractable.enabled = canGrab;

            // Force release if currently grabbed
            if (!canGrab && revolverGrabInteractable.isSelected)
            {
                var interactors = revolverGrabInteractable.interactorsSelecting.ToArray();
                foreach (var interactor in interactors)
                {
                    revolverGrabInteractable.interactionManager.SelectExit(interactor, revolverGrabInteractable);
                }
                DisableRevolverPhysics();
            }

            if (canGrab)
            {
                EnableRevolverPhysics();
            }
        }
    }

    /// <summary>Starts the main game sequence</summary>
    public void StartGame()
    {
        if (players.Count < 2) return;

        gameActive = true;
        currentTurn = 0;
        currentPlayerIndex = UnityEngine.Random.Range(0, players.Count);
        winner = null;
        ChangeState(GameState.Starting);

        PositionRevolver(tableCenterPoint);

        // Initialize revolver
        if (revolver != null)
        {
            revolver.Reload(revolver.GenerateBulletPositions());
            revolver.Spin();
        }

        // UI setup
        if (uiDisplay != null)
        {
            uiDisplay.UpdateGameState("Game Started - Good Luck!");
            uiDisplay.UpdateBulletCount(revolver.BulletPositions.Count, revolver.MaxChambers);
            uiDisplay.ShowSpinAnimation();
        }

        StartCoroutine(StartGameSequence());
    }

    /// <summary>Initial game sequence with delays for dramatic effect</summary>
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

    /// <summary>Animates revolver passing to specific player</summary>
    IEnumerator PassRevolverToPlayer(int playerIndex)
    {
        if (!animateRevolverPass || revolver == null) yield break;

        isPassingRevolver = true;
        SetRevolverGrabbable(false); // Ensure revolver can't be grabbed during pass
        IPlayer targetPlayer = players[playerIndex];
        Transform targetPoint = targetPlayer is IAIPlayer ? tableEdgeNPCSide : tableEdgePlayerSide;

        if (targetPoint == null)
        {
            isPassingRevolver = false;
            yield break;
        }

        DisableRevolverPhysics();

        // Animate revolver movement
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

    /// <summary>Starts a new turn for current player</summary>
    public void StartTurn()
    {
        if (!gameActive) return;

        currentTurnTime = 0f;
        IPlayer currentPlayer = players[currentPlayerIndex];
        ChangeState(GameState.TurnInProgress);

        Events.Publish(new TurnStartedEvent { CurrentPlayer = currentPlayer });

        // UI updates
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

        // Handle AI vs Player turns
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

    /// <summary>Handles AI decision making and shooting</summary>
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

        // Wait for any revolver passing to complete
        while (isPassingRevolver) yield return null;

        // Allow AI to rotate revolver for aiming
        AllowRevolverRotation(true);
        aiPlayer.TakeTurn();

        // Wait for aiming animation
        yield return new WaitForSeconds(0.5f);

        IPlayer target = decision == Target.Self ? aiPlayer as IPlayer : players.FirstOrDefault(p => p != aiPlayer && p.IsAlive);

        if (target != null)
        {
            if (isProcessingShot) yield break;
            isProcessingShot = true;

            FireResult result = revolver.Fire();
            AllowRevolverRotation(false);

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
            AllowRevolverRotation(false);
            EndTurn();
        }
    }

    /// <summary>Forces player to shoot when time runs out</summary>
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
            IPlayer randomTarget = alivePlayers[UnityEngine.Random.Range(0, alivePlayers.Count)];

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

    /// <summary>Called when player fires the revolver</summary>
    private void OnRevolverFiredEvent(RevolverFiredEvent evt)
    {
        try
        {
            if (currentState != GameState.TurnInProgress || isProcessingShot) return;

            waitingForPlayerAction = false;
            isProcessingShot = true;
            ChangeState(GameState.ProcessingShot);
            IPlayer currentPlayer = players[currentPlayerIndex];
            IPlayer target = evt.Target;

            // Fallback to first alive player if no target in sight
            if (target == null)
            {
                var alivePlayers = GetAllPlayers().Where(p => p.IsAlive).ToList();
                if (alivePlayers.Count > 0)
                {
                    target = alivePlayers[0];
                }
                else
                {
                    // No valid targets, end turn
                    EndTurn();
                    return;
                }
            }

            // Use the result from the event
            FireResult result = evt.Result;
            ProcessShotResult(currentPlayer, target, result);
        }
        catch (System.Exception ex)
        {
            Events.Publish(new GameErrorEvent { Error = "Error processing revolver fired event", Exception = ex });
        }
    }

    private void OnRevolverReloaded(RevolverReloadedEvent evt)
    {
        if (uiDisplay != null)
        {
            uiDisplay.UpdateBulletCount(evt.BulletPositions.Count(), revolver.MaxChambers);
            uiDisplay.ShowReloadAnimation();
        }
    }

    private void OnRevolverSpun(RevolverSpunEvent evt)
    {
        if (uiDisplay != null)
        {
            uiDisplay.UpdateChamberInfo(evt.NewChamber, revolver.MaxChambers);
            uiDisplay.ShowSpinAnimation();
        }
    }

    private void OnGameError(GameErrorEvent evt)
    {
        Debug.LogError($"Game Error: {evt.Error}");
        if (evt.Exception != null)
        {
            Debug.LogException(evt.Exception);
        }

        // Attempt recovery
        Events.Publish(new GameResetEvent());
    }

    private void OnPlayerEliminated(PlayerEliminatedEvent evt)
    {
        if (uiDisplay != null)
        {
            uiDisplay.UpdatePlayerStatus(evt.Player, false);
        }

        if (IsGameOver())
        {
            EndGame();
        }
    }

    private void OnPlayerDamaged(PlayerDamagedEvent evt)
    {
        // Additional logic for damage events if needed
        Debug.Log($"{evt.Player.PlayerName} took {evt.Damage} damage");
    }

    private void OnGameReset(GameResetEvent evt)
    {
        Debug.Log("Game reset requested - attempting recovery");
        ResetRound();
    }

    /// <summary>Processes shot result and handles consequences</summary>
    private void ProcessShotResult(IPlayer shooter, IPlayer target, FireResult result)
    {
        // UI feedback
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
            // Handle blank shot - AI learning and turn logic
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

            // Self-shot with blank gives another turn
            bool getsAnotherTurn = shotSelf && result == FireResult.Blank;

            if (getsAnotherTurn)
            {
                if (uiDisplay != null) uiDisplay.ShowWarning($"{shooter.PlayerName} gets another turn!");
                StartCoroutine(ReturnRevolverForSameTurn());
            }
            else
            {
                StartCoroutine(ReturnRevolverAndEndTurn());
            }
        }
    }

    /// <summary>Handles damage application and ragdoll effects</summary>
    private void DealDamageToTarget(IPlayer target, IPlayer shooter)
    {
        StartCoroutine(HitSequence(target, shooter));
    }

    IEnumerator RunHitSequenceInternal(
    IPlayer target,
    IPlayer shooter,
    MonoBehaviour targetMono,
    Action<System.Exception> onError)
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForFixedUpdate();

        MakeAllPlayersImmovable(target);
        bool ragdollSuccess = TriggerRagdoll(target, shooter);

        if (ragdollSuccess)
        {
            yield return new WaitForSeconds(ragdollDuration);

            Rigidbody[] targetRigidbodies = targetMono.gameObject.GetComponentsInChildren<Rigidbody>();
            foreach (Rigidbody rb in targetRigidbodies)
            {
                if (rb != null && !rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
                rb.isKinematic = true;
            }
        }

        yield return StartCoroutine(FadeToBlack());
        yield return StartCoroutine(ResetCharacterPositions());
        yield return new WaitForSeconds(0.2f);

        target.TakeDamage(1);

        if (uiDisplay != null)
            uiDisplay.UpdatePlayerStatus(target, target.IsAlive);

        if (IsGameOver())
        {
            EndGame();
            yield break;
        }

        int chambersLeft = revolver.MaxChambers - revolver.CurrentChamber;
        foreach (var player in players.OfType<IAIPlayer>().ToArray())
        {
            if (player != target && player != shooter)
                player.ObservePlayerAction(Target.Opponent, chambersLeft, false);
        }

        yield return StartCoroutine(FadeFromBlack());
        yield return new WaitForSeconds(0.1f);
    }

    /// <summary>Complete hit sequence with ragdoll and screen effects</summary>
    IEnumerator HitSequence(IPlayer target, IPlayer shooter)
    {
        isResettingScene = true;
        MonoBehaviour targetMono = target as MonoBehaviour;

        if (targetMono == null)
        {
            StartCoroutine(ReturnRevolverAndEndTurn());
            yield break;
        }

        Exception caughtException = null;

        yield return RunHitSequenceInternal(target, shooter, targetMono, e => caughtException = e);

        if (caughtException != null)
        {
            Events.Publish(new GameErrorEvent
            {
                Error = "Error in hit sequence",
                Exception = caughtException
            });
        }

        isResettingScene = false;
        StartCoroutine(ReturnRevolverAndEndTurn());
    }

    /// <summary>Freezes all players except specified exclusion</summary>
    void MakeAllPlayersImmovable(IPlayer excludePlayer = null)
    {
        foreach (var player in players)
        {
            if (excludePlayer != null && player == excludePlayer) continue;

            MonoBehaviour playerMono = player as MonoBehaviour;
            if (playerMono != null)
            {
                Rigidbody[] allRigidbodies = playerMono.gameObject.GetComponentsInChildren<Rigidbody>();
                foreach (Rigidbody rb in allRigidbodies)
                {
                    if (rb != null)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.isKinematic = true;
                        rb.useGravity = false;
                        rb.constraints = RigidbodyConstraints.FreezeAll;
                    }
                }
            }
        }
    }

    /// <summary>Triggers ragdoll physics on target player</summary>
    public bool TriggerRagdoll(IPlayer target, IPlayer shooter)
    {
        MonoBehaviour targetMono = target as MonoBehaviour;
        if (targetMono == null) return false;

        GameObject targetObj = targetMono.gameObject;

        // Disable animator for ragdoll
        Animator animator = targetObj.GetComponent<Animator>();
        if (animator != null) animator.enabled = false;

        // Get all rigidbodies and make them non-kinematic
        Rigidbody[] allRigidbodies = targetObj.GetComponentsInChildren<Rigidbody>();
        bool hasRagdollSetup = allRigidbodies.Length > 1;

        foreach (Rigidbody rb in allRigidbodies)
        {
            if (rb != null)
            {
                rb.isKinematic = false;
                rb.useGravity = true;
                rb.constraints = RigidbodyConstraints.None;
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity = Vector3.zero;
                rb.WakeUp();
            }
        }

        Rigidbody mainBody = allRigidbodies[0];

        // Use chest for humanoid ragdolls
        if (hasRagdollSetup && animator != null && animator.isHuman)
        {
            Transform chest = animator.GetBoneTransform(HumanBodyBones.Chest);
            if (chest != null)
            {
                Rigidbody chestRb = chest.GetComponent<Rigidbody>();
                if (chestRb != null) mainBody = chestRb;
            }
        }

        // Calculate shot direction and apply force
        Vector3 shotDirection = (targetObj.transform.position - GetShooterPosition(shooter)).normalized;
        if (shotDirection.sqrMagnitude < 0.01f) shotDirection = -targetObj.transform.forward;

        Vector3 shotForce = shotDirection * ragdollForce + Vector3.up * ragdollUpwardForce;

        if (!mainBody.isKinematic)
        {
            mainBody.AddForce(shotForce, ForceMode.Impulse);

            // Add random torque for realism
            Vector3 torque = new Vector3(UnityEngine.Random.Range(-2f, 2f), UnityEngine.Random.Range(-1f, 1f), UnityEngine.Random.Range(-2f, 2f));
            mainBody.AddTorque(torque, ForceMode.Impulse);
            return true;
        }

        return false;
    }

    Vector3 GetShooterPosition(IPlayer shooter)
    {
        MonoBehaviour shooterMono = shooter as MonoBehaviour;
        return shooterMono != null ? shooterMono.transform.position : Vector3.zero;
    }

    /// <summary>Resets all characters to their original positions</summary>
    IEnumerator ResetCharacterPositions()
    {
        foreach (var player in players)
        {
            MonoBehaviour playerMono = player as MonoBehaviour;
            if (playerMono != null && originalTransforms.ContainsKey(player))
            {
                GameObject playerObj = playerMono.gameObject;
                var originalTransform = originalTransforms[player];

                // Disable colliders during reset
                if (disableCollidersDuringReset)
                {
                    Collider[] allColliders = playerObj.GetComponentsInChildren<Collider>();
                    foreach (Collider collider in allColliders) collider.enabled = false;
                }

                // Reset position and rotation
                playerObj.transform.position = originalTransform.position;
                playerObj.transform.rotation = originalTransform.rotation;
                playerObj.transform.localScale = originalTransform.scale;

                yield return new WaitForEndOfFrame();

                // Reset rigidbody properties
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
                        rb.isKinematic = originalTransform.wasKinematic;
                        rb.useGravity = originalTransform.useGravity;
                        rb.constraints = originalTransform.originalConstraints;
                    }
                }

                yield return new WaitForFixedUpdate();

                // Re-enable animator
                Animator animator = playerObj.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.enabled = true;
                    animator.Rebind();
                    animator.Update(0f);
                }
            }
        }

        // Re-enable colliders after delay
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

    // Screen fade coroutines
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

    // Turn management coroutines
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

        // Force release if grabbed
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

    /// <summary>Ends current turn and advances to next player</summary>
    public void EndTurn()
    {
        if (!gameActive) return;

        IPlayer previousPlayer = players[currentPlayerIndex];
        Events.Publish(new TurnEndedEvent { PreviousPlayer = previousPlayer });

        currentTurn++;

        // Find next alive player
        int startIndex = currentPlayerIndex;
        int loopCount = 0;
        do
        {
            currentPlayerIndex = (currentPlayerIndex + 1) % players.Count;
            loopCount++;
            if (loopCount > players.Count * 2) break;
            if (currentPlayerIndex == startIndex) break;
        }
        while (!players[currentPlayerIndex].IsAlive && GetActivePlayers() > 1);

        // Reload if needed, otherwise continue
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

    /// <summary>Reloads revolver and continues game</summary>
    IEnumerator ReloadSequence()
    {
        yield return StartCoroutine(PassRevolverToPoint(tableCenterPoint));
        yield return new WaitForSeconds(0.5f);

        revolver.Reload(revolver.GenerateBulletPositions());
        revolver.Spin();

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

    /// <summary>Controls revolver rotation for AI aiming</summary>
    public void AllowRevolverRotation(bool allowRotation)
    {
        if (revolverRigidbody != null)
        {
            if (allowRotation)
            {
                revolverRigidbody.isKinematic = false;
                revolverRigidbody.constraints = RigidbodyConstraints.None;
            }
            else
            {
                revolverRigidbody.isKinematic = true;
                revolverRigidbody.constraints = RigidbodyConstraints.FreezeAll;
            }
        }

        if (revolverGrabInteractable != null)
        {
            revolverGrabInteractable.enabled = !allowRotation;
        }
    }

    IEnumerator PassRevolverToPoint(Transform point)
    {
        if (!animateRevolverPass || revolver == null || point == null) yield break;

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

    // Game state checking methods
    public bool CheckWinCondition() => GetActivePlayers() <= 1;
    public bool IsGameOver() => CheckWinCondition();
    public IPlayer GetWinner() => IsGameOver() ? players.FirstOrDefault(p => p.IsAlive) : null;
    public int GetActivePlayers() => players.Count(p => p.IsAlive);

    /// <summary>Ends the game and declares winner</summary>
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
            if (uiDisplay != null) uiDisplay.UpdateGameState("Game Over - Draw!");
        }

        StartCoroutine(PassRevolverToPoint(tableCenterPoint));
    }

    // Public interface implementations
    public void NextTurn() => EndTurn();
    public void EliminatePlayer(IPlayer player)
    {
        player.Eliminate();
        if (uiDisplay != null) uiDisplay.UpdatePlayerStatus(player, false);
        if (IsGameOver()) EndGame();
    }
    public void ResetRound() => RestartGame();
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
    public IPlayer GetCurrentPlayer() => players.Count > 0 ? players[currentPlayerIndex] : null;
    public List<IPlayer> GetAllPlayers() => new List<IPlayer>(players);
    public bool IsPlayerTurn() => waitingForPlayerAction;
    public Transform GetPlayerPosition() => playerPosition;
    public Transform GetNPCPosition() => npcPosition;
    public Transform GetTableCenter() => tableCenterPoint;
}