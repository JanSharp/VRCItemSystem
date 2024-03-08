using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.Udon;

namespace JanSharp
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class ItemDespawner : UdonSharpBehaviour
    {
        private void OnTriggerEnter(Collider other)
        {
            ItemSync item = other.GetComponent<ItemSync>();
            if (item == null || !item.LocalPlayerIsInControl)
                return;
            item.Despawn();
        }
    }
}
