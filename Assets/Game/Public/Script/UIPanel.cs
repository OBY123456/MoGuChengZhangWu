using MTFrame.MTEvent;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using DG.Tweening;
using System;
using System.Collections;
using Config;
using LanguageSetting;
using UnityEngine.Events;

public class UIPanel : MonoBehaviour
{
    private static UIPanel instance;

    public static UIPanel Instance
    {
        get
        {
            if (instance == null)
            {
                GameObject prefab = Resources.Load<GameObject>("UI页面");

                if (prefab == null)
                {
                    Debug.LogError("Resources 下没有找到名为【UI页面】的预制体，请检查路径和名称。");
                    return null;
                }

                GameObject temp = Instantiate(prefab);
                temp.name = "UI页面";

                instance = temp.GetComponent<UIPanel>();

                if (instance == null)
                {
                    Debug.LogError("结算页面预制体上没有挂 UIPanel 脚本。");
                }
            }

            return instance;
        }
    }

    [Header("全部UI的总控制开关，包括倒计时和ReadyGo")]
    public CanvasGroup AllUICanvasGroup;

    [Header("结算页面开关控制,不包括倒计时和ReadyGo")]
    public CanvasGroup EndPanelCanvasGroup;

    [Header("倒计时页面开关控制")]
    public CanvasGroup GameReadyGoCanvasGroup;

    [Header("返回首页按钮,0是中文按钮，1是英文按钮")]
    public TouchUIButton[] BackBtn = new TouchUIButton[2];

    [Header("再来一次按钮，0是中文按钮，1是英文按钮")]
    public TouchUIButton[] AgainBtn = new TouchUIButton[2];

    [Header("倒计时30秒后自动返回首页")]
    public Text CountDownText;

    [Header("胜利/失败音效")]
    public AudioSource _audioSource;

    internal static void CancelForSceneExit()
    {
        throw new NotImplementedException();
    }

    public AudioClip VictoryAudio;
    public AudioClip FailureAudio;

    [Header("结果文本，直接显示ResultType")]
    public Image ResultImage;

    [Header("激励语文本")]
    public Text MotivationalPhraseText;

    [Header("结算动画参数")]
    [Tooltip("结算面板淡入时间")]
    public float panelFadeInDuration = 0.5f;

    [Header("结算倒计时")]
    [Tooltip("结算页面显示多久后自动返回首页（秒）")]
    private float autoBackCountDown = 5f;

    [Header("胜利特效")]
    public GameObject VectorEff;

    // UIPanel 是 DontDestroyOnLoad 单例，只允许执行一次基础初始化，
    // 防止 Preload() 和 Start() 在相近时序内重复重置 UI 状态。
    private bool hasInitialized = false;

    /// <summary>
    /// UIPanel 下所有 TouchUIButton。
    /// 包括当前处于 inactive 状态的按钮。
    /// </summary>
    private TouchUIButton[] touchUIButtons;


    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {

    }

    private void AgainEvent()
    {

    }

    private IEnumerator HideEndPanelAndAgain()
    {
        yield return null;

        // 通知 BaseGameManager：这是“再来一次”请求，需要重新进入 ReadyGo。
        UIState.SendEventMsg("Again", "Again");

    }

    private void BackEvent()
    {

    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void OnValidate()
    {
        panelFadeInDuration = Mathf.Max(0.01f, panelFadeInDuration);
        autoBackCountDown = Mathf.Max(1f, autoBackCountDown);
    }

    internal static void Preload()
    {
        throw new NotImplementedException();
    }

    internal void ReadyGo()
    {
        throw new NotImplementedException();
    }

    internal void ShowResult(ResultType result, int leftScore, int rightScore)
    {
        throw new NotImplementedException();
    }
}
