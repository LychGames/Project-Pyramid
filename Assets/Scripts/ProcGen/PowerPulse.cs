using UnityEngine;

[CreateAssetMenu(menuName = "Abilities/Power Pulse")]
public class PowerPulse : PlayerAbility
{
    public float radius = 5f;

    public override void Activate(GameObject player)
    {
        Debug.Log($"{abilityName} activated!");
        Collider[] hits = Physics.OverlapSphere(player.transform.position, radius);
        foreach (var hit in hits)
        {
            Debug.Log($"Pulse hit: {hit.name}");
        }
    }
}
