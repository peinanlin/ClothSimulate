using UnityEngine;

/// <summary>
/// PBD 质点
/// </summary>
public class PBDPoint : MonoBehaviour
{
    [HideInInspector] public Vector3 position;
    [HideInInspector] public Vector3 prevPosition;
    [HideInInspector] public Vector3 predictedPosition;
    [HideInInspector] public Vector3 velocity;

    [HideInInspector] public float invMass = 1f;
    [HideInInspector] public bool isFixed = false;

    [HideInInspector] public float drag = 0.01f;

    private Vector3 _externalForce;
    private Vector3 _randomForce;

    private Renderer _renderer;

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
        SyncFromTransform();
    }

    public void SyncFromTransform()
    {
        position = transform.localPosition;
        prevPosition = position;
        predictedPosition = position;
        velocity = Vector3.zero;
    }

    public void SetExternalForce(Vector3 force)
    {
        _externalForce = force;
    }

    public void SetRandomForce(Vector3 force)
    {
        _randomForce = force;
    }

    public Vector3 GetTotalForce()
    {
        return _externalForce + _randomForce;
    }

    public void SetColor(Color color)
    {
        if (_renderer == null) _renderer = GetComponent<Renderer>();
        if (_renderer != null && _renderer.material != null)
        {
            _renderer.material.color = color;
        }
    }

    public void UpdateView(float massSize, bool showMass)
    {
        if (isFixed)
        {
            transform.localScale = Vector3.one * 0.05f;
            gameObject.SetActive(true);
            SetColor(Color.green);
        }
        else
        {
            transform.localScale = Vector3.one * massSize;
            gameObject.SetActive(showMass);
            SetColor(Color.black);
        }
    }
}