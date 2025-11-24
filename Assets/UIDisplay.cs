using UnityEngine;
using TMPro;
using UnityEngine.UI;
using System.Collections;

public class UIDisplay : MonoBehaviour, IUIDisplay
{
    [Header("Main UI Elements")]
    [SerializeField] private TMP_Text turnIndicator;
    [SerializeField] private TMP_Text gameStateText;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private TMP_Text bulletCountText;
    [SerializeField] private TMP_Text chamberInfoText;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private TMP_Text winnerText;
    [SerializeField] private TMP_Text warningText;

    [Header("Player Status")]
    [SerializeField] private TMP_Text playerStatusText;
    [SerializeField] private TMP_Text npcStatusText;
    [SerializeField] private Image playerStatusIcon;
    [SerializeField] private Image npcStatusIcon;

    [Header("Visual Effects")]
    [SerializeField] private Animator resultAnimator;
    [SerializeField] private Animator reloadAnimator;
    [SerializeField] private Animator spinAnimator;
    [SerializeField] private ParticleSystem bulletEffect;
    [SerializeField] private ParticleSystem dangerEffect;
    [SerializeField] private ParticleSystem eliminationEffect;
    [SerializeField] private ParticleSystem safeEffect;

    [Header("Colors")]
    [SerializeField] private Color aliveColor = Color.green;
    [SerializeField] private Color deadColor = Color.red;
    [SerializeField] private Color warningColor = Color.yellow;
    [SerializeField] private Color bulletColor = Color.red;
    [SerializeField] private Color safeColor = Color.white;

    [Header("Icons")]
    [SerializeField] private Sprite aliveIcon;
    [SerializeField] private Sprite deadIcon;

    private Coroutine warningCoroutine;

    void Start()
    {
        InitializeUI();
    }

    private void InitializeUI()
    {
        if (turnIndicator != null) turnIndicator.text = "Game Starting...";
        if (gameStateText != null) gameStateText.text = "Russian Roulette";
        if (timerText != null) timerText.text = "";
        if (bulletCountText != null) bulletCountText.text = "";
        if (chamberInfoText != null) chamberInfoText.text = "";
        if (resultText != null) resultText.text = "";
        if (winnerText != null) winnerText.text = "";
        if (warningText != null) warningText.text = "";

        if (playerStatusText != null) playerStatusText.text = "ALIVE";
        if (npcStatusText != null) npcStatusText.text = "ALIVE";

        if (playerStatusIcon != null) playerStatusIcon.color = aliveColor;
        if (npcStatusIcon != null) npcStatusIcon.color = aliveColor;
    }

    public void UpdateTurnIndicator(IPlayer currentPlayer)
    {
        if (turnIndicator != null)
        {
            turnIndicator.text = $"{currentPlayer.PlayerName}'s Turn";
            turnIndicator.color = currentPlayer is IAIPlayer ? Color.yellow : Color.cyan;
        }
    }

    public void ShowResult(FireResult result)
    {
        if (resultText != null)
        {
            resultText.text = result == FireResult.Bullet ? "BANG!" : "CLICK";
            resultText.color = result == FireResult.Bullet ? bulletColor : safeColor;
            resultText.fontSize = result == FireResult.Bullet ? 48 : 36;
            resultText.fontStyle = result == FireResult.Bullet ? FontStyles.Bold : FontStyles.Normal;
        }

        if (resultAnimator != null) resultAnimator.SetTrigger("ShowResult");

        StartCoroutine(HideResultAfterDelay(2f));
    }

    public void DisplayWinner(IPlayer winner)
    {
        if (winnerText != null)
        {
            winnerText.text = $"{winner.PlayerName} WINS!";
            winnerText.color = Color.green;
            winnerText.fontSize = 42;
            winnerText.fontStyle = FontStyles.Bold;
        }

        if (eliminationEffect != null)
        {
            var main = eliminationEffect.main;
            main.startColor = Color.green;
            eliminationEffect.Play();
        }
    }

    public void UpdateGameState(string stateMessage)
    {
        if (gameStateText != null) gameStateText.text = stateMessage;
    }

    public void UpdateTurnTimer(float timeRemaining)
    {
        if (timerText != null)
        {
            timerText.text = $"{timeRemaining:F1}s";
            if (timeRemaining < 5f) { timerText.color = Color.red; timerText.fontStyle = FontStyles.Bold; }
            else if (timeRemaining < 10f) { timerText.color = warningColor; timerText.fontStyle = FontStyles.Bold; }
            else { timerText.color = Color.white; timerText.fontStyle = FontStyles.Normal; }
        }
    }

    public void UpdateBulletCount(int currentBullets, int maxChambers)
    {
        if (bulletCountText != null)
        {
            bulletCountText.text = $"Bullets: {currentBullets}/{maxChambers}";
            float dangerLevel = (float)currentBullets / maxChambers;
            bulletCountText.color = dangerLevel > 0.5f ? Color.red : dangerLevel > 0.2f ? warningColor : safeColor;
        }
    }

    public void UpdateChamberInfo(int currentChamber, int maxChambers)
    {
        if (chamberInfoText != null) chamberInfoText.text = $"Chamber: {currentChamber + 1}/{maxChambers}";
    }

    public void UpdatePlayerStatus(IPlayer player, bool isAlive)
    {
        if (player is IAIPlayer)
        {
            if (npcStatusText != null) { npcStatusText.text = isAlive ? "ALIVE" : "ELIMINATED"; npcStatusText.color = isAlive ? aliveColor : deadColor; }
            if (npcStatusIcon != null) { npcStatusIcon.color = isAlive ? aliveColor : deadColor; npcStatusIcon.sprite = isAlive ? aliveIcon : deadIcon; }
        }
        else
        {
            if (playerStatusText != null) { playerStatusText.text = isAlive ? "ALIVE" : "ELIMINATED"; playerStatusText.color = isAlive ? aliveColor : deadColor; }
            if (playerStatusIcon != null) { playerStatusIcon.color = isAlive ? aliveColor : deadColor; playerStatusIcon.sprite = isAlive ? aliveIcon : deadIcon; }
        }

        if (!isAlive && eliminationEffect != null) eliminationEffect.Play();
    }

    public void ShowWarning(string warningMessage)
    {
        if (warningText != null)
        {
            warningText.text = warningMessage;
            warningText.color = warningColor;
            warningText.fontStyle = FontStyles.Bold;
        }

        if (warningCoroutine != null) StopCoroutine(warningCoroutine);
        warningCoroutine = StartCoroutine(ClearWarningAfterDelay(3f));

        if (warningMessage.Contains("Time") && dangerEffect != null) dangerEffect.Play();
    }

    public void ShowEffect(UIEffect effect)
    {
        switch (effect)
        {
            case UIEffect.BulletLoaded: if (bulletEffect != null) bulletEffect.Play(); break;
            case UIEffect.PlayerEliminated: if (eliminationEffect != null) eliminationEffect.Play(); break;
            case UIEffect.DangerWarning: if (dangerEffect != null) dangerEffect.Play(); break;
            case UIEffect.SafeShot: if (safeEffect != null) safeEffect.Play(); break;
        }
    }

    public void ShowReloadAnimation()
    {
        if (reloadAnimator != null) reloadAnimator.SetTrigger("Reload");
        ShowEffect(UIEffect.BulletLoaded);
    }

    public void ShowSpinAnimation()
    {
        if (spinAnimator != null) spinAnimator.SetTrigger("Spin");
    }

    private IEnumerator HideResultAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (resultText != null) resultText.text = "";
    }

    private IEnumerator ClearWarningAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (warningText != null) warningText.text = "";
        warningCoroutine = null;
    }

    public void ShowGameStart()
    {
        UpdateGameState("Game Starting...");
        ShowEffect(UIEffect.DangerWarning);
    }

    public void ShowGameOver(bool hasWinner)
    {
        if (!hasWinner) UpdateGameState("Game Over - Draw!");
    }

    public void ResetUI()
    {
        InitializeUI();
        if (warningCoroutine != null) { StopCoroutine(warningCoroutine); warningCoroutine = null; }
    }

    void OnDestroy()
    {
        if (warningCoroutine != null) StopCoroutine(warningCoroutine);
    }
}
