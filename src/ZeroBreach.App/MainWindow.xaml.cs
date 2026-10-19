using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using ZeroBreach.Core.Reports;
using ZeroBreach.Core.Scanning;
using ZeroBreach.Core.Updates;

namespace ZeroBreach.App;
public partial class MainWindow : Window
{
    private const string PythonHash = "97DAE5274CC54867065E8D5A3226E48C35017ED332A0FDB0E27D5B5821961297";
    private readonly string root = Path.Combine(AppContext.BaseDirectory,"ZeroBreach-data");
    private readonly ScanWorkflow workflow = new();
    private readonly Stopwatch clock = new();
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly ReleaseClient releases = new();
    private readonly VerifiedCache cache;
    private IDisposable? workspace;
    private CancellationTokenSource? cancellation;
    private Task? active;
    private ScanReport? report;
    private string diagnostics = "No diagnostic details are available yet.";
    private bool closing;

    public MainWindow()
    {
        InitializeComponent(); cache = new VerifiedCache(Path.Combine(root,"scanners"));
        var area = SystemParameters.WorkArea;
        Width = Math.Min(900,area.Width * 0.95); Height = Math.Min(760,area.Height * 0.95);
        MinWidth = Math.Min(760,Width); MinHeight = Math.Min(650,Height);
        timer.Tick += (_,_) => ElapsedText.Text = $"Elapsed {clock.Elapsed:mm\\:ss} · {workflow.State}";
        Loaded += (_,_) => {
            try {
                workspace = PortableWorkspace.Acquire(root);
                var retained = Directory.EnumerateDirectories(root,"run-*").Concat(Directory.EnumerateDirectories(root,"runtime-stage-*")).ToArray();
                if (retained.Length > 0) ShowError("Temporary files remain from a previously interrupted run. They may contain private scan information. Review these local folders after closing the app:\n" + string.Join("\n",retained));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError(ex.Message); StartButton.IsEnabled = false; }
        };
    }

    private void Start_Click(object sender,RoutedEventArgs e) => active = RunAsync(false);
    private void Saved_Click(object sender,RoutedEventArgs e) => active = RunAsync(true);
    private void Cancel_Click(object sender,RoutedEventArgs e) { cancellation?.Cancel(); workflow.Cancel(); StatusText.Text = "Cancelling… waiting for the scanner to stop."; CancelButton.IsEnabled = false; }

    private async Task RunAsync(bool useSaved)
    {
        if (workspace == null || !workflow.TryStart()) return;
        report = null; ResultPanel.Visibility = ErrorPanel.Visibility = SavedButton.Visibility = Visibility.Collapsed;
        StartButton.IsEnabled = Standard.IsEnabled = Quick.IsEnabled = false;
        CancelButton.Visibility = Progress.Visibility = Visibility.Visible; CancelButton.IsEnabled = true;
        cancellation?.Dispose(); cancellation = new CancellationTokenSource(); var token = cancellation.Token;
        clock.Restart(); timer.Start(); diagnostics = "";
        ScanRunner? runner = null;
        var progress = new Progress<string>(s => { if (workflow.IsRunning && !token.IsCancellationRequested) StatusText.Text = s; });
        try {
            StatusText.Text = "Preparing the bundled runtime…";
            var python = await Task.Run(() => RuntimeManager.Ensure(Resource("python.zip"),PythonHash,root),token);
            token.ThrowIfCancellationRequested();
            var scanner = useSaved ? await cache.OpenSavedAsync(token) : await cache.UpdateAsync(releases,progress,token);
            token.ThrowIfCancellationRequested(); workflow.Scan();
            StatusText.Text = $"Running verified scanner {scanner.Tag}…";
            runner = new ScanRunner(python,root,cache);
            var result = await runner.RunAsync(scanner,Quick.IsChecked == true,progress,token);
            token.ThrowIfCancellationRequested();
            if (!workflow.Complete()) return;
            report = result; diagnostics = runner.Diagnostics; ShowReport(result);
            StatusText.Text = $"Scan completed with verified scanner {scanner.Tag}. Review the result and coverage gaps below.";
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { workflow.Cancel(); StatusText.Text = "Scan cancelled. No completed result is available."; }
        catch (Exception ex) {
            var wasUpdating = workflow.State == ScanState.Updating; workflow.Fail();
            diagnostics = (runner?.Diagnostics ?? "") + "\n" + ex;
            ShowError(ex.Message); StatusText.Text = "The scan could not complete. No completed result is available.";
            if (wasUpdating && !token.IsCancellationRequested) {
                try {
                    var saved = await cache.OpenSavedAsync(token);
                    SavedButton.Content = $"Scan with saved version {saved.Tag}";
                    SavedButton.Visibility = Visibility.Visible;
                    ErrorText.Text += $"\n\nSaved scanner {saved.Tag} has passed signature verification. You may explicitly choose it above; this does not confirm it is the latest release.";
                } catch (Exception savedError) when (savedError is IOException or UnauthorizedAccessException or OperationCanceledException) { }
            }
        }
        finally {
            clock.Stop(); timer.Stop(); ElapsedText.Text = $"Elapsed {clock.Elapsed:mm\\:ss} · {workflow.State}";
            StartButton.IsEnabled = Standard.IsEnabled = Quick.IsEnabled = true;
            CancelButton.Visibility = Progress.Visibility = Visibility.Collapsed;
            if (runner?.CleanupWarning is { } warning) ShowError(warning);
        }
    }

    private void ShowReport(ScanReport r)
    {
        ResultPanel.Visibility = Visibility.Visible;
        ResultEyebrow.Text = $"SCANNER {r.Version} · {r.Timestamp} · {r.ScanMode}";
        ResultTitle.Text = r.Title;
        ResultTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(r.Findings.Count == 0 ? "#236653" : "#935119"));
        ResultSummary.Text = r.Summary; FindingsList.ItemsSource = r.Findings;
        EmptyFindings.Visibility = r.Findings.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        GapsText.Text = string.Join("\n\n",r.Gaps.Select(g => "• " + g));
        CoverageText.Text = string.Join("\n\n",r.Coverage); NextText.Text = r.NextSteps;
        FindingsTab.IsSelected = true;
    }
    private void ShowError(string text) { ErrorPanel.Visibility = Visibility.Visible; ErrorText.Text = text; }
    private void SaveText_Click(object sender,RoutedEventArgs e) => Save(false);
    private void SaveJson_Click(object sender,RoutedEventArgs e) => Save(true);
    private void Save(bool json)
    {
        if (report == null) return;
        var dialog = new SaveFileDialog { Title = json ? "Save original JSON — includes raw indicators and local paths" : "Save report — includes local paths and profile names",
            Filter = json ? "JSON report (*.json)|*.json" : "Text report (*.txt)|*.txt", FileName = $"ZeroBreach-{DateTime.Now:yyyyMMdd-HHmmss}.{(json ? "json" : "txt")}", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        var temp = dialog.FileName + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try {
            File.WriteAllText(temp,json ? report.OriginalJson : ReportExporter.ToText(report),new UTF8Encoding(false));
            File.Move(temp,dialog.FileName,true); StatusText.Text = "Report saved. Share it securely; it may contain private local details.";
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError("The report could not be saved. " + ex.Message); }
        finally { if (File.Exists(temp)) { try { File.Delete(temp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { ShowError("An incomplete export could not be removed: " + temp); } } }
    }
    private static byte[] Resource(string name)
    {
        using var input = typeof(ScanRunner).Assembly.GetManifestResourceStream(name) ?? throw new InvalidDataException("An embedded application component is missing.");
        using var output = new MemoryStream(); input.CopyTo(output); return output.ToArray();
    }
    private void About_Click(object sender,RoutedEventArgs e) => ShowDetails("About Zero Breach", "Zero Breach Windows GUI 0.1.0\n\nIndependent interface for interplains/zero-breach. Not endorsed by the upstream project or cited researchers.\n\nOfficial scanner: https://github.com/interplains/zero-breach\nTrusted publisher fingerprint: " + SignatureVerifier.Fingerprint + "\n\n" + Encoding.UTF8.GetString(Resource("notices.txt")));
    private void Source_RequestNavigate(object sender,System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        { ShowError("Could not open the browser. Python source: https://github.com/interplains/zero-breach/tree/main/scan"); }
    }
    private void Diagnostics_Click(object sender,RoutedEventArgs e) => ShowDetails("Diagnostic details", "Diagnostics may contain local paths or profile information. Share securely.\n\n" + diagnostics);
    private void ShowDetails(string title,string text)
    {
        var box = new System.Windows.Controls.TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto, Padding = new Thickness(20), BorderThickness = new Thickness(0) };
        new Window { Owner = this, Title = title, Width = 760, Height = 580, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = box }.ShowDialog();
    }
    private async void OnClosing(object? sender,CancelEventArgs e)
    {
        if (active is { IsCompleted:false } && !closing) {
            e.Cancel = true; cancellation?.Cancel(); workflow.Cancel();
            await active; closing = true; Close(); return;
        }
        timer.Stop(); cancellation?.Dispose(); releases.Dispose(); workspace?.Dispose();
    }
}
