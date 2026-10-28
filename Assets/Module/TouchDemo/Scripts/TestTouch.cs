using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Lean.Pool;

public class TestTouch : BaseTouch
{
    public override void OnTouch(Vector3 point)
    {
        base.OnTouch(point);
        GameObject effect = LeanPool.Spawn(clickEffectPrefab, point, clickEffectPrefab.transform.rotation, null);
        effect.transform.SetParent(null, true);
        if (effectLifeTime > 0f) LeanPool.Despawn(effect, effectLifeTime);
    }
}
