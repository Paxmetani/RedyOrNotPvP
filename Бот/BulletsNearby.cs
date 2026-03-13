using UnityEngine;

/// <summary>
/// Компонент для обнаружения пролетающих мимо пуль
/// Добавьте к пулям или используйте в Gun.cs
/// </summary>
public class BulletDetection : MonoBehaviour
{
    [SerializeField] private float detectionRadius = 3f;
    [SerializeField] private LayerMask enemyLayer;

    private Vector3 lastPosition;
    private Collider[] detectionBuffer = new Collider[10];

    private void Start()
    {
        lastPosition = transform.position;
    }

    private void Update()
    {
        Vector3 currentPosition = transform.position;
        Vector3 direction = (currentPosition - lastPosition).normalized;

        // Check for nearby enemies
        int numEnemies = Physics.OverlapSphereNonAlloc(
            currentPosition,
            detectionRadius,
            detectionBuffer,
            enemyLayer
        );

        for (int i = 0; i < numEnemies; i++)
        {

        }

        lastPosition = currentPosition;
    }
}