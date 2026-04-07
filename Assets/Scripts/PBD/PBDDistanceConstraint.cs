using UnityEngine;

/// <summary>
/// PBD 距离约束
/// </summary>
public class PBDDistanceConstraint : MonoBehaviour
{
    public PBDPoint pointA;
    public PBDPoint pointB;

    public float restLength = 1f;
    [Range(0f, 1f)]
    public float stiffness = 1f;

    /// <summary>
    /// 初始化约束参数
    /// </summary>
    public void Init(PBDPoint a, PBDPoint b, float restLen, float stiff)
    {
        pointA = a;
        pointB = b;
        restLength = restLen;
        stiffness = stiff;
    }

    /// <summary>
    /// 求解距离约束
    /// </summary>
    public void Solve()
    {
        if (pointA == null || pointB == null) return;

        float w1 = pointA.invMass;
        float w2 = pointB.invMass;
        float wSum = w1 + w2;

        if (wSum <= Mathf.Epsilon) return;

        Vector3 delta = pointB.predictedPosition - pointA.predictedPosition;
        float len = delta.magnitude;

        if (len <= 1e-6f) return;

        float diff = (len - restLength) / len;
        Vector3 correction = stiffness * diff * delta;

        if (!pointA.isFixed && w1 > 0f)
        {
            pointA.predictedPosition += correction * (w1 / wSum);
        }

        if (!pointB.isFixed && w2 > 0f)
        {
            pointB.predictedPosition -= correction * (w2 / wSum);
        }
    }
}