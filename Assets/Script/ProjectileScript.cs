using System;
using UnityEngine;

namespace RD.Core
{
    [RequireComponent(typeof(Rigidbody))]
    public class ProjectileScript : MonoBehaviour
    {
        [SerializeField] private float lifetime = 10f;

        private float timer;
        private bool isReturning = true; // inactive until fired
        private Rigidbody rb;

        public event Action<ProjectileScript> ReturnToPool;
        public Rigidbody Rigidbody => rb;

        void Awake()
        {
            rb = GetComponent<Rigidbody>();
        }

        void Update()
        {
            if (isReturning) return;

            timer -= Time.deltaTime;
            if (timer <= 0f) ReturnProjectile();
        }

        void OnCollisionEnter(Collision _)
        {
            ReturnProjectile();
        }

        public void Launch(Vector3 position, Quaternion rotation, Vector3 impulse)
        {
            transform.SetPositionAndRotation(position, rotation);

            // Unity 6: linearVelocity. On older versions use rb.velocity.
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.AddForce(impulse, ForceMode.Impulse);

            timer = lifetime;
            isReturning = false;
        }

        private void ReturnProjectile()
        {
            if (isReturning) return;
            isReturning = true;
            ReturnToPool?.Invoke(this);
        }
    }
}