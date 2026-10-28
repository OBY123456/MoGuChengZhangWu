using System;
using System.Collections;
using System.Collections.Generic;
using System.Net.NetworkInformation;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using Newtonsoft.Json.Linq;
using UnityEngine.UI;
using ZXing;
using ZXing.QrCode;
using System.IO;
using BestHTTP;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading;
using Debug = UnityEngine.Debug;

namespace Encryption
{
    /// <summary>
    /// 计算硬件相关唯一标识符
    /// </summary>
    /// <summary>
    /// 计算硬件相关唯一标识符
    /// </summary>
    public class HardwareCodeUtil
    {
        const string cpu_command = "wmic CPU get ProcessorID "; //不可改变
        const string cpu_Tag = "ProcessorId";

        const string Disk_command = "wmic diskdrive where index=0 get SerialNumber"; //不可改变
        const string Disk_Tag = "SerialNumber";

        const string uuid_command = "wmic csproduct get UUID";
        const string uuid_Tag = "UUID";
        public static string GetWin_ID(string command, string Tag)
        {
            string cpuID = string.Empty;

            Process process = new Process();
            process.StartInfo.UseShellExecute = false;   //是否使用操作系统shell启动 
            process.StartInfo.CreateNoWindow = true;   //是否在新窗口中启动该进程的值 (不显示程序窗口)
            process.StartInfo.RedirectStandardInput = true;  // 接受来自调用程序的输入信息 
            process.StartInfo.RedirectStandardOutput = true;  // 由调用程序获取输出信息
            process.StartInfo.RedirectStandardError = true;  //重定向标准错误输出
            process.StartInfo.FileName = "cmd.exe";
            process.StartInfo.Arguments = "/c" + command;
            process.StartInfo.WorkingDirectory = @"C:\Windows\System32";
            process.Start();                         // 启动程序
            //Thread.Sleep(10);
            //process.StandardInput.WriteLine(command); //向cmd窗口发送输入信息
            process.StandardInput.AutoFlush = true;
            Thread.Sleep(10);
            // 前面一个命令不管是否执行成功都执行后面(exit)命令，如果不执行exit命令，后面调用ReadToEnd()方法会假死
            process.StandardInput.WriteLine("exit");

            //循环等待读取缓冲区
            List<string> ReadLines = new List<string>();
            StreamReader reader = process.StandardOutput;//获取exe处理之后的输出信息
            string curLine = reader.ReadLine(); //获取错误信息到error
            while (!reader.EndOfStream)
            {
                Thread.Sleep(1);
                if (!string.IsNullOrEmpty(curLine))
                {
                    ReadLines.Add(curLine);
                }
                curLine = reader.ReadLine();
            }
            reader.Close(); //close进程
            process.WaitForExit();  //等待程序执行完退出进程
            process.Close();


            bool ReadCpuID = false;
            for (int i = 0; i < ReadLines.Count; i++)
            {
                if (ReadCpuID)
                {
                    cpuID += ReadLines[i];
                }
                if (ReadLines[i].StartsWith(Tag))
                {
                    ReadCpuID = true;
                }
            }

            return cpuID;
        }

        public static string GetMac()
        {
            StringBuilder builder = new StringBuilder();
#if UNITY_STANDALONE_WIN
            string id = string.Format("{0}&{1}&{2}", GetWin_ID(cpu_command, cpu_Tag), GetWin_ID(Disk_command, Disk_Tag), GetWin_ID(uuid_command, uuid_Tag));
            builder.Append(id);
#else
            builder.Append(SystemInfo.deviceUniqueIdentifier);
#endif
            builder.Append(Application.productName);
            string cl = builder.ToString().Replace(" ", "").Replace("\n", "").Replace("\r", "");
            //Debug.Log(cl);
            MD5 md5 = MD5.Create(); //实例化一个md5对像
                                    // 加密后是一个字节类型的数组，这里要注意编码UTF8/Unicode等的选择　
            byte[] s = md5.ComputeHash(Encoding.UTF8.GetBytes(cl));
            // 通过使用循环，将字节类型的数组转换为字符串，此字符串是常规字符格式化所得
            string pwd = string.Empty;
            for (int i = 0; i < s.Length; i++)
            {
                // 将得到的字符串使用十六进制类型格式。格式后的字符是小写的字母，如果使用大写（X）则格式后的字符是大写字符 
                pwd = pwd + s[i].ToString("X");
            }

            return pwd;
        }
    }

    /// <summary>
    /// 授权与绑定
    /// </summary>
    public class Authorization : MonoBehaviour
    {
        /// <summary>
        /// 授权服务器地址
        /// </summary>
        private const string HttpSeverUrl = @"https://api.itaocow.com.cn/factory";

        /// <summary>
        /// 付款服务器地址
        /// </summary>
        private const string PaySeverUrl = @"https://api.itaocow.com.cn/youku";

        /// <summary>
        /// 获取订单接口，Post请求，需拼接PaySeverUrl
        /// </summary>
        private const string OrderUrl = @"/api/Order/CreateOrder";

        /// <summary>
        /// 轮询订单结果接口，Get请求，需拼接PaySeverUrl
        /// </summary>
        private const string OrderResultUrl = @"/api/Order/PollPayResult";

        /// <summary>
        /// 检查是否付过款，Post请求，需拼接PaySeverUrl
        /// </summary>
        private const string CheckPayUrl = @"/api/Order/CheckPaid";

        [Header("未授权时二维码显示模式")]
        public UnauthorizedQRCodeMode QRCodeDisplayMode = UnauthorizedQRCodeMode.授权码加付款码;

        [Header("是否显示授权日志")]
        public bool ShowAuthorizationLog = true;

        // ==================== 旧版授权注册表 Key ====================
        // 旧版只有这三个 Key，并且没有 GrantSource。
        // 为了兼容已经安装过旧版的用户，这三个 Key 永远保留给旧授权系统使用。
        public static readonly string MachineKey = "machineCode";
        public static readonly string DateKey = "DateCode";
        public static readonly string TimeKey = "TimeCode";

        // 这是“旧的新版”(加入付款码后的过渡版本)使用过的共享 GrantSource。
        // 它只作为版本识别/迁移标记使用，新版正式数据不再写这个 Key。
        public static readonly string GrantSourceKey = "GrantSource";

        // ==================== 新版授权注册表 Key ====================
        // 新版中文、英文完全分开，避免中文和英文授权/付款数据互相串用。
        private const string NewMachineKey_CN = "machineCode_CN";
        private const string NewDateKey_CN = "DateCode_CN";
        private const string NewTimeKey_CN = "TimeCode_CN";
        private const string NewGrantSourceKey_CN = "GrantSource_CN";

        private const string NewMachineKey_EN = "machineCode_EN";
        private const string NewDateKey_EN = "DateCode_EN";
        private const string NewTimeKey_EN = "TimeCode_EN";
        private const string NewGrantSourceKey_EN = "GrantSource_EN";

        // True：发现旧版三 Key 且没有 GrantSource，整个运行周期都走旧授权流程。
        // False：走新版授权/付款流程。
        private bool useLegacyAuthorizationSystem = false;

        // 授权流程启动时锁定语言，保证 ProductCode / HardwareCode / 本地 Key 始终属于同一语言。
        private LanguageType authorizationLanguageType;
        private bool authorizationLanguageInitialized = false;

        private bool IsEnglishAuthorizationLanguage
        {
            get
            {
                return authorizationLanguageType == LanguageType.英文;
            }
        }

        private string CurrentMachineKey
        {
            get { return IsEnglishAuthorizationLanguage ? NewMachineKey_EN : NewMachineKey_CN; }
        }

        private string CurrentDateKey
        {
            get { return IsEnglishAuthorizationLanguage ? NewDateKey_EN : NewDateKey_CN; }
        }

        private string CurrentTimeKey
        {
            get { return IsEnglishAuthorizationLanguage ? NewTimeKey_EN : NewTimeKey_CN; }
        }

        private string CurrentGrantSourceKey
        {
            get { return IsEnglishAuthorizationLanguage ? NewGrantSourceKey_EN : NewGrantSourceKey_CN; }
        }

        private bool Is显示绑定二维码 = false;
        private bool Is显示授权二维码 = false;

        [Header("授权二维码父物体")]
        private GameObject AuthorizationQRCodeParent;

        [Header("微信小程序码父物体")]
        private GameObject WeChatQRCodeParent;

        [Header("授权二维码")]
        public RawImage AuthorizationQRCode;

        [Header("二维码信息")]
        public Text AuthorizationQRText;

        [Header("微信小程序码")]
        public RawImage WeChatQRCode;

        [Header("小程序码信息")]
        public Text WeChatQRCodeText;

        [Header("网络提示")]
        public Text NetworkTipsText;

        [Header("版本号")]
        public Text VersionText;

        /// <summary>
        /// 设备唯一码
        /// </summary>
        private string HardwareCode = string.Empty;

        [Header("跳转场景")]
        public string SceneName;

        [Header("中文产品编号")]
        //http://master.itaocow.com.cn/:游库管理->游库产品列表->产品编号
        public string ProductCode_CN;

        [Header("英文产品编号")]
        //http://master.itaocow.com.cn/:游库管理->游库产品列表->产品编号
        public string ProductCode_EN;

        /// <summary>
        /// 根据当前语言获取对应的产品编号。
        /// 中文使用 ProductCode_CN，英文使用 ProductCode_EN。
        /// 每次请求时动态获取，避免运行时切换语言后仍使用旧产品编号。
        /// </summary>
        private string GetCurrentProductCode()
        {
            if (!authorizationLanguageInitialized)
            {
                authorizationLanguageType =
                    LanguageManger.Instance.GetLanguageType();
                authorizationLanguageInitialized = true;
            }

            return authorizationLanguageType == LanguageType.英文
                ? ProductCode_EN
                : ProductCode_CN;
        }

        private string machineCode = "未授权";
        private string currentOrderCode = string.Empty;
        private string currentOrderAmount = string.Empty;
        private string currentPayQrUrl = string.Empty;
        private bool isCreatingOrder = false;
        private bool isPollingPay = false;

        // 付款成功后锁定授权二维码/付款码界面。
        // 主要用于拦截付款成功前已经发出的授权/绑定请求回调，
        // 防止这些旧回调在付款成功后又把授权二维码重新打开。
        private bool paymentSuccessConfirmed = false;

        // 防止授权流程在同一帧/短时间内重复触发场景加载。
        // 同时用于阻止目标场景就是当前场景时再次重载自己。
        private bool isSceneLoading = false;

        private InvokeInfo payPollInvoke = null;
        private InvokeInfo payRetryInvoke = null;
        private InvokeInfo networkRetryInvoke = null;
        private Texture2D payQrTexture = null;

        //允许误差2小时
        private int ErrorRange = 60 * 60 * 2;

        private static readonly string ExpirationTime = "2380-12-31 00:00:00";

        /// <summary>
        /// 检查状态-流程
        /// </summary>
        public enum CheckState
        {
            检查授权,
            检查绑定用户,
            检查是否设备合法性,
        }

        public enum UnauthorizedQRCodeMode
        {
            授权码,
            付款码,
            授权码加付款码,
        }

        private enum GrantSourceType
        {
            Unknown,
            Authorization,
            Payment,
        }

        private enum LocalAuthCheckContext
        {
            Online,
            Offline,
        }

        private bool authorizationGrantExpired = false;
        private bool paymentGrantExpired = false;
        private bool localTimeInvalid = false;
        private bool isFirstUnauthorizedDualQRCode = false;

        // 兼容旧版本：旧版没有 CurrentGrantSourceKey。
        // 即使旧脚本已经把 DateCode / TimeCode 删除，只要 machineCode 还在，
        // 仍然认为这台设备“曾经授权过”。
        private bool hasLegacyGrantHistory = false;

        private void Awake()
        {
            InitializeAuthorizationLanguage();

            // 规则：
            // 1. machineCode / DateCode / TimeCode 存在，并且没有 GrantSource => 旧版授权系统。
            // 2. 只要存在 GrantSource，就属于加入付款码后的新版数据，不按旧版处理。
            // 3. 没有旧版数据 => 走新版中英文独立系统。
            useLegacyAuthorizationSystem = HasLegacySystemData();

            IsNullHardwareCode();

            AuthorizationQRCodeParent = AuthorizationQRCode.transform.parent.gameObject;
            WeChatQRCodeParent = WeChatQRCode.transform.parent.gameObject;

            VersionText.text = "Version:" + Application.version;
            if (AuthorizationQRCodeParent != null) AuthorizationQRCodeParent.SetActive(false);
            if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(false);
            if (WeChatQRCodeText != null) WeChatQRCodeText.gameObject.SetActive(true);
        }

        // Start is called before the first frame update
        private void Start()
        {
            if (useLegacyAuthorizationSystem)
            {
                AuthorizationLog(
                    "检测到旧版授权数据：machineCode/DateCode/TimeCode，且不存在GrantSource。" +
                    "本次启动完全使用旧版授权流程，付款码与新版中英文Key不参与判断。");

                StopPaymentFlow(true);
                StartLegacyAuthorizationFlow();
                return;
            }

            // 兼容“已经有GrantSource、但仍使用共享machineCode/DateCode/TimeCode”的过渡版本。
            // 能识别语言时迁移到对应的新Key；迁移成功后删除共享Key，避免中英文继续串用。
            TryMigrateSharedNewVersionData();

            ValidateLocalGrantSourceAgainstQRCodeDisplayMode();
            RefreshDebugMachineCodeFromLocal();

            bool hasGrantSource = PlayerPrefs.HasKey(CurrentGrantSourceKey);
            hasLegacyGrantHistory = !hasGrantSource && HasLegacyGrantHistory();

            AuthorizationLog(
                "新版授权Key检查：Language=" + authorizationLanguageType +
                "，GrantSourceKey=" + CurrentGrantSourceKey +
                "，GrantSource=" + hasGrantSource +
                "，GrantSourceValue=" + (hasGrantSource ? GetLocalGrantSource().ToString() : "None") +
                "，DateKey=" + CurrentDateKey + "=" + PlayerPrefs.HasKey(CurrentDateKey) +
                "，TimeKey=" + CurrentTimeKey + "=" + PlayerPrefs.HasKey(CurrentTimeKey) +
                "，MachineKey=" + CurrentMachineKey + "=" + PlayerPrefs.HasKey(CurrentMachineKey) +
                "，LegacyHistory=" + hasLegacyGrantHistory);

            PrintLocalGrantDate();

            // 重要：
            // 启动时不再先用注册表判断是否进入游戏。
            // 只要网络请求能够正常完成，就必须以后台最新状态为准。
            // 只有 HTTPRequest 进入 Error / ConnectionTimedOut / TimedOut 时，
            // 才允许使用当前语言的新版本地缓存进行离线验证。
            authorizationGrantExpired = false;
            paymentGrantExpired = false;
            localTimeInvalid = false;

            AuthorizationLog(
                "启动新版授权流程：Language=" + authorizationLanguageType +
                "，HardwareCode=" + HardwareCode +
                "，ProductCode=" + GetCurrentProductCode());

            CheckPaidOnStartup();
        }

        private bool Check设备合法授权(LocalAuthCheckContext context = LocalAuthCheckContext.Online)
        {
            string V1 = string.Empty;
            string V2 = string.Empty;

            if (!TryPlayerGet(CurrentDateKey, HardwareCode, ref V1, ref V2))
            {
                SetText2("本地授权数据异常，请重新授权。");
                return false;
            }

            Debug.Log("HardwareCode==" + HardwareCode);
            Debug.Log("machineCode==" + machineCode);
            Debug.Log("CurrentDateKey==" + V1);
            Debug.Log("V2==" + V2);

            DebugManager.Instance.AddData("设备编码", machineCode);

            if (V1.Equals(ExpirationTime))
            {
                return true;
            }

            if (!V1.Equals(V2))
            {
                SetText2("本地授权数据异常，请重新联网验证。");
                return false;
            }

            DateTime AuthTime;

            if (!DateTime.TryParse(V1, out AuthTime))
            {
                DeleteCorruptedLocalKey(CurrentDateKey, "授权时间格式异常");
                SetText2("授权时间格式异常，请重新授权。");
                return false;
            }

            if (DateTime.Compare(AuthTime, GetBeiJingTime()) < 0)
            {
                bool hasGrantSource = PlayerPrefs.HasKey(CurrentGrantSourceKey);

                if (!hasGrantSource)
                {
                    // 旧版本没有记录授权来源。
                    // 按兼容规则：只要旧授权数据已经到期，就一律只允许重新授权，不允许付款。
                    authorizationGrantExpired = true;
                    StopPaymentFlow(true);
                    if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(false);
                    AuthorizationLog("旧版本授权数据已到期（无GrantSource），只允许重新授权。");
                    return false;
                }

                GrantSourceType source = GetLocalGrantSource();

                if (source == GrantSourceType.Payment)
                {
                    ShowPaymentGrantExpired();
                    return false;
                }

                paymentGrantExpired = false;
                authorizationGrantExpired = true;
                StopPaymentFlow(true);
                if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(false);
                AuthorizationLog("授权码获取的授权已经到期，等待重新获取授权码。");
                return false;
            }

            V1 = string.Empty;
            V2 = string.Empty;

            if (!TryPlayerGet(CurrentMachineKey, HardwareCode, ref V1, ref V2))
            {
                SetText2("设备授权数据异常，请重新授权。");
                return false;
            }

            if (!V1.Equals(V2))
            {
                return false;
            }

            machineCode = V1;
            DebugManager.Instance.AddData("设备编码", machineCode);

            V1 = string.Empty;
            V2 = string.Empty;

            if (!TryPlayerGet(CurrentTimeKey, HardwareCode, ref V1, ref V2))
            {
                SetText2("系统时间数据异常，请重新联网授权。");
                return false;
            }

            if (!V1.Equals(V2))
            {
                SetText2("本地时间数据异常，请重新联网验证。");
                return false;
            }

            DateTime LastTime;

            if (!DateTime.TryParse(V1, out LastTime))
            {
                DeleteCorruptedLocalKey(CurrentTimeKey, "本地时间格式异常");
                SetText2("本地时间数据异常，请重新授权。");
                return false;
            }

            TimeSpan temp = GetBeiJingTime() - LastTime;

            if (temp.TotalSeconds <= -ErrorRange)
            {
                ShowLocalTimeInvalid(context);
                return false;
            }

            if (localTimeInvalid)
            {
                localTimeInvalid = false;
                AuthorizationLog("本地时间已经恢复正常，解除时间异常锁定。");
                Text2Hide();
            }

            AESEncryption.PlayerSet(CurrentTimeKey, GetBeiJingTime().ToString(), HardwareCode);
            Debug.Log(GetBeiJingTime().ToString());

            return true;
        }

        /// <summary>
        /// 锁定本次授权流程使用的语言。
        /// ProductCode、HardwareCode、本地Key全部使用同一份语言状态。
        /// </summary>
        private void InitializeAuthorizationLanguage()
        {
            authorizationLanguageType =
                LanguageManger.Instance.GetLanguageType();
            authorizationLanguageInitialized = true;
        }

        private string BuildLanguageHardwareCode(string baseHardwareCode, bool english)
        {
            string languageTag = english ? "EN" : "CN";
            string source =
                (baseHardwareCode ?? string.Empty) +
                "|LANG=" +
                languageTag;

            using (MD5 md5 = MD5.Create())
            {
                byte[] hash =
                    md5.ComputeHash(
                        Encoding.UTF8.GetBytes(source));

                StringBuilder builder =
                    new StringBuilder(hash.Length * 2);

                for (int i = 0; i < hash.Length; i++)
                {
                    builder.Append(hash[i].ToString("X2"));
                }

                return builder.ToString();
            }
        }

        /// <summary>
        /// 新版：根据授权语言生成独立HardwareCode。
        /// 同一台电脑中文和英文得到不同HardwareCode。
        /// </summary>
        private string GetLanguageHardwareCode()
        {
            string baseHardwareCode = HardwareCodeUtil.GetMac();
            return BuildLanguageHardwareCode(
                baseHardwareCode,
                IsEnglishAuthorizationLanguage);
        }

        private string GetHardwareCodeForLanguage(bool english)
        {
            return BuildLanguageHardwareCode(
                HardwareCodeUtil.GetMac(),
                english);
        }

        /// <summary>
        /// 旧版使用原始HardwareCode；新版使用带语言隔离的HardwareCode。
        /// </summary>
        private void IsNullHardwareCode()
        {
            if (!string.IsNullOrEmpty(HardwareCode))
            {
                return;
            }

            HardwareCode = useLegacyAuthorizationSystem
                ? HardwareCodeUtil.GetMac()
                : GetLanguageHardwareCode();

            AuthorizationLog(
                "授权系统=" +
                (useLegacyAuthorizationSystem ? "Legacy" : "New") +
                "，Language=" + authorizationLanguageType +
                "，HardwareCode=" + HardwareCode +
                (useLegacyAuthorizationSystem
                    ? string.Empty
                    : "，ProductCode=" + GetCurrentProductCode()));
        }

        /// <summary>
        /// 旧版识别规则：
        /// 只要旧版三个Key中任意一个存在，并且不存在共享GrantSource，就优先走旧版流程。
        /// 这是为了兼容旧脚本在Tag=0时只删DateCode/TimeCode、保留machineCode的情况。
        /// </summary>
        private bool HasLegacySystemData()
        {
            if (PlayerPrefs.HasKey(GrantSourceKey))
            {
                return false;
            }

            return PlayerPrefs.HasKey(MachineKey) ||
                   PlayerPrefs.HasKey(DateKey) ||
                   PlayerPrefs.HasKey(TimeKey);
        }

        private string GetNewMachineKey(bool english)
        {
            return english ? NewMachineKey_EN : NewMachineKey_CN;
        }

        private string GetNewDateKey(bool english)
        {
            return english ? NewDateKey_EN : NewDateKey_CN;
        }

        private string GetNewTimeKey(bool english)
        {
            return english ? NewTimeKey_EN : NewTimeKey_CN;
        }

        private string GetNewGrantSourceKey(bool english)
        {
            return english ? NewGrantSourceKey_EN : NewGrantSourceKey_CN;
        }

        private bool HasCompleteNewLanguageData(bool english)
        {
            return PlayerPrefs.HasKey(GetNewMachineKey(english)) &&
                   PlayerPrefs.HasKey(GetNewDateKey(english)) &&
                   PlayerPrefs.HasKey(GetNewTimeKey(english)) &&
                   PlayerPrefs.HasKey(GetNewGrantSourceKey(english));
        }

        private bool TryReadEncryptedValueNoDelete(
            string key,
            string hardwareCode,
            out string value)
        {
            value = string.Empty;
            string v1 = string.Empty;
            string v2 = string.Empty;

            if (!PlayerPrefs.HasKey(key))
            {
                return false;
            }

            try
            {
                AESEncryption.PlayerGet(
                    key,
                    hardwareCode,
                    ref v1,
                    ref v2);
            }
            catch
            {
                return false;
            }

            if (!v1.Equals(v2))
            {
                return false;
            }

            value = v1;
            return true;
        }

        private bool TryReadSharedNewVersionData(
            string hardwareCode,
            out string sharedMachineCode,
            out string sharedDateCode,
            out string sharedTimeCode,
            out string sharedGrantSource)
        {
            sharedMachineCode = string.Empty;
            sharedDateCode = string.Empty;
            sharedTimeCode = string.Empty;
            sharedGrantSource = string.Empty;

            return TryReadEncryptedValueNoDelete(
                       MachineKey,
                       hardwareCode,
                       out sharedMachineCode) &&
                   TryReadEncryptedValueNoDelete(
                       DateKey,
                       hardwareCode,
                       out sharedDateCode) &&
                   TryReadEncryptedValueNoDelete(
                       TimeKey,
                       hardwareCode,
                       out sharedTimeCode) &&
                   TryReadEncryptedValueNoDelete(
                       GrantSourceKey,
                       hardwareCode,
                       out sharedGrantSource);
        }

        private void SaveMigratedNewLanguageData(
            bool english,
            string targetHardwareCode,
            string sharedMachineCode,
            string sharedDateCode,
            string sharedTimeCode,
            string sharedGrantSource)
        {
            if (HasCompleteNewLanguageData(english))
            {
                AuthorizationLog(
                    "目标语言已经存在完整新版数据，不覆盖。Language=" +
                    (english ? "EN" : "CN"));
                return;
            }

            AESEncryption.PlayerSet(
                GetNewMachineKey(english),
                sharedMachineCode,
                targetHardwareCode);
            AESEncryption.PlayerSet(
                GetNewDateKey(english),
                sharedDateCode,
                targetHardwareCode);
            AESEncryption.PlayerSet(
                GetNewTimeKey(english),
                sharedTimeCode,
                targetHardwareCode);
            AESEncryption.PlayerSet(
                GetNewGrantSourceKey(english),
                sharedGrantSource,
                targetHardwareCode);

            PlayerPrefs.Save();
        }

        /// <summary>
        /// 兼容已有“GrantSource + 共享三Key”的过渡新版数据。
        /// 优先尝试按CN/EN语言HardwareCode解密，能够确定语言时迁移到对应语言；
        /// 如果只能用老HardwareCode解密，则无法知道当时语言，只迁移到本次启动语言。
        /// </summary>
        private void TryMigrateSharedNewVersionData()
        {
            if (!PlayerPrefs.HasKey(GrantSourceKey))
            {
                return;
            }

            string sharedMachineCode;
            string sharedDateCode;
            string sharedTimeCode;
            string sharedGrantSource;

            string cnHardwareCode = GetHardwareCodeForLanguage(false);
            string enHardwareCode = GetHardwareCodeForLanguage(true);
            string baseHardwareCode = HardwareCodeUtil.GetMac();

            bool targetEnglish = IsEnglishAuthorizationLanguage;
            string targetHardwareCode = HardwareCode;
            string sourceHardwareType = string.Empty;

            bool readSuccess = TryReadSharedNewVersionData(
                cnHardwareCode,
                out sharedMachineCode,
                out sharedDateCode,
                out sharedTimeCode,
                out sharedGrantSource);

            if (readSuccess)
            {
                targetEnglish = false;
                targetHardwareCode = cnHardwareCode;
                sourceHardwareType = "CN-LanguageHardwareCode";
            }
            else
            {
                readSuccess = TryReadSharedNewVersionData(
                    enHardwareCode,
                    out sharedMachineCode,
                    out sharedDateCode,
                    out sharedTimeCode,
                    out sharedGrantSource);

                if (readSuccess)
                {
                    targetEnglish = true;
                    targetHardwareCode = enHardwareCode;
                    sourceHardwareType = "EN-LanguageHardwareCode";
                }
                else
                {
                    readSuccess = TryReadSharedNewVersionData(
                        baseHardwareCode,
                        out sharedMachineCode,
                        out sharedDateCode,
                        out sharedTimeCode,
                        out sharedGrantSource);

                    if (readSuccess)
                    {
                        targetEnglish = IsEnglishAuthorizationLanguage;
                        targetHardwareCode = targetEnglish
                            ? enHardwareCode
                            : cnHardwareCode;
                        sourceHardwareType =
                            "LegacyBaseHardwareCode->CurrentLanguage";
                    }
                }
            }

            if (!readSuccess)
            {
                AuthorizationLogWarning(
                    "检测到共享GrantSource，但旧共享新版数据无法完整解密。" +
                    "为避免误删授权数据，暂时保留共享Key，新版当前语言将按自己的新Key继续检查。");
                return;
            }

            SaveMigratedNewLanguageData(
                targetEnglish,
                targetHardwareCode,
                sharedMachineCode,
                sharedDateCode,
                sharedTimeCode,
                sharedGrantSource);

            // 迁移成功后删除“共享新版”旧Key。
            // 从下一次启动开始只剩语言独立的新Key，不会再与旧版三Key混淆。
            if (PlayerPrefs.HasKey(MachineKey)) PlayerPrefs.DeleteKey(MachineKey);
            if (PlayerPrefs.HasKey(DateKey)) PlayerPrefs.DeleteKey(DateKey);
            if (PlayerPrefs.HasKey(TimeKey)) PlayerPrefs.DeleteKey(TimeKey);
            if (PlayerPrefs.HasKey(GrantSourceKey)) PlayerPrefs.DeleteKey(GrantSourceKey);
            PlayerPrefs.Save();

            AuthorizationLog(
                "共享新版授权数据迁移完成。SourceHardware=" +
                sourceHardwareType +
                "，TargetLanguage=" +
                (targetEnglish ? "EN" : "CN") +
                "，NewMachineKey=" + GetNewMachineKey(targetEnglish) +
                "，NewGrantSourceKey=" + GetNewGrantSourceKey(targetEnglish));
        }

        // ============================================================
        // 旧版授权系统
        // 规则：旧版只有 machineCode / DateCode / TimeCode 三个Key，
        //       使用原始 HardwareCodeUtil.GetMac() 作为加密HardwareCode。
        //       该流程不参与付款码，也不会写入任何新版Key。
        // ============================================================

        private void StartLegacyAuthorizationFlow()
        {
            machineCode = "未授权";
            RefreshLegacyDebugMachineCode();
            LegacyCheck(CheckState.检查授权, false);
        }

        private void RefreshLegacyDebugMachineCode()
        {
            if (!PlayerPrefs.HasKey(MachineKey))
            {
                DebugManager.Instance.AddData("设备编码", machineCode);
                return;
            }

            string v1 = string.Empty;
            string v2 = string.Empty;

            if (LegacyTryPlayerGet(
                MachineKey,
                HardwareCode,
                ref v1,
                ref v2))
            {
                if (v1.Equals(v2) && !string.IsNullOrWhiteSpace(v1))
                {
                    machineCode = v1;
                    DebugManager.Instance.AddData("设备编码", machineCode);
                }
            }
        }

        private bool LegacyCheckDeviceGrant()
        {
            string v1 = string.Empty;
            string v2 = string.Empty;

            if (!LegacyTryPlayerGet(
                DateKey,
                HardwareCode,
                ref v1,
                ref v2))
            {
                SetText2("本地授权数据异常，请重新授权。");
                return false;
            }

            Debug.Log("[Legacy] HardwareCode==" + HardwareCode);
            Debug.Log("[Legacy] DateKey==" + v1);
            Debug.Log("[Legacy] V2==" + v2);
            Debug.Log("[Legacy] machineCode==" + machineCode);
            DebugManager.Instance.AddData("设备编码", machineCode);

            if (v1.Equals(ExpirationTime))
            {
                return true;
            }

            if (!v1.Equals(v2))
            {
                SetText2("请联网更新系统时间...");
                return false;
            }

            DateTime authTime;
            if (!DateTime.TryParse(v1, out authTime))
            {
                LegacyClearLocalAuthCache();
                SetText2("授权时间格式异常，请重新授权。");
                return false;
            }

            if (DateTime.Compare(authTime, GetBeiJingTime()) < 0)
            {
                SetText2("请联网更新授权时间");
                return false;
            }

            v1 = string.Empty;
            v2 = string.Empty;

            if (!LegacyTryPlayerGet(
                MachineKey,
                HardwareCode,
                ref v1,
                ref v2))
            {
                SetText2("设备授权数据异常，请重新授权。");
                return false;
            }

            if (!v1.Equals(v2))
            {
                return false;
            }

            machineCode = v1;
            DebugManager.Instance.AddData("设备编码", machineCode);

            v1 = string.Empty;
            v2 = string.Empty;

            if (!LegacyTryPlayerGet(
                TimeKey,
                HardwareCode,
                ref v1,
                ref v2))
            {
                SetText2("系统时间数据异常，请重新联网授权。");
                return false;
            }

            if (!v1.Equals(v2))
            {
                SetText2("请联网更新系统时间!");
                return false;
            }

            DateTime lastTime;
            if (!DateTime.TryParse(v1, out lastTime))
            {
                LegacyClearLocalAuthCache();
                SetText2("本地时间数据异常，请重新授权。");
                return false;
            }

            TimeSpan temp = GetBeiJingTime() - lastTime;
            if (temp.TotalSeconds <= -ErrorRange)
            {
                SetText2("请联网更新系统时间。");
                return false;
            }

            AESEncryption.PlayerSet(
                TimeKey,
                GetBeiJingTime().ToString(),
                HardwareCode);
            PlayerPrefs.Save();

            return true;
        }

        private void LegacyCheck(CheckState checkState, bool isDelayed = true)
        {
            switch (checkState)
            {
                case CheckState.检查授权:
                    InvokeUtil.Instance.Run(
                        () =>
                        {
                            LegacyCheckMachineAuth(
                                LegacyOnCheckMachineAuth);
                        },
                        1.0f);
                    break;

                case CheckState.检查绑定用户:
                    InvokeUtil.Instance.Run(
                        () =>
                        {
                            LegacyCheckBind(
                                machineCode,
                                LegacyOnCheckBind);
                        },
                        1.0f);
                    break;

                case CheckState.检查是否设备合法性:
                    if (!LegacyCheckDeviceGrant())
                    {
                        LegacyCheck(CheckState.检查授权, isDelayed);
                    }
                    else
                    {
                        LoadTargetScene();
                    }
                    break;
            }
        }

        private void LegacyOnCheckMachineAuth(string js, bool success)
        {
            if (!success)
            {
                Is显示授权二维码 = false;
                LegacyCheck(CheckState.检查授权);
                return;
            }

            try
            {
                AuthorizationLog("[Legacy] 授权返回数据：" + js);
                JObject pairs = JObject.Parse(js);
                if (!pairs.ContainsKey("Tag"))
                {
                    LegacyCheck(CheckState.检查授权);
                    return;
                }

                int tag = (int)pairs.GetValue("Tag");
                JToken data = pairs.GetValue("Data");

                if (tag == 0)
                {
                    AuthorizationLog("[Legacy] 当前设备未授权，显示旧版授权二维码。");

                    if (!Is显示授权二维码)
                    {
                        Is显示授权二维码 = true;

                        string authorizationUrl =
                            data == null
                                ? string.Empty
                                : data.ToString();

                        if (!string.IsNullOrWhiteSpace(authorizationUrl))
                        {
                            CreatCode(
                                authorizationUrl,
                                "设备授权");
                        }

                        // 完全保留旧版行为：Tag=0时删Date/Time，不删machineCode。
                        if (PlayerPrefs.HasKey(DateKey))
                            PlayerPrefs.DeleteKey(DateKey);
                        if (PlayerPrefs.HasKey(TimeKey))
                            PlayerPrefs.DeleteKey(TimeKey);
                        PlayerPrefs.Save();

                        if (machineCode.Equals("未授权"))
                        {
                            string v1 = string.Empty;
                            string v2 = string.Empty;

                            if (LegacyTryPlayerGet(
                                MachineKey,
                                HardwareCode,
                                ref v1,
                                ref v2))
                            {
                                if (v1.Equals(v2))
                                {
                                    DebugManager.Instance.AddData(
                                        "设备编码",
                                        v1);
                                }
                            }
                        }
                    }

                    LegacyCheck(CheckState.检查授权);
                    return;
                }

                if (tag == 1)
                {
                    JObject authData = data as JObject;
                    if (authData == null)
                    {
                        LegacyCheck(CheckState.检查授权);
                        return;
                    }

                    machineCode = GetStringValue(
                        authData,
                        "MachineCode");
                    string expiresDate = GetStringValue(
                        authData,
                        "ExpiresDate");
                    string nowDate = GetStringValue(
                        authData,
                        "NowDate");

                    if (string.IsNullOrWhiteSpace(machineCode) ||
                        string.IsNullOrWhiteSpace(expiresDate))
                    {
                        SetText2("服务器数据获取失败...");
                        LegacyCheck(CheckState.检查授权);
                        return;
                    }

                    DebugManager.Instance.AddData(
                        "设备编码",
                        machineCode);

                    AESEncryption.PlayerSet(
                        DateKey,
                        expiresDate,
                        HardwareCode);
                    AESEncryption.PlayerSet(
                        TimeKey,
                        string.IsNullOrWhiteSpace(nowDate)
                            ? GetBeiJingTime().ToString()
                            : nowDate,
                        HardwareCode);
                    AESEncryption.PlayerSet(
                        MachineKey,
                        machineCode,
                        HardwareCode);
                    PlayerPrefs.Save();

                    AuthorizationLog(
                        "[Legacy] 旧版授权成功，继续旧版绑定流程。MachineCode=" +
                        machineCode);

                    LegacyCheck(CheckState.检查绑定用户, false);
                }
            }
            catch (Exception e)
            {
                Is显示授权二维码 = false;
                AuthorizationLogError(
                    "[Legacy] 授权返回解析错误：" +
                    e.Message);
                LegacyCheck(CheckState.检查授权);
            }
        }

        private void LegacyCheckMachineAuth(Action<string, bool> callback)
        {
            string url =
                HttpSeverUrl +
                "/Factory/CheckMachineAuth?hardwareCode=" +
                Uri.EscapeDataString(HardwareCode ?? string.Empty);

            HTTPRequest request = new HTTPRequest(
                new Uri(url),
                HTTPMethods.Get,
                (req, resp) =>
                {
                    switch (req.State)
                    {
                        case HTTPRequestStates.Finished:
                            if (resp != null &&
                                !string.IsNullOrEmpty(resp.DataAsText))
                            {
                                Text2Hide();
                                callback?.Invoke(
                                    resp.DataAsText,
                                    true);
                            }
                            else
                            {
                                SetText2("请检查网络是否连接");
                                Is显示授权二维码 = false;
                                callback?.Invoke(
                                    resp == null
                                        ? string.Empty
                                        : resp.DataAsText,
                                    false);
                            }
                            break;

                        case HTTPRequestStates.Error:
                        case HTTPRequestStates.Aborted:
                        case HTTPRequestStates.ConnectionTimedOut:
                        case HTTPRequestStates.TimedOut:
                        default:
                            SetText2("请检查网络是否连接!");
                            Is显示授权二维码 = false;
                            LegacyCheck(
                                CheckState.检查是否设备合法性);
                            break;
                    }
                });

            request.SetHeader("accept", "*/*");
            request.Send();
        }

        private void LegacyCheckBind(
            string legacyMachineCode,
            Action<string, bool> callback)
        {
            string url =
                HttpSeverUrl +
                "/Factory/CheckIsBind?machineCode=" +
                Uri.EscapeDataString(legacyMachineCode ?? string.Empty);

            HTTPRequest request = new HTTPRequest(
                new Uri(url),
                HTTPMethods.Get,
                (req, resp) =>
                {
                    switch (req.State)
                    {
                        case HTTPRequestStates.Finished:
                            if (resp != null &&
                                !string.IsNullOrEmpty(resp.DataAsText))
                            {
                                callback?.Invoke(
                                    resp.DataAsText,
                                    true);
                            }
                            else
                            {
                                SetText2("请检查网络是否连接");
                                Is显示授权二维码 = false;
                                callback?.Invoke(
                                    resp == null
                                        ? string.Empty
                                        : resp.DataAsText,
                                    false);
                            }
                            break;

                        case HTTPRequestStates.Error:
                        case HTTPRequestStates.Aborted:
                        case HTTPRequestStates.ConnectionTimedOut:
                        case HTTPRequestStates.TimedOut:
                        default:
                            SetText2("请检查网络是否连接!");
                            Is显示授权二维码 = false;
                            LegacyCheck(CheckState.检查绑定用户);
                            break;
                    }
                });

            request.SetHeader("accept", "*/*");
            request.Send();
        }

        private void LegacyOnCheckBind(string js, bool success)
        {
            if (!success)
            {
                Is显示授权二维码 = false;
                LegacyCheck(CheckState.检查绑定用户);
                return;
            }

            try
            {
                JObject pairs = JObject.Parse(js);
                if (!pairs.ContainsKey("Tag"))
                {
                    LegacyCheck(CheckState.检查绑定用户);
                    return;
                }

                int tag = (int)pairs.GetValue("Tag");
                if (tag == 0)
                {
                    StopPaymentFlow(true);
                    string url =
                        pairs.GetValue("Data") == null
                            ? string.Empty
                            : pairs.GetValue("Data").ToString();

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        SetText1("绑定用户");
                        if (!Is显示绑定二维码)
                        {
                            LegacyDownPicTexture(
                                url,
                                LegacyOnDownPicTexture);
                        }
                    }

                    LegacyCheck(CheckState.检查绑定用户);
                    return;
                }

                if (tag == 1)
                {
                    AuthorizationLog(
                        "[Legacy] 已经绑定设备，进入旧版本地合法性检查。");
                    LegacyCheck(
                        CheckState.检查是否设备合法性,
                        false);
                }
            }
            catch (Exception e)
            {
                AuthorizationLogError(
                    "[Legacy] 绑定返回解析错误：" +
                    e.Message);
                Is显示绑定二维码 = false;
                LegacyCheck(CheckState.检查绑定用户);
            }
        }

        private void LegacyDownPicTexture(
            string url,
            Action<Texture2D, bool> action)
        {
            HTTPRequest request = new HTTPRequest(
                new Uri(url),
                HTTPMethods.Get,
                (req, resp) =>
                {
                    switch (req.State)
                    {
                        case HTTPRequestStates.Finished:
                            if (resp != null &&
                                resp.DataAsTexture2D != null)
                            {
                                action?.Invoke(
                                    resp.DataAsTexture2D,
                                    true);
                                SetText1("绑定用户");
                            }
                            else
                            {
                                action?.Invoke(null, false);
                            }
                            break;

                        case HTTPRequestStates.Error:
                        case HTTPRequestStates.Aborted:
                        case HTTPRequestStates.ConnectionTimedOut:
                        case HTTPRequestStates.TimedOut:
                        default:
                            SetText2("请检查网络是否连接!");
                            Is显示绑定二维码 = false;
                            LegacyDownPicTexture(url, action);
                            break;
                    }
                });

            request.SetHeader("accept", "*/*");
            request.Send();
        }

        private void LegacyOnDownPicTexture(
            Texture2D texture2D,
            bool success)
        {
            Is显示绑定二维码 = success;
            if (!success || texture2D == null)
            {
                return;
            }

            AuthorizationQRCode.texture = texture2D;
            SetText1("用户绑定设备");
        }

        private void LegacyClearLocalAuthCache()
        {
            if (PlayerPrefs.HasKey(DateKey))
                PlayerPrefs.DeleteKey(DateKey);
            if (PlayerPrefs.HasKey(TimeKey))
                PlayerPrefs.DeleteKey(TimeKey);
            if (PlayerPrefs.HasKey(MachineKey))
                PlayerPrefs.DeleteKey(MachineKey);

            PlayerPrefs.Save();
            machineCode = "未授权";
            DebugManager.Instance.AddData("设备编码", machineCode);

            AuthorizationLog(
                "[Legacy] 旧版本地授权缓存已清理。只删除machineCode/DateCode/TimeCode，不影响新版中英文Key。");
        }

        private bool LegacyTryPlayerGet(
            string key,
            string hardwareCode,
            ref string v1,
            ref string v2)
        {
            try
            {
                AESEncryption.PlayerGet(
                    key,
                    hardwareCode,
                    ref v1,
                    ref v2);
                return true;
            }
            catch (Exception e)
            {
                AuthorizationLogError(
                    "[Legacy] 读取旧版本地授权数据失败，按旧版规则清理三Key。Key=" +
                    key +
                    "，Error=" +
                    e.Message);
                LegacyClearLocalAuthCache();
                return false;
            }
        }

        private void SetText1(string msg)
        {
            // 付款已经确认成功后，任何之前遗留的授权/绑定回调都不允许再打开授权二维码。
            if (paymentSuccessConfirmed)
            {
                HideAuthorizationAndPaymentQRCodes();
                return;
            }

            if (localTimeInvalid)
            {
                if (AuthorizationQRCodeParent != null) AuthorizationQRCodeParent.SetActive(false);
                if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(false);
                return;
            }

            if (AuthorizationQRCodeParent != null) AuthorizationQRCodeParent.SetActive(true);
            if (!AuthorizationQRText.gameObject.activeInHierarchy)
            {
                AuthorizationQRCode.gameObject.SetActive(true);
                Text2Hide();
            }

            AuthorizationQRText.text = LanguageManger.Instance.GetValue(msg);
        }

        private void SetText2(string msg)
        {
            if (AuthorizationQRCodeParent != null) AuthorizationQRCodeParent.SetActive(false);
            if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(false);

            if (localTimeInvalid)
            {
                if (NetworkTipsText != null) NetworkTipsText.gameObject.SetActive(true);
                return;
            }

            if (NetworkTipsText != null)
            {
                NetworkTipsText.gameObject.SetActive(true);
                NetworkTipsText.text = LanguageManger.Instance.GetValue(msg);
            }
        }

        private void Text2Hide()
        {
            if (localTimeInvalid)
            {
                if (AuthorizationQRCodeParent != null) AuthorizationQRCodeParent.SetActive(false);
                if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(false);
                if (NetworkTipsText != null) NetworkTipsText.gameObject.SetActive(true);
                return;
            }

            if (NetworkTipsText != null && NetworkTipsText.gameObject.activeInHierarchy)
                NetworkTipsText.gameObject.SetActive(false);
        }

        private void Text1Hide()
        {
            if (AuthorizationQRCode.gameObject.activeInHierarchy)
                AuthorizationQRCode.gameObject.SetActive(false);
        }

        private void Check(CheckState checkState, bool IsDelayed = true)
        {
            // 付款成功后只允许继续执行最后的“设备合法性检查/进入场景”。
            // 之前排队的授权检查、绑定检查全部丢弃，避免重新显示授权二维码。
            if (paymentSuccessConfirmed &&
                checkState != CheckState.检查是否设备合法性)
            {
                HideAuthorizationAndPaymentQRCodes();
                return;
            }

            switch (checkState)
            {
                case CheckState.检查授权:
                    InvokeUtil.Instance.Run(() => { Check检查设备授权(On检查设备授权); }, 1.0f);
                    break;
                case CheckState.检查绑定用户:
                    InvokeUtil.Instance.Run(() => { Check检查绑定用户(machineCode, On检查绑定用户); }, 1.0f);
                    break;
                case CheckState.检查是否设备合法性:
                    if (!Check设备合法授权(LocalAuthCheckContext.Online))
                    {
                        if (localTimeInvalid)
                        {
                            ScheduleStartupNetworkRetry();
                        }
                        else if (!paymentGrantExpired)
                        {
                            Check(CheckState.检查授权, IsDelayed);
                        }
                    }
                    else
                    {
                        LoadTargetScene();
                    }
                    break;
                default:
                    break;
            }
        }

        /// <summary>
        /// 统一进入目标场景。
        /// 1. 防止多个授权回调同时重复加载场景；
        /// 2. 如果目标场景就是当前场景，则不再重新加载自己；
        /// 3. 真正切场景前停止授权相关的重试/轮询，避免旧回调继续触发。
        /// </summary>
        private void LoadTargetScene()
        {
            if (isSceneLoading)
            {
                AuthorizationLog("场景正在加载中，忽略重复的场景加载请求。Target=" + SceneName);
                return;
            }

            if (string.IsNullOrWhiteSpace(SceneName))
            {
                AuthorizationLogError("SceneName为空，无法进入目标场景。");
                return;
            }

            string currentSceneName =
                UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;

            if (string.Equals(
                currentSceneName,
                SceneName,
                StringComparison.Ordinal))
            {
                AuthorizationLog(
                    "当前已经处于目标场景，取消重复加载。Current=" +
                    currentSceneName +
                    "，Target=" +
                    SceneName);
                return;
            }

            // 从这里开始，后续所有回调都不能再次触发场景加载。
            isSceneLoading = true;

            if (networkRetryInvoke != null)
            {
                InvokeUtil.Instance.Remove(networkRetryInvoke);
                networkRetryInvoke = null;
            }

            // 停止付款轮询和付款重试，避免场景切换期间旧回调继续执行。
            StopPaymentFlow(true);

            AuthorizationLog(
                "授权验证通过，准备进入目标场景。Current=" +
                currentSceneName +
                "，Target=" +
                SceneName);

            UnityEngine.SceneManagement.SceneManager.LoadScene(SceneName);
        }

        private void On检查设备授权(string js, bool suc)
        {
            // 付款成功后，忽略付款成功前已经发出的授权请求回调。
            if (paymentSuccessConfirmed)
            {
                HideAuthorizationAndPaymentQRCodes();
                return;
            }

            if (suc)
            {
                try
                {
                    JObject pairs = JObject.Parse(js);

                    if (!pairs.ContainsKey("Tag"))
                    {
                        AuthorizationLogError("CheckMachineAuth返回中没有Tag，Response=" + js);
                        Check(CheckState.检查授权);
                        return;
                    }

                    int tag = (int)pairs.GetValue("Tag");
                    JToken data = pairs.GetValue("Data");

                    if (tag == 0)
                    {
                        string authorizationUrl = data == null ? string.Empty : data.ToString();
                        AuthorizationLog("[后台授权数据] Tag=0，当前设备未授权，Data=" + (string.IsNullOrWhiteSpace(authorizationUrl) ? "<空>" : authorizationUrl));
                        LogLocalKeySnapshot("收到Tag=0时");
                        AuthorizationLog("Tag=0只影响二维码显示逻辑，不删除DateCode、TimeCode、machineCode、GrantSource。");

                        HandleUnauthorizedQRCodeDisplay(authorizationUrl);
                        Check(CheckState.检查授权);
                        return;
                    }

                    if (tag != 1)
                    {
                        AuthorizationLogWarning("CheckMachineAuth返回未知Tag=" + tag + "，Response=" + js);
                        Check(CheckState.检查授权);
                        return;
                    }

                    JObject authData = data as JObject;

                    if (authData == null)
                    {
                        AuthorizationLogError("CheckMachineAuth返回Tag=1，但Data不是对象，Response=" + js);
                        Check(CheckState.检查授权);
                        return;
                    }

                    machineCode = GetStringValue(authData, "MachineCode");
                    string expiresDate = GetStringValue(authData, "ExpiresDate");
                    string nowDate = GetStringValue(authData, "NowDate");

                    AuthorizationLog("[后台授权数据] Tag=1，MachineCode=" + (string.IsNullOrWhiteSpace(machineCode) ? "<空>" : machineCode) + "，ExpiresDate=" + (string.IsNullOrWhiteSpace(expiresDate) ? "<空>" : expiresDate) + "，NowDate=" + (string.IsNullOrWhiteSpace(nowDate) ? "<空>" : nowDate));

                    if (string.IsNullOrWhiteSpace(machineCode) || string.IsNullOrWhiteSpace(expiresDate))
                    {
                        AuthorizationLogError("后台授权数据缺少MachineCode或ExpiresDate，Response=" + js);
                        SetText2("服务器数据获取失败...");
                        ScheduleStartupNetworkRetry();
                        return;
                    }

                    if (!IsGrantSourceAllowedByQRCodeDisplayMode(GrantSourceType.Authorization))
                    {
                        AuthorizationLog("后台返回有效授权码授权，但当前QRCodeDisplayMode=" + QRCodeDisplayMode + "不包含授权码，以QRCodeDisplayMode为准，不使用该授权数据。");
                        DeleteAllLocalGrantData("后台授权来源与QRCodeDisplayMode冲突");
                        HandleUnauthorizedQRCodeDisplay(string.Empty);
                        return;
                    }

                    paymentGrantExpired = false;
                    authorizationGrantExpired = false;
                    StopPaymentFlow(true);
                    SaveGrantData(machineCode, expiresDate, nowDate, GrantSourceType.Authorization);
                    AuthorizationLog("合法设备，已保存后台返回的授权数据，进入绑定流程。");
                    Check(CheckState.检查绑定用户, false);
                }
                catch (Exception e)
                {
                    Is显示授权二维码 = false;
                    AuthorizationLogError("CheckMachineAuth解析错误：" + e.Message + "，Response=" + js);
                    Check(CheckState.检查授权);
                }
            }
            else
            {
                IsNullHardwareCode();
                Is显示授权二维码 = false;
                ScheduleStartupNetworkRetry();
            }
        }

        /// <summary>
        /// 检查设备是否授权(app使用)  
        /// </summary>
        /// <param name="hardwareCode">硬件mac地址</param>
        /// <param name="cb">回调函数 js，err </param>
        /// 返回结果对象的Tag==1，合法授权Data字段则为备授权期唯一id（MachineCode） 返回结果对象的Tag==0，非合法授权Data字段则是含硬件码的授权地址，可用于生成二维码
        /// <returns></returns>
        public void Check检查设备授权(Action<string, bool> cb)
        {
            string url =
                HttpSeverUrl +
                "/Factory/CheckMachineAuth?hardwareCode=" +
                Uri.EscapeDataString(HardwareCode ?? string.Empty);

            HTTPRequest request = new HTTPRequest(
                new Uri(url),
                methodType: HTTPMethods.Get,
                (req, resp) =>
                {
                    switch (req.State)
                    {
                        case HTTPRequestStates.Finished:
                            if (localTimeInvalid) ShowLocalTimeInvalid(LocalAuthCheckContext.Online);

                            // Finished 说明请求已经到达服务器并收到响应。
                            // 这时不允许回退读取本地授权作为最终结果。
                            if (resp == null)
                            {
                                AuthorizationLogError(
                                    "CheckMachineAuth已Finished，但Response为空。");
                                SetText2("服务器数据获取失败...");
                                ScheduleStartupNetworkRetry();
                                return;
                            }

                            AuthorizationLog(
                                "Check检查设备授权，HTTP=" + resp.StatusCode +
                                "，Response=" + resp.DataAsText);

                            if (resp.StatusCode < 200 || resp.StatusCode >= 300)
                            {
                                AuthorizationLogError(
                                    "CheckMachineAuth服务器返回异常HTTP=" +
                                    resp.StatusCode);
                                SetText2("服务器数据获取失败...");
                                ScheduleStartupNetworkRetry();
                                return;
                            }

                            if (string.IsNullOrWhiteSpace(resp.DataAsText))
                            {
                                AuthorizationLogError(
                                    "CheckMachineAuth服务器响应为空。");
                                SetText2("服务器数据获取失败...");
                                ScheduleStartupNetworkRetry();
                                return;
                            }

                            // 后台数据有效，以后台最新状态为准。
                            Text2Hide();
                            cb?.Invoke(resp.DataAsText, true);
                            break;

                        case HTTPRequestStates.Error:
                        case HTTPRequestStates.ConnectionTimedOut:
                        case HTTPRequestStates.TimedOut:
                            AuthorizationLog(
                                "CheckMachineAuth网络失败，State=" +
                                req.State +
                                "，此时才允许读取本地注册表授权数据。");

                            HandleOfflineGrantFallback(
                                "CheckMachineAuth",
                                req.State);
                            break;

                        case HTTPRequestStates.Aborted:
                            AuthorizationLog(
                                "CheckMachineAuth请求被取消，不读取本地授权。");
                            break;

                        default:
                            AuthorizationLogWarning(
                                "CheckMachineAuth未处理状态：" + req.State);
                            break;
                    }
                });

            request.SetHeader("accept", "*/*");
            request.Send();
        }

        /// <summary>
        /// 检查设备是否绑定用户(app使用)  
        /// </summary>
        /// <param name="machineCode">用户绑定获取的唯一编号</param>
        /// <param name="cb">回调函数 js，err </param>
        /// 返回结果对象的Tag==1，已经绑定 返回结果对象的Tag==0，未绑定，Data返回设备二维码图片连接
        /// <returns></returns>
        public void Check检查绑定用户(string machineCode, Action<string, bool> cb)
        {
            string url = HttpSeverUrl + "/Factory/CheckIsBind" + "?machineCode=" + machineCode;
            HTTPRequest request = new HTTPRequest(new Uri(url), methodType: HTTPMethods.Get, (req, resp) =>
            {
                switch (req.State)
                {
                    case HTTPRequestStates.Finished:
                        AuthorizationLog("Check检查绑定用户");
                        if (!string.IsNullOrEmpty(resp.DataAsText))
                        {
                            cb?.Invoke(resp.DataAsText, true);
                        }
                        else
                        {
                            SetText2("请连接网络...");
                            Is显示授权二维码 = false;
                            cb?.Invoke(resp.DataAsText, false);
                        }
                        break;
                    case HTTPRequestStates.Error:
                    case HTTPRequestStates.Aborted:
                    case HTTPRequestStates.ConnectionTimedOut:
                    case HTTPRequestStates.TimedOut:
                    default:
                        SetText2("请连接网络...");
                        Is显示授权二维码 = false;
                        Check(CheckState.检查绑定用户);
                        break;
                }
            });
            request.SetHeader("accept", "*/*");
            request.Send();
        }

        private void On检查绑定用户(string js, bool suc)
        {
            // 付款成功后，忽略付款成功前已经发出的绑定请求回调。
            if (paymentSuccessConfirmed)
            {
                HideAuthorizationAndPaymentQRCodes();
                return;
            }

            if (suc)
            {
                try
                {
                    JObject pairs = JObject.Parse(js);
                    if (pairs.ContainsKey("Tag"))
                    {
                        int Tag = (int)pairs.GetValue("Tag");
                        if (Tag == 0)
                        {
                            StopPaymentFlow(true);
                            string temp = pairs.GetValue("Data").ToString();
                            if (!string.IsNullOrWhiteSpace(temp))
                            {
                                string url = pairs.GetValue("Data").ToString();
                                SetText1("绑定用户");
                                if (!Is显示绑定二维码)
                                {
                                    DownPicTexture(url, OnDownPicTexture);
                                }
                            }
                            Check(CheckState.检查绑定用户);
                        }
                        else if (Tag == 1)
                        {
                            AuthorizationLog("已经绑定设备，进入游戏");
                            Check(CheckState.检查是否设备合法性, false);
                        }
                    }
                }
                catch (Exception e)
                {
                    AuthorizationLog("解析错误:" + e.Message);
                    Is显示绑定二维码 = false;
                    Check(CheckState.检查绑定用户);
                }
            }
            else
            {
                IsNullHardwareCode();
                Is显示绑定二维码 = false;
                Check(CheckState.检查绑定用户);
            }
        }

        private void DownPicTexture(string url, Action<Texture2D, bool> action)
        {
            HTTPRequest request = new HTTPRequest(new Uri(url), methodType: HTTPMethods.Get, (req, resp) =>
            {
                switch (req.State)
                {
                    case HTTPRequestStates.Finished:
                        action?.Invoke(resp.DataAsTexture2D, true);
                        SetText1("绑定用户");
                        break;
                    case HTTPRequestStates.Error:
                    case HTTPRequestStates.Aborted:
                    case HTTPRequestStates.ConnectionTimedOut:
                    case HTTPRequestStates.TimedOut:
                    default:
                        SetText2("请连接网络...");
                        Is显示绑定二维码 = false;
                        DownPicTexture(url, action);
                        break;
                }
            });
            request.SetHeader("accept", "*/*");
            request.Send();
        }

        private void OnDownPicTexture(Texture2D texture2D, bool suc)
        {
            Is显示绑定二维码 = suc;
            if (suc)
            {
                AuthorizationQRCode.texture = texture2D;
                SetText1("用户绑定设备");
            }
        }

        private void CreatCode(string url, string tips = "设备授权")
        {
            var rect = AuthorizationQRCode.rectTransform.rect;
            QrCodeEncodingOptions options = new QrCodeEncodingOptions();
            options.CharacterSet = "UTF-8";
            options.DisableECI = true;
            options.ErrorCorrection = ZXing.QrCode.Internal.ErrorCorrectionLevel.H;
            options.Width = (int)rect.width;
            options.Height = (int)rect.height;
            options.Margin = 1;
            BarcodeWriter writer = new BarcodeWriter();
            writer.Format = BarcodeFormat.QR_CODE;
            writer.Options = options;

            var Lastresult = url;
            //Debug.Log(Lastresult);
            var colors = writer.Write(Lastresult);

            var texture = new Texture2D((int)rect.width, (int)rect.height, TextureFormat.RGB24, false, false);
            texture.SetPixels32(colors);
            texture.Apply();
            AuthorizationQRCode.texture = texture;

            SetText1(tips);
        }

        private void HandleUnauthorizedQRCodeDisplay(string authorizationUrl)
        {
            if (localTimeInvalid)
            {
                if (AuthorizationQRCodeParent != null) AuthorizationQRCodeParent.SetActive(false);
                if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(false);
                return;
            }

            if (paymentGrantExpired)
            {
                ShowPaymentGrantExpired();
                return;
            }

            bool hasGrantSource = PlayerPrefs.HasKey(CurrentGrantSourceKey);
            GrantSourceType source = hasGrantSource ? GetLocalGrantSource() : GrantSourceType.Unknown;
            bool hasAnyLocalGrantHistory = PlayerPrefs.HasKey(CurrentMachineKey) || PlayerPrefs.HasKey(CurrentDateKey) || PlayerPrefs.HasKey(CurrentTimeKey) || PlayerPrefs.HasKey(CurrentGrantSourceKey);
            isFirstUnauthorizedDualQRCode = !hasAnyLocalGrantHistory && QRCodeDisplayMode == UnauthorizedQRCodeMode.授权码加付款码;

            bool localExpired = false;
            string localExpiresDate = string.Empty;
            bool hasReadableLocalDate = TryGetLocalGrantExpireState(out localExpiresDate, out localExpired);

            AuthorizationLog("[授权未授权判定] 后台Tag=0，GrantSource=" + source + "，本地DateCode可读=" + hasReadableLocalDate + "，本地ExpiresDate=" + (string.IsNullOrWhiteSpace(localExpiresDate) ? "<空>" : localExpiresDate) + "，本地日期已过期=" + localExpired + "，当前北京时间=" + GetBeiJingTime().ToString("yyyy-MM-dd HH:mm:ss"));

            bool authorizationSourceExpired = source == GrantSourceType.Authorization;

            if (!hasGrantSource && HasLegacyGrantHistory())
            {
                authorizationSourceExpired = true;
                AuthorizationLog("检测到旧版本授权历史且没有GrantSource，兼容旧版本，按授权码历史授权失效处理。");
            }

            if (authorizationSourceExpired)
            {
                authorizationGrantExpired = true;

                StopPaymentFlow(true);

                if (WeChatQRCodeParent != null)
                {
                    WeChatQRCodeParent.SetActive(false);
                }

                AuthorizationLog("后台返回未授权，并且确认本地授权来源为Authorization，按授权过期处理，只显示授权二维码，禁止显示付款码。");

                if (!Is显示授权二维码 && !string.IsNullOrWhiteSpace(authorizationUrl))
                {
                    Is显示授权二维码 = true;
                    CreatCode(authorizationUrl, "设备到期，请重新授权");
                }
                else if (Is显示授权二维码)
                {
                    SetText1("设备到期，请重新授权");
                }

                return;
            }

            authorizationGrantExpired = false;

            bool showAuthorization = QRCodeDisplayMode == UnauthorizedQRCodeMode.授权码 || QRCodeDisplayMode == UnauthorizedQRCodeMode.授权码加付款码;
            bool showPayment = QRCodeDisplayMode == UnauthorizedQRCodeMode.付款码 || QRCodeDisplayMode == UnauthorizedQRCodeMode.授权码加付款码;

            if (showAuthorization)
            {
                string authorizationTips = isFirstUnauthorizedDualQRCode ? "授权码" : "设备授权";

                if (!Is显示授权二维码 && !string.IsNullOrWhiteSpace(authorizationUrl))
                {
                    Is显示授权二维码 = true;
                    CreatCode(authorizationUrl, authorizationTips);
                }
                else if (Is显示授权二维码)
                {
                    SetText1(authorizationTips);
                }
            }
            else
            {
                Is显示授权二维码 = false;

                if (AuthorizationQRCodeParent != null)
                {
                    AuthorizationQRCodeParent.SetActive(false);
                }
            }

            if (showPayment)
            {
                EnsurePaymentOrder();
            }
            else
            {
                isFirstUnauthorizedDualQRCode = false;
                StopPaymentFlow(true);

                if (WeChatQRCodeParent != null)
                {
                    WeChatQRCodeParent.SetActive(false);
                }
            }
        }

        private bool TryGetLocalGrantExpireState(out string expiresDate, out bool expired)
        {
            expiresDate = string.Empty;
            expired = false;
            if (!PlayerPrefs.HasKey(CurrentDateKey)) return false;

            string v1 = string.Empty;
            string v2 = string.Empty;
            try
            {
                AESEncryption.PlayerGet(CurrentDateKey, HardwareCode, ref v1, ref v2);
            }
            catch (Exception e)
            {
                AuthorizationLogError("[本地授权日期] DateCode读取失败：" + e.Message);
                return false;
            }

            expiresDate = v1;
            if (!v1.Equals(v2))
            {
                AuthorizationLogError("[本地授权日期] DateCode校验失败，明文与解密结果不一致，不能用于判断是否过期。");
                return false;
            }

            if (v1.Equals(ExpirationTime))
            {
                AuthorizationLog("[本地授权日期] ExpiresDate=" + v1 + "，永久授权，Expired=False");
                return true;
            }

            DateTime authTime;
            if (!DateTime.TryParse(v1, out authTime))
            {
                AuthorizationLogError("[本地授权日期] DateCode时间格式错误，Value=" + v1);
                return false;
            }

            DateTime now = GetBeiJingTime();
            expired = DateTime.Compare(authTime, now) < 0;
            AuthorizationLog("[本地授权日期] ExpiresDate=" + authTime.ToString("yyyy-MM-dd HH:mm:ss") + "，当前北京时间=" + now.ToString("yyyy-MM-dd HH:mm:ss") + "，Expired=" + expired);
            return true;
        }

        private GrantSourceType GetLocalGrantSource()
        {
            if (!PlayerPrefs.HasKey(CurrentGrantSourceKey)) return GrantSourceType.Unknown;

            string V1 = string.Empty;
            string V2 = string.Empty;

            try
            {
                AESEncryption.PlayerGet(CurrentGrantSourceKey, HardwareCode, ref V1, ref V2);
            }
            catch
            {
                return GrantSourceType.Unknown;
            }

            if (!V1.Equals(V2)) return GrantSourceType.Unknown;

            GrantSourceType source;
            return Enum.TryParse(V1, out source) ? source : GrantSourceType.Unknown;
        }

        private bool IsAuthorizationGrantExpired()
        {
            if (!PlayerPrefs.HasKey(CurrentDateKey)) return false;

            // 新版存在 GrantSource 时按来源处理：
            // Payment 到期不进入“强制重新授权”状态；
            // Authorization / Unknown 到期则只允许重新授权。
            if (PlayerPrefs.HasKey(CurrentGrantSourceKey))
            {
                GrantSourceType source = GetLocalGrantSource();
                if (source == GrantSourceType.Payment) return false;
            }

            // 没有 CurrentGrantSourceKey 代表旧版本授权数据。
            // 旧数据只要 DateCode 已经过期，就按“历史授权到期”处理，只允许重新授权。
            return IsLegacyGrantExpired();
        }

        private bool IsLegacyGrantExpired()
        {
            if (!PlayerPrefs.HasKey(CurrentDateKey)) return false;

            string V1 = string.Empty;
            string V2 = string.Empty;

            try
            {
                AESEncryption.PlayerGet(CurrentDateKey, HardwareCode, ref V1, ref V2);
            }
            catch (Exception e)
            {
                AuthorizationLogError("读取旧版本授权到期时间失败：" + e.Message);
                return false;
            }

            if (!V1.Equals(V2)) return false;
            if (V1.Equals(ExpirationTime)) return false;

            DateTime authTime;
            if (!DateTime.TryParse(V1, out authTime)) return false;

            bool expired = DateTime.Compare(authTime, GetBeiJingTime()) < 0;
            if (expired)
            {
                AuthorizationLog("检测到旧版本授权已到期，DateCode=" + V1);
            }
            return expired;
        }

        /// <summary>
        /// 有网络时先检查是否已经付过款；请求失败则直接复用原来的本地授权合法性验证。
        /// </summary>
        private void CheckPaidOnStartup()
        {
            if (networkRetryInvoke != null)
            {
                InvokeUtil.Instance.Remove(networkRetryInvoke);
                networkRetryInvoke = null;
            }

            string currentProductCode = GetCurrentProductCode();

            string url = PaySeverUrl + CheckPayUrl + "?productCode=" + Uri.EscapeDataString(currentProductCode ?? string.Empty) + "&hardwareCode=" + Uri.EscapeDataString(HardwareCode ?? string.Empty);

            JObject body = new JObject();
            body["productCode"] = currentProductCode ?? string.Empty;
            body["hardwareCode"] = HardwareCode ?? string.Empty;

            HTTPRequest request = new HTTPRequest(new Uri(url), HTTPMethods.Post, (req, resp) =>
            {
                LogPayHttp("CheckPaid", url, req, resp);

                switch (req.State)
                {
                    case HTTPRequestStates.Finished:
                        if (localTimeInvalid) ShowLocalTimeInvalid(LocalAuthCheckContext.Online);

                        if (resp == null)
                        {
                            AuthorizationLogError("CheckPaid已Finished，但Response为空。");
                            SetText2("服务器数据获取失败...");
                            ScheduleStartupNetworkRetry();
                            return;
                        }

                        if (resp.StatusCode < 200 || resp.StatusCode >= 300)
                        {
                            AuthorizationLogError("CheckPaid服务器返回异常HTTP=" + resp.StatusCode + "，不会回退本地授权。");
                            SetText2("服务器数据获取失败...");
                            ScheduleStartupNetworkRetry();
                            return;
                        }

                        if (string.IsNullOrWhiteSpace(resp.DataAsText))
                        {
                            AuthorizationLogError("CheckPaid请求成功，但后台返回内容为空。");
                            SetText2("服务器数据获取失败...");
                            ScheduleStartupNetworkRetry();
                            return;
                        }

                        try
                        {
                            JObject root = JObject.Parse(resp.DataAsText);
                            JObject data = root["Data"] as JObject;

                            bool isPaid = data != null && GetBool(data, "IsPaid");
                            bool machineValid = data != null && GetBool(data, "MachineValid");
                            string paidMachineCode = data == null ? string.Empty : GetStringValue(data, "MachineCode");
                            string serverExpiresDate = data == null ? string.Empty : GetStringValue(data, "ExpiresDate");
                            string paidHardwareCode = data == null ? string.Empty : GetStringValue(data, "HardwareCode");
                            string nowDate = data == null || data["NowDate"] == null ? string.Empty : data["NowDate"].ToString();

                            AuthorizationLog("[后台付款数据] IsPaid=" + isPaid + "，MachineValid=" + machineValid + "，MachineCode=" + (string.IsNullOrWhiteSpace(paidMachineCode) ? "<空>" : paidMachineCode) + "，ExpiresDate=" + (string.IsNullOrWhiteSpace(serverExpiresDate) ? "<空>" : serverExpiresDate) + "，当前北京时间=" + GetBeiJingTime().ToString("yyyy-MM-dd HH:mm:ss"));

                            bool paymentAllowed = IsGrantSourceAllowedByQRCodeDisplayMode(GrantSourceType.Payment);
                            bool serverHardwareMatched = string.IsNullOrWhiteSpace(paidHardwareCode) || string.Equals(paidHardwareCode, HardwareCode, StringComparison.OrdinalIgnoreCase);

                            if (paymentAllowed && serverHardwareMatched && !string.IsNullOrWhiteSpace(paidMachineCode) && IsValidGrantDateString(serverExpiresDate))
                            {
                                SaveLatestExpireDate(serverExpiresDate, nowDate);
                            }

                            if (isPaid && !paymentAllowed)
                            {
                                AuthorizationLog("CheckPaid检测到付款授权，但当前QRCodeDisplayMode=" + QRCodeDisplayMode + "不包含付款码，忽略付款授权并继续检查授权码。");
                            }

                            if (isPaid && paymentAllowed)
                            {
                                if (!string.IsNullOrWhiteSpace(paidHardwareCode) && !string.Equals(paidHardwareCode, HardwareCode, StringComparison.OrdinalIgnoreCase))
                                {
                                    AuthorizationLogError("CheckPaid返回的HardwareCode与当前设备不一致，继续请求后台授权数据。");
                                    paymentGrantExpired = false;
                                    Check检查设备授权(On检查设备授权);
                                    return;
                                }

                                string effectiveExpiresDate = serverExpiresDate;
                                bool usedLocalExpiresDate = false;
                                bool expiresDateValid = IsValidGrantDateString(effectiveExpiresDate);

                                if (!expiresDateValid)
                                {
                                    bool localExpired;
                                    string localExpiresDate;
                                    bool hasReadableLocalDate = TryGetLocalGrantExpireState(out localExpiresDate, out localExpired);

                                    if (hasReadableLocalDate)
                                    {
                                        effectiveExpiresDate = localExpiresDate;
                                        usedLocalExpiresDate = true;
                                        expiresDateValid = true;
                                        AuthorizationLog("CheckPaid返回的ExpiresDate异常，已改用本地DateCode。ServerExpiresDate=" + (string.IsNullOrWhiteSpace(serverExpiresDate) ? "<空>" : serverExpiresDate) + "，LocalExpiresDate=" + localExpiresDate);
                                    }
                                    else
                                    {
                                        AuthorizationLogWarning("CheckPaid返回的ExpiresDate异常，并且本地DateCode也不可用，继续请求后台授权数据。");
                                    }
                                }

                                if (expiresDateValid)
                                {
                                    bool permanent = string.Equals(effectiveExpiresDate, ExpirationTime, StringComparison.Ordinal);
                                    DateTime expiresTime = DateTime.MaxValue;

                                    if (!permanent && !DateTime.TryParse(effectiveExpiresDate, out expiresTime))
                                    {
                                        AuthorizationLogError("付款授权日期解析失败，ExpiresDate=" + effectiveExpiresDate);
                                        paymentGrantExpired = false;
                                        Check检查设备授权(On检查设备授权);
                                        return;
                                    }

                                    bool expired = !permanent && expiresTime <= GetBeiJingTime();
                                    AuthorizationLog("[付款授权日期检查] ExpiresDate=" + effectiveExpiresDate + "，DateSource=" + (usedLocalExpiresDate ? "LocalDateCode" : "CheckPaid") + "，当前北京时间=" + GetBeiJingTime().ToString("yyyy-MM-dd HH:mm:ss") + "，Expired=" + expired);

                                    if (expired)
                                    {
                                        ShowPaymentGrantExpired();
                                        return;
                                    }

                                    bool canUsePaidGrant = !string.IsNullOrWhiteSpace(paidMachineCode) && (machineValid || usedLocalExpiresDate);

                                    if (canUsePaidGrant)
                                    {
                                        paymentGrantExpired = false;
                                        authorizationGrantExpired = false;
                                        SaveGrantData(paidMachineCode, effectiveExpiresDate, nowDate, GrantSourceType.Payment);
                                        CompletePaymentSuccessUI();
                                        Text2Hide();
                                        Check(CheckState.检查是否设备合法性, false);
                                        return;
                                    }

                                    AuthorizationLog("CheckPaid确认存在付款记录，但MachineValid=False且后台日期本身有效，不使用本地日期覆盖后台其它有效性判断，继续请求授权接口。");
                                }
                            }

                            paymentGrantExpired = false;
                            AuthorizationLog("后台没有可直接使用的有效付款授权，继续请求后台最新设备授权。HTTP=" + resp.StatusCode);
                            Check检查设备授权(On检查设备授权);
                        }
                        catch (Exception e)
                        {
                            AuthorizationLogError("CheckPaid返回解析错误，HTTP=" + resp.StatusCode + "，Error=" + e.Message + "，Response=" + resp.DataAsText);
                            SetText2("服务器数据获取失败...");
                            ScheduleStartupNetworkRetry();
                        }
                        break;

                    case HTTPRequestStates.Error:
                    case HTTPRequestStates.ConnectionTimedOut:
                    case HTTPRequestStates.TimedOut:
                        AuthorizationLog("CheckPaid网络请求失败，State=" + req.State + "，此时才使用本地付款/授权缓存。");
                        HandleOfflineGrantFallback("CheckPaid", req.State);
                        break;

                    case HTTPRequestStates.Aborted:
                        AuthorizationLog("CheckPaid请求已取消，不按断网处理，也不读取本地授权。");
                        break;

                    default:
                        AuthorizationLogWarning("CheckPaid出现未处理状态：" + req.State);
                        break;
                }
            });

            SetPayHeaders(request, true);
            request.RawData = Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));
            request.Send();
        }

        private void SaveLatestExpireDate(string expiresDate, string nowDate)
        {
            if (!IsValidGrantDateString(expiresDate))
            {
                return;
            }

            string oldExpiresDate = string.Empty;
            string oldDecryptedDate = string.Empty;

            if (PlayerPrefs.HasKey(CurrentDateKey))
            {
                try
                {
                    AESEncryption.PlayerGet(CurrentDateKey, HardwareCode, ref oldExpiresDate, ref oldDecryptedDate);
                }
                catch (Exception e)
                {
                    AuthorizationLogError("更新后台最新授权日期前读取本地DateCode失败，Error=" + e.Message);
                }
            }

            AESEncryption.PlayerSet(CurrentDateKey, expiresDate, HardwareCode);

            if (!string.IsNullOrWhiteSpace(nowDate))
            {
                DateTime serverNow;
                if (DateTime.TryParse(nowDate, out serverNow))
                {
                    AESEncryption.PlayerSet(CurrentTimeKey, nowDate, HardwareCode);
                }
            }

            PlayerPrefs.Save();
            AuthorizationLog("[后台最新授权日期已写入本地] OldExpiresDate=" + (string.IsNullOrWhiteSpace(oldExpiresDate) ? "<空>" : oldExpiresDate) + "，NewExpiresDate=" + expiresDate + "，GrantSource=" + GetLocalGrantSource());
        }

        private bool IsValidGrantDateString(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (string.Equals(value, ExpirationTime, StringComparison.Ordinal)) return true;

            DateTime time;
            return DateTime.TryParse(value, out time);
        }

        private void ShowPaymentGrantExpired()
        {
            isFirstUnauthorizedDualQRCode = false;
            paymentGrantExpired = true;
            authorizationGrantExpired = false;
            StopPaymentFlow(true);

            if (AuthorizationQRCodeParent != null)
            {
                AuthorizationQRCodeParent.SetActive(false);
            }

            if (WeChatQRCodeParent != null)
            {
                WeChatQRCodeParent.SetActive(false);
            }

            SetText2("付款码授权日期到期，请联系相关人员");
            AuthorizationLog("付款码授权日期已到期，已关闭授权码和付款码，只显示NetworkTipsText。");
        }

        /// <summary>
        /// 仅用于日志，不参与有网络时的授权结果判断。
        /// </summary>
        private void PrintLocalGrantDate()
        {
            if (!PlayerPrefs.HasKey(CurrentDateKey))
            {
                AuthorizationLog("[本地授权数据] DateCode不存在。");
                return;
            }

            string v1 = string.Empty;
            string v2 = string.Empty;
            try
            {
                AESEncryption.PlayerGet(CurrentDateKey, HardwareCode, ref v1, ref v2);
                AuthorizationLog("[本地授权数据] ExpiresDate=" + v1 + "，校验一致=" + v1.Equals(v2) + "，当前北京时间=" + GetBeiJingTime().ToString("yyyy-MM-dd HH:mm:ss"));
            }
            catch (Exception e)
            {
                AuthorizationLogError("[本地授权数据] DateCode读取失败：" + e.Message);
            }
        }

        /// <summary>
        /// 只有 BestHTTP 明确进入网络错误/超时状态时才调用。
        /// 有网络且后台有响应时绝不走这里。
        /// </summary>
        private void HandleOfflineGrantFallback(
            string requestName,
            HTTPRequestStates state)
        {
            AuthorizationLog(
                "[离线兜底] Request=" + requestName +
                "，State=" + state);

            PrintLocalGrantDate();

            bool hasGrantSource =
                PlayerPrefs.HasKey(CurrentGrantSourceKey);

            hasLegacyGrantHistory =
                !hasGrantSource &&
                HasLegacyGrantHistory();

            if (HasLocalGrantData())
            {
                if (Check设备合法授权(LocalAuthCheckContext.Offline))
                {
                    AuthorizationLog(
                        "[离线兜底] 本地授权验证通过，进入游戏。");

                    LoadTargetScene();
                    return;
                }

                if (localTimeInvalid)
                {
                    ShowLocalTimeInvalid(LocalAuthCheckContext.Offline);
                }
                else if (paymentGrantExpired)
                {
                    SetText2("付款码授权到期，请联系相关人员并连接网络");
                    AuthorizationLog("[离线兜底] 本地付款授权已经到期，当前无网络，显示付款授权到期联网提示。");
                }
                else if (authorizationGrantExpired)
                {
                    SetText2("授权码到期，请联系相关人员并连接网络");
                }

                AuthorizationLog(
                    "[离线兜底] 本地授权存在，但验证未通过。");
            }
            else
            {
                SetText2("请连接网络...");

                AuthorizationLog(
                    "[离线兜底] 本地没有完整的DateCode / TimeCode / machineCode。");
            }

            ScheduleStartupNetworkRetry();
        }

        /// <summary>
        /// 判断是否存在旧版本授权历史。
        /// 旧版没有 CurrentGrantSourceKey，并且旧脚本在服务端返回 Tag=0 时可能删除 DateCode / TimeCode，
        /// 所以不能要求三个Key必须同时存在。只要其中任意一个旧授权Key还在，就视为曾经授权过。
        /// </summary>
        private bool HasLegacyGrantHistory()
        {
            if (PlayerPrefs.HasKey(CurrentGrantSourceKey)) return false;

            return PlayerPrefs.HasKey(CurrentMachineKey) ||
                   PlayerPrefs.HasKey(CurrentDateKey) ||
                   PlayerPrefs.HasKey(CurrentTimeKey);
        }

        private bool HasLocalGrantData()
        {
            return PlayerPrefs.HasKey(CurrentDateKey) &&
                   PlayerPrefs.HasKey(CurrentMachineKey) &&
                   PlayerPrefs.HasKey(CurrentTimeKey);
        }

        private void ScheduleStartupNetworkRetry()
        {
            if (networkRetryInvoke != null) InvokeUtil.Instance.Remove(networkRetryInvoke);
            networkRetryInvoke = InvokeUtil.Instance.Run(() =>
            {
                networkRetryInvoke = null;
                CheckPaidOnStartup();
            }, 2f);
        }

        /// <summary>
        /// 只有“未付款 + 未授权”时才创建付款订单。已经有未完成订单时不会重复创建。
        /// </summary>
        private void EnsurePaymentOrder()
        {
            if (localTimeInvalid)
            {
                StopPaymentFlow(true);
                return;
            }

            if (paymentGrantExpired)
            {
                ShowPaymentGrantExpired();
                return;
            }

            if (authorizationGrantExpired)
            {
                StopPaymentFlow(true);
                return;
            }

            string currentProductCode = GetCurrentProductCode();

            if (string.IsNullOrWhiteSpace(currentProductCode))
            {
                StopPaymentFlow(true);
                if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(true);
                if (WeChatQRCode != null) WeChatQRCode.gameObject.SetActive(false);
                SetWeChatQRCodeText(LanguageManger.Instance.GetValue("产品编号不能为空"));
                return;
            }
            if (isCreatingOrder || !string.IsNullOrWhiteSpace(currentOrderCode)) return;
            if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(true);
            if (WeChatQRCode != null) WeChatQRCode.gameObject.SetActive(false);
            SetWeChatQRCodeText(LanguageManger.Instance.GetValue("小程序码获取中..."));

            string url = PaySeverUrl + OrderUrl;
            JObject body = new JObject();
            body["productCode"] = currentProductCode ?? string.Empty;
            body["hardwareCode"] = HardwareCode ?? string.Empty;
            isCreatingOrder = true;

            HTTPRequest request = new HTTPRequest(new Uri(url), HTTPMethods.Post, (req, resp) =>
            {
                isCreatingOrder = false;
                LogPayHttp("CreateOrder", url, req, resp);
                switch (req.State)
                {
                    case HTTPRequestStates.Finished:
                        if (resp == null || string.IsNullOrWhiteSpace(resp.DataAsText))
                        {
                            AuthorizationLogError("CreateOrder请求已完成，但返回为空。HTTP=" + (resp == null ? 0 : resp.StatusCode));
                            SchedulePaymentRetry(EnsurePaymentOrder);
                            return;
                        }

                        try
                        {
                            JObject root = JObject.Parse(resp.DataAsText);
                            JObject data = root["Data"] as JObject;
                            if (data == null)
                            {
                                AuthorizationLogError("CreateOrder返回中没有Data。HTTP=" + resp.StatusCode + "，Response=" + resp.DataAsText);
                                SchedulePaymentRetry(EnsurePaymentOrder);
                                return;
                            }

                            currentOrderCode = GetStringValue(data, "OrderCode");
                            currentOrderAmount = GetFirstValue(data, "TotalAmount", "Amount", "PayAmount");
                            currentPayQrUrl = GetFirstValue(data, "QrCodeFullUrl", "QrCodeUrl");
                            if (string.IsNullOrWhiteSpace(currentOrderCode) || string.IsNullOrWhiteSpace(currentPayQrUrl))
                            {
                                AuthorizationLogError("CreateOrder没有返回OrderCode或付款小程序码地址。HTTP=" + resp.StatusCode);
                                ResetPaymentOrder(false);
                                SchedulePaymentRetry(EnsurePaymentOrder);
                                return;
                            }

                            Text2Hide();
                            DownloadPaymentQRCode(BuildPayResourceUrl(currentPayQrUrl));
                            StartPayPolling();
                        }
                        catch (Exception e)
                        {
                            AuthorizationLogError("CreateOrder解析错误，HTTP=" + resp.StatusCode + "，Error=" + e.Message + "，Response=" + resp.DataAsText);
                            ResetPaymentOrder(false);
                            SchedulePaymentRetry(EnsurePaymentOrder);
                        }
                        break;

                    case HTTPRequestStates.Error:
                    case HTTPRequestStates.ConnectionTimedOut:
                    case HTTPRequestStates.TimedOut:
                        SetText2("请连接网络...");
                        AuthorizationLog("CreateOrder网络失败，State=" + req.State + "，2秒后重试。");
                        SchedulePaymentRetry(EnsurePaymentOrder);
                        break;

                    case HTTPRequestStates.Aborted:
                        AuthorizationLog("CreateOrder请求已取消，不按网络失败处理。");
                        break;

                    default:
                        AuthorizationLogWarning("CreateOrder出现未处理状态：" + req.State);
                        break;
                }
            });
            SetPayHeaders(request, true);
            request.RawData = Encoding.UTF8.GetBytes(body.ToString(Newtonsoft.Json.Formatting.None));
            request.Send();
        }

        private void DownloadPaymentQRCode(string url)
        {
            if (localTimeInvalid)
            {
                StopPaymentFlow(true);
                return;
            }

            if (authorizationGrantExpired)
            {
                StopPaymentFlow(true);
                return;
            }
            if (string.IsNullOrWhiteSpace(url)) return;
            if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(true);
            if (WeChatQRCode != null) WeChatQRCode.gameObject.SetActive(false);
            if (isFirstUnauthorizedDualQRCode)
            {
                SetWeChatQRCodeText(LanguageManger.Instance.GetValue("付款码") + ":" + FormatAmount(currentOrderAmount) + LanguageManger.Instance.GetValue("元"));
            }
            else
            {
                SetWeChatQRCodeText(LanguageManger.Instance.GetValue("小程序码获取中..."));
            }

            HTTPRequest request = new HTTPRequest(new Uri(url), HTTPMethods.Get, (req, resp) =>
            {
                if (authorizationGrantExpired)
                {
                    if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(false);
                    return;
                }
                LogPayHttp("DownloadPayQRCode", url, req, resp);
                switch (req.State)
                {
                    case HTTPRequestStates.Finished:
                        if (resp == null || resp.DataAsTexture2D == null)
                        {
                            AuthorizationLogError("付款小程序码请求已完成，但图片数据为空。HTTP=" + (resp == null ? 0 : resp.StatusCode));
                            SchedulePaymentRetry(() => { DownloadPaymentQRCode(url); });
                            return;
                        }

                        if (payQrTexture != null) Destroy(payQrTexture);
                        payQrTexture = resp.DataAsTexture2D;
                        if (WeChatQRCode != null)
                        {
                            WeChatQRCode.texture = payQrTexture;
                            WeChatQRCode.gameObject.SetActive(true);
                        }
                        if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(true);
                        if (isFirstUnauthorizedDualQRCode)
                        {
                            SetWeChatQRCodeText(LanguageManger.Instance.GetValue("付款码") + ":" + FormatAmount(currentOrderAmount) + LanguageManger.Instance.GetValue("元"));
                        }
                        else
                        {
                            SetWeChatQRCodeText(LanguageManger.Instance.GetValue("微信付款:") + FormatAmount(currentOrderAmount) + LanguageManger.Instance.GetValue("元"));
                        }

                        Text2Hide();
                        break;

                    case HTTPRequestStates.Error:
                    case HTTPRequestStates.ConnectionTimedOut:
                    case HTTPRequestStates.TimedOut:
                        SetText2("请连接网络...");
                        AuthorizationLog("付款小程序码下载网络失败，State=" + req.State + "，2秒后重试。");
                        SchedulePaymentRetry(() => { DownloadPaymentQRCode(url); });
                        break;

                    case HTTPRequestStates.Aborted:
                        AuthorizationLog("付款小程序码下载请求已取消，不按网络失败处理。");
                        break;

                    default:
                        AuthorizationLogWarning("付款小程序码下载出现未处理状态：" + req.State);
                        break;
                }
            });
            request.SetHeader("accept", "image/*");
            request.Send();
        }

        private void StartPayPolling()
        {
            if (localTimeInvalid)
            {
                StopPaymentFlow(true);
                return;
            }

            if (payPollInvoke != null) InvokeUtil.Instance.Remove(payPollInvoke);
            PollPayResult();
            payPollInvoke = InvokeUtil.Instance.Run(PollPayResult, 1f, 0);
        }

        private void PollPayResult()
        {
            if (localTimeInvalid)
            {
                StopPaymentFlow(true);
                return;
            }

            if (authorizationGrantExpired)
            {
                StopPaymentFlow(true);
                return;
            }
            if (isPollingPay || string.IsNullOrWhiteSpace(currentOrderCode)) return;
            isPollingPay = true;
            string orderCode = currentOrderCode;
            string url = PaySeverUrl + OrderResultUrl + "?orderCode=" + Uri.EscapeDataString(orderCode);
            HTTPRequest request = new HTTPRequest(new Uri(url), HTTPMethods.Get, (req, resp) =>
            {
                isPollingPay = false;
                if (orderCode != currentOrderCode) return;
                LogPayHttp("PollPayResult", url, req, resp);
                switch (req.State)
                {
                    case HTTPRequestStates.Finished:
                        if (resp == null || string.IsNullOrWhiteSpace(resp.DataAsText))
                        {
                            AuthorizationLogError("PollPayResult请求已完成，但返回为空。HTTP=" + (resp == null ? 0 : resp.StatusCode));
                            return;
                        }

                        Text2Hide();
                        try
                        {
                            JObject root = JObject.Parse(resp.DataAsText);
                            JObject data = root["Data"] as JObject;
                            if (data == null) return;
                            string status = GetStringValue(data, "Status");
                            if (string.Equals(status, "Pending", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(status)) return;

                            if (string.Equals(status, "Expired", StringComparison.OrdinalIgnoreCase))
                            {
                                AuthorizationLog("付款订单已过期，重新创建付款订单。");
                                ResetPaymentOrder(true);
                                EnsurePaymentOrder();
                                return;
                            }

                            if (!string.Equals(status, "Success", StringComparison.OrdinalIgnoreCase)) return;

                            string paidMachineCode = GetStringValue(data, "MachineCode");
                            string expiresDate = GetStringValue(data, "ExpiresDate");
                            string paidHardwareCode = GetStringValue(data, "HardwareCode");
                            if (!string.IsNullOrWhiteSpace(paidHardwareCode) && !string.Equals(paidHardwareCode, HardwareCode, StringComparison.OrdinalIgnoreCase))
                            {
                                AuthorizationLogError("付款成功返回的HardwareCode与当前设备不一致。");
                                return;
                            }

                            if (string.IsNullOrWhiteSpace(paidMachineCode) || string.IsNullOrWhiteSpace(expiresDate))
                            {
                                AuthorizationLogError("付款成功但接口没有返回MachineCode或ExpiresDate。");
                                return;
                            }

                            if (!IsGrantSourceAllowedByQRCodeDisplayMode(GrantSourceType.Payment))
                            {
                                AuthorizationLog("付款成功，但当前QRCodeDisplayMode=" + QRCodeDisplayMode + "不包含付款码，以QRCodeDisplayMode为准，不保存付款授权。");
                                DeleteAllLocalGrantData("付款成功时QRCodeDisplayMode已不允许付款码");
                                return;
                            }

                            AuthorizationLog("付款成功，复用设备授权的MachineCode/ExpiresDate/TimeCode验证逻辑。");
                            authorizationGrantExpired = false;
                            SaveGrantData(paidMachineCode, expiresDate, data["NowDate"] == null ? string.Empty : data["NowDate"].ToString(), GrantSourceType.Payment);
                            CompletePaymentSuccessUI();
                            Check(CheckState.检查是否设备合法性, false);
                        }
                        catch (Exception e)
                        {
                            AuthorizationLogError("PollPayResult解析错误，HTTP=" + resp.StatusCode + "，Error=" + e.Message + "，Response=" + resp.DataAsText);
                        }
                        break;

                    case HTTPRequestStates.Error:
                    case HTTPRequestStates.ConnectionTimedOut:
                    case HTTPRequestStates.TimedOut:
                        SetText2("请连接网络...");
                        AuthorizationLog("PollPayResult网络失败，State=" + req.State + "，等待下一次轮询。");
                        break;

                    case HTTPRequestStates.Aborted:
                        AuthorizationLog("PollPayResult请求已取消，不按网络失败处理。");
                        break;

                    default:
                        AuthorizationLogWarning("PollPayResult出现未处理状态：" + req.State);
                        break;
                }
            });
            SetPayHeaders(request, false);
            request.Send();
        }

        private void ShowLocalTimeInvalid(LocalAuthCheckContext context)
        {
            isFirstUnauthorizedDualQRCode = false;
            localTimeInvalid = true;
            StopPaymentFlow(true);

            if (AuthorizationQRCodeParent != null) AuthorizationQRCodeParent.SetActive(false);
            if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(false);

            string message = context == LocalAuthCheckContext.Offline ? "请联网并校准本地时间" : "请校准本地时间";

            if (NetworkTipsText != null)
            {
                NetworkTipsText.gameObject.SetActive(true);
                NetworkTipsText.text = LanguageManger.Instance.GetValue(message);
            }

            AuthorizationLog("[本地时间异常] Context=" + context + "，提示=" + message + "。NetworkTipsText保持显示，授权码和付款码保持隐藏。");
        }

        /// <summary>
        /// 判断授权来源是否被当前QRCodeDisplayMode允许。
        /// </summary>
        private bool IsGrantSourceAllowedByQRCodeDisplayMode(GrantSourceType source)
        {
            switch (source)
            {
                case GrantSourceType.Authorization:
                    return QRCodeDisplayMode == UnauthorizedQRCodeMode.授权码 || QRCodeDisplayMode == UnauthorizedQRCodeMode.授权码加付款码;

                case GrantSourceType.Payment:
                    return QRCodeDisplayMode == UnauthorizedQRCodeMode.付款码 || QRCodeDisplayMode == UnauthorizedQRCodeMode.授权码加付款码;

                default:
                    return true;
            }
        }

        /// <summary>
        /// 启动时检查本地GrantSource是否与QRCodeDisplayMode冲突。
        /// 冲突时以QRCodeDisplayMode为准，删除全部本地授权数据。
        /// </summary>
        private void ValidateLocalGrantSourceAgainstQRCodeDisplayMode()
        {
            if (!PlayerPrefs.HasKey(CurrentGrantSourceKey))
            {
                return;
            }

            GrantSourceType source = GetLocalGrantSource();

            if (source == GrantSourceType.Unknown)
            {
                AuthorizationLog("本地存在GrantSource，但无法确认授权来源，不执行QRCodeDisplayMode冲突清理。");
                return;
            }

            if (IsGrantSourceAllowedByQRCodeDisplayMode(source))
            {
                AuthorizationLog("本地GrantSource=" + source + "与QRCodeDisplayMode=" + QRCodeDisplayMode + "一致，保留本地授权数据。");
                return;
            }

            AuthorizationLog("本地GrantSource=" + source + "与QRCodeDisplayMode=" + QRCodeDisplayMode + "冲突，以QRCodeDisplayMode为准，删除全部本地授权数据。");
            DeleteAllLocalGrantData("GrantSource与QRCodeDisplayMode冲突");
        }

        /// <summary>
        /// 删除全部本地授权数据，仅删除授权相关四个Key。
        /// </summary>
        private void DeleteAllLocalGrantData(string reason)
        {
            StopPaymentFlow(true);

            if (PlayerPrefs.HasKey(CurrentMachineKey)) PlayerPrefs.DeleteKey(CurrentMachineKey);
            if (PlayerPrefs.HasKey(CurrentDateKey)) PlayerPrefs.DeleteKey(CurrentDateKey);
            if (PlayerPrefs.HasKey(CurrentTimeKey)) PlayerPrefs.DeleteKey(CurrentTimeKey);
            if (PlayerPrefs.HasKey(CurrentGrantSourceKey)) PlayerPrefs.DeleteKey(CurrentGrantSourceKey);

            PlayerPrefs.Save();

            machineCode = "未授权";
            hasLegacyGrantHistory = false;
            authorizationGrantExpired = false;
            paymentGrantExpired = false;
            localTimeInvalid = false;
            isFirstUnauthorizedDualQRCode = false;

            DebugManager.Instance.AddData("设备编码", machineCode);
            AuthorizationLog("已删除全部本地授权数据：CurrentMachineKey、CurrentDateKey、CurrentTimeKey、CurrentGrantSourceKey。Reason=" + reason);
        }

        /// <summary>
        /// 付款成功和设备授权成功共用同一套本地缓存。
        /// 支付接口截图没有NowDate时，使用当前北京时间写入TimeCode。
        /// </summary>
        private void SaveGrantData(string newMachineCode, string expiresDate, string nowDate, GrantSourceType source)
        {
            if (string.IsNullOrWhiteSpace(newMachineCode) || string.IsNullOrWhiteSpace(expiresDate)) return;
            isFirstUnauthorizedDualQRCode = false;
            machineCode = newMachineCode;
            string time = string.IsNullOrWhiteSpace(nowDate) ? GetBeiJingTime().ToString("yyyy-MM-dd HH:mm:ss") : nowDate;
            AESEncryption.PlayerSet(CurrentDateKey, expiresDate, HardwareCode);
            AESEncryption.PlayerSet(CurrentTimeKey, time, HardwareCode);
            AESEncryption.PlayerSet(CurrentMachineKey, machineCode, HardwareCode);
            AESEncryption.PlayerSet(CurrentGrantSourceKey, source.ToString(), HardwareCode);
            PlayerPrefs.Save();
            hasLegacyGrantHistory = false;
            DebugManager.Instance.AddData("设备编码", machineCode);
            AuthorizationLog("本地授权数据已更新，MachineCode=" + machineCode + "，ExpiresDate=" + expiresDate + "，TimeCode=" + time + "，Source=" + source);
            LogLocalKeySnapshot("SaveGrantData保存后");
        }

        /// <summary>
        /// 付款成功后的统一UI收尾：
        /// 1. 停止付款轮询/重试并关闭付款码；
        /// 2. 同时关闭授权码；
        /// 3. 锁定付款成功状态，阻止之前已经发出的授权/绑定回调再次打开授权码。
        /// </summary>
        private void CompletePaymentSuccessUI()
        {
            paymentSuccessConfirmed = true;
            StopPaymentFlow(true);
            HideAuthorizationAndPaymentQRCodes();

            AuthorizationLog("付款成功，已同时关闭付款码和授权码，并阻止旧授权回调重新打开二维码。");
        }

        /// <summary>
        /// 同时隐藏授权二维码和付款二维码。
        /// </summary>
        private void HideAuthorizationAndPaymentQRCodes()
        {
            Is显示授权二维码 = false;
            isFirstUnauthorizedDualQRCode = false;

            if (AuthorizationQRCodeParent != null)
            {
                AuthorizationQRCodeParent.SetActive(false);
            }

            if (WeChatQRCodeParent != null)
            {
                WeChatQRCodeParent.SetActive(false);
            }
        }

        private void StopPaymentFlow(bool hide)
        {
            if (payPollInvoke != null)
            {
                InvokeUtil.Instance.Remove(payPollInvoke);
                payPollInvoke = null;
            }
            if (payRetryInvoke != null)
            {
                InvokeUtil.Instance.Remove(payRetryInvoke);
                payRetryInvoke = null;
            }
            currentOrderCode = string.Empty;
            currentOrderAmount = string.Empty;
            currentPayQrUrl = string.Empty;
            isCreatingOrder = false;
            isPollingPay = false;
            if (hide && WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(false);
        }

        private void ResetPaymentOrder(bool hide)
        {
            StopPaymentFlow(hide);
            if (payQrTexture != null)
            {
                Destroy(payQrTexture);
                payQrTexture = null;
            }
            if (!hide)
            {
                if (WeChatQRCodeParent != null) WeChatQRCodeParent.SetActive(true);
                if (WeChatQRCode != null) WeChatQRCode.gameObject.SetActive(false);
                SetWeChatQRCodeText(LanguageManger.Instance.GetValue("小程序码获取中..."));
            }
        }

        private void SchedulePaymentRetry(Action action)
        {
            if (payRetryInvoke != null) InvokeUtil.Instance.Remove(payRetryInvoke);
            payRetryInvoke = InvokeUtil.Instance.Run(() =>
            {
                payRetryInvoke = null;
                action?.Invoke();
            }, 2f);
        }

        private void SetWeChatQRCodeText(string text)
        {
            if (WeChatQRCodeText == null) return;
            WeChatQRCodeText.gameObject.SetActive(true);
            WeChatQRCodeText.text = text;
        }

        private void SetPayHeaders(HTTPRequest request, bool hasBody)
        {
            request.SetHeader("accept", "*/*");
            if (hasBody) request.SetHeader("Content-Type", "application/json-patch+json");
        }

        private static bool IsHttpSuccess(HTTPRequest request, HTTPResponse response)
        {
            return request != null && request.State == HTTPRequestStates.Finished && response != null && response.StatusCode >= 200 && response.StatusCode < 300;
        }

        private static bool GetBool(JObject data, string key)
        {
            JToken token = data[key];
            bool value;
            return token != null && bool.TryParse(token.ToString(), out value) && value;
        }

        private static string GetStringValue(JObject data, string key)
        {
            JToken token = data[key];
            return token == null || token.Type == JTokenType.Null ? string.Empty : token.ToString();
        }

        private static string GetFirstValue(JObject data, params string[] keys)
        {
            for (int i = 0; i < keys.Length; i++)
            {
                string value = GetStringValue(data, keys[i]);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            return string.Empty;
        }

        private static string FormatAmount(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "--";
            decimal amount;
            if (decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out amount)) return amount.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            return value.Trim();
        }

        private static string BuildPayResourceUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            value = value.Trim().Replace("\\", "/");
            Uri absolute;
            if (Uri.TryCreate(value, UriKind.Absolute, out absolute)) return absolute.AbsoluteUri;
            try { return new Uri(new Uri(PaySeverUrl + "/"), value.TrimStart('/')).AbsoluteUri; }
            catch { return value; }
        }

        private static void LogPayHttp(string name, string url, HTTPRequest request, HTTPResponse response)
        {
            string state = request == null ? "null" : request.State.ToString();
            int http = response == null ? 0 : response.StatusCode;
            string body = response == null ? string.Empty : response.DataAsText;
            Debug.Log("========== Order API ==========\n接口：" + name + "\nURL：" + url + "\nState：" + state + "\nHTTP：" + http + "\nResponse：\n" + body + "\n===============================");
        }

        private void OnDestroy()
        {
            if (networkRetryInvoke != null)
            {
                InvokeUtil.Instance.Remove(networkRetryInvoke);
                networkRetryInvoke = null;
            }
            StopPaymentFlow(true);
            if (payQrTexture != null)
            {
                Destroy(payQrTexture);
                payQrTexture = null;
            }
            StopAllCoroutines();
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Application.Quit();
            }
        }

        public DateTime GetBeiJingTime()
        {
            return DateTime.UtcNow + new TimeSpan(8, 0, 0);
        }

        private void AuthorizationLog(string message)
        {
            if (!ShowAuthorizationLog) return;
            DebugManager.Log(message);
        }

        private void AuthorizationLogWarning(string message)
        {
            if (!ShowAuthorizationLog) return;
            DebugManager.LogWarning(message);
        }

        private void AuthorizationLogError(string message)
        {
            if (!ShowAuthorizationLog) return;
            DebugManager.LogError(message);
        }

        private void DeleteCorruptedLocalKey(string key, string reason)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            if (PlayerPrefs.HasKey(key))
            {
                PlayerPrefs.DeleteKey(key);
                PlayerPrefs.Save();
                AuthorizationLogError("本地授权Key确认异常，已删除该Key。Key=" + key + "，Reason=" + reason);
            }
        }

        /// <summary>
        /// 只要本地存在 machineCode，就优先读取并显示到 DebugManager。
        /// 该逻辑不依赖网络状态，也不依赖当前是否仍然处于有效授权状态。
        /// </summary>
        private void RefreshDebugMachineCodeFromLocal()
        {
            if (!PlayerPrefs.HasKey(CurrentMachineKey))
            {
                DebugManager.Instance.AddData("设备编码", machineCode);
                AuthorizationLog("[本地设备编码] machineCode不存在，当前显示=" + machineCode);
                return;
            }

            string v1 = string.Empty;
            string v2 = string.Empty;

            try
            {
                AESEncryption.PlayerGet(CurrentMachineKey, HardwareCode, ref v1, ref v2);
            }
            catch (Exception e)
            {
                AuthorizationLogError("[本地设备编码] machineCode读取/解密失败，Error=" + e.Message);
                DeleteCorruptedLocalKey(CurrentMachineKey, "machineCode读取或解密异常：" + e.Message);
                DebugManager.Instance.AddData("设备编码", machineCode);
                return;
            }

            if (!string.IsNullOrWhiteSpace(v1))
            {
                machineCode = v1;
                DebugManager.Instance.AddData("设备编码", machineCode);
                AuthorizationLog("[本地设备编码] 已读取并显示 machineCode=" + machineCode + "，校验一致=" + v1.Equals(v2));
            }
            else
            {
                DebugManager.Instance.AddData("设备编码", machineCode);
                AuthorizationLog("[本地设备编码] machineCode内容为空，当前显示=" + machineCode);
            }
        }

        private void LogLocalKeySnapshot(string stage)
        {
            AuthorizationLog("[本地授权Key快照] Stage=" + stage + "，DateCode=" + PlayerPrefs.HasKey(CurrentDateKey) + "，TimeCode=" + PlayerPrefs.HasKey(CurrentTimeKey) + "，machineCode=" + PlayerPrefs.HasKey(CurrentMachineKey) + "，GrantSource=" + PlayerPrefs.HasKey(CurrentGrantSourceKey));
        }

        private bool TryPlayerGet(string key, string hardwareCode, ref string v1, ref string v2)
        {
            try
            {
                AESEncryption.PlayerGet(key, hardwareCode, ref v1, ref v2);
                return true;
            }
            catch (Exception e)
            {
                AuthorizationLogError("读取/解密本地授权数据失败，只删除当前异常Key。Key=" + key + "，Error=" + e.Message);
                DeleteCorruptedLocalKey(key, "读取或解密异常：" + e.Message);
                return false;
            }
        }
    }
}

