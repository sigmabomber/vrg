using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

public class Revolver : MonoBehaviour, IRevolverMechanic
{
    [Header("Raycast Settings")]
    public Transform muzzlePoint;
    public float raycastDistance = 100f;
    public LayerMask targetLayers;
    public bool showDebugRay = true;

    [Header("Laser Sight Settings")]
    [SerializeField] private bool showLaserSight = true;
    [SerializeField] private LineRenderer laserSight;
    [SerializeField] private Color laserColor = Color.red;
    [SerializeField] private float laserWidth = 0.01f;
    [SerializeField] private Material laserMaterial;

    [Header("Revolver Settings")]
    public int MaxBullets = 3;
    private const int CHAMBERS = 6;

    [Header("SFX - Audio Clips")]
    [SerializeField] private AudioClip gunshotClip;
    [SerializeField] private AudioClip clickClip;
    [SerializeField] private AudioClip reloadClip;
    [SerializeField] private AudioClip spinClip;
    [SerializeField] private AudioClip hammerCockClip;
    [SerializeField] private AudioClip chamberRotateClip;

    [Header("SFX - Settings")]
    [SerializeField] private float gunshotVolume = 1.0f;
    [SerializeField] private float clickVolume = 0.8f;
    [SerializeField] private float reloadVolume = 0.7f;
    [SerializeField] private float spinVolume = 0.8f;
    [SerializeField] private float hammerCockVolume = 0.5f;
    [SerializeField] private float chamberRotateVolume = 0.3f;

    [Header("Recoil & Shake Settings")]
    [SerializeField] private float recoilForce = 2f;
    [SerializeField] private float recoilDuration = 0.1f;
    [SerializeField] private float screenShakeIntensity = 0.5f;
    [SerializeField] private float screenShakeDuration = 0.3f;
    [SerializeField] private float emptyRecoilForce = 0.5f;
    [SerializeField] private float emptyScreenShakeIntensity = 0.1f;

    [Header("XR")]
    private XRGrabInteractable grabInteractable;
    private bool isHeld = false;
    private bool triggerPressed = false;
    private bool shotThisTurn = false;

    [Header("Game Manager")]
    private GameManager gameManager;

    private List<int> _bulletPositions = new List<int>();
    private int currentChamber = 0;
    private AudioSource audioSource;
    private Rigidbody rb;
    private Vector3 originalPosition;
    private Quaternion originalRotation;
    private bool isRecoiling = false;

    private Camera mainCamera;
    private Vector3 cameraOriginalPosition;
    private bool isScreenShaking = false;
    private FireResult lastShotResult;

    public int CurrentChamber => currentChamber;
    public int MaxChambers => CHAMBERS;
    public IReadOnlyList<int> BulletPositions => _bulletPositions;

    void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        grabInteractable.activated.AddListener(OnTriggerPull);
        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);

        rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            rb = gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = false;
            rb.useGravity = false;
        }

        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1.0f;
        audioSource.maxDistance = 10f;
        audioSource.rolloffMode = AudioRolloffMode.Logarithmic;

        originalPosition = transform.localPosition;
        originalRotation = transform.localRotation;

        mainCamera = Camera.main;
        if (mainCamera != null)
        {
            cameraOriginalPosition = mainCamera.transform.localPosition;
        }

        InitializeLaserSight();
        Reload(GenerateBulletPositions());

        if (muzzlePoint == null)
        {
            muzzlePoint = transform;
            Debug.LogWarning("Muzzle point not assigned! Using revolver transform.");
        }

        gameManager = FindAnyObjectByType<GameManager>();
        if (gameManager == null) Debug.LogWarning("Game Manager not found!");
    }

    void InitializeLaserSight()
    {
        laserSight = GetComponent<LineRenderer>();
        if (laserSight == null)
        {
            laserSight = gameObject.AddComponent<LineRenderer>();
        }

        laserSight.positionCount = 2;
        laserSight.startWidth = laserWidth;
        laserSight.endWidth = laserWidth;
        laserSight.material = laserMaterial != null ? laserMaterial : CreateDefaultLaserMaterial();
        laserSight.startColor = laserColor;
        laserSight.endColor = laserColor;
        laserSight.enabled = showLaserSight && isHeld;
        laserSight.useWorldSpace = true;
    }

    Material CreateDefaultLaserMaterial()
    {
        Shader unlitShader = Shader.Find("Unlit/Color");
        if (unlitShader != null)
        {
            Material mat = new Material(unlitShader);
            mat.color = laserColor;
            return mat;
        }
        return new Material(Shader.Find("Sprites/Default"));
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        isHeld = true;
        UpdateLaserSight();
    }

    void OnRelease(SelectExitEventArgs args)
    {
        isHeld = false;
        triggerPressed = false;
        UpdateLaserSight();
    }

    void OnTriggerPull(ActivateEventArgs args)
    {
        if (!isHeld || triggerPressed || shotThisTurn || isRecoiling) return;
        triggerPressed = true;
        PlaySound(hammerCockClip, hammerCockVolume);
        Invoke(nameof(Fire), 0.1f);
    }

    void Update()
    {
        if (showDebugRay && muzzlePoint != null)
        {
            Debug.DrawRay(muzzlePoint.position, muzzlePoint.forward * raycastDistance, Color.red);
        }
        UpdateLaserSight();
        if (isScreenShaking && mainCamera != null)
        {
            HandleScreenShake();
        }
    }

    void UpdateLaserSight()
    {
        if (laserSight == null || muzzlePoint == null) return;
        bool shouldShowLaser = showLaserSight && isHeld;
        laserSight.enabled = shouldShowLaser;
        if (!shouldShowLaser) return;

        RaycastHit hit;
        Vector3 endPoint;

        if (Physics.Raycast(muzzlePoint.position, muzzlePoint.forward, out hit, raycastDistance, targetLayers))
        {
            endPoint = hit.point;
            IPlayer player = hit.collider.GetComponent<IPlayer>();
            if (player != null && player.IsAlive)
            {
                laserSight.startColor = Color.red;
                laserSight.endColor = Color.red;
            }
            else
            {
                laserSight.startColor = new Color(1f, 0f, 0f, 0.5f);
                laserSight.endColor = new Color(1f, 0f, 0f, 0.5f);
            }
        }
        else
        {
            endPoint = muzzlePoint.position + muzzlePoint.forward * raycastDistance;
            laserSight.startColor = laserColor;
            laserSight.endColor = laserColor;
        }

        laserSight.SetPosition(0, muzzlePoint.position);
        laserSight.SetPosition(1, endPoint);
    }

    void FixedUpdate()
    {
        if (isRecoiling)
        {
            HandleRecoilRecovery();
        }
    }

    public FireResult Fire()
    {
        shotThisTurn = true;
        if (laserSight != null)
        {
            laserSight.enabled = false;
            Invoke(nameof(ShowLaserAfterShot), 0.5f);
        }

        if (IsAimingAtValidTarget(out GameObject targetObject))
        {
            FireResult result = FireAtTarget(targetObject);
            lastShotResult = result;
            return result;
        }
        else
        {
            PlaySound(clickClip, clickVolume);
            TriggerRecoil(false);
            shotThisTurn = false;
            lastShotResult = FireResult.Blank;
            return FireResult.Blank;
        }
    }

    void ShowLaserAfterShot()
    {
        if (laserSight != null && isHeld && showLaserSight)
        {
            laserSight.enabled = true;
        }
    }

    public FireResult FireAtTarget(IPlayer target)
    {
        if (shotThisTurn) return FireResult.Blank;
        shotThisTurn = true;
        if (target == null || !target.IsAlive) { shotThisTurn = false; return FireResult.Blank; }

        bool wasBullet = _bulletPositions.Contains(currentChamber);
        if (wasBullet)
        {
            _bulletPositions.Remove(currentChamber);
            PlaySound(gunshotClip, gunshotVolume);
            TriggerRecoil(true);
            gameManager?.OnRevolverFired(target);
        }
        else
        {
            PlaySound(clickClip, clickVolume);
            TriggerRecoil(false);
            gameManager?.OnRevolverFired(target);
        }

        AdvanceChamber();
        FireResult result = wasBullet ? FireResult.Bullet : FireResult.Blank;
        lastShotResult = result;
        return result;
    }

    private FireResult FireAtTarget(GameObject targetObject)
    {
        bool wasBullet = _bulletPositions.Contains(currentChamber);
        IPlayer target = targetObject.GetComponent<IPlayer>() ?? targetObject.GetComponent<AI>() as IPlayer ?? targetObject.GetComponent<Player>() as IPlayer;
        if (target == null) { shotThisTurn = false; return FireResult.Blank; }

        if (wasBullet)
        {
            _bulletPositions.Remove(currentChamber);
            PlaySound(gunshotClip, gunshotVolume);
            TriggerRecoil(true);
            gameManager?.OnRevolverFired(target);
        }
        else
        {
            PlaySound(clickClip, clickVolume);
            TriggerRecoil(false);
            gameManager?.OnRevolverFired(target);
        }

        AdvanceChamber();
        FireResult result = wasBullet ? FireResult.Bullet : FireResult.Blank;
        lastShotResult = result;
        return result;
    }

    public FireResult GetLastShotResult() => lastShotResult;

    private void TriggerRecoil(bool isBulletShot)
    {
        float force = isBulletShot ? recoilForce : emptyRecoilForce;
        float shakeIntensity = isBulletShot ? screenShakeIntensity : emptyScreenShakeIntensity;

        if (rb != null && isHeld)
        {
            Vector3 recoilDirection = -transform.forward + transform.up * 0.3f;
            rb.AddForce(recoilDirection * force, ForceMode.Impulse);

            Vector3 torque = new Vector3(
                Random.Range(-force * 0.5f, -force * 1f),
                Random.Range(-force * 0.2f, force * 0.2f),
                Random.Range(-force * 0.1f, force * 0.1f)
            );
            rb.AddTorque(torque, ForceMode.Impulse);
        }

        StartCoroutine(RecoilAnimation(force, isBulletShot));
        if (isBulletShot && mainCamera != null)
        {
            StartScreenShake(shakeIntensity, screenShakeDuration);
        }
    }

    private IEnumerator RecoilAnimation(float force, bool isBulletShot)
    {
        isRecoiling = true;
        float elapsed = 0f;
        float duration = isBulletShot ? recoilDuration : recoilDuration * 0.5f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            yield return null;
        }
        isRecoiling = false;
    }

    private void HandleRecoilRecovery()
    {
        if (rb != null)
        {
            rb.linearVelocity = Vector3.Lerp(rb.linearVelocity, Vector3.zero, Time.deltaTime * 5f);
            rb.angularVelocity = Vector3.Lerp(rb.angularVelocity, Vector3.zero, Time.deltaTime * 5f);
        }
    }

    private void StartScreenShake(float intensity, float duration)
    {
        if (isScreenShaking) return;
        isScreenShaking = true;
        StartCoroutine(ScreenShakeCoroutine(intensity, duration));
    }

    private IEnumerator ScreenShakeCoroutine(float intensity, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float currentIntensity = intensity * (1f - (elapsed / duration));
            if (mainCamera != null)
            {
                Vector3 shakeOffset = new Vector3(
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f),
                    Random.Range(-1f, 1f)
                ) * currentIntensity * 0.01f;
                mainCamera.transform.localPosition = cameraOriginalPosition + shakeOffset;
            }
            yield return null;
        }
        if (mainCamera != null) mainCamera.transform.localPosition = cameraOriginalPosition;
        isScreenShaking = false;
    }

    private void HandleScreenShake() { }

    public void ResetShotTracking() => shotThisTurn = false;

    public bool IsAimingAtValidTarget(out GameObject target)
    {
        target = null;
        if (Physics.Raycast(muzzlePoint.position, muzzlePoint.forward, out RaycastHit hit, raycastDistance, targetLayers))
        {
            IPlayer player = hit.collider.GetComponent<IPlayer>();
            if (player != null)
            {
                target = hit.collider.gameObject;
                return true;
            }
        }
        return false;
    }

    public IPlayer GetAimedTarget()
    {
        if (IsAimingAtValidTarget(out GameObject target))
            return target.GetComponent<IPlayer>();
        return null;
    }

    private void AdvanceChamber()
    {
        PlaySound(chamberRotateClip, chamberRotateVolume);
        currentChamber = (currentChamber + 1) % CHAMBERS;
    }

    public List<int> GenerateBulletPositions()
    {
        _bulletPositions.Clear();
        int bulletAmount = Random.Range(1, MaxBullets + 1);
        while (_bulletPositions.Count < bulletAmount)
        {
            int pos = Random.Range(0, CHAMBERS);
            if (!_bulletPositions.Contains(pos)) _bulletPositions.Add(pos);
        }
        return _bulletPositions;
    }

    public void Spin()
    {
        PlaySound(spinClip, spinVolume);
        currentChamber = Random.Range(0, CHAMBERS);
        PlaySound(chamberRotateClip, chamberRotateVolume * 0.5f);
    }

    public void Reload(IEnumerable<int> newBulletPositions)
    {
        PlaySound(reloadClip, reloadVolume);
        _bulletPositions = new List<int>(newBulletPositions);
        currentChamber = 0;
    }

    private void PlaySound(AudioClip clip, float volume = 1.0f)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.PlayOneShot(clip, volume);
        }
    }

    public void ToggleLaserSight(bool enabled)
    {
        showLaserSight = enabled;
        if (laserSight != null)
        {
            laserSight.enabled = enabled && isHeld;
        }
    }

    public void SetLaserColor(Color color)
    {
        laserColor = color;
        if (laserSight != null)
        {
            laserSight.startColor = color;
            laserSight.endColor = color;
        }
    }

    private void OnDrawGizmos()
    {
        if (muzzlePoint == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawLine(muzzlePoint.position, muzzlePoint.position + muzzlePoint.forward * raycastDistance);
        Gizmos.DrawSphere(muzzlePoint.position + muzzlePoint.forward * raycastDistance, 0.01f);
    }
}
