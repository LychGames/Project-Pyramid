using UnityEngine;

public abstract class PlayerAbility : ScriptableObject
{
    public string abilityName = "New Ability";
    public KeyCode activationKey = KeyCode.Q;

    public virtual void Initialize(GameObject player) { }
    public virtual void OnUpdate(GameObject player) { }
    public virtual void Activate(GameObject player) { }
}
