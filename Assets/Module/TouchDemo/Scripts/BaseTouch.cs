using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public abstract class BaseTouch : MonoBehaviour
{
    [Header("点击特效")]
    public GameObject clickEffectPrefab;

    [Tooltip("特效存活时间，秒。小于或等于 0 时不会自动回收到 LeanPool。")]
    public float effectLifeTime = 1f;

    protected virtual void Awake()
    {
        gameObject.layer = LayerMask.NameToLayer("Touch");
    }

    public virtual void OnTouch(Vector3 point)
    {

    }
}

