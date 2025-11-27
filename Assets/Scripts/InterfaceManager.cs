using System;
using System.Collections.Generic;

// Represents a player in the game
public interface IPlayer
{
    int Health { get; }
    string PlayerName { get; }
    bool IsAlive { get; }
    int ID { get; }

    void TakeTurn();      // Executes player's turn
    void Eliminate();     // Marks player as eliminated
    void TakeDamage(int damage); // Reduces player's health
}

// Player statistics used for AI or gameplay decisions
public interface IPlayerStats
{
    float Aggression { get; }
    float Fear { get; }
    float Confidence { get; }
}

// Targeting options for actions
public enum Target
{
    Self,
    Opponent
}

// Controls player decisions
public interface IPlayerController
{
    Target ChooseTarget(IPlayer self, IReadOnlyList<IPlayer> others, int chambersLeft);
    bool ShouldSpin();
    bool ShouldReload();
}

// AI-specific player interface
public interface IAIPlayer : IPlayer
{
    Target DecideTarget(int chambersLeft);
    void ObservePlayerAction(Target playerChoice, int chambersLeft, bool npcShotSelfLastTurn);
    bool IsAiming();
}

// Handles ragdoll effects in physics
public interface IRagdoll
{
    bool TriggerRagdoll(IPlayer target, IPlayer cause);
    float RagdollDuration { get; }
    float RagdollForce { get; }
    float RagdollUpwardForce { get; }
}

// Turn-based system interface
public interface ITurnBased
{
    int CurrentTurn { get; }
    float TimeSpan { get; }
    int currentIDsTurn { get; }

    void StartTurn();
    void EndTurn();
}

// Outcome of firing the revolver
public enum FireResult
{
    Bullet,
    Blank
}

// Revolver mechanics interface
public interface IRevolverMechanic
{
    int CurrentChamber { get; }
    int MaxChambers { get; }
    IReadOnlyList<int> BulletPositions { get; }

    FireResult Fire();
    void Spin();
    void Reload(IEnumerable<int> newBulletPositions);
    List<int> GenerateBulletPositions();
}

// Game rules interface
public interface IGameRules
{
    bool CheckWinCondition();
    bool IsGameOver();
    IPlayer GetWinner();
    int GetActivePlayers();
}

// UI effects for animations or feedback
public enum UIEffect
{
    ChamberAdvance,
    BulletLoaded,
    PlayerEliminated,
    DangerWarning,
    SafeShot
}

// Handles all UI updates
public interface IUIDisplay
{
    void UpdateTurnIndicator(IPlayer currentPlayer);
    void ShowResult(FireResult result);
    void DisplayWinner(IPlayer winner);
    void UpdateGameState(string stateMessage);
    void UpdateTurnTimer(float timeRemaining);
    void UpdateBulletCount(int currentBullets, int maxChambers);
    void UpdateChamberInfo(int currentChamber, int maxChambers);
    void UpdatePlayerStatus(IPlayer player, bool isAlive);
    void ShowWarning(string warningMessage);
    void ShowEffect(UIEffect effect);
    void ShowReloadAnimation();
    void ShowSpinAnimation();
}

// Event logging interface
public interface IEventLog
{
    void Log(string message);
    void LogFire(IPlayer shooter, FireResult result);
    void LogElimination(IPlayer eliminated);
    void LogTurnStart(IPlayer player);
}

// Game manager handling main game logic
public interface IGameManager
{
    IReadOnlyList<IPlayer> Players { get; }
    IRevolverMechanic Revolver { get; }
    ITurnBased TurnSystem { get; }
    IGameRules Rules { get; }

    void StartGame();
    void NextTurn();
    void EliminatePlayer(IPlayer player);
    void ResetRound();
    bool IsPlayerTurn();
}

// Round system interface
public interface IRoundSystem
{
    int RoundNumber { get; }

    void StartRound();
    void EndRound();
    void PrepareNextRound();
}
