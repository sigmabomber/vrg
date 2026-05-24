using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using Doody.GameEvents;

public class Revolver : EventListener, IRevolverMechanic
{
    [Header("Raycast Settings")]
    public Transform muzzlePoint;
    public float raycastDistance = 100f;
    public LayerMask targetLayers;
    public bool showDebugRay = true;

    [Header("Target Validation")]
    [SerializeField] private bool requireTargetToShoot = true;
    [SerializeField] private float targetCheckDistance = 50f;
    [SerializeField] private AudioClip invalidTargetClip;
    [SerializeField] private float invalidTargetVolume = 0.5f;

    [Header("Laser Sight")]
    [SerializeField] private bool showLaserSight = true;
    [SerializeField] private LineRenderer laserSight;
    [SerializeField] private Color laserColorNoTarget = Color.red;
    [SerializeField] private Color laserColorValidTarget = Color.green;
    [SerializeField] private float laserWidth = 0.01f;
    [SerializeField] private Material laserMaterial;

    [Header("Revolver Settings")]
    public int MaxBullets = 3;
    private const int CHAMBERS = 6;

    [Header("SFX")]
    [SerializeField] private AudioClip gunshotClip;
    [SerializeField] private AudioClip clickClip;
    [SerializeField] private AudioClip reloadClip;
    [SerializeField] private AudioClip spinClip;
    [SerializeField] private AudioClip hammerCockClip;
    [SerializeField] private AudioClip chamberRotateClip;

    [Header("SFX Volumes")]
    [SerializeField] private float gunshotVolume = 1.0f;
    [SerializeField] private float clickVolume = 0.8f;
    [SerializeField] private float reloadVolume = 0.7f;
    [SerializeField] private float spinVolume = 0.8f;
    [SerializeField] private float hammerCockVolume = 0.5f;
    [SerializeField] private float chamberRotateVolume = 0.3f;

    [Header("Recoil")]
    [SerializeField] private float recoilStrength = 0.1f;
    [SerializeField] private float recoilReturnSpeed = 10f;
    [SerializeField] private float cameraShakeIntensity = 0.5f;
    [SerializeField] private float cameraShakeDuration = 0.2f;

    // XR Interaction components
    private XRGrabInteractable grab;
    private bool isHeld;

    // Physics and audio
    private Rigidbody rb;
    private AudioSource audioSource;

    // Revolver chamber state
    private List<int> _bulletPositions = new List<int>();
    private int currentChamber = 0;

    // Game references
    private Camera mainCam;

    // Recoil system
    private Vector3 localRecoilOffset;
    private Vector3 localRecoilVelocity;

    // Shot tracking
    private bool shotThisTurn = false;
    private FireResult lastShotResult;

    // Targeting system
    private IPlayer currentTargetInSight = null;


 
    void Awake()
    {
        grab = GetComponent<XRGrabInteractable>();
        grab.activated.AddListener(OnTriggerPulled);
        grab.selectEntered.AddListener(OnGrab);
        grab.selectExited.AddListener(OnRelease);



        rb = GetComponent<Rigidbody>();
        if (rb == null) rb = gameObject.AddComponent<Rigidbody>();
        rb.isKinematic = false;
        rb.useGravity = false;

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.spatialBlend = 1f;

        mainCam = Camera.main;

        SetupLaser();
        Reload(GenerateBulletPositions());

        if (muzzlePoint == null) muzzlePoint = transform;
    }

    /// <summary>Handles when player grabs the revolver</summary>
    void OnGrab(SelectEnterEventArgs args)
    {
        isHeld = true;
        UpdateLaser();
    }

    /// <summary>Handles when player releases the revolver</summary>
    void OnRelease(SelectExitEventArgs args)
    {
        isHeld = false;
        currentTargetInSight = null;
        UpdateLaser();
        if (GameManager.Instance.IsPlayerTurn())
        {
           StartCoroutine( GameManager.Instance.SetRevolverBackToPosition(GameManager.Instance.currentPlayerIndex, 3f));
        }
    }

    /// <summary>Randomizes chamber position for new round</summary>
    public void Spin()
    {
        PlaySound(spinClip, spinVolume);
        currentChamber = Random.Range(0, CHAMBERS);
        PlaySound(chamberRotateClip, chamberRotateVolume * 0.5f);

        Events.Publish(new RevolverSpunEvent { NewChamber = currentChamber });
    }

    /// <summary>Handles trigger pull - validates target and initiates shot</summary>
    void OnTriggerPulled(ActivateEventArgs args)
    {
        if (!isHeld || shotThisTurn)
            return;

        // Validate target before allowing shot
        IPlayer targetInSight = GetTargetInSight();
        if (requireTargetToShoot && targetInSight == null)
        {
            PlaySound(invalidTargetClip, invalidTargetVolume);
            Debug.Log("No valid target! Aim at a player or NPC to shoot.");

            // Haptic feedback for invalid target
            if (grab.isSelected)
            {
                var controller = grab.interactorsSelecting[0] as UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor;
                if (controller != null)
                {
                    controller.SendHapticImpulse(0.3f, 0.1f);
                }
            }

            return;
        }

      /*  if (targetInSight != null && IsShootingSelf(targetInSight))
        {
            PlaySound(invalidTargetClip, invalidTargetVolume);
            Debug.Log("Cannot shoot yourself!");

            // Haptic feedback for self-target
            if (grab.isSelected)
            {
                var controller = grab.interactorsSelecting[0] as UnityEngine.XR.Interaction.Toolkit.Interactors.XRBaseInputInteractor;
                if (controller != null)
                {
                    controller.SendHapticImpulse(0.5f, 0.15f);
                }
            }

            return;
        }*/

        shotThisTurn = true;
        PlaySound(hammerCockClip, hammerCockVolume);
        Invoke(nameof(Fire), 0.08f); // Small delay for hammer cock sound
    }

    void Update()
    {
        UpdateTargetCheck();
        UpdateLaser();
        UpdateVRRecoil();

        // Debug visualization
        if (showDebugRay)
        {
            Color debugColor = currentTargetInSight != null ? Color.green : Color.red;
            Debug.DrawRay(muzzlePoint.position, muzzlePoint.forward * raycastDistance, debugColor);
        }
    }

    /// <summary>Continuously checks for valid targets in sight</summary>
    void UpdateTargetCheck()
    {
        if (!isHeld)
        {
            currentTargetInSight = null;
            return;
        }

        currentTargetInSight = GetTargetInSight();
    }

    /// <summary>Checks if currently aiming at a valid living player</summary>
    bool IsAimingAtValidTarget()
    {
        return GetTargetInSight() != null;
    }

    /// <summary>Checks if the target in sight is the current player (self)</summary>
    bool IsShootingSelf(IPlayer targetInSight)
    {
        if (targetInSight == null) return false;

        // Get current player from game manager
        GameManager gameManager = FindObjectOfType<GameManager>();
        if (gameManager == null) return false;

        IReadOnlyList<IPlayer> players = gameManager.Players;
        if (players.Count == 0) return false;

        // Check if this target is the current player by ID
        return targetInSight.ID == gameManager.currentIDsTurn;
    }

    /// <summary>Performs raycast to find IPlayer targets in sight line</summary>
    IPlayer GetTargetInSight()
    {
        if (muzzlePoint == null) return null;

        // Raycast for target detection
        if (Physics.Raycast(muzzlePoint.position, muzzlePoint.forward, out RaycastHit hit, targetCheckDistance, targetLayers))
        {
            // Check hit object and parents for IPlayer component
            IPlayer target = hit.collider.GetComponent<IPlayer>();

            if (target != null && target.IsAlive)
            {
                return target;
            }

            // Search parent hierarchy for IPlayer component
            Transform current = hit.collider.transform;
            while (current != null)
            {
                target = current.GetComponent<IPlayer>();
                if (target != null && target.IsAlive)
                {
                    return target;
                }
                current = current.parent;
            }
        }

        return null;
    }

    /// <summary>Initializes laser sight visual component</summary>
    void SetupLaser()
    {
        laserSight = GetComponent<LineRenderer>();
        if (laserSight == null) laserSight = gameObject.AddComponent<LineRenderer>();

        laserSight.positionCount = 2;
        laserSight.startWidth = laserWidth;
        laserSight.endWidth = laserWidth;

        if (laserMaterial == null)
        {
            Shader sh = Shader.Find("Unlit/Color");
            laserMaterial = new Material(sh);
        }

        laserSight.material = laserMaterial;
    }

    /// <summary>Updates laser sight position and color based on targeting</summary>
    void UpdateLaser()
    {
        if (!showLaserSight || !isHeld || laserSight == null)
        {
            if (laserSight != null) laserSight.enabled = false;
            return;
        }

        laserSight.enabled = true;

        // Color indicates targeting status
        Color currentLaserColor = currentTargetInSight != null ? laserColorValidTarget : laserColorNoTarget;
        laserSight.startColor = currentLaserColor;
        laserSight.endColor = currentLaserColor;

        // Calculate laser endpoint
        Vector3 endPoint;
        if (Physics.Raycast(muzzlePoint.position, muzzlePoint.forward, out RaycastHit hit, raycastDistance, targetLayers))
        {
            endPoint = hit.point;
        }
        else
        {
            endPoint = muzzlePoint.position + muzzlePoint.forward * raycastDistance;
        }

        laserSight.SetPosition(0, muzzlePoint.position);
        laserSight.SetPosition(1, endPoint);
    }

    /// <summary>Main firing method - handles bullet check, effects, and game notification</summary>
    public FireResult Fire()
    {
        Debug.Log($"=== REVOLVER FIRE === Chamber: {currentChamber}, Bullets: [{string.Join(", ", _bulletPositions)}]");
        GameManager.Instance.SetRevolverGrabbable(false);
        // Determine shot result
        bool wasBullet = _bulletPositions.Contains(currentChamber);
        lastShotResult = wasBullet ? FireResult.Bullet : FireResult.Blank;

        if (wasBullet)
        {
            _bulletPositions.Remove(currentChamber);
            PlaySound(gunshotClip, gunshotVolume);
            ApplyRecoil();
        }
        else
        {
            PlaySound(clickClip, clickVolume);
            ApplyRecoilEmpty();
        }

        // Notify via events
        IPlayer target = GetTargetInSight();
        Events.Publish(new RevolverFiredEvent
        {
            Target = target,
            Result = lastShotResult,
            WasHeld = isHeld
        });

        // Advance chamber after processing shot
        AdvanceChamber();
        Invoke(nameof(ResetShotTracking), 0.2f);

        return lastShotResult;
    }

    /// <summary>Alternative fire method with explicit target raycasting</summary>
    public FireResult FireAtTarget()
    {
        bool wasBullet = _bulletPositions.Contains(currentChamber);
        FireResult result = wasBullet ? FireResult.Bullet : FireResult.Blank;

        // Perform target detection raycast
        if (Physics.Raycast(muzzlePoint.position, muzzlePoint.forward, out RaycastHit hit, raycastDistance, targetLayers))
        {
            IPlayer target = hit.collider.GetComponent<IPlayer>();

            // Search parent hierarchy for target
            if (target == null)
            {
                Transform current = hit.collider.transform;
                while (current != null && target == null)
                {
                    target = current.GetComponent<IPlayer>();
                    current = current.parent;
                }
            }

            if (target != null)
            {
                Events.Publish(new RevolverFiredEvent
                {
                    Target = target,
                    Result = result,
                    WasHeld = false
                });
            }
        }

        lastShotResult = result;
        return result;
    }

    /// <summary>Advances to next chamber with sound effect</summary>
    void AdvanceChamber()
    {
        PlaySound(chamberRotateClip, chamberRotateVolume);
        currentChamber = (currentChamber + 1) % CHAMBERS;
    }

    /// <summary>Applies full recoil for live rounds</summary>
    void ApplyRecoil()
    {
        localRecoilOffset += new Vector3(-recoilStrength, recoilStrength * 0.2f, 0f);
        StartCoroutine(CameraShake());
    }

    /// <summary>Applies reduced recoil for blank rounds</summary>
    void ApplyRecoilEmpty()
    {
        localRecoilOffset += new Vector3(-recoilStrength * 0.3f, 0f, 0f);
    }

    /// <summary>Smoothly returns revolver to original position after recoil</summary>
    void UpdateVRRecoil()
    {
        if (!isHeld) return;

        localRecoilOffset = Vector3.SmoothDamp(
            localRecoilOffset,
            Vector3.zero,
            ref localRecoilVelocity,
            1f / recoilReturnSpeed
        );

        transform.localPosition += localRecoilOffset * Time.deltaTime;
    }

    /// <summary>Camera shake effect for bullet shots</summary>
    IEnumerator CameraShake()
    {
        if (mainCam == null) yield break;

        Vector3 original = mainCam.transform.localPosition;
        float t = 0;

        while (t < cameraShakeDuration)
        {
            t += Time.deltaTime;
            float intensity = cameraShakeIntensity * (1f - t / cameraShakeDuration);

            mainCam.transform.localPosition = original + Random.insideUnitSphere * intensity * 0.01f;
            yield return null;
        }

        mainCam.transform.localPosition = original;
    }

    /// <summary>Reloads revolver with new bullet positions</summary>
    public void Reload(IEnumerable<int> bulletPositions)
    {
        PlaySound(reloadClip, reloadVolume);
        _bulletPositions = new List<int>(bulletPositions);
        currentChamber = 0;

        Events.Publish(new RevolverReloadedEvent { BulletPositions = bulletPositions });
    }

    /// <summary>Generates random bullet positions within chambers</summary>
    public List<int> GenerateBulletPositions()
    {
        _bulletPositions.Clear();
        int bulletAmount = Random.Range(1, MaxBullets + 1);

        while (_bulletPositions.Count < bulletAmount)
        {
            int pos = Random.Range(0, CHAMBERS);
            if (!_bulletPositions.Contains(pos))
                _bulletPositions.Add(pos);
        }

        return _bulletPositions;
    }

    /// <summary>Helper method for audio playback</summary>
    private void PlaySound(AudioClip clip, float volume)
    {
        if (clip == null || audioSource == null) return;
        audioSource.PlayOneShot(clip, volume);
    }

    // Public properties and methods
    public void ResetShotTracking() => shotThisTurn = false;
    public FireResult GetLastShotResult() => lastShotResult;
    public int CurrentChamber => currentChamber;
    public int MaxChambers => CHAMBERS;
    public IReadOnlyList<int> BulletPositions => _bulletPositions;
    public bool HasValidTargetInSight => currentTargetInSight != null;
    public IPlayer CurrentTarget => currentTargetInSight;
}