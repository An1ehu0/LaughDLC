using LaughDlc.Services;

namespace LaughDlc;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        AppLog.Info("Application starting.");

        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) =>
        {
            AppLog.Error("Unhandled UI thread exception.", e.Exception);
            MessageBox.Show(
                $"程序发生未处理错误：\n\n{e.Exception.Message}\n\n详细信息已写入：{AppLog.LogFilePath}",
                "笑声DLC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        };
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            AppLog.Error("Unhandled application exception.", e.ExceptionObject as Exception);
        };

        SingleInstanceService? singleInstance = null;
        MainForm? mainForm = null;

        try
        {
            singleInstance = SingleInstanceService.Acquire();

            if (!singleInstance.IsPrimary)
            {
                var activated = singleInstance.TryActivateExisting(TimeSpan.FromSeconds(5));
                if (!activated)
                {
                    MessageBox.Show(
                        "已检测到“笑声DLC”正在运行，但无法通知已有窗口显示。\n\n" +
                        "请从任务栏或系统托盘打开已有窗口。",
                        "笑声DLC",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                return;
            }

            var settingsStore = new SettingsStore();
            var loadResult = settingsStore.Load();
            mainForm = new MainForm(settingsStore, loadResult);

            _ = mainForm.Handle;
            singleInstance.StartServer(mainForm.RequestActivation);

            AppLog.Info("Entering WinForms message loop.");
            Application.Run(mainForm);
        }
        catch (Exception ex)
        {
            AppLog.Error("Fatal startup error.", ex);
            MessageBox.Show(
                $"笑声DLC 启动失败：\n\n{ex.Message}\n\n详细信息已写入：{AppLog.LogFilePath}",
                "笑声DLC",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            mainForm?.Dispose();
            singleInstance?.Dispose();
            AppLog.Info("Application stopped.");
        }
    }
}
