namespace WalkerRouteBuilder;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => Report(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Report(args.ExceptionObject as Exception);
        try
        {
            Application.Run(new MainForm());
        }
        catch (Exception exception)
        {
            Report(exception);
        }
    }

    private static void Report(Exception? exception)
    {
        if (exception is null)
            return;
        try
        {
            FileLog.Trace("crash", exception.ToString());
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "walker-route-builder-crash.txt"), exception.ToString());
        }
        catch
        {
            // Журнал необязателен: ниже всё равно покажется текст ошибки.
        }

        MessageBox.Show(exception.Message, "Конструктор траектории", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
