using System.Collections.Generic;
using Lean.Pool;
using OBYTouch;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class ClickManager : MonoBehaviour
{
    public enum DetectObjectType
    {
        UGUI,
        Object3D,
        UGUIAndObject3D
    }

    public enum ClickEffectType
    {
        ObjectOnly,
        ScreenOnly,
        ObjectFirstScreenFallback
    }

    [System.Serializable]
    public class TouchPointCache
    {
        public Vector2 point;

        [Tooltip("这个点被记录时的时间")]
        public float time;

        public TouchPointCache() { }

        public TouchPointCache(Vector2 point, float time)
        {
            this.point = point;
            this.time = time;
        }
    }

    [Header("检测类型")]
    [SerializeField] private DetectObjectType detectObjectType = DetectObjectType.UGUI;

    [Header("点击特效类型")]
    [SerializeField] private ClickEffectType clickEffectType = ClickEffectType.ObjectOnly;

    [Header("点击特效")]
    [SerializeField] private GameObject clickEffectPrefab;

    [Tooltip("特效存活时间，秒。小于或等于 0 时不会自动回收到 LeanPool。")]
    [SerializeField] private float effectLifeTime = 1f;

    [Tooltip("点击屏幕时，特效生成平面距离 Camera.main 的距离。透视和正交相机都支持。")]
    [SerializeField] private float screenEffectDistance = 10f;

    [Header("3D 射线")]
    [SerializeField] private float maxRayDistance = 1000f;

    [SerializeField] private QueryTriggerInteraction queryTriggerInteraction = QueryTriggerInteraction.Collide;

    [Header("触碰间隔")]
    [SerializeField] private float interval = 0.02f;

    private float timer;

    [Header("触点距离间隔")]
    [SerializeField] private float touchDistance = 250f;

    [Header("触点时间间隔")]
    [SerializeField] private float touchInterval = 0.2f;

    [Header("有效点缓存")]
    [SerializeField] private List<TouchPointCache> touchPointCaches = new List<TouchPointCache>();

    [Header("TUIO 输入队列")]
    [Tooltip("每次 Update 最多处理多少个 TUIO 点。小于或等于 0 表示本帧处理完队列里所有点。")]
    [SerializeField] private int maxTuioProcessPerFrame = 30;

    private const string TouchLayerName = "Touch";
    private readonly Queue<Vector3> tuioQueue = new Queue<Vector3>();
    private readonly object tuioQueueLock = new object();
    private readonly List<RaycastResult> uiRaycastResults = new List<RaycastResult>();
    private readonly RaycastHit[] raycastHits = new RaycastHit[64];

    private Camera mainCamera;
    private int touchLayer = -1;
    private int touchLayerMask;

    private void Awake()
    {
        mainCamera = Camera.main;
        touchLayer = LayerMask.NameToLayer(TouchLayerName);

        if (touchLayer < 0)
        {
            Debug.LogError($"[{nameof(ClickManager)}] 没有找到名为“{TouchLayerName}”的 Layer，请先在 Project Settings > Tags and Layers 中创建。", this);
            enabled = false;
            return;
        }

        touchLayerMask = 1 << touchLayer;
    }

    private void OnEnable()
    {
        ClickEvent.clickevent -= RTuioOnCursorAddedEvent;
        ClickEvent.clickevent += RTuioOnCursorAddedEvent;
    }

    private void OnDisable()
    {
        ClickEvent.clickevent -= RTuioOnCursorAddedEvent;

        lock (tuioQueueLock)
        {
            tuioQueue.Clear();
        }
    }

    private void RTuioOnCursorAddedEvent(Vector3 point)
    {
        lock (tuioQueueLock)
        {
            tuioQueue.Enqueue(point);
        }
    }

    private void Update()
    {
        timer += Time.unscaledDeltaTime;

        if (timer < interval)
        {
            return;
        }

        bool hasProcessedInput = false;

        if (Input.GetMouseButtonDown(0))
        {
            Click(Input.mousePosition);
            hasProcessedInput = true;
        }

        if (ProcessTuioQueue() > 0)
        {
            hasProcessedInput = true;
        }

        if (hasProcessedInput)
        {
            timer = 0f;
        }
    }

    private int ProcessTuioQueue()
    {
        int processedCount = 0;
        int maxCount = maxTuioProcessPerFrame <= 0 ? int.MaxValue : maxTuioProcessPerFrame;

        while (processedCount < maxCount)
        {
            Vector3 point;

            lock (tuioQueueLock)
            {
                if (tuioQueue.Count == 0)
                {
                    break;
                }

                point = tuioQueue.Dequeue();
            }

            Click(point);
            processedCount++;
        }

        return processedCount;
    }

    private void RemoveOldTouchPoints(float now)
    {
        for (int i = touchPointCaches.Count - 1; i >= 0; i--)
        {
            if (now - touchPointCaches[i].time >= touchInterval)
            {
                touchPointCaches.RemoveAt(i);
            }
        }
    }

    private void Click(Vector3 newPoint)
    {
        float now = Time.unscaledTime;
        Vector2 screenPoint = new Vector2(newPoint.x, newPoint.y);

        RemoveOldTouchPoints(now);

        for (int i = 0; i < touchPointCaches.Count; i++)
        {
            if (Vector2.Distance(touchPointCaches[i].point, screenPoint) < touchDistance)
            {
                return;
            }
        }

        touchPointCaches.Add(new TouchPointCache(screenPoint, now));
        TriggerClickEffect(screenPoint);
    }

    private void TriggerClickEffect(Vector2 screenPoint)
    {
        switch (clickEffectType)
        {
            case ClickEffectType.ObjectOnly:
                if (TryGetTouchTarget(screenPoint, out BaseTouch objectOnlyTarget, out Vector3 objectOnlyHitPoint)) objectOnlyTarget.OnTouch(objectOnlyHitPoint);
                break;

            case ClickEffectType.ScreenOnly:
                SpawnEffect(ScreenPointToWorldPosition(screenPoint));
                break;

            case ClickEffectType.ObjectFirstScreenFallback:
                if (TryGetTouchTarget(screenPoint, out BaseTouch fallbackTarget, out Vector3 fallbackHitPoint)) fallbackTarget.OnTouch(fallbackHitPoint);
                else SpawnEffect(ScreenPointToWorldPosition(screenPoint));
                break;
        }
    }

    private bool TryGetTouchTarget(Vector2 screenPoint, out BaseTouch baseTouch, out Vector3 hitWorldPoint)
    {
        baseTouch = null;
        hitWorldPoint = Vector3.zero;

        switch (detectObjectType)
        {
            case DetectObjectType.UGUI:
                return TryGetUGUITouchTarget(screenPoint, out baseTouch, out hitWorldPoint);

            case DetectObjectType.Object3D:
                return TryGet3DTouchTarget(screenPoint, out baseTouch, out hitWorldPoint);

            case DetectObjectType.UGUIAndObject3D:
                if (TryGetUGUITouchTarget(screenPoint, out baseTouch, out hitWorldPoint)) return true;
                return TryGet3DTouchTarget(screenPoint, out baseTouch, out hitWorldPoint);

            default:
                return false;
        }
    }

    private bool TryGetUGUITouchTarget(Vector2 screenPoint, out BaseTouch baseTouch, out Vector3 hitWorldPoint)
    {
        baseTouch = null;
        hitWorldPoint = Vector3.zero;

        EventSystem eventSystem = EventSystem.current;
        if (eventSystem == null) return false;

        PointerEventData pointerEventData = new PointerEventData(eventSystem) { position = screenPoint };
        uiRaycastResults.Clear();
        eventSystem.RaycastAll(pointerEventData, uiRaycastResults);

        for (int i = 0; i < uiRaycastResults.Count; i++)
        {
            RaycastResult result = uiRaycastResults[i];
            if (!(result.module is GraphicRaycaster)) continue;

            BaseTouch target = FindBaseTouchOnTouchLayer(result.gameObject);
            if (target == null) continue;

            RectTransform hitRectTransform = result.gameObject.transform as RectTransform;
            if (hitRectTransform == null) continue;

            Camera eventCamera = result.module != null ? result.module.eventCamera : null;

            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(hitRectTransform, screenPoint, eventCamera, out Vector3 worldPoint)) continue;

            baseTouch = target;
            hitWorldPoint = worldPoint;
            return true;
        }

        return false;
    }

    private bool TryGetUGUITouchTarget(Vector2 screenPoint, out BaseTouch baseTouch)
    {
        baseTouch = null;
        EventSystem eventSystem = EventSystem.current;

        if (eventSystem == null)
        {
            return false;
        }

        PointerEventData pointerEventData = new PointerEventData(eventSystem) { position = screenPoint };
        uiRaycastResults.Clear();
        eventSystem.RaycastAll(pointerEventData, uiRaycastResults);

        for (int i = 0; i < uiRaycastResults.Count; i++)
        {
            RaycastResult result = uiRaycastResults[i];

            if (!(result.module is GraphicRaycaster))
            {
                continue;
            }

            BaseTouch target = FindBaseTouchOnTouchLayer(result.gameObject);

            if (target != null)
            {
                baseTouch = target;
                return true;
            }
        }

        return false;
    }

    private bool TryGet3DTouchTarget(Vector2 screenPoint, out BaseTouch baseTouch, out Vector3 hitWorldPoint)
    {
        baseTouch = null;
        hitWorldPoint = Vector3.zero;

        Camera camera = GetMainCamera();
        if (camera == null) return false;

        Ray ray = camera.ScreenPointToRay(screenPoint);
        int hitCount = Physics.RaycastNonAlloc(ray, raycastHits, maxRayDistance, touchLayerMask, queryTriggerInteraction);
        float nearestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = raycastHits[i];
            BaseTouch target = FindBaseTouchOnTouchLayer(hit.collider.gameObject);

            if (target == null || hit.distance >= nearestDistance) continue;

            nearestDistance = hit.distance;
            baseTouch = target;
            hitWorldPoint = hit.point;
        }

        return baseTouch != null;
    }

    private BaseTouch FindBaseTouchOnTouchLayer(GameObject hitObject)
    {
        Transform current = hitObject != null ? hitObject.transform : null;

        while (current != null)
        {
            if (current.gameObject.layer == touchLayer)
            {
                BaseTouch baseTouch = current.GetComponent<BaseTouch>();

                if (baseTouch != null)
                {
                    return baseTouch;
                }
            }

            current = current.parent;
        }

        return null;
    }

    private Vector3 GetTargetWorldPosition(BaseTouch target)
    {
        RectTransform rectTransform = target.transform as RectTransform;

        if (rectTransform == null)
        {
            return target.transform.position;
        }

        Canvas canvas = target.GetComponentInParent<Canvas>();

        if (canvas == null || canvas.renderMode == RenderMode.WorldSpace)
        {
            return rectTransform.position;
        }

        Camera uiCamera = canvas.renderMode == RenderMode.ScreenSpaceCamera ? canvas.worldCamera : null;

        if (canvas.renderMode == RenderMode.ScreenSpaceCamera && uiCamera == null)
        {
            uiCamera = GetMainCamera();
        }

        Vector3 uiWorldCenter = rectTransform.TransformPoint(rectTransform.rect.center);
        Vector2 targetScreenPoint = RectTransformUtility.WorldToScreenPoint(uiCamera, uiWorldCenter);
        return ScreenPointToWorldPosition(targetScreenPoint);
    }

    private Vector3 ScreenPointToWorldPosition(Vector2 screenPoint)
    {
        Camera camera = GetMainCamera();

        if (camera == null)
        {
            return Vector3.zero;
        }

        float distance = Mathf.Max(0.01f, screenEffectDistance);
        Vector3 planePoint = camera.transform.position + camera.transform.forward * distance;
        Plane effectPlane = new Plane(camera.transform.forward, planePoint);
        Ray ray = camera.ScreenPointToRay(screenPoint);

        if (effectPlane.Raycast(ray, out float enter))
        {
            return ray.GetPoint(enter);
        }

        return camera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, distance));
    }

    private void SpawnEffect(Vector3 worldPosition)
    {
        if (clickEffectPrefab == null)
        {
            Debug.LogWarning($"[{nameof(ClickManager)}] 没有设置点击特效预制体。", this);
            return;
        }

        GameObject effect = LeanPool.Spawn(clickEffectPrefab, worldPosition, clickEffectPrefab.transform.rotation, null);
        effect.transform.SetParent(null, true);

        if (effectLifeTime > 0f)
        {
            LeanPool.Despawn(effect, effectLifeTime);
        }
    }

    private Camera GetMainCamera()
    {
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
        }

        if (mainCamera == null)
        {
            Debug.LogError($"[{nameof(ClickManager)}] 找不到 Camera.main，请给主相机设置 MainCamera Tag。", this);
        }

        return mainCamera;
    }
}
