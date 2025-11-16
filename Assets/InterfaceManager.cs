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
}

public interface ITurnBased
{
    int CurrentTurn { get; }
    float TimeSpan { get; }

    int currentIDsTurn { get; }
    void StartTurn();
    void EndTurn();
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


public enum FireResult
{
    Bullet,
    Blank  
}


public interface IUIDisplay
{
    void UpdateTurnIndicator(IPlayer currentPlayer);
    void ShowResult(FireResult result);
    void DisplayWinner(IPlayer winner);
}

public interface IPlayerStats
{
    float Aggression { get; }
    float Fear { get; }
    float Confidence { get; }
}

public interface IAIPlayer : IPlayer
{
    Target DecideTarget(int chambersLeft);
    void ObservePlayerAction(Target playerChoice, int chambersLeft, bool npcShotSelfLastTurn);
}

public enum Target
{
    Self,
    Opponent
}

