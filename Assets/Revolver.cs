using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public class Revolver : MonoBehaviour, IRevolverMechanic
{
    [Header("Gun FX")]
    public ParticleSystem muzzleFlash;
    public AudioSource gunshotSound;

    [Header("Revolver Settings")]
    public int MaxBullets = 3;
    private const int CHAMBERS = 6;

    [Header("XR")]
    public float triggerThreshold = 0.9f;

    private XRGrabInteractable grabInteractable;
    private ActionBasedController controller;  
    private bool isHeld = false;
    private bool triggerPressed = false;

    private List<int> _bulletPositions = new List<int>();
    private int currentChamber = 0;

    public int CurrentChamber => currentChamber;
    public int MaxChambers => CHAMBERS;
    public IReadOnlyList<int> BulletPositions => _bulletPositions;

    void Awake()
    {
        grabInteractable = GetComponent<XRGrabInteractable>();
        grabInteractable.selectEntered.AddListener(OnGrab);
        grabInteractable.selectExited.AddListener(OnRelease);

        Reload(GenerateBulletPositions());
    }

    void OnGrab(SelectEnterEventArgs args)
    {
        isHeld = true;
        Debug.Log("GRABBED: " + args.interactorObject);
        if (args.interactorObject.transform.TryGetComponent(out ActionBasedController ctrl))
        {
            controller = ctrl;
            controller.activateAction.action.performed += OnTriggerDown;
            controller.activateAction.action.canceled += OnTriggerUp;
        }
    }

    void OnRelease(SelectExitEventArgs args)
    {
        isHeld = false;
        triggerPressed = false;

        if (controller != null)
        {
            controller.activateAction.action.performed -= OnTriggerDown;
            controller.activateAction.action.canceled -= OnTriggerUp;
        }

        controller = null;
    }

    private void OnTriggerDown(InputAction.CallbackContext ctx)
    {
        if (!isHeld || triggerPressed) return;

        triggerPressed = true;
        Fire();
    }

    private void OnTriggerUp(InputAction.CallbackContext ctx)
    {
        triggerPressed = false;
    }


    public FireResult Fire()
    {
        bool wasBullet = _bulletPositions.Contains(currentChamber);

        if (wasBullet)
        {
            _bulletPositions.Remove(currentChamber);
            muzzleFlash?.Play();
            gunshotSound?.Play();
        }

        AdvanceChamber();
        Debug.Log($"FIRE: Chamber {currentChamber}, Bullet: {wasBullet}");
        return wasBullet ? FireResult.Bullet : FireResult.Blank;
    }

    private void AdvanceChamber()
    {
        currentChamber = (currentChamber + 1) % CHAMBERS;
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

    public void Spin()
    {
        currentChamber = Random.Range(0, CHAMBERS);
    }

    public void Reload(IEnumerable<int> newBulletPositions)
    {
        _bulletPositions = new List<int>(newBulletPositions);
        currentChamber = 0;
    }
}
