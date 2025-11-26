using System;
using System.Collections.Generic;


public interface IPlayer
{
    int Health { get; }
    string PlayerName { get; }
    bool IsAlive { get; }
    int ID { get; }

    void TakeTurn();
    void Eliminate();
    void TakeDamage(int damage);
}

public interface IPlayerStats
{
    float Aggression { get; }
    float Fear { get; }
    float Confidence { get; }
}

public enum Target
{
    Self,
    Opponent
}

public interface IPlayerController
{
    Target ChooseTarget(IPlayer self, IReadOnlyList<IPlayer> others, int chambersLeft);
    bool ShouldSpin();
    bool ShouldReload();
}

public interface IAIPlayer : IPlayer
{
    Target DecideTarget(int chambersLeft);
    void ObservePlayerAction(Target playerChoice, int chambersLeft, bool npcShotSelfLastTurn);
}


public interface ITurnBased
{
    int CurrentTurn { get; }
    float TimeSpan { get; }
    int currentIDsTurn { get; }

    void StartTurn();
    void EndTurn();
}


public enum FireResult
{
    Bullet,
    Blank
}

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


public interface IGameRules
{
    bool CheckWinCondition();
    bool IsGameOver();
    IPlayer GetWinner();
    int GetActivePlayers();
}


public enum UIEffect
{
    ChamberAdvance,
    BulletLoaded,
    PlayerEliminated,
    DangerWarning,
    SafeShot
}

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


public interface IEventLog
{
    void Log(string message);
    void LogFire(IPlayer shooter, FireResult result);
    void LogElimination(IPlayer eliminated);
    void LogTurnStart(IPlayer player);
}


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
}


public interface IRoundSystem
{
    int RoundNumber { get; }

    void StartRound();
    void EndRound();
    void PrepareNextRound();
}
