using Doody.GameEvents;
using TMPro;
using UnityEngine;
public class UIDisplay : EventListener, IUIDisplay
{
    [SerializeField] private TMP_Text turnIndicator;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text winnerText;
    [SerializeField] private TMP_Text stateText;
    [SerializeField] private TMP_Text healthText;

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
        Listen<PlayerDamagedEvent>(OnPlayerDamaged);
        Listen<UIUpdateEvent>(OnUIUpdate);
        Listen<UIEffectEvent>(OnUIEffect);
    }

    private void InitializeUI()
    {
        // Clear or set default text for UI elements
        if (turnIndicator != null) turnIndicator.text = "Game Starting...";
        if (timerText != null) timerText.text = "";
        if (winnerText != null) winnerText.text = "";
        if (stateText != null) stateText.text = "";
        if (healthText != null) healthText.text = "";
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

    public void ShowResult(FireResult result)
    {
        string message = result == FireResult.Bullet ? "Bullet fired!" : "Blank shot.";
        UpdateGameState(message);
    }

    public void UpdateGameState(string stateMessage)
    {
        if (stateText != null)
        {
            stateText.text = stateMessage;
            return;
        }

        if (turnIndicator != null)
        {
            turnIndicator.text = stateMessage;
        }
    }

    public void UpdateBulletCount(int currentBullets, int maxChambers)
    {
        if (timerText != null)
        {
            timerText.text = $"Bullets: {currentBullets}/{maxChambers}";
            timerText.color = timerNormalColor;
        }
    }

    public void UpdateChamberInfo(int currentChamber, int maxChambers)
    {
        if (winnerText != null)
        {
            winnerText.text = $"Chamber: {currentChamber + 1}/{maxChambers}";
            winnerText.color = Color.white;
        }
    }

    public void UpdatePlayerStatus(IPlayer player, bool isAlive)
    {
        if (healthText != null)
        {
            healthText.text = isAlive ? $"{player.PlayerName}: {player.Health} HP" : $"{player.PlayerName} eliminated";
        }

        if (stateText != null)
        {
            stateText.text = $"{player.PlayerName} is {(isAlive ? "alive" : "eliminated")}";
        }
    }

    public void UpdatePlayerHealth(IPlayer player)
    {
        if (healthText != null)
        {
            healthText.text = $"{player.PlayerName}: {player.Health} HP";
        }
    }

    public void ShowWarning(string warningMessage)
    {
        if (timerText != null)
        {
            timerText.text = warningMessage;
            timerText.color = timerWarningColor;
        }
        else if (stateText != null)
        {
            stateText.text = warningMessage;
        }
    }

    public void ShowEffect(UIEffect effect)
    {
        if (stateText == null) return;
        stateText.text = effect switch
        {
            UIEffect.PlayerEliminated => "A player has been eliminated!",
            UIEffect.DangerWarning => "Danger! A risky shot is coming.",
            UIEffect.SafeShot => "Safe shot - no bullet fired.",
            UIEffect.BulletLoaded => "Revolver reloaded.",
            UIEffect.ChamberAdvance => "Chamber advanced.",
            _ => stateText.text
        };
    }

    public void ShowReloadAnimation()
    {
        UpdateGameState("Reloading revolver...");
    }

    public void ShowSpinAnimation()
    {
        UpdateGameState("Spinning revolver...");
    }

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
        UpdatePlayerHealth(evt.Player);
    }

    private void OnPlayerDamaged(PlayerDamagedEvent evt)
    {
        UpdatePlayerHealth(evt.Player);
        UpdatePlayerStatus(evt.Player, evt.Player.IsAlive);
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
