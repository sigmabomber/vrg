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

    [Header("Laser Sight")]
    [SerializeField] private bool showLaserSight = true;
    [SerializeField] private LineRenderer laserSight;
    [SerializeField] private Color laserColor = Color.red;
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
    [SerializeField] private float recoilStrength = 0.1f;          // VR recoil (local transform)
    [SerializeField] private float recoilReturnSpeed = 10f;

    [SerializeField] private float cameraShakeIntensity = 0.5f;
    [SerializeField] private float cameraShakeDuration = 0.2f;

    private XRGrabInteractable grab;
    private bool isHeld;

    private Rigidbody rb;
    private AudioSource audioSource;

    private List<int> _bulletPositions = new List<int>();
    private int currentChamber = 0;

    private GameManager gameManager;
    private Camera mainCam;

    // VR Recoil
    private Vector3 localRecoilOffset;
    private Vector3 localRecoilVelocity;

    // Shot tracking
    private bool shotThisTurn = false;
    private FireResult lastShotResult;

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

        gameManager = FindAnyObjectByType<GameManager>();
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        isHeld = true;
        UpdateLaser();
    }

    void OnRelease(SelectExitEventArgs args)
    {
        isHeld = false;
        UpdateLaser();
    }
    public void Spin()
    {
        PlaySound(spinClip, spinVolume);

        // Randomly rotate to a new chamber
        currentChamber = Random.Range(0, CHAMBERS);

        PlaySound(chamberRotateClip, chamberRotateVolume * 0.5f);
    }

    void OnTriggerPulled(ActivateEventArgs args)
    {
        if (!isHeld || shotThisTurn)
            return;

        shotThisTurn = true;
        PlaySound(hammerCockClip, hammerCockVolume);
        Invoke(nameof(Fire), 0.08f);
    }

    void Update()
    {
        UpdateLaser();
        UpdateVRRecoil();

        if (showDebugRay)
            Debug.DrawRay(muzzlePoint.position, muzzlePoint.forward * raycastDistance, Color.red);
    }

    // ------------------------------
    // LASER
    // ------------------------------
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
        laserSight.startColor = laserColor;
        laserSight.endColor = laserColor;
    }

    void UpdateLaser()
    {
        if (!showLaserSight || !isHeld || laserSight == null)
        {
            if (laserSight != null) laserSight.enabled = false;
            return;
        }

        laserSight.enabled = true;

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

    // ------------------------------
    // FIRING
    // ------------------------------
    public FireResult Fire()
    {
        bool wasBullet = _bulletPositions.Contains(currentChamber);

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

        FireAtTarget();
        AdvanceChamber();

        lastShotResult = wasBullet ? FireResult.Bullet : FireResult.Blank;
        Invoke(nameof(ResetShotTracking), 0.2f);

        return lastShotResult;
    }

    public FireResult FireAtTarget()
    {
        bool wasBullet = _bulletPositions.Contains(currentChamber);
        FireResult result = wasBullet ? FireResult.Bullet : FireResult.Blank;

        // Raycast check
        if (Physics.Raycast(muzzlePoint.position, muzzlePoint.forward, out RaycastHit hit, raycastDistance, targetLayers))
        {
            IPlayer target = hit.collider.GetComponent<IPlayer>();
            if (target != null)
            {
                gameManager?.OnRevolverFired(target);
            }
        }

        lastShotResult = result;
        return result;
    }


    void AdvanceChamber()
    {
        PlaySound(chamberRotateClip, chamberRotateVolume);
        currentChamber = (currentChamber + 1) % CHAMBERS;
    }

    // ------------------------------
    // RECOIL (VR)
    // ------------------------------
    void ApplyRecoil()
    {
        localRecoilOffset += new Vector3(-recoilStrength, recoilStrength * 0.2f, 0f);
        StartCoroutine(CameraShake());
    }

    void ApplyRecoilEmpty()
    {
        localRecoilOffset += new Vector3(-recoilStrength * 0.3f, 0f, 0f);
    }

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

    // ------------------------------
    // CAMERA SHAKE (non-VR fallback)
    // ------------------------------
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

    // ------------------------------
    // UTILS
    // ------------------------------
    public void Reload(IEnumerable<int> bulletPositions)
    {
        PlaySound(reloadClip, reloadVolume);
        _bulletPositions = new List<int>(bulletPositions);
        currentChamber = 0;
    }

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

    private void PlaySound(AudioClip clip, float volume)
    {
        if (clip == null) return;
        audioSource.PlayOneShot(clip, volume);
    }

    public void ResetShotTracking() => shotThisTurn = false;

    public FireResult GetLastShotResult() => lastShotResult;

    public int CurrentChamber => currentChamber;
    public int MaxChambers => CHAMBERS;
    public IReadOnlyList<int> BulletPositions => _bulletPositions;
}
