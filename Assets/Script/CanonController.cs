using UnityEngine;
using UnityEngine.Pool;

namespace RD.Core
{
    public class CannonController : MonoBehaviour
    {
        [Header("Canon Config")]
        [SerializeField] private GameObject canonObject;
        [SerializeField] private Transform shootingPos;
        [SerializeField] private float shootForce = 3f;

        [Header("Object Pool Config")]
        [SerializeField] private Transform projectilePoolParent;
        [SerializeField] private int poolSize = 10;

        [Header("Projectile")]
        [SerializeField] private ProjectileScript projectilePrefab;

        private ObjectPool<ProjectileScript> objectPool;

        void OnEnable()  => GameEvent.OnBasePlacement += HandleCanonAwake;
        void OnDisable() => GameEvent.OnBasePlacement -= HandleCanonAwake;

        private void HandleCanonAwake(Vector3 pos)
        {
            canonObject.SetActive(true);
            CreatePool();
        }

        public void ShootProjectile()
        {
            if (objectPool == null) return;

            ProjectileScript p = objectPool.Get();
            p.Launch(shootingPos.position, shootingPos.rotation,
                     -shootingPos.up * shootForce);
        }

        #region Object Pool
        private void CreatePool()
        {
            objectPool?.Dispose(); // clears and destroys old instances via OnDestroyObject

            objectPool = new ObjectPool<ProjectileScript>(
                CreatePooledObject,
                OnTakeFromPool,
                OnReturnToPool,
                OnDestroyObject,
                collectionCheck: true,
                defaultCapacity: poolSize,
                maxSize: poolSize * 4
            );

            // Prewarm
            var buffer = new ProjectileScript[poolSize];
            for (int i = 0; i < poolSize; i++) buffer[i] = objectPool.Get();
            for (int i = 0; i < poolSize; i++) objectPool.Release(buffer[i]);
        }

        private ProjectileScript CreatePooledObject()
        {
            var p = Instantiate(projectilePrefab, projectilePoolParent);
            p.ReturnToPool += ReturnObjectToPool;
            p.gameObject.SetActive(false);
            return p;
        }

        private void ReturnObjectToPool(ProjectileScript p) => objectPool.Release(p);
        private void OnTakeFromPool(ProjectileScript p)     => p.gameObject.SetActive(true);
        private void OnReturnToPool(ProjectileScript p)     => p.gameObject.SetActive(false);

        private void OnDestroyObject(ProjectileScript p)
        {
            if (p != null) Destroy(p.gameObject);
        }
        #endregion
    }
}