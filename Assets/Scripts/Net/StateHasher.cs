using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace Net
{
    // Simple state hasher: hash positions/rotations of replicated ids once per second.
    public class StateHasher : MonoBehaviour
    {
        public float interval = 1f;
        float timer;

        void Update()
        {
            timer += Time.deltaTime;
            if (timer >= interval)
            {
                timer = 0f;
                string h = ComputeHash();
                Debug.Log($"[StateHash] {h}");
                // In a real net stack, send to peers and compare for desync detection
            }
        }

        string ComputeHash()
        {
            var sb = new StringBuilder(1024);
            var objs = FindObjectsOfType<ReplicatedId>();
            System.Array.Sort(objs, (a,b) => a.netId.CompareTo(b.netId));
            for (int i = 0; i < objs.Length; i++)
            {
                var t = objs[i].transform;
                sb.Append(objs[i].netId).Append('|');
                sb.Append(t.position.x.ToString("F2")).Append(',');
                sb.Append(t.position.y.ToString("F2")).Append(',');
                sb.Append(t.position.z.ToString("F2")).Append('|');
                sb.Append(t.eulerAngles.y.ToString("F1")).Append(';');
            }
            using var md5 = MD5.Create();
            var bytes = Encoding.UTF8.GetBytes(sb.ToString());
            var hash = md5.ComputeHash(bytes);
            var hex = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2"));
            return hex.ToString();
        }
    }
}


