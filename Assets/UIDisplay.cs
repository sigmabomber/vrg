using UnityEngine;
using TMPro;

public class UIDisplay : MonoBehaviour, IUIDisplay
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
    }

    private void InitializeUI()
    {
        if (turnIndicator != null) turnIndicator.text = "Game Starting...";
        if (timerText != null) timerText.text = "";
        if (winnerText != null) winnerText.text = "";
    }

    public void UpdateTurnIndicator(IPlayer currentPlayer)
    {
        if (turnIndicator != null)
        {
            turnIndicator.text = $"{currentPlayer.PlayerName}'s Turn";
            turnIndicator.color = currentPlayer is IAIPlayer ? aiTurnColor : playerTurnColor;
        }
    }

    public void DisplayWinner(IPlayer winner)
    {
        if (winnerText != null)
        {
            winnerText.text = $"{winner.PlayerName} WINS!";
            winnerText.color = Color.green;
        }
    }

    public void UpdateTurnTimer(float timeRemaining)
    {
        if (timerText != null)
        {
            timerText.text = $"Time: {timeRemaining:F1}s";
            timerText.color = timeRemaining < 5f ? timerWarningColor : timerNormalColor;
        }
    }

    public void ResetUI()
    {
        InitializeUI();
    }
    // Orkar inte lägga till allt, men finns om jag vill fortsätta.
    public void ShowResult(FireResult result) { }
    public void UpdateGameState(string stateMessage) { }
    public void UpdateBulletCount(int currentBullets, int maxChambers) { }
    public void UpdateChamberInfo(int currentChamber, int maxChambers) { }
    public void UpdatePlayerStatus(IPlayer player, bool isAlive) { }
    public void ShowWarning(string warningMessage) { }
    public void ShowEffect(UIEffect effect) { }
    public void ShowReloadAnimation() { }
    public void ShowSpinAnimation() { }
}