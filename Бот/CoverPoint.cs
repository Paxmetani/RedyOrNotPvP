using UnityEngine;

/// <summary>
/// Точка укрытия для тактического AI
/// </summary>
public class CoverPoint : MonoBehaviour
{
    [SerializeField] private float coverHeight = 1.2f;
    [SerializeField] private bool isHighCover = true;
    [SerializeField] private float coverQuality = 1f; // 0-1

    public bool IsOccupied { get; private set; }
    public bool IsHighCover => isHighCover;
    public float CoverQuality => coverQuality;

    public void Occupy()
    {
        IsOccupied = true;
    }

    public void Release()
    {
        IsOccupied = false;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = IsOccupied ? Color.red : Color.green;
        Gizmos.DrawWireCube(
            transform.position + Vector3.up * coverHeight / 2f,
            new Vector3(1f, coverHeight, 0.5f)
        );

        // Show cover direction
        Gizmos.color = Color.blue;
        Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2f);
    }
}