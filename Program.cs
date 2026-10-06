using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;

class Program
{
    static string gameFolder =
        @"C:\Program Files (x86)\Steam\steamapps\common\Black Myth Wukong Benchmark Tool";

    static string exePath = Path.Combine(gameFolder, "b1_benchmark.exe");
    static string configPath = Path.Combine(
        gameFolder, "b1", "Saved", "Config", "Windows", "GameUserSettings.ini");

    static string resultsFolder = Path.Combine(AppContext.BaseDirectory, "Results");

    static async Task Main()
    {
        Console.WriteLine("Black Myth: Wukong Benchmark Automation");
        Console.WriteLine();

        if (!File.Exists(exePath))
        {
            Console.WriteLine("Benchmark Tool was not found:");
            Console.WriteLine(exePath);
            Console.WriteLine("Change gameFolder in Program.cs.");
            return;
        }

        Directory.CreateDirectory(resultsFolder);

        string computerInfo = GetComputerInfo();
        string oldConfig = File.Exists(configPath)
            ? File.ReadAllText(configPath)
            : "";

        try
        {
            Console.WriteLine("1/2 CPU test");
            ApplyCpuSettings();
            TestResult cpu = await RunTest("CPU");

            Console.WriteLine();
            Console.WriteLine("2/2 GPU test");
            ApplyGpuSettings();
            TestResult gpu = await RunTest("GPU");

            string report = CreateReport(computerInfo, cpu, gpu);
            string reportPath = Path.Combine(resultsFolder, "report.txt");

            File.WriteAllText(reportPath, report);

            Console.WriteLine();
            Console.WriteLine("Done.");
            Console.WriteLine("Report: " + reportPath);
        }
        finally
        {
            if (oldConfig != "")
                File.WriteAllText(configPath, oldConfig);
        }
    }

    static void ApplyCpuSettings()
    {//тестик cpu
        SetSetting("ResolutionSizeX", "1280");
        SetSetting("ResolutionSizeY", "720");
        SetSetting("LastUserConfirmedResolutionSizeX", "1280");
        SetSetting("LastUserConfirmedResolutionSizeY", "720");

        SetSetting("sg.ResolutionQuality", "50");
        SetSetting("sg.ViewDistanceQuality", "3");
        SetSetting("sg.AntiAliasingQuality", "0");
        SetSetting("sg.ShadowQuality", "0");
        SetSetting("sg.GlobalIlluminationQuality", "0");
        SetSetting("sg.ReflectionQuality", "0");
        SetSetting("sg.PostProcessQuality", "0");
        SetSetting("sg.TextureQuality", "0");
        SetSetting("sg.EffectsQuality", "0");
        SetSetting("sg.FoliageQuality", "0");
        SetSetting("sg.ShadingQuality", "0");
        SetSetting("sg.RayTracingQuality", "0");
    }

    static void ApplyGpuSettings()
    {//тестик gpu
        SetSetting("ResolutionSizeX", "2560");
        SetSetting("ResolutionSizeY", "1440");
        SetSetting("LastUserConfirmedResolutionSizeX", "2560");
        SetSetting("LastUserConfirmedResolutionSizeY", "1440");

        SetSetting("sg.ResolutionQuality", "100");
        SetSetting("sg.ViewDistanceQuality", "3");
        SetSetting("sg.AntiAliasingQuality", "3");
        SetSetting("sg.ShadowQuality", "3");
        SetSetting("sg.GlobalIlluminationQuality", "3");
        SetSetting("sg.ReflectionQuality", "3");
        SetSetting("sg.PostProcessQuality", "3");
        SetSetting("sg.TextureQuality", "3");
        SetSetting("sg.EffectsQuality", "3");
        SetSetting("sg.FoliageQuality", "3");
        SetSetting("sg.ShadingQuality", "3");
        SetSetting("sg.RayTracingQuality", "0");
    }

    static async Task<TestResult> RunTest(string testName)
    {
        string csvPath = Path.Combine(resultsFolder, testName + "_frames.csv");
        if (File.Exists(csvPath))
            File.Delete(csvPath);
        Process? presentMon = StartPresentMon(csvPath);

        Process game = new Process();
        game.StartInfo.FileName = exePath;
        game.StartInfo.WorkingDirectory = gameFolder;
        game.Start();

        Console.WriteLine("Benchmark Tool started.");
        await Task.Delay(8000);
        ActivateWindow(game);
        SendEnter();

        Console.WriteLine("Benchmark started. Waiting for it to finish...");
        await game.WaitForExitAsync();

        if (presentMon != null && !presentMon.HasExited)
        {
            presentMon.Kill();
            await presentMon.WaitForExitAsync();
        }

        return ReadFps(csvPath);
    }

    static Process? StartPresentMon(string csvPath)
    {
        string presentMonPath = Path.Combine(AppContext.BaseDirectory, "PresentMon.exe");

        if (!File.Exists(presentMonPath))
        {
            Console.WriteLine("PresentMon.exe not found. FPS will be N/A.");
            return null;
        }

        Process p = new Process();
        p.StartInfo.FileName = presentMonPath;
        p.StartInfo.Arguments =
            $"-process_name b1_benchmark.exe -output_file \"{csvPath}\" -terminate_after_timed";
        p.StartInfo.UseShellExecute = false;
        p.StartInfo.CreateNoWindow = true;
        p.Start();

        return p;
    }

    static TestResult ReadFps(string csvPath)
    {
        if (!File.Exists(csvPath))
            return new TestResult(0, 0, 0, 0);

        string[] lines = File.ReadAllLines(csvPath);
        if (lines.Length < 2)
            return new TestResult(0, 0, 0, 0);

        string[] header = SplitCsv(lines[0]);

        int frameTimeColumn = Array.FindIndex(
            header,
            x => x.Equals("MsBetweenPresents", StringComparison.OrdinalIgnoreCase));

        if (frameTimeColumn < 0)
            return new TestResult(0, 0, 0, 0);

        List<double> fps = new List<double>();

        for (int i = 1; i < lines.Length; i++)
        {
            string[] values = SplitCsv(lines[i]);

            if (frameTimeColumn >= values.Length)
                continue;

            if (double.TryParse(
                values[frameTimeColumn],
                System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture,
                out double ms) && ms > 0)
            {
                fps.Add(1000.0 / ms);
            }
        }

        if (fps.Count == 0)
            return new TestResult(0, 0, 0, 0);

        fps.Sort();

        double average = fps.Average();
        double minimum = fps.First();
        double maximum = fps.Last();

        int lowIndex = Math.Max(0, (int)(fps.Count * 0.05) - 1);
        double low5 = fps[lowIndex];

        return new TestResult(average, minimum, maximum, low5);
    }

    static string[] SplitCsv(string line)
    {
        return line.Split(',');
    }

    static string CreateReport(
        string computerInfo,
        TestResult cpu,
        TestResult gpu)
    {
        return
$@"BLACK MYTH: WUKONG BENCHMARK REPORT
Date: {DateTime.Now}

COMPUTER
{computerInfo}

CPU TEST
Settings:
Resolution: 1280x720
Resolution scale: 50%
Graphics: Low
View distance: High
Ray tracing: Off

Average FPS: {Show(cpu.AverageFps)}
Minimum FPS: {Show(cpu.MinimumFps)}
Maximum FPS: {Show(cpu.MaximumFps)}
Low 5% FPS: {Show(cpu.Low5Fps)}

GPU TEST
Settings:
Resolution: 2560x1440
Resolution scale: 100%
Graphics: High
Ray tracing: Off

Average FPS: {Show(gpu.AverageFps)}
Minimum FPS: {Show(gpu.MinimumFps)}
Maximum FPS: {Show(gpu.MaximumFps)}
Low 5% FPS: {Show(gpu.Low5Fps)}

WHY THESE SETTINGS WERE CHOSEN
CPU test uses a low resolution and low graphics quality.
This reduces GPU load, so CPU performance has a larger effect.

GPU test uses a higher resolution and high graphics quality.
This increases GPU load and makes the video card the main bottleneck.
";
    }

    static string Show(double value){
        if (value == 0)
            return "N/A";

        return value.ToString("0.0"); }

    static string GetComputerInfo()
    { string cpu = GetWmiValue("Win32_Processor", "Name"); string gpu = GetWmiValue("Win32_VideoController", "Name");

        double ramGb = 0;

        using (ManagementObjectSearcher searcher =
               new ManagementObjectSearcher(
                   "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem"))
        {
            foreach (ManagementObject item in searcher.Get())
            {
                if (item["TotalPhysicalMemory"] != null)
                {
                    ramGb = Convert.ToDouble(item["TotalPhysicalMemory"])
                            / 1024 / 1024 / 1024;
                }
            }
        }

        return
$@"CPU: {cpu}
GPU: {gpu}
RAM: {ramGb:0.0} GB
OS: {Environment.OSVersion}";
    }

    static string GetWmiValue(string table, string property)
    {
        using ManagementObjectSearcher searcher =
            new ManagementObjectSearcher(
                $"SELECT {property} FROM {table}");

        foreach (ManagementObject item in searcher.Get())
        {
            if (item[property] != null)
                return item[property]!.ToString()!;
        }

        return "Unknown";}

    static void SetSetting(string key, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        string text = File.Exists(configPath)
            ? File.ReadAllText(configPath)
            : "";

        string pattern =
            @"(?m)^" + Regex.Escape(key) + @"=.*$";

        if (Regex.IsMatch(text, pattern)) {
            text = Regex.Replace(
                text,
                pattern,
                key + "=" + value); }
        else{ text += Environment.NewLine + key + "=" + value; }

        File.WriteAllText(configPath, text);
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr hWnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern void keybd_event( byte bVk, byte bScan,  uint dwFlags,  UIntPtr dwExtraInfo);

    static void ActivateWindow(Process process)
    {process.Refresh();

        if (process.MainWindowHandle != IntPtr.Zero)
            SetForegroundWindow(process.MainWindowHandle);  }

    static void SendEnter()
    { const byte ENTER = 0x0D; const uint KEYUP = 0x0002;

        keybd_event(ENTER, 0, 0, UIntPtr.Zero);
        keybd_event(ENTER, 0, KEYUP, UIntPtr.Zero);
    }
}

class TestResult
{
    public double AverageFps;
    public double MinimumFps;
    public double MaximumFps;
    public double Low5Fps;

    public TestResult(
        double average,
        double minimum,
        double maximum,
        double low5)
    {
        AverageFps = average;
        MinimumFps = minimum;
        MaximumFps = maximum;
        Low5Fps = low5;
    }
}
