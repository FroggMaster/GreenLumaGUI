﻿using CN_GreenLumaGUI.Messages;
using CN_GreenLumaGUI.Models;
using CN_GreenLumaGUI.tools;
using CN_GreenLumaGUI.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using System.Windows;

namespace CN_GreenLumaGUI.ViewModels
{
    public partial class ManagerViewModel : ObservableObject
    {
        const string defStartButtonColor = "#64bd4d";
        const string closeStartButtonColor = "#f44b56";//ffa754
        const string darkStartButtonColor = "#424242";
        const string defStartButtonContent = "Start Steam";
        const string closeStartButtonContent = "Close Steam";
        const string darkStartButtonContent = "X";

        private bool CancelWait { get; set; }

        void DockInit()
        {
            lock (this)
            {
                CancelWait = false;
            }
            StartButtonColor = darkStartButtonColor;
            StartButtonContent = darkStartButtonContent;
            LoadingBarEcho = Visibility.Hidden;
            ButtonPromptTextEcho = Visibility.Collapsed;
            FAQButtonEcho = Visibility.Collapsed;
            FAQButtonCmd = new RelayCommand(FAQButton);
            StartButtonCmd = new RelayCommand(StartButton);
            checkedNum = DataSystem.Instance.CheckedNum;

            WeakReferenceMessenger.Default.Register<LoadFinishedMessage>(this, (r, m) =>
            {
                StateToStartSteam();
                Thread updateThread = new(UpdateSteamState)
                {
                    IsBackground = true
                };
                updateThread.Start();
            });
            WeakReferenceMessenger.Default.Register<CheckedNumChangedMessage>(this, (r, m) =>
            {
                CheckedNumNow = DataSystem.Instance.CheckedNum;
            });

            WeakReferenceMessenger.Default.Register<DockInformMessage>(this, (r, m) =>
            {
                Application.Current.Dispatcher.Invoke((Action)delegate ()
                {
                    PrivateDockInform(m.messageText);
                });
            });

            WeakReferenceMessenger.Default.Register<ConfigChangedMessage>(this, (r, m) =>
            {
                if (m.kind == "ScrollBarEcho")
                {
                    Application.Current.Dispatcher.Invoke((Action)delegate ()
                    {
                        OnPropertyChanged(nameof(ScrollBarEchoState));
                    });
                }
                if (m.kind == "HidePromptText")
                {
                    ButtonPromptTextEcho = DataSystem.Instance.HidePromptText ? Visibility.Collapsed : Visibility.Visible;
                    FAQButtonEcho = ButtonPromptTextEcho;
                }
            });
        }
        public static void Inform(string str)
        {
            WeakReferenceMessenger.Default.Send(new DockInformMessage(str));
        }
        private void PrivateDockInform(string message)
        {
            if (windowFrom.SnackbarInform.MessageQueue is { } messageQueue)
            {
                _ = Task.Factory.StartNew(() =>
                {
                    messageQueue.Clear();
                    messageQueue.Enqueue(message);
                }
                );
            }
        }

        //Binding
        private readonly int maxUnlockNum = DllReader.TotalMaxUnlockNum;
        public long MaxUnlockNum { get { return maxUnlockNum; } }

        private long checkedNum;
        public long CheckedNumNow
        {
            get { return checkedNum; }
            set
            {
                checkedNum = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CheckedNumColor));
            }
        }
        public string CheckedNumColor
        {
            get { return checkedNum > MaxUnlockNum ? "#f04a55" : "#4ec9ae"; }
        }
        private string? startButtonColor;
        public string? StartButtonColor
        {
            get { return startButtonColor; }
            set
            {
                startButtonColor = value;
                OnPropertyChanged();
            }
        }
        private string? startButtonContent;
        public string? StartButtonContent
        {
            get { return startButtonContent; }
            set
            {
                startButtonContent = value;
                OnPropertyChanged();
            }
        }

        private Visibility loadingBarEcho;
        public Visibility LoadingBarEcho
        {
            get { return loadingBarEcho; }
            set
            {
                loadingBarEcho = value;
                OnPropertyChanged();
            }
        }

        private Visibility buttonPromptTextEcho;
        public Visibility ButtonPromptTextEcho
        {
            get { return buttonPromptTextEcho; }
            set
            {
                buttonPromptTextEcho = value;
                OnPropertyChanged();
            }
        }

        private Visibility fAQButtonEcho;
        public Visibility FAQButtonEcho
        {
            get { return fAQButtonEcho; }
            set
            {
                fAQButtonEcho = value;
                OnPropertyChanged();
            }
        }
        //Commands
        private InformWindow? faqWindow;
        public RelayCommand? FAQButtonCmd { get; set; }
        private void FAQButton()
        {
            try
            {
                if (faqWindow is null || faqWindow.IsClosed)
                {
                    string? readme = OutAPI.GetFromRes("README-EN.md");
                    if (readme is null) return;
                    faqWindow = new("FAQ", TextItemModel.CreateListFromMarkDown(readme));
                }
                if (!faqWindow.IsVisible)
                {
                    faqWindow.Show();
                }
                else
                {
                    faqWindow.Close();
                }
            }
            catch { }
        }
        public RelayCommand? StartButtonCmd { get; set; }
        private string buttonState = "Disable";
        public static bool SteamRunning => ManagerWindow.ViewModel?.buttonState == "CloseSteam";
        private void StartButton()
        {
            if (ButtonPromptTextEcho == Visibility.Visible)
                ButtonPromptTextEcho = Visibility.Collapsed;

            switch (buttonState)
            {
                case "StartSteam":
                    //超出上限时提醒
                    if (CheckedNumNow > MaxUnlockNum)
                    {
                        _ = OutAPI.MsgBox("Exceeded unlock limit.");
                        return;
                    }
                    //点击开始按钮如果配置中没有路径就读取steam路径
                    if (DataSystem.Instance.SteamPath is null or "")
                    {
                        DataSystem.Instance.SteamPath = GLFileTools.GetSteamPath_Auto();
                        if (DataSystem.Instance.SteamPath == "")
                        {
                            DataSystem.Instance.SteamPath = null;
                            return;
                        }
                    }
                    lock (this)
                    {
                        CancelWait = false;
                    }
                    StateToDisable();
                    Task.Run(StartSteamUnlock);
                    break;
                case "CloseSteam":
                    KillSteam();
                    StateToStartSteam();
                    break;
                default:
                    return;
            }
        }

        private async Task StartSteamUnlock()
        {
            bool isNoCheckedGame = false;
            int nowStartSteamTimes = startSteamTimes;
            try
            {
                DataSystem.Instance.SaveData();
                OutAPI.PrintLog(DataSystem.Instance.ToJSON());
                OutAPI.PrintLog($"isLoaded = {DataSystem.isLoaded};isLoadedEnd = {DataSystem.isLoadedEnd};isError = {DataSystem.isError}");
                OutAPI.PrintLog("Task start.");
                if (!File.Exists(DataSystem.Instance.SteamPath))
                {
                    StateToStartSteam();
                    await Task.Delay(50);
                    _ = OutAPI.MsgBox("Incorrect Steam path!");
                    return;
                }
                KillSteam();
                //防止前一次kill不及时，略微延时
                await Task.Delay(500);
                //解锁模式启动steam
                if (DataSystem.Instance.CheckedNum > 0)
                {
                    OutAPI.PrintLog($"CheckedNum = {DataSystem.Instance.CheckedNum}");
                    OutAPI.PrintLog("GreenLuma ready start.");
                    //清理GreenLuma配置文件
                    GLFileTools.DeleteGreenLumaConfig();
                    await Task.Delay(50);
                    //写入GreenLuma配置文件
                    if (!GLFileTools.WriteGreenLumaConfig(DataSystem.Instance.SteamPath))
                    {
                        StateToStartSteam();
                        _ = OutAPI.MsgBox("Failed to write to the configuration file!");
                        return;
                    }
                    await Task.Delay(50);
                    //检测GreenLuma完整性
                    if (!GLFileTools.IsGreenLumaReady())
                    {
                        //GLFileTools.DeleteGreenLumaConfig();
                        StateToStartSteam();
                        await Task.Delay(50);
                        _ = OutAPI.MsgBox("Temporary files were deleted by another program!");
                        return;
                    }
                    //throw new Exception();//测试模拟异常
                    //启动GreenLuma
                    OutAPI.PrintLog("GreenLuma start.");
                    int exitCode;
                    bool withBak = DataSystem.Instance.StartWithBak;
                    bool withAdmin = DataSystem.Instance.RunSteamWithAdmin;
                    if (withBak)
                    {
                        exitCode = GLFileTools.StartGreenLuma_Bak(withAdmin);
                    }
                    else exitCode = GLFileTools.StartGreenLuma(withAdmin);
                    OutAPI.PrintLog("Exit " + exitCode);
                    await Task.Delay(3000);
                    //返回值分析
                    bool exitCodeIgnore = false;
                    //对已知返回值分析
                    if (retValueNeedHandle.TryGetValue(exitCode, out var reason))
                    {
                        //对已知普通返回值分析
                        _ = OutAPI.MsgBox(reason);
                        exitCodeIgnore = true;
                    }
                    if (exitCode == -1073741515)
                    {
                        //对已知运行库缺失返回值分析
                        _ = OutAPI.MsgBox("Launch failed, VC++2015 x86 runtime library is not installed.");
                        _ = Task.Run(() =>
                        {
                            //点击确定打开
                            if (MessageBox.Show("Would you like to open the official Microsoft VC++ Runtime library download page?", "Download confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                            {
                                OutAPI.OpenInBrowser("https://download.microsoft.com/download/9/3/F/93FCF1E7-E6A4-478B-96E7-D4B285925B00/vc_redist.x86.exe");
                            }
                        });
                        exitCodeIgnore = true;
                    }
                    //未知返回值，转而处理stderr通道的错误信息
                    string? errStr = "";
                    if (exitCode != 0 && !exitCodeIgnore)
                    {
                        try
                        {
                            if (File.Exists(GLFileTools.DLLInjectorLogErrTxt))
                                errStr = File.ReadAllText(GLFileTools.DLLInjectorLogErrTxt).Trim();
                        }
                        catch { }
                        if (!DataSystem.Instance.HaveTriedBak && withBak != true && errStr == "Access is denied.")
                        {
                            DataSystem.Instance.StartWithBak = true;
                            DataSystem.Instance.HaveTriedBak = true;
                            OutAPI.MsgBox("System version compatibility issue detected, attempting to launch in compatibility mode.").Wait();
                            //清理GreenLuma配置文件
                            GLFileTools.DeleteGreenLumaConfig();
                            //重新写入GreenLuma配置文件
                            if (!GLFileTools.WriteGreenLumaConfig(DataSystem.Instance.SteamPath))
                            {
                                StateToStartSteam();
                                _ = OutAPI.MsgBox("Failed to write to the configuration file!");
                                return;
                            }
                            ;
                            //备用方式启动
                            exitCode = GLFileTools.StartGreenLuma_Bak(withAdmin);
                            OutAPI.PrintLog("Bak First Exit " + exitCode);
                            errStr = null;
                        }
                    }
                    //等待启动，超过时间则认为未成功
                    long waitSeconds = 30;//前面等了3秒
                    while (waitSeconds < 200)
                    {
                        await Task.Delay(100);
                        waitSeconds++;
                        if (startSteamTimes != nowStartSteamTimes)
                            break;//启动已经成功则不再等待
                        if (waitSeconds >= 130 && exitCodeIgnore)
                            break;//已经识别出错误则不再等待
                    }
                    OutAPI.PrintLog($"Wait time finish. (After {waitSeconds / 10.0} seconds)");
                    bool fileLost = false;
                    if (!File.Exists(GLFileTools.DLLInjectorExePath))
                    {
                        OutAPI.PrintLog("DLLInjectorExe lost");
                        fileLost = true;
                    }
                    if (!File.Exists(GLFileTools.DLLInjectorExeBakPath))
                    {
                        OutAPI.PrintLog("DLLInjectorExe_Bak lost");
                        fileLost = true;
                    }
                    if (!File.Exists(GLFileTools.SpcrunExePath))
                    {
                        OutAPI.PrintLog("SpcrunExe lost");
                        fileLost = true;
                    }
                    if (!File.Exists(GLFileTools.GreenLumaDllPath))
                    {
                        OutAPI.PrintLog("dll lost");
                        fileLost = true;
                    }
                    if (fileLost)
                    {
                        _ = OutAPI.MsgBox("Temporary files are missing, possibly deleted by Windows Defender by mistake.");
                        //_ = Task.Run(() =>
                        //{
                        //	//点击确定打开
                        //	if (MessageBox.Show("是否打开火绒官网下载地址？", "下载提示", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        //	{
                        //		OutAPI.OpenInBrowser("https://www.huorong.cn/person5.html");
                        //	}
                        //});
                        exitCodeIgnore = true;
                    }
                    //读取错误信息
                    try
                    {
                        if (string.IsNullOrEmpty(errStr) && File.Exists(GLFileTools.DLLInjectorLogErrTxt))
                            errStr = File.ReadAllText(GLFileTools.DLLInjectorLogErrTxt).Trim();
                    }
                    catch { }
                    OutAPI.PrintLog($"{{ exitCodeIgnore({exitCodeIgnore}) beforeTimes({nowStartSteamTimes}) nowTimes({startSteamTimes}) errStr({errStr ?? "null"}) }}");
                    //返回值异常 或是 到时间了还是没成功启动(有异常)
                    if (!exitCodeIgnore && (exitCode != 0 || (startSteamTimes == nowStartSteamTimes && errStr != null && errStr.Length > 0)))
                    {
                        string errmsg = "Launch failed! Please contact the developer.";
                        if (!string.IsNullOrEmpty(errStr))
                            errmsg += $"({errStr})";
                        _ = Task.Run(async () =>
                        {
                            await OutAPI.MsgBox(errmsg);

                            if (errStr == "The system cannot execute the specified program.")
                            {
                                await OutAPI.MsgBox("Checking the \"FAQ\" might be helpful. If the issue persists, consider submitting an issue on the GitHub page.");
                            }
                        });
                    }
                    else
                    {
                        OutAPI.PrintLog($"Skip MsgBox");
                    }
                }
                else
                {
                    OutAPI.PrintLog("checkednum<=0");
                    _ = OutAPI.MsgBox("Please select at least one game to unlock first.");
                    isNoCheckedGame = true;
                }

            }
            catch (Exception e)
            {
                _ = Task.Run(async () =>
                {
                    await OutAPI.MsgBox(e.Message);
                    if (e.StackTrace is not null)
                        await OutAPI.MsgBox(e.StackTrace);
                });
            }
            await Task.Delay(5000);
            if (startSteamTimes == nowStartSteamTimes)//buttonState == "Disable"
            {
                if (isNoCheckedGame)
                {
                    ////普通模式启动steam
                    //var steamProcess = new Process();
                    //steamProcess.StartInfo.FileName = DataSystem.Instance.SteamPath;
                    ////steamProcess.StartInfo.Arguments = ;
                    //steamProcess.Start();
                }
                else
                {
                    //尝试发送日志
                    try
                    {
                        string data = $"[v{Program.Version}]\n";
                        if (File.Exists(OutAPI.LogFilePath))
                            data += "-----[log0.txt]-----\n" + File.ReadAllText(OutAPI.LogFilePath) + "\n";
                        if (File.Exists(GLFileTools.DLLInjectorLogTxt))
                        {
                            string logStr = File.ReadAllText(GLFileTools.DLLInjectorLogTxt);
                            data += "-----[log.txt]-----\n" + logStr + "\n";
                        }
                        if (File.Exists(GLFileTools.DLLInjectorLogErrTxt))
                        {
                            string logStr = File.ReadAllText(GLFileTools.DLLInjectorLogErrTxt);
                            data += "-----[logerr.txt]-----\n" + logStr + "\n";
                        }
                        if (File.Exists(GLFileTools.GreenLumaLogTxt))
                        {
                            string logStr = File.ReadAllText(GLFileTools.GreenLumaLogTxt);
                            data += "-----[GL_log.log]-----\n" + logStr + "\n";
                        }
                        string dataB64 = Base64.Base64Encode(Encoding.UTF8, data);
                        dataB64 = HttpUtility.UrlEncode(dataB64);
                        Dictionary<string, string> dic = new()
                        {
                            { "logString", dataB64  ?? ""}
                        };
                        //发送日志
                        OutAPI.Post(SteamWebData.LogUploadAddress, dic);
                    }
                    catch (Exception e)
                    {
                        string data = $"[v{Program.Version}]\n";
                        data += $"{e.Message}\n";
                        data += "Have expection when send log\n";
                        data += $"{e.StackTrace}\n";
                        string dataB64 = Base64.Base64Encode(Encoding.UTF8, data);
                        dataB64 = HttpUtility.UrlEncode(dataB64);
                        Dictionary<string, string> dic = new()
                        {
                            { "logString", dataB64 ?? ""}
                        };
                        //发送日志发送错误的日志
                        OutAPI.Post(SteamWebData.LogUploadAddress, dic);
                    }
                }
                await Task.Delay(50);
                lock (this)
                {
                    CancelWait = true;
                }
            }

        }
        private readonly Dictionary<int, string> retValueNeedHandle = new()
        {
            { 2048,"Injector failed to launch."},
            { 2049,"Injector crashed"},
            { -10010,"Injector crashed:[Unknown error]"},
            { -10020,"Injector crashed:[Failed to create the startup file]"},
            { -10030,"Injector crashed:[Unable to read DLL file]"},
            { -10040,"Injector crashed:[Unable to locate Steam.exe]"},
            { -10050,"Injector crashed:[Configuration file missing]"},
            { -10100,"Injector crashed:[Failed to create the termination file]"}
        };
        private void KillSteam()
        {
            //如果有残留注入器，就关闭进程(防止出问题了没退出)
            var injectorProcesses = Process.GetProcessesByName("spcrun");
            if (injectorProcesses.Length > 0)
            {
                foreach (var process in injectorProcesses)
                {
                    process.Kill(true);
                }
            }
            var injectorProcesses1 = Process.GetProcessesByName("DLLInjector");
            if (injectorProcesses1.Length > 0)
            {
                foreach (var process in injectorProcesses1)
                {
                    process.Kill(true);
                }
            }
            var injectorProcesses2 = Process.GetProcessesByName("DLLInjector_bak");
            if (injectorProcesses2.Length > 0)
            {
                foreach (var process in injectorProcesses2)
                {
                    process.Kill(true);
                }
            }
            //关闭Steam
            if (steamProcesses is null) return;
            foreach (var process in steamProcesses)
            {
                process.Kill(true);
            }
        }
        public void StateToDisable()
        {
            buttonState = "Disable";
            StartButtonColor = darkStartButtonColor;
            StartButtonContent = darkStartButtonContent;
            LoadingBarEcho = Visibility.Visible;
        }
        public void StateToStartSteam()
        {
            buttonState = "StartSteam";
            StartButtonColor = defStartButtonColor;
            StartButtonContent = defStartButtonContent;
            LoadingBarEcho = Visibility.Hidden;
        }
        public void StateToCloseSteam()
        {
            buttonState = "CloseSteam";
            StartButtonColor = closeStartButtonColor;
            StartButtonContent = closeStartButtonContent;
            LoadingBarEcho = Visibility.Hidden;
        }
        private volatile int startSteamTimes = 0;
        private Process[]? steamProcesses;
        private void UpdateSteamState()
        {
            while (true)
            {
                steamProcesses = Process.GetProcessesByName("steam");//获取指定的进程名   
                if (steamProcesses.Length > 0) //如果可以获取到知道的进程名则说明已经启动
                {
                    if (buttonState != "CloseSteam")
                    {
                        //记录本次运行启动次数
                        startSteamTimes++;
                        //记录总启动次数
                        DataSystem.Instance.StartSuccessTimes++;
                    }
                    StateToCloseSteam();
                }
                else
                {
                    steamProcesses = null;
                    if (buttonState != "Disable")
                        StateToStartSteam();
                    lock (this)
                    {
                        if (CancelWait)
                            StateToStartSteam();
                    }

                }
                Thread.Sleep(1000);
            }
        }
    }
}
