using UnityEngine;

public class Player : MonoBehaviour, IPlayer
{
    [Header("Player Info")]
    [SerializeField] private string playerName = "Player";
    [SerializeField] private int playerID = 0;
    [SerializeField] private int maxHealth = 1;

    [Header("Visual Feedback")]
    [SerializeField] private Renderer playerRenderer;
    [SerializeField] private Color aliveColor = Color.blue;
    [SerializeField] private Color deadColor = Color.gray;
    [SerializeField] private GameObject eliminatedEffect;

    private int currentHealth;
    private bool isAlive = true;

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

    public void TakeTurn()
    {
    }

    public void Eliminate()
    {
        if (!isAlive) return;

        isAlive = false;
        currentHealth = 0;
        UpdateVisuals();

        if (eliminatedEffect != null)
        {
            Instantiate(eliminatedEffect, transform.position, Quaternion.identity);
        }

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = false;
    }

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

    public void Reset()
    {
        currentHealth = maxHealth;
        isAlive = true;

        Collider col = GetComponent<Collider>();
        if (col != null) col.enabled = true;

        UpdateVisuals();
    }

    public void TakeDamage(int damage)
    {
        currentHealth -= damage;
        if (currentHealth <= 0)
        {
            currentHealth = 0;
            Eliminate();
        }
    }

    void OnDrawGizmos()
    {
        if (!Application.isPlaying) return;

        Gizmos.color = isAlive ? Color.blue : Color.gray;
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 2f, 0.3f);
    }
}
