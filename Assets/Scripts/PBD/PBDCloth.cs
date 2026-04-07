using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

[Serializable]
public struct RandomForceRange
{
    public Vector2 RangeX;
    public Vector2 RangeY;
    public Vector2 RangeZ;
}

/// <summary>
/// PBD 布料
/// 说明：
/// 1. 思路尽量与当前 MassSpringCloth 保持一致
/// 2. 但核心模拟已改成 PBD：预测位置 -> 约束投影 -> 碰撞修正 -> 回写速度
/// </summary>
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class PBDCloth : MonoBehaviour
{
    [Header("PBD 迭代参数")]
    [Range(1, 64)]
    public int looper = 12; // 约束迭代次数

    [Range(2, 64)]
    public int massCount = 10;

    [Header("布料参数")]
    public float restLen = 0.3f;
    public float clothMass = 1f;
    public float massSize = 0.05f;
    public bool showMass = true;

    [Header("约束刚度")]
    [Range(0f, 1f)]
    public float structuralStiffness = 1.0f;
    [Range(0f, 1f)]
    public float shearStiffness = 0.9f;
    [Range(0f, 1f)]
    public float bendStiffness = 0.6f;

    [Header("运动参数")]
    [Tooltip("速度阻尼，越大衰减越明显")]
    [Range(0f, 10f)]
    public float drag = 0.2f;

    [Tooltip("重力")]
    public Vector3 gravity = new Vector3(0, -9.8f, 0);

    [Header("外力")]
    [Tooltip("恒定外力")]
    public Vector3 externalForce;

    [Tooltip("随机风力范围")]
    public RandomForceRange randomForce;

    [Tooltip("随机力刷新间隔")]
    public float randomInterval = 0.2f;

    [Header("碰撞")]
    [Tooltip("球体碰撞体，可拖场景中的球")]
    public SphereCollider sphereCollider;

    [Tooltip("是否启用地面碰撞")]
    public bool enableGround = true;

    [Tooltip("地面世界高度 y")]
    public float groundY = 0f;

    [Header("布料材质")]
    public Material clothMat;

    // 数据
    private PBDPoint[,] allPoints;
    private List<PBDDistanceConstraint> allConstraints;

    // Mesh
    private Mesh clothMesh;
    private Vector3[] _vertices;
    private Vector2[] _uv;
    private int[] _triangles;

    // 随机风
    private float randomIntervalCount = 0f;
    private Vector3 currentRandomForce = Vector3.zero;

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;

    private void Start()
    {
        _meshFilter = GetComponent<MeshFilter>();
        _meshRenderer = GetComponent<MeshRenderer>();

        clothMesh = new Mesh();
        clothMesh.name = "PBD Cloth Mesh";
        _meshFilter.sharedMesh = clothMesh;

        if (clothMat != null)
        {
            _meshRenderer.sharedMaterial = clothMat;
        }

        _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        _meshRenderer.receiveShadows = true;

        InitPointList();
        InitConstraints();
        CreateMesh();
    }

    private void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 0.033f);
        if (dt <= 0f) return;

        UpdateWindForce();
        PredictPositions(dt);
        SolveConstraints();
        SolveCollisions();
        FinalizePositions(dt);
        UpdateMeshVertices();
        UpdatePointView();
    }

    #region 初始化

    private void InitPointList()
    {
        allPoints = new PBDPoint[massCount, massCount];
        float pointMass = clothMass / (massCount * massCount);
        float invMass = pointMass > 1e-6f ? 1f / pointMass : 0f;

        for (int i = 0; i < massCount; i++)
        {
            for (int j = 0; j < massCount; j++)
            {
                GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                obj.name = $"PBDPoint_{i}_{j}";
                obj.transform.SetParent(transform);
                obj.transform.localScale = Vector3.one * massSize;
                obj.transform.localPosition = new Vector3(i * restLen, -j * restLen, 0f);

                Collider col = obj.GetComponent<Collider>();
                if (col != null) Destroy(col);

                PBDPoint point = obj.AddComponent<PBDPoint>();
                point.invMass = invMass;
                point.drag = drag;
                point.SetExternalForce(externalForce);
                point.SetRandomForce(Vector3.zero);
                point.SyncFromTransform();

                allPoints[i, j] = point;
            }
        }

        // 默认固定顶部两个点，保持和你当前项目一致
        allPoints[0, 0].isFixed = true;
        allPoints[0, 0].invMass = 0f;

        allPoints[massCount - 1, 0].isFixed = true;
        allPoints[massCount - 1, 0].invMass = 0f;
        allPoints[massCount - 1, 0].transform.localPosition += new Vector3(-0.3f, 0f, 0.1f);
        allPoints[massCount - 1, 0].SyncFromTransform();
    }

    private void InitConstraints()
    {
        allConstraints = new List<PBDDistanceConstraint>();

        for (int i = 0; i < massCount; i++)
        {
            for (int j = 0; j < massCount; j++)
            {
                // 横向结构约束
                if (i < massCount - 1)
                {
                    CreateConstraint(i, j, i + 1, j, restLen, structuralStiffness);
                }

                // 纵向结构约束
                if (j < massCount - 1)
                {
                    CreateConstraint(i, j, i, j + 1, restLen, structuralStiffness);
                }

                // 斜向剪切约束
                if (i < massCount - 1 && j < massCount - 1)
                {
                    CreateConstraint(i, j, i + 1, j + 1, restLen * Mathf.Sqrt(2f), shearStiffness);
                }

                if (i < massCount - 1 && j > 0)
                {
                    CreateConstraint(i, j, i + 1, j - 1, restLen * Mathf.Sqrt(2f), shearStiffness);
                }

                // 抗弯约束：隔一个点
                if (i < massCount - 2)
                {
                    CreateConstraint(i, j, i + 2, j, restLen * 2f, bendStiffness);
                }

                if (j < massCount - 2)
                {
                    CreateConstraint(i, j, i, j + 2, restLen * 2f, bendStiffness);
                }
            }
        }
    }

    private PBDDistanceConstraint CreateConstraint(int ai, int aj, int bi, int bj, float restLength, float stiffness)
    {
        PBDDistanceConstraint c = allPoints[ai, aj].gameObject.AddComponent<PBDDistanceConstraint>();
        c.Init(allPoints[ai, aj], allPoints[bi, bj], restLength, stiffness);
        allConstraints.Add(c);
        return c;
    }

    private void CreateMesh()
    {
        _vertices = new Vector3[massCount * massCount];
        _uv = new Vector2[_vertices.Length];

        int index = 0;
        for (int i = 0; i < massCount; i++)
        {
            for (int j = 0; j < massCount; j++)
            {
                _vertices[index] = allPoints[i, j].transform.localPosition;
                _uv[index] = new Vector2((float)i / (massCount - 1), (float)j / (massCount - 1));
                index++;
            }
        }

        _triangles = new int[6 * (massCount - 1) * (massCount - 1)];

        int ti = 0;
        int vi = 0;
        for (int i = 0; i < massCount - 1; i++)
        {
            for (int j = 0; j < massCount - 1; j++)
            {
                _triangles[ti] = _triangles[ti + 3] = vi;
                _triangles[ti + 1] = _triangles[ti + 5] = vi + massCount + 1;
                _triangles[ti + 2] = vi + 1;
                _triangles[ti + 4] = vi + massCount;

                ti += 6;
                vi++;
            }
            vi++;
        }

        clothMesh.vertices = _vertices;
        clothMesh.triangles = _triangles;
        clothMesh.uv = _uv;
        clothMesh.RecalculateNormals();
        clothMesh.RecalculateTangents();
        clothMesh.RecalculateBounds();
    }

    #endregion

    #region 模拟

    /// <summary>
    /// 更新随机风
    /// </summary>
    private void UpdateWindForce()
    {
        randomIntervalCount += Time.deltaTime;
        if (randomInterval <= 0f || randomIntervalCount >= randomInterval)
        {
            currentRandomForce = new Vector3(
                Random.Range(randomForce.RangeX.x, randomForce.RangeX.y),
                Random.Range(randomForce.RangeY.x, randomForce.RangeY.y),
                Random.Range(randomForce.RangeZ.x, randomForce.RangeZ.y)
            );

            randomIntervalCount = 0f;
        }
    }

    /// <summary>
    /// PBD 第一步：预测位置
    /// </summary>
    private void PredictPositions(float dt)
    {
        for (int i = 0; i < massCount; i++)
        {
            for (int j = 0; j < massCount; j++)
            {
                PBDPoint p = allPoints[i, j];

                p.SetExternalForce(externalForce);
                p.SetRandomForce(currentRandomForce);
                p.drag = drag;

                if (p.isFixed || p.invMass <= 0f)
                {
                    p.predictedPosition = p.position;
                    continue;
                }

                p.prevPosition = p.position;

                Vector3 totalForce = gravity + p.GetTotalForce() * p.invMass;

                // 半隐式风格的速度更新
                p.velocity += totalForce * dt;

                // 简单阻尼
                float damping = Mathf.Clamp01(1f - p.drag * dt);
                p.velocity *= damping;

                p.predictedPosition = p.position + p.velocity * dt;
            }
        }
    }

    /// <summary>
    /// PBD 第二步：多轮约束求解
    /// </summary>
    private void SolveConstraints()
    {
        for (int iter = 0; iter < looper; iter++)
        {
            for (int k = 0; k < allConstraints.Count; k++)
            {
                allConstraints[k].Solve();
            }

            // 固定点约束再钉一次，防止数值漂移
            for (int i = 0; i < massCount; i++)
            {
                for (int j = 0; j < massCount; j++)
                {
                    PBDPoint p = allPoints[i, j];
                    if (p.isFixed)
                    {
                        p.predictedPosition = p.position;
                    }
                }
            }

            // 迭代中也可以顺便做碰撞，稳定性更好
            SolveCollisionsSinglePass();
        }
    }

    /// <summary>
    /// PBD 第三步：碰撞修正
    /// </summary>
    private void SolveCollisions()
    {
        SolveCollisionsSinglePass();
    }

    private void SolveCollisionsSinglePass()
    {
        for (int i = 0; i < massCount; i++)
        {
            for (int j = 0; j < massCount; j++)
            {
                PBDPoint p = allPoints[i, j];
                if (p.isFixed) continue;

                // 地面碰撞（世界坐标）
                if (enableGround)
                {
                    Vector3 worldPos = transform.TransformPoint(p.predictedPosition);
                    if (worldPos.y < groundY)
                    {
                        worldPos.y = groundY;
                        p.predictedPosition = transform.InverseTransformPoint(worldPos);
                    }
                }

                // 球体碰撞
                if (sphereCollider != null)
                {
                    SolveSphereCollision(p, sphereCollider);
                }
            }
        }
    }

    private void SolveSphereCollision(PBDPoint p, SphereCollider sphere)
    {
        Vector3 sphereCenterWS = sphere.transform.TransformPoint(sphere.center);

        // 考虑缩放后的半径
        Vector3 lossy = sphere.transform.lossyScale;
        float maxScale = Mathf.Max(lossy.x, Mathf.Max(lossy.y, lossy.z));
        float radiusWS = sphere.radius * maxScale;

        Vector3 pointWS = transform.TransformPoint(p.predictedPosition);
        Vector3 dir = pointWS - sphereCenterWS;
        float dist = dir.magnitude;

        if (dist < radiusWS)
        {
            Vector3 normal;
            if (dist > 1e-6f)
            {
                normal = dir / dist;
            }
            else
            {
                normal = Vector3.up;
            }

            pointWS = sphereCenterWS + normal * radiusWS;
            p.predictedPosition = transform.InverseTransformPoint(pointWS);
        }
    }

    /// <summary>
    /// PBD 第四步：回写位置并由位置反推速度
    /// </summary>
    private void FinalizePositions(float dt)
    {
        for (int i = 0; i < massCount; i++)
        {
            for (int j = 0; j < massCount; j++)
            {
                PBDPoint p = allPoints[i, j];

                if (p.isFixed)
                {
                    p.velocity = Vector3.zero;
                    p.position = p.predictedPosition;
                    p.transform.localPosition = p.position;
                    continue;
                }

                p.velocity = (p.predictedPosition - p.prevPosition) / Mathf.Max(dt, 1e-6f);
                p.position = p.predictedPosition;
                p.transform.localPosition = p.position;
            }
        }
    }

    #endregion

    #region Mesh & View

    private void UpdateMeshVertices()
    {
        int index = 0;
        for (int i = 0; i < massCount; i++)
        {
            for (int j = 0; j < massCount; j++)
            {
                _vertices[index] = allPoints[i, j].transform.localPosition;
                index++;
            }
        }

        clothMesh.vertices = _vertices;
        clothMesh.RecalculateNormals();
        clothMesh.RecalculateTangents();
        clothMesh.RecalculateBounds();
    }

    private void UpdatePointView()
    {
        for (int i = 0; i < massCount; i++)
        {
            for (int j = 0; j < massCount; j++)
            {
                allPoints[i, j].UpdateView(massSize, showMass);
            }
        }
    }

    #endregion

    #region 外部接口

    public void RemoveAllFixedPoints()
    {
        float pointMass = clothMass / (massCount * massCount);
        float invMass = pointMass > 1e-6f ? 1f / pointMass : 0f;

        for (int i = 0; i < massCount; i++)
        {
            for (int j = 0; j < massCount; j++)
            {
                allPoints[i, j].isFixed = false;
                allPoints[i, j].invMass = invMass;
            }
        }
    }

    public void SetWindEnabled(bool enabled)
    {
        if (!enabled)
        {
            currentRandomForce = Vector3.zero;
            randomForce.RangeX = Vector2.zero;
            randomForce.RangeY = Vector2.zero;
            randomForce.RangeZ = Vector2.zero;
        }
    }

    public void ChangeStructuralStiffness(float value)
    {
        structuralStiffness = Mathf.Clamp01(value);
        RefreshConstraintStiffness();
    }

    public void ChangeShearStiffness(float value)
    {
        shearStiffness = Mathf.Clamp01(value);
        RefreshConstraintStiffness();
    }

    public void ChangeBendStiffness(float value)
    {
        bendStiffness = Mathf.Clamp01(value);
        RefreshConstraintStiffness();
    }

    private void RefreshConstraintStiffness()
    {
        for (int i = 0; i < massCount; i++)
        {
            for (int j = 0; j < massCount; j++)
            {
                // 空函数占位
            }
        }

        foreach (var c in allConstraints)
        {
            float len = c.restLength;

            if (Mathf.Abs(len - restLen) < 1e-4f)
            {
                c.stiffness = structuralStiffness;
            }
            else if (Mathf.Abs(len - restLen * Mathf.Sqrt(2f)) < 1e-4f)
            {
                c.stiffness = shearStiffness;
            }
            else if (Mathf.Abs(len - restLen * 2f) < 1e-4f)
            {
                c.stiffness = bendStiffness;
            }
        }
    }

    #endregion

    #region 简易按钮

    private void OnGUI()
    {
        if (GUILayout.Button("风力开关", GUILayout.Width(100)))
        {
            randomForce.RangeX = Vector2.zero;
            randomForce.RangeY = Vector2.zero;
            randomForce.RangeZ = Vector2.zero;
            randomInterval = 1f;
            currentRandomForce = Vector3.zero;
        }

        if (GUILayout.Button("移除固定节点", GUILayout.Width(100)))
        {
            RemoveAllFixedPoints();
        }
    }

    #endregion
}