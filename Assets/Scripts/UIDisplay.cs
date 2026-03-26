using Doody.GameEvents;
using TMPro;
using UnityEngine;
public class UIDisplay : EventListener, IUIDisplay
{
    [SerializeField] private TMP_Text turnIndicator;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text winnerText;

    [SerializeField] private Color playerTurnColor = Color.cyan;
    [SerializeField] private Color aiTurnColor = Color.yellow;
    [SerializeField] private Color timerWarningColor = Color.red;
    [SerializeField] private Color timerNormalColor = Color.white;

    void Start()
    {
        InitializeUI();
        SubscribeToEvents();
    }

    private void SubscribeToEvents()
    {
        Listen<TurnStartedEvent>(OnTurnStarted);
        Listen<RevolverFiredEvent>(OnRevolverFired);
        Listen<GameStateChangedEvent>(OnGameStateChanged);
        Listen<PlayerEliminatedEvent>(OnPlayerEliminated);
        Listen<UIUpdateEvent>(OnUIUpdate);
        Listen<UIEffectEvent>(OnUIEffect);
    }

    private void InitializeUI()
    {
        // Clear or set default text for UI elements
        if (turnIndicator != null) turnIndicator.text = "Game Starting...";
        if (timerText != null) timerText.text = "";
        if (winnerText != null) winnerText.text = "";
    }

    public void UpdateTurnIndicator(IPlayer currentPlayer)
    {
        // Update whose turn it is and set corresponding color
        if (turnIndicator != null)
        {
            turnIndicator.text = $"{currentPlayer.PlayerName}'s Turn";
            turnIndicator.color = currentPlayer is IAIPlayer ? aiTurnColor : playerTurnColor;
        }
    }

    public void DisplayWinner(IPlayer winner)
    {
        // Display the winner's name
        if (winnerText != null)
        {
            winnerText.text = $"{winner.PlayerName} WINS!";
            winnerText.color = Color.green;
        }
    }

    public void UpdateTurnTimer(float timeRemaining)
    {
        // Update timer display and color based on remaining time
        if (timerText != null)
        {
            timerText.text = $"Time: {timeRemaining:F1}s";
            timerText.color = timeRemaining < 5f ? timerWarningColor : timerNormalColor;
        }
    }

    public void ResetUI()
    {
        // Reset UI to initial state
        InitializeUI();
    }

    // Empty implementations for optional UI updates
    public void ShowResult(FireResult result) { }
    public void UpdateGameState(string stateMessage) { }
    public void UpdateBulletCount(int currentBullets, int maxChambers) { }
    public void UpdateChamberInfo(int currentChamber, int maxChambers) { }
    public void UpdatePlayerStatus(IPlayer player, bool isAlive) { }
    public void ShowWarning(string warningMessage) { }
    public void ShowEffect(UIEffect effect) { }
    public void ShowReloadAnimation() { }
    public void ShowSpinAnimation() { }

    // Event handlers
    private void OnTurnStarted(TurnStartedEvent evt)
    {
        UpdateTurnIndicator(evt.CurrentPlayer);
    }

    private void OnRevolverFired(RevolverFiredEvent evt)
    {
        ShowResult(evt.Result);
        UpdateGameState($"Shot fired - {evt.Result}");

        if (evt.Result == FireResult.Bullet)
        {
            ShowEffect(UIEffect.PlayerEliminated);
        }
        else
        {
            ShowEffect(UIEffect.SafeShot);
        }
    }

    private void OnGameStateChanged(GameStateChangedEvent evt)
    {
        // Handle state-specific UI updates
        switch (evt.NewState)
        {
            case GameState.GameOver:
                UpdateGameState("Game Over!");
                break;
            case GameState.ResettingScene:
                UpdateGameState("Resetting scene...");
                break;
        }
    }

    private void OnPlayerEliminated(PlayerEliminatedEvent evt)
    {
        UpdatePlayerStatus(evt.Player, false);
    }

    private void OnUIUpdate(UIUpdateEvent evt)
    {
        UpdateGameState(evt.Message);
    }

    private void OnUIEffect(UIEffectEvent evt)
    {
        ShowEffect(evt.Effect);
    }
}
