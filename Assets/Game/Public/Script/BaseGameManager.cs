using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using MTFrame.MTEvent;
using System;

public enum ResultType
{
    左边胜利 = 0,
    右边胜利 = 1,
    平局 = 2
}

public abstract class BaseGameManager : MonoBehaviour
{
    [Header("背景音乐")]
    public AudioSource BGM;

    [Header("游戏时长（秒）")]
    protected float GameTime = 60f;

    [Header("游戏当前状态")]
    public bool IsGameRunning = false;

    protected int leftScore = 0;
    protected int rightScore = 0;

    [Header("游戏计时器")]
    protected float gameTimer = 0f;

    [Header("游戏倒计时文本（可选）")]
    public UnityEngine.UI.Text GameTimerText;

    [Header("启动流程")]
    [Tooltip("直接运行游戏场景时，是否自动播放 ReadyGo。SceneControl 加载的场景不受此开关影响，由 SceneControl 显式接管。")]
    public bool AutoReadyGoOnDirectSceneStart = true;

    // true：当前场景是由 SceneControl 加载，并且 ReadyGo 的启动时机由 SceneControl 显式控制。
    // false：没有 SceneControl 启动交接，视为直接运行当前游戏场景。
    private bool startupOwnedBySceneControl = false;

    // 防止 SceneControl 在 Start 之前就完成交接时，Start 再次重复启动 ReadyGo。
    private bool readyGoStarted = false;

    [Tooltip("出题间隔时间")]
    protected float NextQuestionIntervalTimes = 2;

    protected virtual void Awake()
    {
        // 防止 AudioSource 勾选 Play On Awake 后，在 ReadyGo 前提前播放。
        StopBGM();

        // “Again”现在只表示：玩家点击了【再来一次】，需要重新走 ReadyGo。
        EventManager.AddListener(GenericEventEnumType.Generic, "Again", AgainCallback);

        // “StartGame”只表示：3、2、1、Go 已经播放完成，可以正式开局。
        EventManager.AddListener(GenericEventEnumType.Generic, "StartGame", StartGameCallback);

        // 在 Awake 阶段立即完成启动所有权登记，不再通过“等几帧再猜启动来源”。
        // SceneControl 在激活目标场景之前已经写入待启动场景，
        // 因此这里可以同步、确定地知道 ReadyGo 应该由谁启动。
        startupOwnedBySceneControl = SceneControl.TryRegisterGameManagerForStartup(this);
    }

    /// <summary>
    /// 玩家在结算页点击“再来一次”。
    /// 不直接 RestartGame，而是重新进入 3、2、1、Go。
    /// </summary>
    private void AgainCallback(EventParamete parameteData)
    {
        if (IsGameRunning)
            return;

        StartReadyGo();
    }

    /// <summary>
    /// ReadyGo 完成后才真正开始游戏。
    /// </summary>
    private void StartGameCallback(EventParamete parameteData)
    {
        // 允许下一次“再来一次”重新启动 ReadyGo。
        readyGoStarted = false;
        RestartGame();
    }

    protected virtual void Start()
    {
        // ReadyGo 阶段游戏还没有正式开始，Update() 不会更新倒计时。
        // 先把计时器重置并刷新一次 UI，让 ReadyGo 阶段直接显示完整游戏时间。
        PrepareTimerForReadyGo();

        // SceneControl 可能在场景激活完成后、Start 执行前就显式启动了 ReadyGo。
        // 这种情况下 Start 只负责完成自身生命周期，不再重复启动。
        if (readyGoStarted)
            return;

        if (startupOwnedBySceneControl)
        {
            Debug.Log(
                $"[BaseGameManager] 已收到 SceneControl 启动交接，等待 SceneControl 显式启动 ReadyGo：{gameObject.scene.name}"
            );
            return;
        }

        // 没有收到 SceneControl 的启动交接，说明当前游戏场景是直接启动的。
        // 不等待任何帧，Start 当帧直接进入 ReadyGo。
        if (AutoReadyGoOnDirectSceneStart)
        {
            Debug.Log(
                $"[BaseGameManager] 当前游戏场景为直接启动，立即进入 ReadyGo：{gameObject.scene.name}"
            );

            StartReadyGo();
        }
    }

    /// <summary>
    /// SceneControl 完成转场后，通过这个方法显式把启动权交回 BaseGameManager。
    /// 不使用事件猜测、不查找启动来源、不等待帧。
    /// </summary>
    public void BeginReadyGoFromSceneControl()
    {
        if (!startupOwnedBySceneControl)
        {
            Debug.LogWarning(
                $"[BaseGameManager] 收到 SceneControl 的 ReadyGo 启动通知，但当前没有 SceneControl 启动交接：{gameObject.scene.name}",
                this
            );
        }

        startupOwnedBySceneControl = false;
        StartReadyGo();
    }

    /// <summary>
    /// ReadyGo 开始前统一准备计时 UI。
    /// ReadyGo 本身不消耗游戏时间。
    /// </summary>
    private void PrepareTimerForReadyGo()
    {
        gameTimer = 0f;
        UpdateTimerUI();
    }

    /// <summary>
    /// ReadyGo 真正显示之前，允许具体游戏提前准备需要展示的游戏内容。
    ///
    /// 例如：
    /// - 提前生成题目文本；
    /// - 提前显示目标序列；
    /// - 但不要在这里正式开始游戏计时。
    ///
    /// 默认什么都不做，避免影响其它游戏。
    /// </summary>
    protected virtual void PrepareGameForReadyGo()
    {
    }

    /// <summary>
    /// 统一启动 ReadyGo。UIPanel 不可用时直接开局，避免游戏卡死。
    /// </summary>
    private void StartReadyGo()
    {
        if (readyGoStarted)
        {
            Debug.LogWarning("[BaseGameManager] ReadyGo 已经启动，忽略重复请求。", this);
            return;
        }

        if (IsGameRunning)
        {
            Debug.LogWarning("[BaseGameManager] 游戏已经开始，忽略 ReadyGo 请求。", this);
            return;
        }

        readyGoStarted = true;

        // ReadyGo 阶段绝对不播放游戏 BGM。
        StopBGM();

        PrepareTimerForReadyGo();

        // 先让具体游戏把 ReadyGo 阶段需要显示的内容准备好，
        // 再真正打开 3、2、1、Go。
        // 这样题目不会等到 ReadyGo 结束后的 InitGame() 才出现。
        PrepareGameForReadyGo();

        UIPanel.Preload();

        UIPanel panel = UIPanel.Instance;

        if (panel != null)
        {
            panel.ReadyGo();
        }
        else
        {
            Debug.LogError(
                "[BaseGameManager] UIPanel.Instance 为空，无法播放 ReadyGo，直接启动游戏。",
                this
            );

            readyGoStarted = false;
            RestartGame();
        }
    }

    protected virtual void Update()
    {
        if (!IsGameRunning)
            return;

        // 更新游戏计时器
        gameTimer += Time.deltaTime;

        // 更新UI显示
        UpdateTimerUI();

        // 检查是否超时
        if (gameTimer >= GameTime)
        {
            GameEnd(DetermineResult());
        }
    }

    /// <summary>
    /// 更新计时器UI显示
    /// </summary>
    protected virtual void UpdateTimerUI()
    {
        if (GameTimerText != null)
        {
            float remainingTime = Mathf.Max(0f, GameTime - gameTimer);

            // 使用 Ceil，避免游戏刚开始第一帧就从 01:00 跳成 00:59。
            int totalSeconds = Mathf.CeilToInt(remainingTime);
            int minutes = totalSeconds / 60;
            int seconds = totalSeconds % 60;

            GameTimerText.text = string.Format("{0:00}:{1:00}", minutes, seconds);
        }
    }

    /// <summary>
    /// 重新开始游戏（由 UIPanel 的 ReadyGo 完成后或 Again 按钮触发）
    /// </summary>
    public virtual void RestartGame()
    {
        IsGameRunning = true;
        gameTimer = 0f;
        leftScore = 0;
        rightScore = 0;

        // ReadyGo 结束正式开局时再次刷新为完整时间，
        // 保证开局第一帧显示正确。
        UpdateTimerUI();

        // 清理场景中的旧对象
        CleanupGame();

        // 初始化游戏
        InitGame();

        // 只有 ReadyGo 已结束并且游戏确实成功进入运行状态后才播放 BGM。
        if (IsGameRunning)
        {
            PlayBGM();
        }

        Debug.Log("[BaseGameManager] 游戏开始");
    }

    /// <summary>
    /// 游戏初始化（子类实现具体逻辑）
    /// </summary>
    protected abstract void InitGame();

    /// <summary>
    /// 清理游戏场景（子类实现具体逻辑）
    /// </summary>
    protected virtual void CleanupGame()
    {
        // 子类可以重写此方法来清理游戏对象
    }

    /// <summary>
    /// 游戏结束
    /// </summary>
    public virtual void GameEnd(ResultType result)
    {
        if (!IsGameRunning)
            return;

        IsGameRunning = false;

        // 进入结算页面前立即停止游戏 BGM。
        StopBGM();

        // 显示结算页面
        if (UIPanel.Instance != null)
        {
            UIPanel.Instance.ShowResult(
                result,
                leftScore,
                rightScore
            );
        }
        else
        {
            Debug.LogError("[BaseGameManager] UIPanel.Instance 为空，无法显示结算页面");
        }
    }

    /// <summary>
    /// 判定游戏结果（子类可以重写）
    /// </summary>
    protected virtual ResultType DetermineResult()
    {
        if (leftScore > rightScore)
        {
            return ResultType.左边胜利;
        }
        else if (leftScore == rightScore)
        {
            return ResultType.平局;
        }
        else
        {
            return ResultType.右边胜利;
        }
    }

    public virtual void AddLeftScore(int points)
    {
        if (!IsGameRunning)
            return;

        leftScore = Mathf.Max(0, leftScore + points);
        OnBattleScoreChanged(leftScore, rightScore);
    }

    public virtual void AddRightScore(int points)
    {
        if (!IsGameRunning)
            return;

        rightScore = Mathf.Max(0, rightScore + points);
        OnBattleScoreChanged(leftScore, rightScore);
    }

    public virtual void SetBattleScores(int left, int right)
    {
        leftScore = Mathf.Max(0, left);
        rightScore = Mathf.Max(0, right);
        OnBattleScoreChanged(leftScore, rightScore);
    }

    public virtual void GameEndBattle(int left, int right)
    {
        SetBattleScores(left, right);
        GameEnd(DetermineBattleResult());
    }

    protected virtual ResultType DetermineBattleResult()
    {
        if (leftScore > rightScore)
            return ResultType.左边胜利;

        if (leftScore < rightScore)
            return ResultType.右边胜利;

        return ResultType.平局;
    }

    /// <summary>
    /// 分数改变时的回调（子类可以重写）
    /// </summary>
    protected virtual void OnScoreChanged(int newScore)
    {
        // 子类可以在这里更新分数UI
    }

    protected virtual void OnBattleScoreChanged(int left, int right)
    {
    }

    public int GetLeftScore()
    {
        return leftScore;
    }

    public int GetRightScore()
    {
        return rightScore;
    }

    /// <summary>
    /// 获取剩余时间
    /// </summary>
    public float GetRemainingTime()
    {
        return Mathf.Max(0f, GameTime - gameTimer);
    }

    /// <summary>
    /// 正式游戏开始后播放 BGM。
    /// 每一局都从头播放。
    /// </summary>
    private void PlayBGM()
    {
        if (BGM == null)
            return;

        BGM.Stop();
        BGM.Play();
    }

    /// <summary>
    /// ReadyGo、结算、销毁阶段统一停止 BGM。
    /// </summary>
    private void StopBGM()
    {
        if (BGM == null)
            return;

        BGM.Stop();
    }

    public virtual void CancelForSceneExit()
    {
        IsGameRunning = false;
        readyGoStarted = false;
        StopBGM();
    }

    /// <summary>
    /// 立即结束游戏（用于特殊情况）
    /// </summary>
    public void ForceEndGame()
    {
        if (IsGameRunning)
        {
            GameEnd(DetermineResult());
        }
    }

    protected virtual void OnDestroy()
    {
        StopBGM();

        // 如果对象在 SceneControl 交接期间被销毁，主动注销，避免 SceneControl 保留失效引用。
        SceneControl.UnregisterGameManagerForStartup(this);

        EventManager.RemoveListener(GenericEventEnumType.Generic, "Again", AgainCallback);
        EventManager.RemoveListener(GenericEventEnumType.Generic, "StartGame", StartGameCallback);
    }
}
