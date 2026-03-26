using System;
using Doody.GameEvents;

namespace Doody.GameEvents
{
    public enum GameState
    {
        WaitingForStart,
        Starting,
        TurnInProgress,
        ProcessingShot,
        ResettingScene,
        PassingRevolver,
        GameOver
    }

    // Game state events
    public class GameStartedEvent { }
    public class GameEndedEvent { public IPlayer Winner { get; set; } }
    public class TurnStartedEvent { public IPlayer CurrentPlayer { get; set; } }
    public class TurnEndedEvent { public IPlayer PreviousPlayer { get; set; } }
    public class GameStateChangedEvent { public GameState NewState { get; set; } public GameState PreviousState { get; set; } }

    // Player events
    public class PlayerEliminatedEvent { public IPlayer Player { get; set; } }
    public class PlayerDamagedEvent { public IPlayer Player { get; set; } public int Damage { get; set; } }
    public class PlayerResetEvent { public IPlayer Player { get; set; } }

    // Revolver events
    public class RevolverFiredEvent
    {
        public IPlayer Shooter { get; set; }
        public IPlayer Target { get; set; }
        public FireResult Result { get; set; }
        public bool WasHeld { get; set; }
    }

    public class RevolverReloadedEvent { public System.Collections.Generic.IEnumerable<int> BulletPositions { get; set; } }
    public class RevolverSpunEvent { public int NewChamber { get; set; } }

    // UI events
    public class UIUpdateEvent { public string Message { get; set; } }
    public class UIEffectEvent { public UIEffect Effect { get; set; } }

    // Error/Recovery events
    public class GameErrorEvent { public string Error { get; set; } public Exception Exception { get; set; } }
    public class GameResetEvent { }
}