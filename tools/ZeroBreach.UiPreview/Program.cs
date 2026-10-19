using System.IO;
using System.Reflection;
using ZeroBreach.Core.Reports;

internal static class Program
{
    [STAThread] private static void Main()
    {
        var app = new ZeroBreach.App.App(); app.InitializeComponent();
        app.StartupUri = new Uri("/ZeroBreach;component/MainWindow.xaml",UriKind.Relative);
        var report = ReportReader.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"sample-report.json")),2);
        app.Startup += (_,_) => app.Dispatcher.BeginInvoke(() => {
            var window = (ZeroBreach.App.MainWindow)app.MainWindow;
            window.Title = "Zero Breach — synthetic preview";
            typeof(ZeroBreach.App.MainWindow).GetField("report",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,report);
            typeof(ZeroBreach.App.MainWindow).GetMethod("ShowReport",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[report]);
        });
        app.Run();
    }
}
