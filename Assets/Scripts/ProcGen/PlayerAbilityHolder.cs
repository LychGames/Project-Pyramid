using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class PlayerAbilityHolder : MonoBehaviour
{
    public PlayerAbility equippedAbility;
    [Tooltip("Seconds to play the necklace gesture before firing the ability")]
    public float preActivateSeconds = 0.4f;

    // Optional hooks: wire these to Animator/hand raise, SFX, vignette, etc.
    public UnityEvent onPreActivateStart;
    public UnityEvent onPreActivateEnd;

    bool isWindingUp;

    void Start()
    {
        if (equippedAbility) equippedAbility.Initialize(gameObject);
    }

    void Update()
    {
        if (!equippedAbility || isWindingUp) return;
        if (Input.GetKeyDown(equippedAbility.activationKey))
            StartCoroutine(WindupThenActivate());
    }

    IEnumerator WindupThenActivate()
    {
        isWindingUp = true;
        onPreActivateStart?.Invoke();              // e.g., play “raise necklace” animation
        yield return new WaitForSeconds(preActivateSeconds);
        onPreActivateEnd?.Invoke();                // e.g., end pose / flash
        equippedAbility.Activate(gameObject);
        isWindingUp = false;
    }
}
