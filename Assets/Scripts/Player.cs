using Doody.GameEvents;
using UnityEngine;
public class Player : EventListener, IPlayer
{
    [Header("Player Info")]
    [SerializeField] private string playerName = "Player";
    [SerializeField] private int playerID = 0;
    [SerializeField] private int maxHealth = 3;

    [Header("Visual Feedback")]
    [SerializeField] private Renderer playerRenderer;
    [SerializeField] private Color aliveColor = Color.blue;
    [SerializeField] private Color deadColor = Color.gray;
    [SerializeField] private GameObject eliminatedEffect;

    // Core player state
    private int currentHealth;
    private bool isAlive = true;

    // Public interface properties
    public int Health => currentHealth;
    public string PlayerName => playerName;
    public bool IsAlive => isAlive;
    public int ID => playerID;

    void Awake()
    {
        currentHealth = maxHealth;
        UpdateVisuals();
    }

    void Start()
    {
        if (!gameObject.CompareTag("Player"))
        {
            gameObject.tag = "Player";
        }
    }

    /// <summary>Empty implementation - human player turns are handled by GameManager</summary>
    public void TakeTurn()
    {
        // Human player turns are handled through GameManager and XR interaction
        // This method exists to satisfy the IPlayer interface
    }

    /// <summary>Handles player elimination with visual effects</summary>
    public void Eliminate()
    {
        if (!isAlive) return;

        isAlive = false;
        currentHealth = 0;
        UpdateVisuals();

        // Play elimination effect
        if (eliminatedEffect != null)
        {
            Instantiate(eliminatedEffect, transform.position, Quaternion.identity);
        }

        // Disable collision
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;

        Events.Publish(new PlayerEliminatedEvent { Player = this });
    }

    /// <summary>Updates visual appearance based on alive/dead state</summary>
    private void UpdateVisuals()
    {
        if (playerRenderer != null)
        {
            MaterialPropertyBlock props = new MaterialPropertyBlock();
            playerRenderer.GetPropertyBlock(props);
            props.SetColor("_Color", isAlive ? aliveColor : deadColor);
            playerRenderer.SetPropertyBlock(props);
        }
    }

    /// <summary>Resets player to initial state</summary>
    public void Reset()
    {
        currentHealth = maxHealth;
        isAlive = true;

        // Re-enable collision
        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        UpdateVisuals();
    }

    /// <summary>Applies damage and handles death</summary>
    public void TakeDamage(int damage)
    {
        currentHealth -= damage;

        Events.Publish(new PlayerDamagedEvent { Player = this, Damage = damage });

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Eliminate();
        }
    }

    /// <summary>Debug visualization for player state</summary>
    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = isAlive ? Color.blue : Color.gray;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.3f);
    }
}